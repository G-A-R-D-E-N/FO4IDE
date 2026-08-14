using System.IO;
using FluentAssertions;
using FO4RecordEditor.Services.Archives;
using Xunit;
using Xunit.Abstractions;

namespace FO4RecordEditor.Tests;















public class Ba2WriterTests
{
    private readonly ITestOutputHelper _out;
    public Ba2WriterTests(ITestOutputHelper o) => _out = o;




    [Theory]
    [InlineData(@"Meshes\Weapons\HandMade\Muzzles\HandMadeMuzzleParentObject.nif", 0x9B990F90u, 0xFF78256Eu, 0x0066696Eu)]
    [InlineData(@"Meshes\Weapons\HandMade\Grips\HandMadePistolGrip.nif", 0xBB6215A0u, 0xF6CEE42Au, 0x0066696Eu)]
    [InlineData(@"Meshes\Weapons\HandMade\Scopes\PipeRifleIronSights.nif", 0x342D080Eu, 0xEE753300u, 0x0066696Eu)]
    public void HashMatchesRealArchiveEntries(string path, uint name, uint directory, uint extension)
    {
        var h = Ba2Hash.Compute(path);
        h.Name.Should().Be(name);
        h.Directory.Should().Be(directory);
        h.Extension.Should().Be(extension);
    }



    [Fact]
    public void HashNormalizesSlashesEvenWhenTheStoredNameDoesNot()
    {
        var h = Ba2Hash.Compute("Strings/DLCCoast_cn.DLSTRINGS");
        h.Name.Should().Be(0x771F4BECu);
        h.Directory.Should().Be(0x29F6B58Bu);
    }

    private static Ba2Entry MakeEntry(string path, byte[] data, bool compress)
    {
        var (name, ext, dir) = Ba2Hash.Compute(path);
        return new Ba2Entry
        {
            Path = path,
            Chunks = new List<Ba2Chunk> { compress ? Ba2Codec.Compress(data) : Ba2Chunk.Stored(data) },
            NameHash = name,
            ExtensionHash = ext,
            DirectoryHash = dir,
        };
    }

    [Fact]
    public void WritesAReadableGeneralArchive()
    {
        var payload = System.Text.Encoding.UTF8.GetBytes(new string('x', 4096));
        var archive = new Ba2Archive
        {
            Version = 1,
            Format = Ba2Format.General,
            Entries = new List<Ba2Entry> { MakeEntry(@"Meshes\Test\Thing.nif", payload, compress: true) },
        };

        using var ms = new MemoryStream();
        Ba2Codec.Write(archive, ms);
        ms.Position = 0;

        var read = Ba2Codec.Read(ms);
        read.Version.Should().Be(1u);
        read.Format.Should().Be(Ba2Format.General);
        read.Entries.Should().HaveCount(1);
        read.Entries[0].Path.Should().Be(@"Meshes\Test\Thing.nif");
        read.Entries[0].Chunks[0].Compressed.Should().BeTrue();
        Ba2Codec.Decompress(read.Entries[0].Chunks[0]).Should().Equal(payload);
    }



    [Fact]
    public void IncompressibleDataIsStoredRaw()
    {
        var random = new byte[8192];
        new Random(12345).NextBytes(random);

        var chunk = Ba2Codec.Compress(random);
        chunk.Compressed.Should().BeFalse();
        chunk.Data.Should().Equal(random);
        Ba2Codec.Decompress(chunk).Should().Equal(random);
    }

    [Fact]
    public void EmptyFileRoundTrips()
    {
        var archive = new Ba2Archive
        {
            Entries = new List<Ba2Entry> { MakeEntry(@"Meshes\Empty.txt", Array.Empty<byte>(), compress: true) },
        };
        using var ms = new MemoryStream();
        Ba2Codec.Write(archive, ms);
        ms.Position = 0;

        var read = Ba2Codec.Read(ms);
        read.Entries[0].Chunks[0].DecompressedSize.Should().Be(0u);
        Ba2Codec.Decompress(read.Entries[0].Chunks[0]).Should().BeEmpty();
    }


    [Fact]
    public void NonUtf8NamesSurviveARewrite()
    {


        var prefix = System.Text.Encoding.ASCII.GetBytes(@"Sound\Voice\Fallout4.esm\RobotMrHandy\Mar");
        var suffix = System.Text.Encoding.ASCII.GetBytes("a_F.fuz");
        var raw = prefix.Concat(new byte[] { 0xED }).Concat(suffix).ToArray();
        var archive = new Ba2Archive
        {
            Entries = new List<Ba2Entry>
            {
                new()
                {
                    NameBytes = raw,
                    Chunks = new List<Ba2Chunk> { Ba2Chunk.Stored(new byte[] { 1, 2, 3 }) },
                },
            },
        };

        using var ms = new MemoryStream();
        Ba2Codec.Write(archive, ms);
        ms.Position = 0;

        Ba2Codec.Read(ms).Entries[0].NameBytes.Should().Equal(raw);
    }

