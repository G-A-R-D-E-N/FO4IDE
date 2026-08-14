using System.IO;
using System.Text;
using Mutagen.Bethesda.Archives;

namespace FO4RecordEditor.Services.Archives;

internal sealed record ArchiveExtractionPlanItem(IArchiveFile Entry, string DestinationPath);

internal static class ArchiveExtraction
{
    private static readonly char[] ArchiveSeparators = { '\\', '/' };
    private static readonly char[] PortableInvalidFileNameChars = { '<', '>', ':', '"', '|', '?', '*' };

    private static readonly StringComparer FileSystemPathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        "COM¹", "COM²", "COM³", "LPT¹", "LPT²", "LPT³",
    };

    private sealed record NormalizedEntry(
        IArchiveFile Entry,
        string RawPath,
        string[] Segments,
        string LogicalPath);

    internal static bool TryCreatePlan(
        IReadOnlyList<IArchiveFile> entries,
        string outputDirectory,
        out string outputRoot,
        out List<ArchiveExtractionPlanItem> plan,
        out string error,
        Action<string>? directoryEnumerated = null,
        Func<string, bool>? directCandidateExists = null)
    {
        outputRoot = "";
        plan = new List<ArchiveExtractionPlanItem>();
        error = "";

        try
        {
            outputRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory));
        }
        catch (Exception ex)
        {
            error = $"Invalid output directory '{outputDirectory}': {ex.Message}";
            return false;
        }

        if (File.Exists(outputRoot) && !Directory.Exists(outputRoot))
        {
            error = $"Output path '{outputRoot}' is a file, not a directory.";
            return false;
        }

        try
        {
            EnsureNoReparsePointAncestors(outputRoot);
            if (Directory.Exists(outputRoot) && IsReparsePoint(outputRoot))
            {
                error = $"Output directory '{outputRoot}' is a symbolic link or reparse point. Choose a real directory for extraction.";
                return false;
            }
        }
        catch (Exception ex)
        {
            error = $"Could not inspect output directory '{outputRoot}': {ex.Message}";
            return false;
        }

        var normalized = new List<NormalizedEntry>(entries.Count);
        var logicalFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var logicalDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directorySpellings = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var rawPath = entry.Path ?? "";
            if (!TryNormalizeArchivePath(rawPath, out var segments, out var pathError))
            {
                error = $"Unsafe archive entry '{DisplayPath(rawPath)}': {pathError}";
                return false;
            }

            var logicalPath = string.Join("/", segments);
            if (logicalFiles.TryGetValue(logicalPath, out var earlierPath))
            {
                error = $"Archive entries '{earlierPath}' and '{rawPath}' map to the same case-insensitive destination. " +
                        "Extraction was refused to prevent one entry overwriting the other.";
                return false;
            }

            logicalFiles.Add(logicalPath, rawPath);
            normalized.Add(new NormalizedEntry(entry, rawPath, segments, logicalPath));

            for (var i = 0; i < segments.Length - 1; i++)
            {
                var directoryKey = string.Join("/", segments.Take(i + 1));
                logicalDirectories.Add(directoryKey);
                if (!directorySpellings.TryGetValue(directoryKey, out var spellings))
                {
                    spellings = new HashSet<string>(StringComparer.Ordinal);
                    directorySpellings.Add(directoryKey, spellings);
                }
                spellings.Add(segments[i]);
            }
        }

        foreach (var file in normalized)
        {
            if (logicalDirectories.Contains(file.LogicalPath))
            {
                error = $"Archive entry '{file.RawPath}' is both a file and a parent directory for another entry. " +
                        "Extraction was refused before writing anything.";
                return false;
            }
        }

        var canonicalDirectoryNames = directorySpellings.ToDictionary(
            pair => pair.Key,
            pair => ChooseCanonicalDirectorySpelling(pair.Value),
            StringComparer.OrdinalIgnoreCase);

        var completedPlan = new List<ArchiveExtractionPlanItem>(normalized.Count);
        var plannedDestinations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var existingChildren = new Dictionary<string, Dictionary<string, List<string>>>(FileSystemPathComparer);
        foreach (var item in normalized)
        {
            var current = outputRoot;
            for (var i = 0; i < item.Segments.Length - 1; i++)
            {
                var directoryKey = string.Join("/", item.Segments.Take(i + 1));
                var desiredName = canonicalDirectoryNames[directoryKey];
                if (!TryResolveExistingChild(
                        current,
                        desiredName,
                        expectDirectory: true,
                        existingChildren,
                        directoryEnumerated,
                        directCandidateExists,
                        out var actualName,
                        out var resolveError))
                {
                    error = $"Cannot safely extract '{item.RawPath}': {resolveError}";
                    return false;
                }
                current = Path.Combine(current, actualName);
            }

            var fileName = item.Segments[^1];
            if (!TryResolveExistingChild(
                    current,
                    fileName,
                    expectDirectory: false,
                    existingChildren,
                    directoryEnumerated,
                    directCandidateExists,
                    out var actualFileName,
                    out var fileError))
            {
                error = $"Cannot safely extract '{item.RawPath}': {fileError}";
                return false;
            }

            var destination = Path.GetFullPath(Path.Combine(current, actualFileName));
            if (!IsContainedBy(outputRoot, destination))
            {
                error = $"Archive entry '{item.RawPath}' resolves outside output directory '{outputRoot}'.";
                return false;
            }

            if (plannedDestinations.TryGetValue(destination, out var earlierPath))
            {
                error = $"Archive entries '{earlierPath}' and '{item.RawPath}' collide at the same planned destination. " +
                        "Extraction was refused before writing anything.";
                return false;
            }

            plannedDestinations.Add(destination, item.RawPath);
            completedPlan.Add(new ArchiveExtractionPlanItem(item.Entry, destination));
        }

        plan = completedPlan;
        return true;
    }

    internal static bool TryWritePlannedEntry(
        ArchiveExtractionPlanItem item,
        string outputRoot,
        out string error)
    {
        error = "";
        string? tempPath = null;

        try
        {
            var destination = Path.GetFullPath(item.DestinationPath);
            if (!IsContainedBy(outputRoot, destination))
                throw new IOException($"Destination '{destination}' is outside extraction root '{outputRoot}'.");

            var destinationDirectory = Path.GetDirectoryName(destination)
                ?? throw new IOException($"Destination '{destination}' has no parent directory.");

            EnsureSafeDirectoryTree(outputRoot, destinationDirectory);
            EnsureSafeDestinationFile(destination);

            var bytes = item.Entry.GetBytes();
            tempPath = CreateTemporaryPath(destinationDirectory);

            using (var stream = new FileStream(
                       tempPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 81920,
                       FileOptions.SequentialScan))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            EnsureSafeDirectoryTree(outputRoot, destinationDirectory);
            EnsureSafeDestinationFile(destination);
            PreserveExistingUnixMode(destination, tempPath);
            ReplaceTemporaryFile(destination, tempPath);
            tempPath = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            if (tempPath != null)
            {
                try { File.Delete(tempPath); }
                catch { }
            }
        }
    }

    internal static bool TryWriteExplicitFile(string destinationPath, byte[] bytes, out string error)
    {
        error = "";
        string? tempPath = null;

        try
        {
            var destination = Path.GetFullPath(destinationPath);
            var directory = Path.GetDirectoryName(destination)
                ?? throw new IOException($"Destination '{destination}' has no parent directory.");

            EnsureNoReparsePointAncestors(directory);
            Directory.CreateDirectory(directory);
            if (IsReparsePoint(destination))
                throw new IOException($"Destination '{destination}' is a symbolic link or reparse point.");
            if (Directory.Exists(destination))
                throw new IOException($"Destination '{destination}' is a directory, not a file.");

            tempPath = CreateTemporaryPath(directory);
            using (var stream = new FileStream(
                       tempPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 81920,
                       FileOptions.SequentialScan))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            EnsureNoReparsePointAncestors(directory);
            if (IsReparsePoint(destination))
                throw new IOException($"Destination '{destination}' became a symbolic link or reparse point.");
            PreserveExistingUnixMode(destination, tempPath);
            ReplaceTemporaryFile(destination, tempPath);
            tempPath = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            if (tempPath != null)
            {
                try { File.Delete(tempPath); }
                catch { }
            }
        }
    }

    internal static string NormalizeLookupPath(string? path) =>
        (path ?? "").Replace('/', '\\').TrimStart('\\');

    private static bool TryNormalizeArchivePath(string rawPath, out string[] segments, out string error)
    {
        segments = Array.Empty<string>();
        error = "";

        if (string.IsNullOrWhiteSpace(rawPath))
        {
            error = "the entry path is empty.";
            return false;
        }

        if (rawPath[0] is '/' or '\\')
        {
            error = "rooted and UNC-style paths are not allowed.";
            return false;
        }

        if (rawPath.Length >= 2 && rawPath[1] == ':')
        {
            error = "drive-qualified paths are not allowed.";
            return false;
        }

        segments = rawPath.Split(ArchiveSeparators, StringSplitOptions.None);
        if (segments.Length == 0 || segments.Any(string.IsNullOrEmpty))
        {
            error = "empty path segments and repeated/trailing separators are not allowed.";
            return false;
        }

        foreach (var segment in segments)
        {
            if (segment is "." or "..")
            {
                error = "'.' and '..' path segments are not allowed.";
                return false;
            }

            if (segment.Any(char.IsControl))
            {
                error = "control characters are not allowed in entry names.";
                return false;
            }

            if (segment.IndexOfAny(PortableInvalidFileNameChars) >= 0)
            {
                error = $"entry segment '{DisplayPath(segment)}' contains a character that is unsafe or invalid on Windows.";
                return false;
            }

            if (segment.Length > 255 || Encoding.UTF8.GetByteCount(segment) > 255)
            {
                error = $"entry segment '{DisplayPath(segment)}' exceeds the portable 255-unit filename limit.";
                return false;
            }

            if (segment.EndsWith(' ') || segment.EndsWith('.'))
            {
                error = $"entry segment '{DisplayPath(segment)}' ends in a space or dot and would alias another Windows path.";
                return false;
            }

            var dot = segment.IndexOf('.');
            var deviceStem = dot >= 0 ? segment[..dot] : segment;
            if (ReservedWindowsNames.Contains(deviceStem))
            {
                error = $"entry segment '{DisplayPath(segment)}' is a reserved Windows device name.";
                return false;
            }
        }

        return true;
    }

    private static string ChooseCanonicalDirectorySpelling(IReadOnlyCollection<string> spellings)
    {
        if (spellings.Count == 1) return spellings.First();

        var lowercase = spellings
            .Where(value => string.Equals(value, value.ToLowerInvariant(), StringComparison.Ordinal))
            .OrderBy(value => value, StringComparer.Ordinal)
            .FirstOrDefault();
        if (lowercase != null) return lowercase;

        return spellings
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value, StringComparer.Ordinal)
            .First();
    }

    private static bool TryResolveExistingChild(
        string parent,
        string desiredName,
        bool expectDirectory,
        Dictionary<string, Dictionary<string, List<string>>> existingChildren,
        Action<string>? directoryEnumerated,
        Func<string, bool>? directCandidateExists,
        out string actualName,
        out string error)
    {
        actualName = desiredName;
        error = "";

        if (File.Exists(parent) && !Directory.Exists(parent))
        {
            error = $"'{parent}' is a file where a directory is required.";
            return false;
        }

        if (!Directory.Exists(parent)) return true;

        List<string> matches;
        try
        {
            if (IsReparsePoint(parent))
            {
                error = $"directory '{parent}' is a symbolic link or reparse point.";
                return false;
            }

            if (!existingChildren.TryGetValue(parent, out var children))
            {
                directoryEnumerated?.Invoke(parent);
                children = Directory.EnumerateFileSystemEntries(parent)
                    .GroupBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Take(3).ToList(),
                        StringComparer.OrdinalIgnoreCase);
                existingChildren.Add(parent, children);
            }

            if (!children.TryGetValue(desiredName, out matches!))
            {
                var directCandidate = Path.Combine(parent, desiredName);
                var shouldCheckDirectCandidate = OperatingSystem.IsWindows() || directCandidateExists != null;
                var directCandidateResolves = shouldCheckDirectCandidate &&
                    (directCandidateExists?.Invoke(directCandidate) ??
                     (File.Exists(directCandidate) || Directory.Exists(directCandidate)));
                if (directCandidateResolves)
                {
                    error = $"output path '{directCandidate}' resolves to an existing filesystem object under " +
                            "another name (for example, a Windows 8.3 short-name alias or a concurrent directory change). " +
                            "Extraction was refused instead of overwriting an object absent from the directory snapshot.";
                    return false;
                }
                return true;
            }
        }
        catch (Exception ex)
        {
            error = $"could not inspect output directory '{parent}': {ex.Message}";
            return false;
        }

        if (matches.Count > 1)
        {
            error = $"output directory '{parent}' already contains multiple case variants of '{desiredName}'. " +
                    "The destination is ambiguous on a case-insensitive filesystem.";
            return false;
        }

        if (matches.Count == 0) return true;

        var match = matches[0];
        if (expectDirectory && !Directory.Exists(match))
        {
            error = $"existing path '{match}' is a file where the archive needs a directory.";
            return false;
        }
        if (!expectDirectory && Directory.Exists(match))
        {
            error = $"existing path '{match}' is a directory where the archive needs a file.";
            return false;
        }
        if (!expectDirectory && !File.Exists(match))
        {
            error = $"existing path '{match}' is not a regular file.";
            return false;
        }
        try
        {
            if (IsReparsePoint(match))
            {
                error = $"existing path '{match}' is a symbolic link or reparse point.";
                return false;
            }
        }
        catch (Exception ex)
        {
            error = $"could not inspect existing path '{match}': {ex.Message}";
            return false;
        }

        actualName = Path.GetFileName(match) ?? desiredName;
        return true;
    }

    private static void PreserveExistingUnixMode(string destination, string temporaryPath)
    {
        if (OperatingSystem.IsWindows() || !File.Exists(destination)) return;
        File.SetUnixFileMode(temporaryPath, File.GetUnixFileMode(destination));
    }

    private static void ReplaceTemporaryFile(string destination, string temporaryPath)
    {
        if (OperatingSystem.IsWindows() && File.Exists(destination))
        {

            File.Replace(temporaryPath, destination, destinationBackupFileName: null);
            return;
        }

        File.Move(temporaryPath, destination, overwrite: true);
    }

    private static string CreateTemporaryPath(string directory) =>
        Path.Combine(directory, $".pet-{Guid.NewGuid():N}.tmp");

    private static void EnsureNoReparsePointAncestors(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)
            ?? throw new IOException($"Path '{fullPath}' has no filesystem root.");

        var current = Path.TrimEndingDirectorySeparator(root);
        if (current.Length == 0) current = root;
        if (IsReparsePoint(current))
            throw new IOException($"Path ancestor '{current}' is a symbolic link or reparse point.");

        var relative = Path.GetRelativePath(root, fullPath);
        if (relative == ".") return;

        foreach (var segment in relative.Split(
                     new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (IsReparsePoint(current))
                throw new IOException($"Path ancestor '{current}' is a symbolic link or reparse point.");

            if (!File.Exists(current) && !Directory.Exists(current)) break;
        }
    }

    private static void EnsureSafeDirectoryTree(string outputRoot, string destinationDirectory)
    {
        outputRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputRoot));
        destinationDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationDirectory));

        if (!IsContainedBy(outputRoot, destinationDirectory) &&
            !string.Equals(outputRoot, destinationDirectory, PathComparison))
        {
            throw new IOException($"Directory '{destinationDirectory}' is outside extraction root '{outputRoot}'.");
        }

        EnsureNoReparsePointAncestors(outputRoot);
        if (File.Exists(outputRoot) && !Directory.Exists(outputRoot))
            throw new IOException($"Extraction root '{outputRoot}' is a file, not a directory.");

        Directory.CreateDirectory(outputRoot);
        if (IsReparsePoint(outputRoot))
            throw new IOException($"Extraction root '{outputRoot}' is a symbolic link or reparse point.");

        var relative = Path.GetRelativePath(outputRoot, destinationDirectory);
        if (relative == ".") return;

        var current = outputRoot;
        foreach (var segment in relative.Split(
                     new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (File.Exists(current) && !Directory.Exists(current))
                throw new IOException($"'{current}' is a file where a directory is required.");

            Directory.CreateDirectory(current);
            if (IsReparsePoint(current))
                throw new IOException($"Directory '{current}' is a symbolic link or reparse point.");
        }
    }

    private static void EnsureSafeDestinationFile(string destination)
    {
        if (Directory.Exists(destination))
            throw new IOException($"Destination '{destination}' is a directory, not a file.");
        if (IsReparsePoint(destination))
            throw new IOException($"Destination '{destination}' is a symbolic link or reparse point.");
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static bool IsContainedBy(string outputRoot, string destination)
    {
        var relative = Path.GetRelativePath(outputRoot, destination);
        if (Path.IsPathRooted(relative)) return false;
        if (relative == "..") return false;
        return !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
               !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string DisplayPath(string value) =>
        value.Replace("\r", "\\r", StringComparison.Ordinal)
             .Replace("\n", "\\n", StringComparison.Ordinal)
             .Replace("\0", "\\0", StringComparison.Ordinal);
}