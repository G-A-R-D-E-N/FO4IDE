using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Archives;
using FO4RecordEditor.Services.Textures;
using Xunit;
using Xunit.Abstractions;

namespace FO4RecordEditor.Tests;

// The in-process DDS decode that replaced the Texconv.exe shell-out (issue #76). The decoder is a
// port of bcdec.h, the same one Godot ships as modules/bcdec.
//
// The proof that matters is not in this file, because it needs a Windows binary: the output was
// compared against DirectXTex's own conversion of the same file, pixel for pixel, over 484 real mod
// textures picked to cover every format. Results, per format, files exact / files with any
// difference:
//
//   BC2 29/29, BC3 62/62, BC5 128/128, BC7 60/60, B8G8R8A8 29/29, R8G8B8A8 31/31, R8 4/4 -- exact.
//   BC1 52/61 exact, the other 9 differ by at most 1 in one channel, only in the punch-through-alpha
//     mode's half-way colour, and only on exact .5 ties (8 of the 2,327 endpoint pairs seen).
//   BC4 1 file, differs by at most 1, same rounding-tie cause.
//   The sRGB-tagged formats differ on purpose: see DecodeInProcess in TextureService.
//
// What this file can check without that binary is the block arithmetic against hand-computed values,
// and that the PNG we emit is a real PNG.
public class DdsDecoderTests
{
    private readonly ITestOutputHelper _out;
    public DdsDecoderTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void Bc1DecodesItsFourReferenceColours()
    {
        // c0 = white (0xFFFF), c1 = black (0x0000), so c0 > c1: the four-colour mode, where index 2
        // is 2/3 white and index 3 is 1/3 white.
        var block = new byte[]
        {
            0xFF, 0xFF, 0x00, 0x00,
            0b11100100, 0b11100100, 0b11100100, 0b11100100,   // indices 0,1,2,3 on every row
        };

        var rgba = DecodeSingleBlock(block, dxgi: 71);
        Pixel(rgba, 0).Should().Be((255, 255, 255, 255));
        Pixel(rgba, 1).Should().Be((0, 0, 0, 255));
        Pixel(rgba, 2).Should().Be((170, 170, 170, 255));
        Pixel(rgba, 3).Should().Be((85, 85, 85, 255));
    }

    [Fact]
    public void Bc1PunchThroughModeGivesATransparentTexel()
    {
        // c0 < c1 puts the block in the three-colour mode, where index 3 is transparent black.
        var block = new byte[]
        {
            0x00, 0x00, 0xFF, 0xFF,
            0b11100100, 0b11100100, 0b11100100, 0b11100100,
        };

        var rgba = DecodeSingleBlock(block, dxgi: 71);
        Pixel(rgba, 0).Should().Be((0, 0, 0, 255));
        Pixel(rgba, 1).Should().Be((255, 255, 255, 255));
        Pixel(rgba, 2).Should().Be((128, 128, 128, 255));
        Pixel(rgba, 3).Should().Be((0, 0, 0, 0));
    }

    [Fact]
    public void Bc4ExpandsItsSingleChannelToGrey()
    {
        // Endpoints 255 and 0 with e0 > e1 give the eight-value ramp; index 0 is the first endpoint.
        var block = new byte[8];
        block[0] = 255;
        block[1] = 0;
        // Sixteen 3-bit indices, all 0.
        var rgba = DecodeSingleBlock(block, dxgi: 80);
        Pixel(rgba, 0).Should().Be((255, 255, 255, 255), "a one-channel format reads as grey, not as red");
    }

    [Fact]
    public void Bc3TakesColourFromTheSecondHalfAndAlphaFromTheFirst()
    {
        var block = new byte[16];
        block[0] = 0;      // alpha endpoints 0 and 255, e0 < e1 -> the six-value ramp
        block[1] = 255;
        // alpha indices all 0 -> alpha 0
        block[8] = 0xFF; block[9] = 0xFF;   // colour c0 = white
        block[10] = 0; block[11] = 0;       // colour c1 = black
        // colour indices all 0 -> white

        var rgba = DecodeSingleBlock(block, dxgi: 77);
        Pixel(rgba, 0).Should().Be((255, 255, 255, 0));
    }

    [Fact]
    public void AnUndecodableFormatThrowsRatherThanRenderingSomethingWrong()
    {
        DdsDecoder.CanDecode(95).Should().BeFalse("BC6H is HDR and is not ported");
        DdsDecoder.CanDecode(84).Should().BeFalse("BC5_SNORM is not the same decode as BC5_UNORM");
        DdsDecoder.CanDecode(83).Should().BeTrue();
        DdsDecoder.CanDecode(98).Should().BeTrue();
    }