    [Fact]
    public void DirectXArchiveKeepsItsPerTextureHeaderAndMipRanges()
    {
        var archive = new Ba2Archive
        {
            Version = 7,
            Format = Ba2Format.DirectX,
            Entries = new List<Ba2Entry>
            {
                new()
                {
                    Path = @"Textures\Test\Thing_d.dds",
                    Texture = new Ba2TextureInfo(1024, 512, 11, 71, 0, 8),
                    Chunks = new List<Ba2Chunk>
                    {
                        new(new byte[] { 1, 2, 3, 4 }, 4, false, 0, 0),
                        new(new byte[] { 5, 6 }, 2, false, 1, 10),
                    },
                },
            },
        };

        using var ms = new MemoryStream();
        Ba2Codec.Write(archive, ms);
        ms.Position = 0;

        var read = Ba2Codec.Read(ms);
        read.Format.Should().Be(Ba2Format.DirectX);
        read.Entries[0].Texture.Should().Be(new Ba2TextureInfo(1024, 512, 11, 71, 0, 8));
        read.Entries[0].Chunks[1].MipFirst.Should().Be((ushort)1);
        read.Entries[0].Chunks[1].MipLast.Should().Be((ushort)10);
    }

    [Fact]
    public void PackedArchiveIsReadableByMutagensOwnReader()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fo4re_packtest_" + Guid.NewGuid().ToString("N")[..8]);
        var outPath = Path.Combine(dir, "out.ba2");
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "src", "Meshes", "Sub"));
            var compressible = System.Text.Encoding.UTF8.GetBytes(new string('a', 10000));
            var incompressible = new byte[20000];
            new Random(99).NextBytes(incompressible);
            File.WriteAllBytes(Path.Combine(dir, "src", "Meshes", "note.txt"), compressible);
            File.WriteAllBytes(Path.Combine(dir, "src", "Meshes", "Sub", "noise.bin"), incompressible);

            var result = Ba2Packer.Pack(new[] { Path.Combine(dir, "src") }, outPath);
            result.FileCount.Should().Be(2);

            var reader = Mutagen.Bethesda.Archives.Archive.CreateReader(
                Mutagen.Bethesda.GameRelease.Fallout4, new Noggog.FilePath(outPath));
            var byPath = reader.Files.ToDictionary(f => f.Path.ToString(), StringComparer.OrdinalIgnoreCase);

            byPath.Should().ContainKey(@"Meshes\note.txt");
            byPath[@"Meshes\note.txt"].GetBytes().Should().Equal(compressible);
            byPath[@"Meshes\Sub\noise.bin"].GetBytes().Should().Equal(incompressible);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }



    [Fact]
    public void RealArchivesRewriteByteForByte()
    {
        var data = TestDataRoots.DataRoot;
        if (data == null)
        {
            const string msg = "No real Fallout 4 Data folder found (searched FO4RE_TEST_DATA and the known paths).";
            if (TestDataRoots.FixturesRequired) Assert.Fail(msg);
            _out.WriteLine("Skipped -- " + msg);
            return;
        }

        var tmp = Path.Combine(Path.GetTempPath(), "fo4re_ba2_rewrite_" + Guid.NewGuid().ToString("N")[..8] + ".ba2");
        int identical = 0, differing = 0, failed = 0;
        var problems = new List<string>();

        try
        {
            foreach (var path in Directory.EnumerateFiles(data, "*.ba2").OrderBy(x => x))
            {
                var name = Path.GetFileName(path);
                try
                {
                    var archive = Ba2Codec.Read(path);
                    Ba2Codec.Write(archive, tmp);
                    if (FilesEqual(path, tmp, out var at)) identical++;
                    else { differing++; problems.Add($"{name}: first difference at byte {at}"); }
                }
                catch (Exception ex)
                {
                    failed++;
                    problems.Add($"{name}: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }

        _out.WriteLine($"Rewrote {identical + differing + failed} archives: {identical} byte-identical, {differing} differing, {failed} failed.");
        foreach (var p in problems) _out.WriteLine("  " + p);

        identical.Should().BeGreaterThan(0, "the Data folder should contain archives");
        differing.Should().Be(0, "every vanilla archive must rewrite byte-for-byte");
        failed.Should().Be(0, "every vanilla archive must parse");
    }

    private static bool FilesEqual(string a, string b, out long firstDifference)
    {
        using var fa = File.OpenRead(a);
        using var fb = File.OpenRead(b);
        firstDifference = -1;
        var ba = new byte[1 << 20];
        var bb = new byte[1 << 20];
        long pos = 0;
        while (true)
        {
            var na = fa.Read(ba, 0, ba.Length);
            var nb = fb.Read(bb, 0, bb.Length);
            var n = Math.Min(na, nb);
            for (int i = 0; i < n; i++)
                if (ba[i] != bb[i]) { firstDifference = pos + i; return false; }
            if (na != nb) { firstDifference = pos + n; return false; }
            if (na == 0) return true;
            pos += n;
        }
    }
}
