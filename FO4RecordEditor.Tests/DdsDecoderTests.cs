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

















public class DdsDecoderTests
{
    private readonly ITestOutputHelper _out;
    public DdsDecoderTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void Bc1DecodesItsFourReferenceColours()
    {


        var block = new byte[]
        {
            0xFF, 0xFF, 0x00, 0x00,
            0b11100100, 0b11100100, 0b11100100, 0b11100100,
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

        var block = new byte[8];
        block[0] = 255;
        block[1] = 0;

        var rgba = DecodeSingleBlock(block, dxgi: 80);
        Pixel(rgba, 0).Should().Be((255, 255, 255, 255), "a one-channel format reads as grey, not as red");
    }

    [Fact]
    public void Bc3TakesColourFromTheSecondHalfAndAlphaFromTheFirst()
    {
        var block = new byte[16];
        block[0] = 0;
        block[1] = 255;

        block[8] = 0xFF; block[9] = 0xFF;
        block[10] = 0; block[11] = 0;


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
        ihdr[8].Should().Be(8);
        ihdr[9].Should().Be(6);


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
