using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives;
using JsonConvert = Newtonsoft.Json.JsonConvert;

using FO4RecordEditor.Services.Archives;

namespace FO4RecordEditor.Services;

public static class ArchiveService
{
    private static IArchiveReader? TryOpen(string archivePath, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(archivePath)) { error = "Provide an archive path (.ba2 or .bsa)."; return null; }
        if (!File.Exists(archivePath)) { error = ToolError.Fail($"Archive not found: '{archivePath}'."); return null; }
        try { return Archive.CreateReader(GameRelease.Fallout4, new Noggog.FilePath(archivePath)); }
        catch (Exception ex) { error = ToolError.Fail($"Could not read '{archivePath}': {ex.Message}"); return null; }
    }

    private static readonly TimeSpan FilterRegexTimeout = TimeSpan.FromMilliseconds(100);

    internal static Func<string, bool> BuildMatcher(string? filter, string? mode, TimeSpan? regexTimeout = null)
    {
        var normalizedMode = string.IsNullOrWhiteSpace(mode) ? "simple" : mode.Trim().ToLowerInvariant();
        if (normalizedMode is not ("simple" or "contains" or "wildcard" or "glob" or "regex"))
            throw new ArgumentException("filter mode must be 'simple'/'contains', 'wildcard'/'glob', or 'regex'.");

        if (string.IsNullOrWhiteSpace(filter)) return _ => true;
        if (normalizedMode is "simple" or "contains")
            return s => s.Contains(filter, StringComparison.OrdinalIgnoreCase);

        var pattern = normalizedMode == "regex"
            ? filter
            : Regex.Escape(filter).Replace(@"\*", ".*").Replace(@"\?", ".");
        var rx = new Regex(
            pattern,
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant,
            regexTimeout ?? FilterRegexTimeout);
        return s => rx.IsMatch(s);
    }

    internal static bool TryFilterFiles(
        IEnumerable<IArchiveFile> source,
        Func<string, bool> matches,
        string? filterMode,
        out List<IArchiveFile> files,
        out string error)
    {
        try
        {
            files = source.Where(f => matches(f.Path ?? "")).ToList();
            error = "";
            return true;
        }
        catch (RegexMatchTimeoutException)
        {
            files = new List<IArchiveFile>();
            error = $"The {filterMode ?? "regex"} filter took too long to evaluate and was stopped. " +
                    "Use a simpler expression or substring/wildcard mode.";
            return false;
        }
    }

    public static string ListArchive(string archivePath, string? filter, int limit)
    {
        var reader = TryOpen(archivePath, out var err);
        if (reader == null) return err;
        if (limit <= 0) limit = 500;

        var files = reader.Files.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(filter))
            files = files.Where(f => (f.Path ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase));

        var all = files.ToList();
        if (all.Count == 0)
            return $"No entries in '{Path.GetFileName(archivePath)}'" + (string.IsNullOrWhiteSpace(filter) ? "." : $" matching '{filter}'.");

        var shown = all.Take(limit).ToList();
        var header = $"{Path.GetFileName(archivePath)}: {all.Count} entr{(all.Count == 1 ? "y" : "ies")}" +
                     (string.IsNullOrWhiteSpace(filter) ? "" : $" matching '{filter}'") +
                     (all.Count > shown.Count ? $" (showing first {shown.Count})" : "") + ":";
        return header + "\n" + string.Join("\n", shown.Select(f => $"{f.Path}  ({f.Size:N0} bytes)"));
    }

    public static string ListArchiveJson(string archivePath, string? filter, int limit, string? filterMode = null)
    {
        var reader = TryOpen(archivePath, out var err);
        if (reader == null) return JsonConvert.SerializeObject(new { error = err });
        if (limit <= 0) limit = 2000;

        Func<string, bool> matches;
        try { matches = BuildMatcher(filter, filterMode); }
        catch (Exception ex) { return JsonConvert.SerializeObject(new { error = ToolError.Fail($"Invalid {filterMode ?? "simple"} filter: {ex.Message}") }); }

        if (!TryFilterFiles(reader.Files, matches, filterMode, out var all, out var filterError))
            return JsonConvert.SerializeObject(new { error = ToolError.Fail(filterError) });

        var shown = all.Take(limit).ToList();
        return JsonConvert.SerializeObject(new
        {
            archiveName = Path.GetFileName(archivePath),
            totalCount = all.Count,
            shownCount = shown.Count,
            truncated = all.Count > shown.Count,
            entries = shown.Select(f => new { path = f.Path, size = f.Size }),
        });
    }

    public static string ExtractSelected(string archivePath, IEnumerable<string> innerPaths, string outDir)
    {
        var reader = TryOpen(archivePath, out var err);
        if (reader == null) return err;
        if (string.IsNullOrWhiteSpace(outDir)) return ToolError.Fail("Provide an output directory.");

        var wanted = new HashSet<string>(
            innerPaths.Select(ArchiveExtraction.NormalizeLookupPath), StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0) return ToolError.Fail("No files selected.");

        var matches = reader.Files
            .Where(f => wanted.Contains(ArchiveExtraction.NormalizeLookupPath(f.Path)))
            .ToList();
        if (matches.Count == 0) return ToolError.Fail("None of the selected files were found in the archive.");

        return ExtractEntries(archivePath, outDir, matches, selected: true);
    }

    public static string ExtractFile(string archivePath, string innerPath, string outPath)
    {
        var reader = TryOpen(archivePath, out var err);
        if (reader == null) return err;
        if (string.IsNullOrWhiteSpace(innerPath))
            return ToolError.Fail("Provide the file's path INSIDE the archive (as shown by archive_list), e.g. 'Meshes\\mymod\\thing.nif'.");
        if (string.IsNullOrWhiteSpace(outPath))
            return ToolError.Fail("Provide an output path to write the extracted file to.");

        var norm = innerPath.Replace('/', '\\').TrimStart('\\');
        var entry = reader.Files.FirstOrDefault(f =>
            string.Equals((f.Path ?? "").Replace('/', '\\'), norm, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
            return ToolError.Fail($"'{innerPath}' is not in '{Path.GetFileName(archivePath)}'. Call archive_list first to see exact entry paths.");

        byte[] bytes;
        try { bytes = entry.GetBytes(); }
        catch (Exception ex) { return ToolError.Fail($"Extract failed while reading '{innerPath}': {ex.Message}"); }

        if (!ArchiveExtraction.TryWriteExplicitFile(outPath, bytes, out var writeError))
            return ToolError.Fail($"Extract failed: {writeError}");

        return $"Extracted '{innerPath}' ({entry.Size:N0} bytes) from '{Path.GetFileName(archivePath)}' to '{outPath}'.";
    }

    public static string ExtractAll(string archivePath, string outDir, string? filter, int limit, string? filterMode = null)
    {
        var reader = TryOpen(archivePath, out var err);
        if (reader == null) return err;
        if (string.IsNullOrWhiteSpace(outDir)) return ToolError.Fail("Provide an output directory.");
        if (limit <= 0) limit = 2000;

        Func<string, bool> matches;
        try { matches = BuildMatcher(filter, filterMode); }
        catch (Exception ex) { return ToolError.Fail($"Invalid {filterMode ?? "simple"} filter: {ex.Message}"); }

        if (!TryFilterFiles(reader.Files, matches, filterMode, out var all, out var filterError))
            return ToolError.Fail(filterError);
        if (all.Count == 0)
            return $"No entries" + (string.IsNullOrWhiteSpace(filter) ? "" : $" matching '{filter}'") + $" in '{Path.GetFileName(archivePath)}'.";

        if (all.Count > limit)
            return ToolError.Fail($"'{Path.GetFileName(archivePath)}' has {all.Count} matching entries, over the {limit} limit. " +
                "Narrow with 'filter' (a path substring, e.g. 'Meshes\\mymod\\') or raise 'limit'.");

        return ExtractEntries(archivePath, outDir, all, selected: false);
    }

    private static string ExtractEntries(
        string archivePath,
        string outDir,
        IReadOnlyList<IArchiveFile> entries,
        bool selected)
    {
        if (!ArchiveExtraction.TryCreatePlan(entries, outDir, out var outputRoot, out var plan, out var planError))
            return ToolError.Fail($"Extraction refused before writing anything: {planError}");

        var written = 0;
        var failures = new List<string>();
        foreach (var item in plan)
        {
            if (ArchiveExtraction.TryWritePlannedEntry(item, outputRoot, out var writeError))
                written++;
            else
                failures.Add($"{item.Entry.Path}: {writeError}");
        }

        var countDescription = selected ? $"{entries.Count} selected file(s)" : $"{entries.Count} file(s)";
        var msg = $"Extracted {written} of {countDescription} from '{Path.GetFileName(archivePath)}' to '{outDir}'.";
        if (failures.Count > 0) msg += $" {failures.Count} FAILED: " + string.Join("; ", failures.Take(10));
        return msg;
    }

    public static string CompareArchivesJson(string archivePathA, string archivePathB)
    {
        var readerA = TryOpen(archivePathA, out var errA);
        if (readerA == null) return JsonConvert.SerializeObject(new { error = errA });
        var readerB = TryOpen(archivePathB, out var errB);
        if (readerB == null) return JsonConvert.SerializeObject(new { error = errB });

        var filesA = readerA.Files.ToDictionary(f => (f.Path ?? "").Replace('/', '\\'), StringComparer.OrdinalIgnoreCase);
        var filesB = readerB.Files.ToDictionary(f => (f.Path ?? "").Replace('/', '\\'), StringComparer.OrdinalIgnoreCase);

        var allPaths = new HashSet<string>(filesA.Keys, StringComparer.OrdinalIgnoreCase);
        allPaths.UnionWith(filesB.Keys);

        var added = new List<string>();
        var removed = new List<string>();
        var changed = new List<string>();
        int identical = 0;

        foreach (var path in allPaths)
        {
            var inA = filesA.TryGetValue(path, out var a);
            var inB = filesB.TryGetValue(path, out var b);
            if (inA && !inB) { removed.Add(path); continue; }
            if (!inA && inB) { added.Add(path); continue; }

            if (a!.Size != b!.Size) { changed.Add(path); continue; }
            if (a.GetBytes().AsSpan().SequenceEqual(b.GetBytes())) identical++;
            else changed.Add(path);
        }

        added.Sort(StringComparer.OrdinalIgnoreCase);
        removed.Sort(StringComparer.OrdinalIgnoreCase);
        changed.Sort(StringComparer.OrdinalIgnoreCase);

        return JsonConvert.SerializeObject(new
        {
            archiveA = Path.GetFileName(archivePathA),
            archiveB = Path.GetFileName(archivePathB),
            added,
            removed,
            changed,
            identicalCount = identical,
        });
    }

    public static string Pack(IReadOnlyList<string> sourcePaths, string outputBa2, string format, string rootDir, bool compress,
                              bool useArchive2 = false)
    {
        var sources = sourcePaths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim().Trim('"')).ToList();
        if (sources.Count == 0) return ToolError.Fail("Provide at least one source folder to pack.");
        foreach (var s in sources)
            if (!Directory.Exists(s)) return ToolError.Fail($"Source folder not found: {s}");

        if (string.IsNullOrWhiteSpace(outputBa2)) return ToolError.Fail("Provide an output .ba2 path.");
        outputBa2 = outputBa2.Trim().Trim('"');
        if (!outputBa2.EndsWith(".ba2", StringComparison.OrdinalIgnoreCase)) outputBa2 += ".ba2";

        format = string.IsNullOrWhiteSpace(format) ? "General" : format.Trim();
        var dds = string.Equals(format, "DDS", StringComparison.OrdinalIgnoreCase);
        if (!dds && !string.Equals(format, "General", StringComparison.OrdinalIgnoreCase))
            return ToolError.Fail("format must be 'General' (sounds/meshes/scripts/...) or 'DDS' (textures only).");

        if (string.IsNullOrWhiteSpace(rootDir)) return ToolError.Fail("Provide 'root_dir' -- the folder each source's in-archive path is computed relative to.");
        rootDir = rootDir.Trim().Trim('"');
        if (!Directory.Exists(rootDir)) return ToolError.Fail($"Root folder not found: {rootDir}");

        if (!useArchive2)
            return PackInProcess(sources, outputBa2, rootDir, compress, dds ? Ba2Format.DirectX : Ba2Format.General);

        var archive2 = ToolPaths.Archive2();
        if (archive2 == null)
            return ToolError.Fail(
                "Archive2.exe was requested but is not installed. " + ToolPaths.Describe("archive2") + ". " +
                "Leave use_archive2 off to write the archive in process instead.");

        try { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputBa2)) ?? "."); }
        catch (Exception ex) { return ToolError.Fail($"Cannot create output dir for '{outputBa2}': {ex.Message}"); }

        var psi = new ProcessStartInfo { FileName = archive2 };

        psi.ArgumentList.Add(string.Join(",", sources));
        psi.ArgumentList.Add($"-f={format}");
        psi.ArgumentList.Add($"-c={outputBa2}");
        psi.ArgumentList.Add($"-r={rootDir}");
        if (!compress) psi.ArgumentList.Add("-compression=None");

        var run = ProcessRunner.Run(psi, TimeSpan.FromMinutes(10));
        if (!run.Started) return ToolError.Fail("Failed to start Archive2.exe.");
        if (run.TimedOut) return ToolError.Fail("Archive2.exe timed out after 10 minutes (killed).");
        if (run.ExitCode != 0 || !File.Exists(outputBa2))
            return ToolError.Fail($"Archive2.exe failed (exit {run.ExitCode}):\n{run.Combined}");

        var headerVersion = ReadBa2HeaderVersion(outputBa2);
        if (headerVersion is int v && v >= 7)
            return ToolError.Fail(
                $"Archive2.exe at '{archive2}' wrote a version {v} BA2 (Next-Gen format), which this " +
                "tool's Mutagen reader cannot decompress correctly -- archive_extract/archive_list would " +
                "silently return corrupt data from it. Point Archive2Path (Settings, or ARCHIVE2_PATH) " +
                "at an OG-era Archive2.exe (writes header version 1) instead. The output file was left " +
                $"on disk at '{outputBa2}' for inspection but should not be used.");

        var count = run.Combined.Split('\n').Count(l => l.TrimStart().StartsWith("Adding \"", StringComparison.Ordinal));
        return $"RESULT: success ({count} file(s) -> {outputBa2})\n\n{run.Combined}".TrimEnd();
    }

    private static string PackInProcess(List<string> sources, string outputBa2, string rootDir, bool compress, Ba2Format format)
    {
        var full = Path.GetFullPath(rootDir);
        foreach (var s in sources)
        {
            var abs = Path.GetFullPath(s);
            if (!abs.Equals(full, StringComparison.OrdinalIgnoreCase) &&
                !abs.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return ToolError.Fail(
                    $"Source '{s}' is not inside root_dir '{rootDir}', so its in-archive path cannot be " +
                    "computed. Every source folder must live under the root.");
        }

        Ba2Packer.Result result;
        try { result = Ba2Packer.Pack(new[] { full }, outputBa2, version: 1, compress: compress, format: format); }
        catch (Exception ex) { return ToolError.Fail($"Packing failed: {ex.Message}"); }

        return $"RESULT: success ({result.FileCount} file(s) -> {outputBa2})\n" +
               $"{result.TotalBytes:N0} bytes in, {result.ArchiveBytes:N0} bytes out, " +
               $"{result.CompressedCount} compressed, {result.FileCount - result.CompressedCount} stored.";
    }

    private static int? ReadBa2HeaderVersion(string ba2Path)
    {
        try
        {
            using var fs = File.OpenRead(ba2Path);
            Span<byte> header = stackalloc byte[8];
            if (fs.Read(header) < 8) return null;
            if (header[0] != (byte)'B' || header[1] != (byte)'T' || header[2] != (byte)'D' || header[3] != (byte)'X') return null;
            return BitConverter.ToInt32(header.Slice(4, 4));
        }
        catch { return null; }
    }
}
