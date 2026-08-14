using System.IO;

namespace FO4RecordEditor.Services.Textures;

public static class BcnDecoder
{

    public static bool CanDecode(byte dxgi) => dxgi switch
    {
        70 or 71 or 72 => true,
        73 or 74 or 75 => true,
        76 or 77 or 78 => true,
        79 or 80 => true,
        82 or 83 => true,
        97 or 98 or 99 => true,
        _ => false,
    };

    public static void DecodeBlockFormat(ReadOnlySpan<byte> surface, byte dxgi, int width, int height, byte[] rgba)
    {
        var blockBytes = dxgi is 70 or 71 or 72 or 79 or 80 ? 8 : 16;
        var blocksX = (width + 3) / 4;
        var blocksY = (height + 3) / 4;
        var needed = (long)blocksX * blocksY * blockBytes;
        if (surface.Length < needed)
            throw new InvalidDataException($"Surface is {surface.Length} bytes; {width}x{height} needs {needed}.");

        Span<byte> texels = stackalloc byte[64];

        for (int by = 0; by < blocksY; by++)
        for (int bx = 0; bx < blocksX; bx++)
        {
            var block = surface.Slice((by * blocksX + bx) * blockBytes, blockBytes);
            switch (dxgi)
            {
                case 70 or 71 or 72: ColorBlock(block, texels, onlyOpaque: false); break;
                case 73 or 74 or 75: Bc2(block, texels); break;
                case 76 or 77 or 78: Bc3(block, texels); break;
                case 79 or 80: Bc4(block, texels); break;
                case 82 or 83: Bc5(block, texels); break;
                default: Bc7(block, texels); break;
            }

            for (int y = 0; y < 4; y++)
            {
                var py = by * 4 + y;
                if (py >= height) break;
                for (int x = 0; x < 4; x++)
                {
                    var px = bx * 4 + x;
                    if (px >= width) break;
                    texels.Slice((y * 4 + x) * 4, 4).CopyTo(rgba.AsSpan((py * width + px) * 4, 4));
                }
            }
        }
    }

    private static void ColorBlock(ReadOnlySpan<byte> block, Span<byte> texels, bool onlyOpaque)
    {
        int c0 = block[0] | (block[1] << 8);
        int c1 = block[2] | (block[3] << 8);

        int r0 = (c0 >> 11) & 0x1F, g0 = (c0 >> 5) & 0x3F, b0 = c0 & 0x1F;
        int r1 = (c1 >> 11) & 0x1F, g1 = (c1 >> 5) & 0x3F, b1 = c1 & 0x1F;

        Span<byte> refs = stackalloc byte[16];
        Set(refs, 0, (r0 * 527 + 23) >> 6, (g0 * 259 + 33) >> 6, (b0 * 527 + 23) >> 6, 255);
        Set(refs, 1, (r1 * 527 + 23) >> 6, (g1 * 259 + 33) >> 6, (b1 * 527 + 23) >> 6, 255);

        if (c0 > c1 || onlyOpaque)
        {

            Set(refs, 2, ((2 * r0 + r1) * 351 + 61) >> 7, ((2 * g0 + g1) * 2763 + 1039) >> 11, ((2 * b0 + b1) * 351 + 61) >> 7, 255);
            Set(refs, 3, ((r0 + 2 * r1) * 351 + 61) >> 7, ((g0 + 2 * g1) * 2763 + 1039) >> 11, ((b0 + 2 * b1) * 351 + 61) >> 7, 255);
        }
        else
        {

            Set(refs, 2, ((r0 + r1) * 1053 + 125) >> 8, ((g0 + g1) * 4145 + 1019) >> 11, ((b0 + b1) * 1053 + 125) >> 8, 255);
            Set(refs, 3, 0, 0, 0, 0);
        }

        uint indices = (uint)(block[4] | (block[5] << 8) | (block[6] << 16) | (block[7] << 24));
        for (int i = 0; i < 16; i++)
        {
            var idx = (int)(indices & 3);
            indices >>= 2;
            refs.Slice(idx * 4, 4).CopyTo(texels.Slice(i * 4, 4));
        }
    }

    private static void Set(Span<byte> dst, int slot, int r, int g, int b, int a)
    {
        dst[slot * 4] = (byte)r;
        dst[slot * 4 + 1] = (byte)g;
        dst[slot * 4 + 2] = (byte)b;
        dst[slot * 4 + 3] = (byte)a;
    }

    private static void Bc2(ReadOnlySpan<byte> block, Span<byte> texels)
    {
        ColorBlock(block.Slice(8, 8), texels, onlyOpaque: true);
        for (int i = 0; i < 16; i++)
            texels[i * 4 + 3] = (byte)(((block[i >> 1] >> ((i & 1) * 4)) & 0xF) * 17);
    }

