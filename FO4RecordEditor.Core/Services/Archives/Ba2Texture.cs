using System.IO;

namespace FO4RecordEditor.Services.Archives;
















public static class Ba2Texture
{
    private const int SplitMinPixels = 512 * 512;
    private const int MaxSingleMipChunks = 3;
    private const byte TileMode = 8;
    private const byte FlagCubeMap = 1;

    public static Ba2Entry Build(string relPath, byte[] ddsBytes, bool compress)
    {
        var info = DdsCodec.Parse(ddsBytes, relPath);

        var payload = ddsBytes.Length - info.DataOffset;
        if (payload < info.PayloadSize)
            throw new InvalidDataException(
                $"'{relPath}': header describes {info.PayloadSize} bytes of {info.Format.Name} " +
                $"{info.Width}x{info.Height} with {info.MipCount} mips, but only {payload} bytes follow it.");

        var chunks = new List<Ba2Chunk>();
        foreach (var (first, last) in PlanChunks(info))
        {
            long start = info.DataOffset;
            for (int m = 0; m < first; m++) start += DdsCodec.MipSize(info.Format, info.Width >> m, info.Height >> m);

            long length = 0;
            for (int m = first; m <= last; m++) length += DdsCodec.MipSize(info.Format, info.Width >> m, info.Height >> m);



            if (chunks.Count == 0 && first == 0 && last == info.MipCount - 1 && info.ArraySize * Math.Max(1, info.Depth) > 1)
                length = info.PayloadSize;

            var slice = new byte[length];
            Array.Copy(ddsBytes, start, slice, 0, length);

            var chunk = compress ? Ba2Codec.Compress(slice) : Ba2Chunk.Stored(slice);
            chunks.Add(chunk with { MipFirst = (ushort)first, MipLast = (ushort)last });
        }

        var (name, ext, parent) = Ba2Hash.Compute(relPath);
        return new Ba2Entry
        {
            Path = relPath,
            Chunks = chunks,
            Texture = new Ba2TextureInfo(
                (ushort)info.Height,
                (ushort)info.Width,
                (byte)info.MipCount,
                info.DxgiFormat,
                info.IsCubeMap ? FlagCubeMap : (byte)0,
                TileMode),
            NameHash = name,
            ExtensionHash = ext,
            DirectoryHash = parent,
        };
    }





    public static List<(int First, int Last)> PlanChunks(DdsInfo info)
    {
        var ranges = new List<(int, int)>();





        if (info.ArraySize * Math.Max(1, info.Depth) > 1)
        {
            ranges.Add((0, info.MipCount - 1));
            return ranges;
        }

        int singles = 0;
        while (singles < info.MipCount - 1
               && singles < MaxSingleMipChunks
               && (long)Math.Max(1, info.Width >> singles) * Math.Max(1, info.Height >> singles) >= SplitMinPixels)
        {
            ranges.Add((singles, singles));
            singles++;
        }

        ranges.Add((singles, info.MipCount - 1));
        return ranges;
    }
}
