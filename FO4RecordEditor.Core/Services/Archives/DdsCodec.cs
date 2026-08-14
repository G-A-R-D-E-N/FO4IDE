using System.IO;

namespace FO4RecordEditor.Services.Archives;




public sealed record DdsFormatInfo(byte Dxgi, string Name, int BlockBytes, int PixelBytes)
{
    public bool IsBlockCompressed => BlockBytes > 0;
}





public sealed record DdsInfo(
    int Width,
    int Height,
    int MipCount,
    bool IsCubeMap,
    int ArraySize,
    int Depth,
    int DataOffset,
    DdsFormatInfo Format)
{
    public byte DxgiFormat => Format.Dxgi;


    public long PayloadSize
    {
        get
        {
            long one = 0;
            for (int m = 0; m < MipCount; m++) one += DdsCodec.MipSize(Format, Width >> m, Height >> m);
            return one * ArraySize * Math.Max(1, Depth);
        }
    }
}













public static class DdsCodec
{
    private const uint DdsMagic = 0x20534444;
    private const uint Dx10FourCc = 0x30315844;

    private const uint DdpfFourCc = 0x4;
    private const uint DdpfRgb = 0x40;
    private const uint DdpfLuminance = 0x20000;
    private const uint DdpfAlphaOnly = 0x2;

    private const uint Caps2CubeMap = 0x200;
    private const uint Caps2Volume = 0x200000;

    private const int LegacyHeaderSize = 128;
    private const int Dx10HeaderSize = 148;




    private static readonly DdsFormatInfo[] Formats =
    {
        new(70, "BC1_TYPELESS", 8, 0),
        new(71, "BC1_UNORM", 8, 0),
        new(72, "BC1_UNORM_SRGB", 8, 0),
        new(73, "BC2_TYPELESS", 16, 0),
        new(74, "BC2_UNORM", 16, 0),
        new(75, "BC2_UNORM_SRGB", 16, 0),
        new(76, "BC3_TYPELESS", 16, 0),
        new(77, "BC3_UNORM", 16, 0),
        new(78, "BC3_UNORM_SRGB", 16, 0),
        new(79, "BC4_TYPELESS", 8, 0),
        new(80, "BC4_UNORM", 8, 0),
        new(81, "BC4_SNORM", 8, 0),
        new(82, "BC5_TYPELESS", 16, 0),
        new(83, "BC5_UNORM", 16, 0),
        new(84, "BC5_SNORM", 16, 0),
        new(94, "BC6H_TYPELESS", 16, 0),
        new(95, "BC6H_UF16", 16, 0),
        new(96, "BC6H_SF16", 16, 0),
        new(97, "BC7_TYPELESS", 16, 0),
        new(98, "BC7_UNORM", 16, 0),
        new(99, "BC7_UNORM_SRGB", 16, 0),
        new(2, "R32G32B32A32_FLOAT", 0, 16),
        new(10, "R16G16B16A16_FLOAT", 0, 8),
        new(24, "R10G10B10A2_UNORM", 0, 4),
        new(28, "R8G8B8A8_UNORM", 0, 4),
        new(29, "R8G8B8A8_UNORM_SRGB", 0, 4),
        new(34, "R16G16_UNORM", 0, 4),
        new(41, "R32_FLOAT", 0, 4),
        new(49, "R8G8_UNORM", 0, 2),
        new(54, "R16_FLOAT", 0, 2),
        new(56, "R16_UNORM", 0, 2),
        new(61, "R8_UNORM", 0, 1),
        new(65, "A8_UNORM", 0, 1),
        new(85, "B5G6R5_UNORM", 0, 2),
        new(86, "B5G5R5A1_UNORM", 0, 2),
        new(87, "B8G8R8A8_UNORM", 0, 4),
        new(88, "B8G8R8X8_UNORM", 0, 4),
        new(91, "B8G8R8A8_UNORM_SRGB", 0, 4),
        new(93, "B8G8R8X8_UNORM_SRGB", 0, 4),
        new(115, "B4G4R4A4_UNORM", 0, 2),
    };

