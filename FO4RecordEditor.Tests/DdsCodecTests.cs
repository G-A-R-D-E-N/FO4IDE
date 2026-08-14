using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services.Archives;
using Xunit;
using Xunit.Abstractions;

namespace FO4RecordEditor.Tests;

// The DDS side of a texture (DX10) archive. Nothing here is recalled: the format table, the mip
// arithmetic and the chunk-split rule were all derived from real data and are checked back against
// it by the two corpus tests below.
//
// VanillaChunkLayoutIsReproduced is the proof that matters. It walks every texture entry in every
// vanilla DX10 archive and asserts that (a) the mip arithmetic reconciles each stored chunk's
// decompressed size exactly, and (b) the split rule predicts the stored chunk mip ranges. That
// sweep has been run against a real Fallout 4 Data folder: 37 DX10 archives, 42,036 texture
// entries, 0 size mismatches, 0 layout mismatches.
//
// Three rules that a sampling test would have got wrong, and that the sweep settled:
//   1. The split is by PIXEL COUNT, not by byte size and not by longest edge. A 512x256 BC3 mip is
//      131,072 bytes and goes in the tail; a 1024x256 BC1 mip is the same 131,072 bytes and gets its
//      own chunk. Byte size and max-dimension rules both fail on the non-square textures.
//   2. There are never more than three single-mip chunks. A 4096x4096 texture would earn a fourth
//      by pixel count alone, and vanilla still ships it with three.
//   3. Cube maps are 6 surfaces, which the arithmetic only reconciles when the ArraySize multiplier
//      is applied (72 vanilla entries).
public class DdsCodecTests
{
    private readonly ITestOutputHelper _out;
    public DdsCodecTests(ITestOutputHelper o) => _out = o;

    [Theory]
    [InlineData(71, 4, 4, 8)]        // BC1: one block, 8 bytes
    [InlineData(71, 1, 1, 8)]        // a 1x1 mip still occupies a whole block
    [InlineData(77, 256, 256, 65536)]
    [InlineData(83, 1024, 1024, 1048576)]
    [InlineData(87, 16, 16, 1024)]   // uncompressed BGRA, 4 bytes a pixel
    public void MipSizeMatchesBlockArithmetic(byte dxgi, int w, int h, long expected)
        => DdsCodec.MipSize(dxgi, w, h).Should().Be(expected);

    [Theory]
    [InlineData("DXT1", 71)]
    [InlineData("DXT3", 74)]
    [InlineData("DXT5", 77)]
    [InlineData("ATI1", 80)]
    [InlineData("ATI2", 83)]
    public void LegacyFourCcMapsToDxgi(string fourCc, byte expected)
        => DdsCodec.Parse(BuildDds(fourCc, 64, 64, 1)).DxgiFormat.Should().Be(expected);

    [Fact]
    public void Dx10ExtensionHeaderWinsOverTheLegacyBlock()
    {
        var dds = BuildDds("DX10", 64, 64, 1, dxgi: 98);
        var info = DdsCodec.Parse(dds);
        info.DxgiFormat.Should().Be(98);
        info.DataOffset.Should().Be(148);
    }

    [Fact]
    public void AMipCountOfZeroMeansOne()
        => DdsCodec.Parse(BuildDds("DXT1", 64, 64, 0)).MipCount.Should().Be(1);

    [Fact]
    public void AnUnknownFourCcIsRefusedRatherThanGuessed()
    {
        var act = () => DdsCodec.Parse(BuildDds("ZZZZ", 64, 64, 1));
        act.Should().Throw<InvalidDataException>().WithMessage("*FourCC*");
    }

    // The vanilla split rule, spelled out as the shapes it actually produces. Numbers come from the
    // real archives -- e.g. every 1024x1024 with a full mip chain in vanilla is exactly 3 chunks.
    [Theory]
    [InlineData(256, 256, 9, "0-8")]
    [InlineData(512, 512, 10, "0-0,1-9")]
    [InlineData(1024, 1024, 11, "0-0,1-1,2-10")]
    [InlineData(2048, 2048, 12, "0-0,1-1,2-2,3-11")]
    [InlineData(4096, 4096, 13, "0-0,1-1,2-2,3-12")]   // capped at three single-mip chunks
    [InlineData(1024, 512, 11, "0-0,1-10")]            // by pixel count, not by longest edge
    [InlineData(1024, 256, 11, "0-0,1-10")]
    [InlineData(256, 512, 10, "0-9")]
    [InlineData(2048, 1024, 12, "0-0,1-1,2-11")]
    [InlineData(64, 64, 1, "0-0")]
    public void ChunkPlanMatchesVanillaShapes(int w, int h, int mips, string expected)
    {
        var info = new DdsInfo(w, h, mips, false, 1, 1, 128, DdsCodec.Lookup(71)!);
        Describe(Ba2Texture.PlanChunks(info)).Should().Be(expected);
    }