    private static void Bc3(ReadOnlySpan<byte> block, Span<byte> texels)
    {
        ColorBlock(block.Slice(8, 8), texels, onlyOpaque: true);
        Span<byte> alpha = stackalloc byte[16];
        SmoothAlpha(block.Slice(0, 8), alpha);
        for (int i = 0; i < 16; i++) texels[i * 4 + 3] = alpha[i];
    }

    private static void Bc4(ReadOnlySpan<byte> block, Span<byte> texels)
    {

        Span<byte> red = stackalloc byte[16];
        SmoothAlpha(block, red);
        for (int i = 0; i < 16; i++) Set(texels, i, red[i], red[i], red[i], 255);
    }

    private static void Bc5(ReadOnlySpan<byte> block, Span<byte> texels)
    {
        Span<byte> red = stackalloc byte[16];
        Span<byte> green = stackalloc byte[16];
        SmoothAlpha(block.Slice(0, 8), red);
        SmoothAlpha(block.Slice(8, 8), green);
        for (int i = 0; i < 16; i++) Set(texels, i, red[i], green[i], 0, 255);
    }

    private static void SmoothAlpha(ReadOnlySpan<byte> block, Span<byte> values)
    {
        Span<byte> palette = stackalloc byte[8];
        palette[0] = block[0];
        palette[1] = block[1];

        if (palette[0] > palette[1])
            for (int i = 1; i < 7; i++)
                palette[i + 1] = (byte)(((7 - i) * palette[0] + i * palette[1] + 3) / 7);
        else
        {
            for (int i = 1; i < 5; i++)
                palette[i + 1] = (byte)(((5 - i) * palette[0] + i * palette[1] + 2) / 5);
            palette[6] = 0x00;
            palette[7] = 0xFF;
        }

        ulong indices = 0;
        for (int i = 0; i < 6; i++) indices |= (ulong)block[2 + i] << (8 * i);
        for (int i = 0; i < 16; i++) values[i] = palette[(int)((indices >> (3 * i)) & 7)];
    }

    private static readonly int[] ColorBitsPerMode = { 4, 6, 5, 7, 5, 7, 7, 5 };
    private static readonly int[] AlphaBitsPerMode = { 0, 0, 0, 0, 6, 8, 7, 5 };
    private const int ModesWithPBits = 0b11001011;

    private static readonly int[] Weight2 = { 0, 21, 43, 64 };
    private static readonly int[] Weight3 = { 0, 9, 18, 27, 37, 46, 55, 64 };
    private static readonly int[] Weight4 = { 0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64 };

    private static readonly byte[] Partitions2 = ParseTable(Partitions2Hex);
    private static readonly byte[] Partitions3 = ParseTable(Partitions3Hex);