    private static readonly Dictionary<byte, DdsFormatInfo> ByDxgi = Formats.ToDictionary(f => f.Dxgi);


    public static DdsFormatInfo? Lookup(byte dxgi) => ByDxgi.TryGetValue(dxgi, out var f) ? f : null;



    public static long MipSize(DdsFormatInfo format, int width, int height)
    {
        var w = Math.Max(1, width);
        var h = Math.Max(1, height);
        if (!format.IsBlockCompressed) return (long)w * h * format.PixelBytes;
        return (long)((w + 3) / 4) * ((h + 3) / 4) * format.BlockBytes;
    }


    public static long MipSize(byte dxgi, int width, int height)
        => MipSize(Lookup(dxgi) ?? throw new InvalidDataException($"DXGI format {dxgi} is not one this tool can size."),
                   width, height);

    public static DdsInfo Parse(string path) => Parse(File.ReadAllBytes(path), path);






    public static DdsInfo Parse(ReadOnlySpan<byte> bytes, string what = "texture")
    {
        if (bytes.Length < LegacyHeaderSize)
            throw new InvalidDataException($"{what}: {bytes.Length} bytes is too short to be a DDS.");
        if (U32(bytes, 0) != DdsMagic)
            throw new InvalidDataException($"{what}: not a DDS (no 'DDS ' magic).");
        if (U32(bytes, 4) != 124)
            throw new InvalidDataException($"{what}: DDS_HEADER size is {U32(bytes, 4)}, expected 124.");

        var height = (int)U32(bytes, 12);
        var width = (int)U32(bytes, 16);
        var depth = (int)U32(bytes, 24);
        var mipCount = (int)U32(bytes, 28);
        var caps2 = U32(bytes, 112);


        var pfFlags = U32(bytes, 80);
        var fourCc = U32(bytes, 84);
        var rgbBits = U32(bytes, 88);
        var rMask = U32(bytes, 92);
        var gMask = U32(bytes, 96);
        var bMask = U32(bytes, 100);
        var aMask = U32(bytes, 104);

        var isCube = (caps2 & Caps2CubeMap) != 0;
        var isVolume = (caps2 & Caps2Volume) != 0;

        int dataOffset;
        byte dxgi;
        int arraySize = 1;

        if ((pfFlags & DdpfFourCc) != 0 && fourCc == Dx10FourCc)
        {
            if (bytes.Length < Dx10HeaderSize)
                throw new InvalidDataException($"{what}: header claims a DX10 extension but the file ends before it.");
            var raw = U32(bytes, 128);
            if (raw > byte.MaxValue)
                throw new InvalidDataException($"{what}: DXGI format {raw} does not fit the one byte a BA2 entry stores.");
            dxgi = (byte)raw;
            var miscFlag = U32(bytes, 136);
            arraySize = (int)Math.Max(1, U32(bytes, 140));
            if ((miscFlag & 0x4) != 0) isCube = true;
            dataOffset = Dx10HeaderSize;
        }
        else
        {
            dxgi = LegacyToDxgi(pfFlags, fourCc, rgbBits, rMask, gMask, bMask, aMask, what);
            dataOffset = LegacyHeaderSize;
        }

        if (isCube) arraySize *= 6;

        var format = Lookup(dxgi)
            ?? throw new InvalidDataException($"{what}: DXGI format {dxgi} is not one this tool can size, so it cannot be packed into a texture archive.");

        if (width <= 0 || height <= 0)
            throw new InvalidDataException($"{what}: header says {width}x{height}.");
        if (width > ushort.MaxValue || height > ushort.MaxValue)
            throw new InvalidDataException($"{what}: {width}x{height} exceeds the 16-bit width/height a BA2 entry stores.");
        if (mipCount <= 0) mipCount = 1;
        if (mipCount > byte.MaxValue)
            throw new InvalidDataException($"{what}: {mipCount} mips exceeds the one byte a BA2 entry stores.");

        return new DdsInfo(width, height, mipCount, isCube, arraySize, isVolume ? Math.Max(1, depth) : 1, dataOffset, format);
    }