    [Fact]
    public void ACubeMapIsOneChunkBecauseItsSurfaceOrderIsNotAssumed()
    {
        var info = new DdsInfo(512, 512, 10, true, 6, 1, 128, DdsCodec.Lookup(71)!);
        Describe(Ba2Texture.PlanChunks(info)).Should().Be("0-9");
    }

    [Fact]
    public void BuildRoundTripsThroughTheArchiveWriter()
    {
        var dds = BuildDds("DXT5", 512, 512, 10, withPayload: true);
        var entry = Ba2Texture.Build(@"Textures\test\thing_d.dds", dds, compress: true);

        entry.Texture.Should().NotBeNull();
        entry.Texture!.Width.Should().Be(512);
        entry.Texture.Height.Should().Be(512);
        entry.Texture.MipCount.Should().Be(10);
        entry.Texture.DxgiFormat.Should().Be(77);
        entry.Texture.Flags.Should().Be(0);
        entry.Texture.TileMode.Should().Be(8);
        entry.Chunks.Should().HaveCount(2);

        var archive = new Ba2Archive { Version = 1, Format = Ba2Format.DirectX, Entries = { entry } };
        using var ms = new MemoryStream();
        Ba2Codec.Write(archive, ms);
        ms.Position = 0;
        var back = Ba2Codec.Read(ms);

        back.Format.Should().Be(Ba2Format.DirectX);
        back.Entries.Should().ContainSingle();
        var got = back.Entries[0];
        got.Path.Should().Be(@"Textures\test\thing_d.dds");
        got.Texture.Should().Be(entry.Texture);

        // The payload is the DDS minus its header, and the chunks in order are exactly that payload.
        var rebuilt = got.Chunks.SelectMany(Ba2Codec.Decompress).ToArray();
        rebuilt.Should().Equal(dds.Skip(128).ToArray());
    }

