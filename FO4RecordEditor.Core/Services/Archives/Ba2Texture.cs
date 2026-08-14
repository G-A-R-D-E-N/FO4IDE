using System.IO;

namespace FO4RecordEditor.Services.Archives;

/// <summary>
/// Turns a DDS file into the one <see cref="Ba2Entry"/> a DX10 archive stores for it: the per-file
/// texture header, and the payload split into chunks by mip range. The DDS header itself is NOT
/// stored -- the engine rebuilds it from the entry -- so the chunk bytes are the surface data alone.
///
/// The split rule was derived from vanilla data, not from any writer's source: across all 42,036
/// texture entries in the 37 vanilla DX10 archives, every entry is "the first few mips each in their
/// own chunk, then one chunk holding all the rest", and the count of those leading single-mip chunks
/// is reproduced exactly, 42,036 of 42,036, by
///
///     a mip gets its own chunk while its pixel count is at least 512*512, capped at 3 such chunks
///
/// (so never more than 4 chunks in total, which is also the largest chunk count that occurs). The
/// point of the split is that the engine can load the small mips as one read and stream the big ones.
/// </summary>
public static class Ba2Texture
{
    private const int SplitMinPixels = 512 * 512;
    private const int MaxSingleMipChunks = 3;
    private const byte TileMode = 8;      // every vanilla entry, without exception
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

            // One chunk for everything is what a cube map or an array gets, and there the chunk is
            // the whole payload rather than a mip slice -- see PlanChunks.
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

    /// <summary>
    /// The (mipFirst, mipLast) ranges, in order. Exposed so the corpus check can compare a plan
    /// against what a real archive stores without having to build the whole entry.
    /// </summary>
    public static List<(int First, int Last)> PlanChunks(DdsInfo info)
    {
        var ranges = new List<(int, int)>();

        // A cube map, an array or a volume interleaves surfaces, and nothing in vanilla data pins
        // down the order a multi-surface texture's mip-range chunk uses. Keeping it as one chunk
        // stores the payload in exactly its DDS order, which is a layout that occurs in vanilla
        // (176 entries are single-chunk) and cannot be wrong about an ordering it never assumes.
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