    public static byte[] BuildHeader(DdsInfo info)
    {
        var bytes = new byte[Dx10HeaderSize];
        void U32(int offset, uint v) => BitConverter.GetBytes(v).CopyTo(bytes, offset);

        var linearSize = (uint)MipSize(info.Format, info.Width, info.Height);

        U32(0, DdsMagic);
        U32(4, 124);

        U32(8, 0x1 | 0x2 | 0x4 | 0x1000 | 0x20000 | 0x80000);
        U32(12, (uint)info.Height);
        U32(16, (uint)info.Width);
        U32(20, linearSize);
        U32(24, (uint)Math.Max(1, info.Depth));
        U32(28, (uint)info.MipCount);
        U32(76, 32);
        U32(80, DdpfFourCc);
        U32(84, Dx10FourCc);
        U32(108, 0x1000 | (info.MipCount > 1 ? 0x400008u : 0u));
        if (info.IsCubeMap) U32(112, Caps2CubeMap | 0xFC00);
        U32(128, info.DxgiFormat);
        U32(132, 3);
        if (info.IsCubeMap) U32(136, 0x4);
        U32(140, (uint)Math.Max(1, info.IsCubeMap ? info.ArraySize / 6 : info.ArraySize));
        return bytes;
    }

    private static byte LegacyToDxgi(uint pfFlags, uint fourCc, uint rgbBits, uint r, uint g, uint b, uint a, string what)
    {
        if ((pfFlags & DdpfFourCc) != 0)
        {
            return fourCc switch
            {
                0x31545844 => 71,
                0x32545844 => 74,
                0x33545844 => 74,
                0x34545844 => 77,
                0x35545844 => 77,
                0x31495441 => 80,
                0x55344342 => 80,
                0x53344342 => 81,
                0x32495441 => 83,
                0x55354342 => 83,
                0x53354342 => 84,
                0x00000024 => 10,
                0x00000074 => 2,
                _ => throw new InvalidDataException($"{what}: unsupported DDS FourCC 0x{fourCc:X8}.")
            };
        }

        if ((pfFlags & DdpfRgb) != 0)
        {
            switch (rgbBits)
            {
                case 32:
                    if (r == 0x00FF0000 && g == 0x0000FF00 && b == 0x000000FF) return a == 0xFF000000u ? (byte)87 : (byte)88;
                    if (r == 0x000000FF && g == 0x0000FF00 && b == 0x00FF0000) return 28;
                    if (r == 0x3FF00000 && g == 0x000FFC00 && b == 0x000003FF) return 24;
                    break;
                case 16:
                    if (r == 0xF800 && g == 0x07E0 && b == 0x001F) return 85;
                    if (r == 0x7C00 && g == 0x03E0 && b == 0x001F) return 86;
                    if (r == 0x0F00 && g == 0x00F0 && b == 0x000F) return 115;
                    break;
            }
            throw new InvalidDataException($"{what}: unsupported uncompressed DDS layout ({rgbBits}bpp, masks {r:X8}/{g:X8}/{b:X8}/{a:X8}).");
        }

        if ((pfFlags & DdpfLuminance) != 0 && rgbBits == 8) return 61;
        if ((pfFlags & DdpfAlphaOnly) != 0 && rgbBits == 8) return 65;

        throw new InvalidDataException($"{what}: DDS pixel format flags 0x{pfFlags:X8} carry no format this tool recognises.");
    }

    private static uint U32(ReadOnlySpan<byte> b, int offset) => BitConverter.ToUInt32(b.Slice(offset, 4));
}