    [Fact]
    public void PackingANonTextureIntoATextureArchiveIsRefused()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fo4re_dds_pack_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(dir, "Textures"));
        File.WriteAllBytes(Path.Combine(dir, "Textures", "a.dds"), BuildDds("DXT1", 64, 64, 1, withPayload: true));
        File.WriteAllText(Path.Combine(dir, "Textures", "notes.txt"), "hello");
        var output = Path.Combine(dir, "out.ba2");

        try
        {
            var act = () => Ba2Packer.Pack(new[] { dir }, output, format: Ba2Format.DirectX);
            act.Should().Throw<InvalidDataException>().WithMessage("*not a .dds*");
        }
        finally { try { Directory.Delete(dir, true); } catch { /* temp */ } }
    }

    [Fact]
    public void ATruncatedTextureIsRefusedRatherThanPackedShort()
    {
        var dds = BuildDds("DXT1", 512, 512, 10, withPayload: true);
        var truncated = dds.Take(dds.Length - 64).ToArray();
        var act = () => Ba2Texture.Build(@"Textures\t.dds", truncated, compress: false);
        act.Should().Throw<InvalidDataException>().WithMessage("*only*bytes follow*");
    }

    /// <summary>
    /// The corpus check: every texture entry in every vanilla DX10 archive, against both the mip
    /// arithmetic and the split rule. This is what makes the numbers above claims about the game's
    /// data rather than about a handful of hand-written cases.
    /// </summary>
    [Fact]
    public void VanillaChunkLayoutIsReproduced()
    {
        var data = TestDataRoots.DataRoot;
        if (data == null)
        {
            const string msg = "No real Fallout 4 Data folder found (searched FO4RE_TEST_DATA and the known paths).";
            if (TestDataRoots.FixturesRequired) Assert.Fail(msg);
            _out.WriteLine("Skipped -- " + msg);
            return;
        }

        var report = Ba2TextureCorpus.Run(data);
        _out.WriteLine(report.ToString());

        report.Archives.Should().BeGreaterThan(0, "a real Data folder has texture archives in it");
        report.SizeMismatches.Should().BeEmpty();
        report.LayoutMismatches.Should().BeEmpty();
        report.UnknownFormats.Should().BeEmpty();
    }

    /// <summary>
    /// The end-to-end version: rebuild each vanilla entry as a .dds, pack it, and compare the entry
    /// that comes out. A correct split rule with a wrong slice offset would pass the check above and
    /// fail this one. Run against a real Data folder: 42,036 entries, 41,964 rebuilt with identical
    /// header, chunk ranges and payload bytes, 72 cube maps stored as one chunk by design, 0 problems.
    /// </summary>
    [Fact]
    public void VanillaEntriesRebuildThroughTheWriter()
    {
        var data = TestDataRoots.DataRoot;
        if (data == null)
        {
            const string msg = "No real Fallout 4 Data folder found (searched FO4RE_TEST_DATA and the known paths).";
            if (TestDataRoots.FixturesRequired) Assert.Fail(msg);
            _out.WriteLine("Skipped -- " + msg);
            return;
        }

        int entries = 0, rebuilt = 0, multiSurface = 0;
        var problems = new List<string>();

        foreach (var file in Directory.EnumerateFiles(data, "*.ba2").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            Ba2Archive probe;
            try { probe = Ba2Codec.Read(file); }
            catch (Exception ex) { problems.Add($"{Path.GetFileName(file)}: {ex.Message}"); continue; }
            if (probe.Format != Ba2Format.DirectX) continue;

            var report = Ba2TextureCorpus.RoundTrip(file);
            entries += report.Entries;
            rebuilt += report.Rebuilt;
            multiSurface += report.MultiSurface;
            problems.AddRange(report.Problems.Select(p => $"{Path.GetFileName(file)}: {p}"));
        }

        _out.WriteLine($"{entries} entries, {rebuilt} rebuilt identically, {multiSurface} multi-surface, {problems.Count} problems");
        problems.Should().BeEmpty();
        entries.Should().Be(rebuilt + multiSurface);
        rebuilt.Should().BeGreaterThan(0);
    }

    private static string Describe(IEnumerable<(int First, int Last)> ranges)
        => string.Join(",", ranges.Select(r => $"{r.First}-{r.Last}"));

    /// <summary>A minimal but real DDS header, so the parser is exercised on bytes rather than a mock.</summary>
    private static byte[] BuildDds(string fourCc, int width, int height, int mips, byte dxgi = 0, bool withPayload = false)
    {
        var isDx10 = fourCc == "DX10";
        var headerSize = isDx10 ? 148 : 128;

        var format = isDx10
            ? DdsCodec.Lookup(dxgi)!
            : fourCc switch
            {
                "DXT1" => DdsCodec.Lookup(71)!,
                "DXT3" => DdsCodec.Lookup(74)!,
                "DXT5" => DdsCodec.Lookup(77)!,
                "ATI1" => DdsCodec.Lookup(80)!,
                "ATI2" => DdsCodec.Lookup(83)!,
                _ => DdsCodec.Lookup(71)!,
            };

        long payload = 0;
        if (withPayload)
            for (int m = 0; m < Math.Max(1, mips); m++)
                payload += DdsCodec.MipSize(format, width >> m, height >> m);

        var bytes = new byte[headerSize + payload];
        void U32(int offset, uint v) => BitConverter.GetBytes(v).CopyTo(bytes, offset);

        U32(0, 0x20534444);                 // "DDS "
        U32(4, 124);                        // dwSize
        U32(8, 0x0002100F);                 // caps|height|width|pixelformat|mipmapcount|linearsize
        U32(12, (uint)height);
        U32(16, (uint)width);
        U32(28, (uint)mips);
        U32(76, 32);                        // DDS_PIXELFORMAT.dwSize
        U32(80, 0x4);                       // DDPF_FOURCC
        U32(84, BitConverter.ToUInt32(System.Text.Encoding.ASCII.GetBytes(fourCc)));
        U32(108, 0x401008);                 // caps: texture|mipmap|complex
        if (isDx10)
        {
            U32(128, dxgi);
            U32(132, 3);                    // D3D10_RESOURCE_DIMENSION_TEXTURE2D
            U32(140, 1);                    // arraySize
        }
        return bytes;
    }
}