    private static void Bc7(ReadOnlySpan<byte> block, Span<byte> texels)
    {
        var bits = new BitStream(block);

        int mode = 0;
        while (mode < 8 && bits.ReadBit() == 0) mode++;
        if (mode >= 8)
        {

            texels.Clear();
            return;
        }

        int partition = 0, subsets = 1, rotation = 0, indexSelectionBit = 0;

        if (mode is 0 or 1 or 2 or 3 or 7)
        {
            subsets = mode is 0 or 2 ? 3 : 2;
            partition = bits.Read(mode == 0 ? 4 : 6);
        }
        if (mode is 4 or 5)
        {
            rotation = bits.Read(2);
            if (mode == 4) indexSelectionBit = bits.ReadBit();
        }

        var endpointCount = subsets * 2;
        var colorBits = ColorBitsPerMode[mode];
        var alphaBits = AlphaBitsPerMode[mode];
        var hasPBits = (ModesWithPBits & (1 << mode)) != 0;

        var endpoints = new int[6, 4];
        for (int c = 0; c < 3; c++)
            for (int e = 0; e < endpointCount; e++)
                endpoints[e, c] = bits.Read(colorBits);
        if (alphaBits > 0)
            for (int e = 0; e < endpointCount; e++)
                endpoints[e, 3] = bits.Read(alphaBits);

        if (hasPBits)
        {
            for (int e = 0; e < endpointCount; e++)
                for (int c = 0; c < 4; c++)
                    endpoints[e, c] <<= 1;

            if (mode == 1)
            {

                var p0 = bits.ReadBit();
                var p1 = bits.ReadBit();
                for (int c = 0; c < 3; c++)
                {
                    endpoints[0, c] |= p0;
                    endpoints[1, c] |= p0;
                    endpoints[2, c] |= p1;
                    endpoints[3, c] |= p1;
                }
            }
            else
            {
                for (int e = 0; e < endpointCount; e++)
                {
                    var p = bits.ReadBit();
                    for (int c = 0; c < 4; c++) endpoints[e, c] |= p;
                }
            }
        }

        var pBit = hasPBits ? 1 : 0;
        for (int e = 0; e < endpointCount; e++)
        {

            var precision = colorBits + pBit;
            for (int c = 0; c < 3; c++)
            {
                endpoints[e, c] <<= 8 - precision;
                endpoints[e, c] |= endpoints[e, c] >> precision;
            }

            var alphaPrecision = alphaBits + pBit;
            endpoints[e, 3] <<= 8 - alphaPrecision;
            endpoints[e, 3] |= endpoints[e, 3] >> alphaPrecision;
        }

        if (alphaBits == 0)
            for (int e = 0; e < endpointCount; e++) endpoints[e, 3] = 0xFF;

        var indexBits = mode is 0 or 1 ? 3 : mode == 6 ? 4 : 2;
        var indexBits2 = mode == 4 ? 3 : mode == 5 ? 2 : 0;
        var weights = indexBits == 2 ? Weight2 : indexBits == 3 ? Weight3 : Weight4;
        var weights2 = indexBits2 == 2 ? Weight2 : Weight3;

        Span<int> indices = stackalloc int[16];
        for (int i = 0; i < 16; i++)
        {
            var set = PartitionSet(subsets, partition, i);

            indices[i] = bits.Read((set & 0x80) != 0 ? indexBits - 1 : indexBits);
        }

        for (int i = 0; i < 16; i++)
        {
            var set = PartitionSet(subsets, partition, i) & 0x03;
            var e0 = set * 2;
            var e1 = set * 2 + 1;
            var index = indices[i];

            int r, g, b, a;
            if (indexBits2 == 0)
            {
                r = Interpolate(endpoints[e0, 0], endpoints[e1, 0], weights, index);
                g = Interpolate(endpoints[e0, 1], endpoints[e1, 1], weights, index);
                b = Interpolate(endpoints[e0, 2], endpoints[e1, 2], weights, index);
                a = Interpolate(endpoints[e0, 3], endpoints[e1, 3], weights, index);
            }
            else
            {
                var index2 = bits.Read(i == 0 ? indexBits2 - 1 : indexBits2);
                if (indexSelectionBit == 0)
                {
                    r = Interpolate(endpoints[e0, 0], endpoints[e1, 0], weights, index);
                    g = Interpolate(endpoints[e0, 1], endpoints[e1, 1], weights, index);
                    b = Interpolate(endpoints[e0, 2], endpoints[e1, 2], weights, index);
                    a = Interpolate(endpoints[e0, 3], endpoints[e1, 3], weights2, index2);
                }
                else
                {
                    r = Interpolate(endpoints[e0, 0], endpoints[e1, 0], weights2, index2);
                    g = Interpolate(endpoints[e0, 1], endpoints[e1, 1], weights2, index2);
                    b = Interpolate(endpoints[e0, 2], endpoints[e1, 2], weights2, index2);
                    a = Interpolate(endpoints[e0, 3], endpoints[e1, 3], weights, index);
                }
            }

            switch (rotation)
            {
                case 1: (a, r) = (r, a); break;
                case 2: (a, g) = (g, a); break;
                case 3: (a, b) = (b, a); break;
            }

            Set(texels, i, r, g, b, a);
        }
    }

    private static byte PartitionSet(int subsets, int partition, int texel)
        => subsets == 1 ? (byte)(texel == 0 ? 0x80 : 0)
         : subsets == 2 ? Partitions2[partition * 16 + texel]
         : Partitions3[partition * 16 + texel];

    private static int Interpolate(int a, int b, int[] weights, int index)
        => (a * (64 - weights[index]) + b * weights[index] + 32) >> 6;