    [Fact]
    public void ThePngWeEmitIsARealPng()
    {
        var rgba = new byte[4 * 3 * 4];
        for (int i = 0; i < rgba.Length; i++) rgba[i] = (byte)(i * 7);

        var png = PngWriter.Write(rgba, 4, 3);

        png.Take(8).Should().Equal(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);

        var chunks = ReadChunks(png);
        chunks.Select(c => c.Type).Should().Equal("IHDR", "IDAT", "IEND");

        var ihdr = chunks[0].Data;
        BigEndian(ihdr, 0).Should().Be(4);
        BigEndian(ihdr, 4).Should().Be(3);
        ihdr[8].Should().Be(8);   // bit depth
        ihdr[9].Should().Be(6);   // RGBA

        // Inflate IDAT and confirm every scanline is filter 0 followed by the pixels we handed over.
        using var input = new MemoryStream(chunks[1].Data);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        zlib.CopyTo(raw);
        var scanlines = raw.ToArray();
        scanlines.Length.Should().Be(3 * (1 + 4 * 4));
        for (int y = 0; y < 3; y++)
        {
            scanlines[y * 17].Should().Be(0);
            scanlines.Skip(y * 17 + 1).Take(16).Should().Equal(rgba.Skip(y * 16).Take(16));
        }
    }

    /// <summary>
    /// Every loose .dds under a folder decodes without throwing and at the size its header claims.
    /// Point FO4RE_TEST_DDS at a mod folder to run it; it is the sweep that caught the formats the
    /// decoder does not handle, rather than assuming the list.
    /// </summary>
    [Fact]
    public void RealTexturesDecodeAtTheirHeaderSize()
    {
        var root = Environment.GetEnvironmentVariable("FO4RE_TEST_DDS");
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            const string msg = "FO4RE_TEST_DDS is not set to a folder of .dds files.";
            if (TestDataRoots.FixturesRequired) Assert.Fail(msg);
            _out.WriteLine("Skipped -- " + msg);
            return;
        }

        int decoded = 0, unsupported = 0;
        var problems = new System.Collections.Generic.List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.dds", SearchOption.AllDirectories))
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(file); } catch { continue; }

            DdsInfo info;
            try { info = DdsCodec.Parse(bytes, Path.GetFileName(file)); }
            catch { unsupported++; continue; }

            if (!DdsDecoder.CanDecode(info.DxgiFormat)) { unsupported++; continue; }

            try
            {
                var rgba = DdsDecoder.Decode(bytes, out var w, out var h);
                if (w != info.Width || h != info.Height) problems.Add($"{Path.GetFileName(file)}: {w}x{h} != {info.Width}x{info.Height}");
                else if (rgba.Length != w * h * 4) problems.Add($"{Path.GetFileName(file)}: {rgba.Length} bytes for {w}x{h}");
                else decoded++;
            }
            catch (Exception ex) { problems.Add($"{Path.GetFileName(file)}: {ex.Message}"); }
        }

        _out.WriteLine($"{decoded} decoded, {unsupported} unsupported, {problems.Count} problems");
        problems.Should().BeEmpty();
        decoded.Should().BeGreaterThan(0);
    }

    private static byte[] DecodeSingleBlock(byte[] block, byte dxgi)
    {
        var rgba = new byte[4 * 4 * 4];
        BcnDecoder.DecodeBlockFormat(block, dxgi, 4, 4, rgba);
        return rgba;
    }

    private static (byte R, byte G, byte B, byte A) Pixel(byte[] rgba, int index)
        => (rgba[index * 4], rgba[index * 4 + 1], rgba[index * 4 + 2], rgba[index * 4 + 3]);

    private static uint BigEndian(byte[] b, int at)
        => (uint)((b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3]);

    private static System.Collections.Generic.List<(string Type, byte[] Data)> ReadChunks(byte[] png)
    {
        var chunks = new System.Collections.Generic.List<(string, byte[])>();
        var at = 8;
        while (at < png.Length)
        {
            var length = (int)BigEndian(png, at);
            var type = new string(png.Skip(at + 4).Take(4).Select(b => (char)b).ToArray());
            var data = png.Skip(at + 8).Take(length).ToArray();
            chunks.Add((type, data));
            at += 12 + length;
        }
        return chunks;
    }
}
