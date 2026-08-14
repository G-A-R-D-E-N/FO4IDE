using System.IO;
using System.IO.Compression;

namespace FO4RecordEditor.Services.Textures;

/// <summary>
/// Writes 8-bit RGBA to PNG. Small on purpose: the viewport's contract is a PNG data URL, and
/// pulling in an imaging package to produce one 8-bit RGBA image would be the larger change.
/// System.Drawing is not an option here either -- this assembly is plain net8.0 so the Godot
/// project can reference it.
/// </summary>
public static class PngWriter
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    public static byte[] Write(byte[] rgba, int width, int height, CompressionLevel level = CompressionLevel.Fastest)
    {
        if ((long)width * height * 4 != rgba.Length)
            throw new ArgumentException($"{width}x{height} RGBA is {(long)width * height * 4} bytes, got {rgba.Length}.");

        using var output = new MemoryStream();
        output.Write(Signature);

        Span<byte> ihdr = stackalloc byte[13];
        WriteBigEndian(ihdr, 0, (uint)width);
        WriteBigEndian(ihdr, 4, (uint)height);
        ihdr[8] = 8;    // bit depth
        ihdr[9] = 6;    // colour type: truecolour with alpha
        ihdr[10] = 0;   // deflate
        ihdr[11] = 0;   // adaptive filtering
        ihdr[12] = 0;   // no interlace
        WriteChunk(output, "IHDR", ihdr.ToArray());

        WriteChunk(output, "IDAT", Deflate(rgba, width, height, level));
        WriteChunk(output, "IEND", Array.Empty<byte>());

        return output.ToArray();
    }

    // Every scanline gets filter type 0 (None) in front of it. Filtering would compress better but
    // costs a pass over the whole image, and these are throwaway previews, not shipped assets.
    private static byte[] Deflate(byte[] rgba, int width, int height, CompressionLevel level)
    {
        var stride = width * 4;
        using var raw = new MemoryStream();
        using (var zlib = new ZLibStream(raw, level, leaveOpen: true))
        {
            var filterByte = new byte[1];
            for (int y = 0; y < height; y++)
            {
                zlib.Write(filterByte, 0, 1);
                zlib.Write(rgba, y * stride, stride);
            }
        }
        return raw.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        WriteBigEndian(length, 0, (uint)data.Length);
        output.Write(length);

        var typeBytes = new byte[4];
        for (int i = 0; i < 4; i++) typeBytes[i] = (byte)type[i];
        output.Write(typeBytes);
        output.Write(data);

        var crc = Crc32(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        WriteBigEndian(crcBytes, 0, crc);
        output.Write(crcBytes);
    }

    private static void WriteBigEndian(Span<byte> dst, int at, uint value)
    {
        dst[at] = (byte)(value >> 24);
        dst[at + 1] = (byte)(value >> 16);
        dst[at + 2] = (byte)(value >> 8);
        dst[at + 3] = (byte)value;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }

    private static uint Crc32(byte[] a, byte[] b)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var x in a) crc = CrcTable[(crc ^ x) & 0xFF] ^ (crc >> 8);
        foreach (var x in b) crc = CrcTable[(crc ^ x) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFF;
    }
}
