using System;
using System.IO;
using System.Linq;
using System.Text;
using FluentAssertions;
using FO4RecordEditor.Services;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives;
using Xunit;
using Xunit.Abstractions;

namespace FO4RecordEditor.Tests;

// Regression tests for the Next-Gen BA2 entry-layout work in
// Mutagen/Mutagen.Bethesda.Core/Archives/Ba2/Ba2Reader.cs (BA2FileEntry).
//
// Three distinct things broke here at different times, so all three are pinned:
//
//  1. v8 GNRL, compressed. The 4-byte field Mutagen discarded as "unknown" is the real packed
//     (compressed) size; `_size` is the true uncompressed length, and `_realSize` reads back as the
//     sentinel 0xBAADF00D for every entry regardless of compression state -- which is why the old
//     sentinel-based Compressed check never worked and GetBytes() handed back raw zlib bytes.
//  2. v8 GNRL, STORED (packedSize == 0). The FIRST attempt at fix 1 used `Compressed = _size != 0`
//     for all v8 entries, which broke genuinely-uncompressed entries: Fallout4_en.STRINGS threw
//     SharpZipBaseException "Header checksum illegal", and that silently zeroed out cell search.
//  3. v7. Both the GNRL entry layout (classic v1 36-byte entries, WITH the trailing align field --
//     the old `<= 7` read a phantom uint32 and skipped align, drifting 4 bytes per entry) and the
//     v7 DX10 texture layout.
//
// Fixture resolution goes through TestDataRoots, not a hardcoded drive letter -- see the note there
// about skips being recorded as passes.
public class Ba2NextGenDecompressionTests
{
    private readonly ITestOutputHelper _out;
    public Ba2NextGenDecompressionTests(ITestOutputHelper o) => _out = o;

    /// <summary>Resolve a fixture archive, or signal that the test cannot run.</summary>
    private bool TryFixture(string fileName, out string path)
    {
        var found = TestDataRoots.Archive(fileName);
        path = found ?? "";
        if (found != null) return true;
        var msg = $"Fixture archive not present: {fileName} (searched FO4RE_TEST_DATA and the known Data folders)";
        if (TestDataRoots.FixturesRequired) Assert.Fail(msg);
        _out.WriteLine("Skipped -- " + msg);
        return false;
    }

    private static byte[] ReadEntry(string archivePath, Func<IArchiveFile, bool> match)
    {
        var reader = Archive.CreateReader(GameRelease.Fallout4, archivePath);
        var file = reader.Files.FirstOrDefault(match)
                   ?? throw new FileNotFoundException($"No matching entry in {Path.GetFileName(archivePath)}");
        return file.GetBytes();
    }

    [Fact]
    public void V8CompressedEntry_ExtractsARealNif_NotRawZlibBytes()
    {
        if (!TryFixture("Fallout4 - Meshes.ba2", out var archive)) return;

        var outPath = Path.Combine(Path.GetTempPath(),
            "FO4RE_Ba2NextGen_" + Guid.NewGuid().ToString("N")[..8] + ".nif");
        try
        {
            var result = ArchiveService.ExtractFile(archive, "Meshes/Furniture/ParkBench01.nif", outPath);
            _out.WriteLine(result);
            File.Exists(outPath).Should().BeTrue();

            var bytes = File.ReadAllBytes(outPath);
            bytes.Length.Should().Be(69920, "the archive's own uncompressed 'size' field for this entry");
            Encoding.ASCII.GetString(bytes, 0, 20).Should().StartWith("Gamebryo File Format",
                "a fixed reader returns the decompressed NIF -- raw zlib would start 0x78 0x9C");
        }
        finally { try { File.Delete(outPath); } catch { } }
    }

    [Fact]
    public void V8StoredEntry_ReadsRaw_InsteadOfBeingInflated()
    {
        if (!TryFixture("Fallout4 - Interface.ba2", out var archive)) return;

        // packedSize == 0 means "stored". Treating this entry as compressed is what threw
        // "Header checksum illegal" and took cell search down with it.
        var act = () => ReadEntry(archive, f => f.Path.EndsWith("Fallout4_en.STRINGS", StringComparison.OrdinalIgnoreCase));
        var bytes = act.Should().NotThrow("a stored entry must be read raw, not inflated").Subject;
        bytes.Length.Should().BeGreaterThan(0);
        _out.WriteLine($"STRINGS\\Fallout4_en.STRINGS -> {bytes.Length} bytes");
    }

    [Fact]
    public void V7GnrlArchive_EnumeratesEveryEntry_WithoutDrift()
    {
        if (!TryFixture("DLCRobot - Voices_en.ba2", out var archive)) return;

        var files = Archive.CreateReader(GameRelease.Fallout4, archive).Files.ToList();
        _out.WriteLine($"{files.Count} entries");

        files.Should().NotBeEmpty();
        // A 4-byte stride error accumulates into garbage paths, so "every entry is a .fuz" is the
        // cheap proof that the entry table was walked correctly all the way to the end.
        files.Should().OnlyContain(f => f.Path.EndsWith(".fuz", StringComparison.OrdinalIgnoreCase),
            "this archive is all voice .fuz files -- any other extension means the entry stride drifted");
        files.First().GetBytes().Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void V7Dx10TextureArchive_EnumeratesDdsEntries()
    {
        if (!TryFixture("Fallout4 - Textures1.ba2", out var archive)) return;

        var files = Archive.CreateReader(GameRelease.Fallout4, archive).Files.Take(25).ToList();
        files.Should().NotBeEmpty();
        files.Should().OnlyContain(f => f.Path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase),
            "a mis-strided DX10 entry table produces garbage paths and 'Unsupported DDS header format'");
    }
}
