using System.IO;
using System.Text;

namespace FO4RecordEditor.Services.Archives;










public static class Ba2TextureCorpus
{
    public sealed record Report(
        int Archives,
        int Entries,
        List<string> SizeMismatches,
        List<string> LayoutMismatches,
        List<string> UnknownFormats,
        List<string> Unreadable)
    {
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{Archives} DX10 archive(s), {Entries} texture entries.");
            sb.AppendLine($"  chunk size mismatches:   {SizeMismatches.Count}");
            sb.AppendLine($"  chunk layout mismatches: {LayoutMismatches.Count}");
            sb.AppendLine($"  formats not in the table: {UnknownFormats.Count}");
            sb.AppendLine($"  unreadable archives:     {Unreadable.Count}");
            foreach (var line in SizeMismatches.Take(10)) sb.AppendLine("  SIZE   " + line);
            foreach (var line in LayoutMismatches.Take(10)) sb.AppendLine("  LAYOUT " + line);
            foreach (var line in UnknownFormats.Take(10)) sb.AppendLine("  FORMAT " + line);
            foreach (var line in Unreadable.Take(10)) sb.AppendLine("  READ   " + line);
            return sb.ToString().TrimEnd();
        }
    }

    public static Report Run(string dataFolder)
    {
        int archives = 0, entries = 0;
        var sizes = new List<string>();
        var layouts = new List<string>();
        var unknown = new List<string>();
        var unreadable = new List<string>();
        var seenUnknown = new HashSet<byte>();

        foreach (var path in Directory.EnumerateFiles(dataFolder, "*.ba2").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            Ba2Archive archive;
            try { archive = Ba2Codec.Read(path); }
            catch (Exception ex) { unreadable.Add($"{Path.GetFileName(path)}: {ex.Message}"); continue; }
            if (archive.Format != Ba2Format.DirectX) continue;

            archives++;
            foreach (var entry in archive.Entries)
            {
                entries++;
                var t = entry.Texture!;
                var format = DdsCodec.Lookup(t.DxgiFormat);
                if (format == null)
                {
                    if (seenUnknown.Add(t.DxgiFormat))
                        unknown.Add($"{Path.GetFileName(path)}: DXGI {t.DxgiFormat} ({entry.Path})");
                    continue;
                }

                var isCube = (t.Flags & 1) != 0;
                var info = new DdsInfo(t.Width, t.Height, t.MipCount, isCube, isCube ? 6 : 1, 1, 128, format);

                foreach (var chunk in entry.Chunks)
                {
                    long expected = 0;
                    for (int m = chunk.MipFirst; m <= chunk.MipLast; m++)
                        expected += DdsCodec.MipSize(format, info.Width >> m, info.Height >> m);
                    expected *= info.ArraySize;

                    if (expected != chunk.DecompressedSize)
                        sizes.Add($"{Path.GetFileName(path)} {entry.Path} {info.Width}x{info.Height} " +
                                  $"{format.Name} mips {chunk.MipFirst}-{chunk.MipLast}: stored {chunk.DecompressedSize}, computed {expected}");
                }

                var planned = Ba2Texture.PlanChunks(info);
                var stored = entry.Chunks.Select(c => ((int)c.MipFirst, (int)c.MipLast)).ToList();
                if (!planned.SequenceEqual(stored))
                    layouts.Add($"{Path.GetFileName(path)} {entry.Path} {info.Width}x{info.Height} mips={info.MipCount}: " +
                                $"stored [{Describe(stored)}], planned [{Describe(planned)}]");
            }
        }

        return new Report(archives, entries, sizes, layouts, unknown, unreadable);
    }

    public sealed record RoundTripReport(int Entries, int Rebuilt, int MultiSurface, List<string> Problems)
    {
        public override string ToString()
            => $"{Entries} entries: {Rebuilt} rebuilt identically, {MultiSurface} multi-surface (single-chunk by design), " +
               $"{Problems.Count} problem(s)" +
               (Problems.Count == 0 ? "" : "\n  " + string.Join("\n  ", Problems.Take(10)));
    }










    public static RoundTripReport RoundTrip(string archivePath, int limit = int.MaxValue)
    {
        var archive = Ba2Codec.Read(archivePath);
        if (archive.Format != Ba2Format.DirectX)
            throw new InvalidDataException($"{Path.GetFileName(archivePath)} is not a texture archive.");

        int entries = 0, rebuilt = 0, multiSurface = 0;
        var problems = new List<string>();

        foreach (var original in archive.Entries)
        {
            if (entries >= limit) break;
            entries++;

            var t = original.Texture!;
            var format = DdsCodec.Lookup(t.DxgiFormat);
            if (format == null) { problems.Add($"{original.Path}: DXGI {t.DxgiFormat} not in the table"); continue; }

            var isCube = (t.Flags & 1) != 0;
            var info = new DdsInfo(t.Width, t.Height, t.MipCount, isCube, isCube ? 6 : 1, 1, 148, format);
            if (info.ArraySize > 1) { multiSurface++; continue; }

            var payload = original.Chunks.SelectMany(Ba2Codec.Decompress).ToArray();
            var dds = DdsCodec.BuildHeader(info).Concat(payload).ToArray();

            Ba2Entry made;
            try { made = Ba2Texture.Build(original.Path, dds, compress: false); }
            catch (Exception ex) { problems.Add($"{original.Path}: {ex.Message}"); continue; }

            if (!Equals(made.Texture, original.Texture))
            {
                problems.Add($"{original.Path}: header {made.Texture} != stored {original.Texture}");
                continue;
            }

            var madeRanges = made.Chunks.Select(c => (c.MipFirst, c.MipLast)).ToList();
            var storedRanges = original.Chunks.Select(c => (c.MipFirst, c.MipLast)).ToList();
            if (!madeRanges.SequenceEqual(storedRanges))
            {
                problems.Add($"{original.Path}: chunks [{Describe(madeRanges.Select(r => ((int)r.MipFirst, (int)r.MipLast)))}] " +
                             $"!= stored [{Describe(storedRanges.Select(r => ((int)r.MipFirst, (int)r.MipLast)))}]");
                continue;
            }

            if (!made.Chunks.SelectMany(c => c.Data).SequenceEqual(payload))
            {
                problems.Add($"{original.Path}: payload bytes differ");
                continue;
            }

            rebuilt++;
        }

        return new RoundTripReport(entries, rebuilt, multiSurface, problems);
    }






    public static (int Parsed, int Exact, List<string> Problems) CheckLooseTextures(string folder, int limit = int.MaxValue)
    {
        int parsed = 0, exact = 0;
        var problems = new List<string>();

        foreach (var file in Directory.EnumerateFiles(folder, "*.dds", SearchOption.AllDirectories))
        {
            if (parsed >= limit) break;
            byte[] bytes;
            try { bytes = File.ReadAllBytes(file); }
            catch (Exception ex) { problems.Add($"{file}: {ex.Message}"); continue; }

            DdsInfo info;
            try { info = DdsCodec.Parse(bytes, Path.GetFileName(file)); }
            catch (Exception ex) { problems.Add($"{Path.GetFileName(file)}: {ex.Message}"); parsed++; continue; }

            parsed++;
            var expected = info.DataOffset + info.PayloadSize;
            if (expected == bytes.Length) exact++;
            else problems.Add($"{Path.GetFileName(file)}: {info.Format.Name} {info.Width}x{info.Height} " +
                              $"mips={info.MipCount} arrays={info.ArraySize} -> expected {expected} bytes, file is {bytes.Length}");
        }

        return (parsed, exact, problems);
    }

    private static string Describe(IEnumerable<(int First, int Last)> ranges)
        => string.Join(",", ranges.Select(r => $"{r.First}-{r.Last}"));
}
