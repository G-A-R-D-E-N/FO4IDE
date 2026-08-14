using System.IO;

namespace FO4RecordEditor.Services.Archives;

/// <summary>
/// Build a BA2 from loose folders, in process. This is what removes the Creation Kit's
/// Archive2.exe from the packing path: that binary is not redistributable, so it could never be
/// bundled, and a user without the CK installed simply could not pack.
///
/// Both container forms: General (GNRL) for anything, and DirectX (DX10) for a texture archive,
/// where each entry carries a per-file texture header and the payload is split by mip range. See
/// <see cref="Ba2Texture"/> for where those numbers come from.
/// </summary>
public static class Ba2Packer
{
    public sealed record Result(int FileCount, long TotalBytes, long ArchiveBytes, int CompressedCount);

    /// <summary>
    /// Pack every file under <paramref name="sourceDirs"/> into <paramref name="outputPath"/>. Each
    /// entry's in-archive name is its path relative to its own source folder, so a source folder
    /// should be the Data-equivalent root ("...\MyMod"), the same contract Archive2 has.
    ///
    /// A DirectX archive holds textures only, so a non-DDS file under the source folder is an error
    /// rather than something to quietly skip: the file would simply be missing from the build.
    /// </summary>
    public static Result Pack(IEnumerable<string> sourceDirs, string outputPath, uint version = 1, bool compress = true,
                              Ba2Format format = Ba2Format.General)
    {
        var entries = new List<Ba2Entry>();
        long totalBytes = 0;
        int compressedCount = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dirRaw in sourceDirs)
        {
            var dir = Path.GetFullPath(dirRaw);
            if (!Directory.Exists(dir)) throw new DirectoryNotFoundException($"Source folder not found: {dir}");

            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var rel = Path.GetRelativePath(dir, file).Replace('/', '\\');
                if (!seen.Add(rel))
                    throw new InvalidDataException($"'{rel}' appears in more than one source folder; an archive cannot hold the same path twice.");

                var bytes = File.ReadAllBytes(file);
                totalBytes += bytes.Length;

                if (format == Ba2Format.DirectX)
                {
                    if (!rel.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException(
                            $"'{rel}' is not a .dds. A texture archive holds textures only -- pack this as a General archive, " +
                            "or move the file out of the source folder.");

                    var texEntry = Ba2Texture.Build(rel, bytes, compress);
                    compressedCount += texEntry.Chunks.Count(c => c.Compressed);
                    entries.Add(texEntry);
                    continue;
                }

                var chunk = compress ? Ba2Codec.Compress(bytes) : Ba2Chunk.Stored(bytes);
                if (chunk.Compressed) compressedCount++;

                var (name, ext, parent) = Ba2Hash.Compute(rel);
                entries.Add(new Ba2Entry
                {
                    Path = rel,
                    Chunks = new List<Ba2Chunk> { chunk },
                    NameHash = name,
                    ExtensionHash = ext,
                    DirectoryHash = parent,
                });
            }
        }

        if (entries.Count == 0) throw new InvalidDataException("Nothing to pack: no files found under the source folder(s).");

        var archive = new Ba2Archive { Version = version, Format = format, Entries = entries };

        var full = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        Ba2Codec.Write(archive, full);

        return new Result(entries.Count, totalBytes, new FileInfo(full).Length, compressedCount);
    }
}