    private static byte[] ParseTable(string hex)
    {
        var table = new byte[64 * 16];
        for (int i = 0; i < table.Length; i++)
            table[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return table;
    }

    private ref struct BitStream
    {
        private ulong _low;
        private ulong _high;

        public BitStream(ReadOnlySpan<byte> block)
        {
            _low = BitConverter.ToUInt64(block.Slice(0, 8));
            _high = BitConverter.ToUInt64(block.Slice(8, 8));
        }

        public int Read(int count)
        {
            if (count <= 0) return 0;
            var mask = (1UL << count) - 1;
            var value = (int)(_low & mask);
            _low >>= count;
            _low |= (_high & mask) << (64 - count);
            _high >>= count;
            return value;
        }

        public int ReadBit() => Read(1);
    }

    private const string Partitions2Hex =
        "80000101000001010000010100000181" + "80000001000000010000000100000081" +
        "80010101000101010001010100010181" + "80000001000001010000010100010181" +
        "80000000000000010000000100000181" + "80000101000101010001010101010181" +
        "80000001000001010001010101010181" + "80000000000000010000010100010181" +
        "80000000000000000000000100000181" + "80000101000101010101010101010181" +
        "80000000000000010001010101010181" + "80000000000000000000000100010181" +
        "80000001000101010101010101010181" + "80000000000000000101010101010181" +
        "80000000010101010101010101010181" + "80000000000000000000000001010181" +
        "80000000010000000101010001010181" + "80018101000000010000000000000000" +
        "80000000000000008100000001010100" + "80018101000001010000000100000000" +
        "80008101000000010000000000000000" + "80000000010000008101000001010100" +
        "80000000000000008100000001010000" + "80010101000001010000010100000081" +
        "80008101000000010000000100000000" + "80000000010000008100000001010000" +
        "80018100000101000001010000010100" + "80008101000101000001010001010000" +
        "80000001000101018101010001000000" + "80000000010101018101010100000000" +
        "80018101000000010100000001010100" + "80008101010000010100000101010000" +
        "80010001000100010001000100010081" + "80000000010101010000000001010181" +
        "80010001010081000001000101000100" + "80000101000001018101000001010000" +
        "80008101010100000000010101010000" + "80010001000100018100010001000100" +
        "80010100010000010001010001000081" + "80010001010001000100010000010081" +
        "80018101000001010101000001010100" + "80000001000001018101000001000000" +
        "80008101000001000001000001010000" + "80008101010001010101000101010000" +
        "80018100010000010100000100010100" + "80000101010100000101000000000181" +
        "80010100000101000100000101000081" + "80000000000181000001010000000000" +
        "80010000010181000001000000000000" + "80008100000101010000010000000000" +
        "80000000000081000001010100000100" + "80000000000100008101010000010000" +
        "80010100010100000100000100000181" + "80000101000101000101000001000081" +
        "80018100000001010100000101010000" + "80008101010000010101000000010100" +
        "80010100010100000101000001000081" + "80010100000001010000010101000081" +
        "80010101010101000100000000000081" + "80000001010000000101010000010181" +
        "80000000010101010000010100000181" + "80008101000001010101010100000000" +
        "80008100000001000101010001010100" + "80010000000100000001010100010181";

    private const string Partitions3Hex =
        "80000181000001010002020102020282" + "80000081000001018202010102020201" +
        "80000000020000018202010102020181" + "80020282000002020000010100010181" +
        "80000000000000008101020201010282" + "80000181000001010000020200000282" +
        "80000282000002020101010101010181" + "80000101000001018202010102020181" +
        "80000000000000008101010102020282" + "80000000010101018101010102020282" +
        "80000000010181010202020202020282" + "80000102000081020000010200000182" +
        "80010102000181020001010200010182" + "80010202008102020001020200010282" +
        "80000181000101020101020201020282" + "80000181020000018202000002020200" +
        "80000081000001010001010201010282" + "80010181000001018200000102020000" +
        "80000000010102028101020201010282" + "80000282000002020000020201010181" +
        "80010181000101010002020200020282" + "80000081000000018202020102020201" +
        "80000000000081010001020200010282" + "80000000010100008202810002020100" +
        "80010282008102020000010100000000" + "80000102000001028101020202020282" +
        "80010100010282018102020100010100" + "80000000000181000102820101020201" +
        "80000202010100028101000200000282" + "80010100008101000200000202020282" +
        "80000101000102020001820200000181" + "80000000020000008202010102020281" +
        "80000000000000028101020201020282" + "80020282000002020000010200000181" +
        "80000181000001020000020200020282" + "80010200008102000001820000010200" +
        "80000000010181010202820200000000" + "80010200010200018200810200010200" +
        "80010200020001028182000100010200" + "80000101020200000101820200000181" +
        "80000101010182020202000000000181" + "80010081000100010202020202020282" +
        "80000000000000008201020102010281" + "80000202018102020000020201010282" +
        "80000282000001010000020200000181" + "80020200010282010002020001020281" +
        "80010001020282020202020200010081" + "80000000020102018201020102010281" +
        "80010081000100010001000102020282" + "80020282000101010002020200010181" +
        "80000002018101020000000201010182" + "80000000028101020201010202010182" +
        "80020202008101010001010100020282" + "80000002010101028101010200000082" +
        "80010100008101000001010002020282" + "80000000000000000201810202010182" +
        "80010100008101000202020202020282" + "80000202000001010000810100000282" +
        "80000202010102028101020200000282" + "80000000000000000000000002810182" +
        "80000082000000010000000200000081" + "80020202010202020002020281020282" +
        "80010081020202020202020202020282" + "80010181020001018202000102020200";
}
