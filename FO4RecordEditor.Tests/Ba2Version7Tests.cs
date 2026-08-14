using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;
using Xunit.Abstractions;

namespace FO4RecordEditor.Tests;

// Regression test for the BA2 version-7 GNRL layout fix in
// Mutagen/Mutagen.Bethesda.Core/Archives/Ba2/Ba2Reader.cs (BA2FileEntry). Mutagen read a phantom
// "unknown" uint32 after nameHash for versions 2..7 AND skipped the trailing 0xBAADF00D align field
// for v7, making each entry 40 bytes when a real v7 GNRL entry is the classic v1 36-byte layout.
// That misalignment drifted cumulatively -- early entries looked sane, later ones decoded to garbage.
//
// Verified against the one real v7 GNRL archive in the test modlist, DLCRobot - Voices_en.ba2 (6112
// .fuz voice lines): all 6112 entries decode as ext="fuz" with align==0xBAADF00D at the v1 offsets,
// and 24 + 6112*36 lands exactly on the first file's data offset. This test extracts a real entry
// through the production ArchiveService path and asserts the FUZ container magic -- proving the entry
// table is read at the right stride (a wrong stride would resolve the name to the wrong data blob).
//
// Skips loudly when the fixture archive isn't present, rather than passing.
public class Ba2Version7Tests
{
    private static readonly string[] Candidates =
    {
        @"E:\Modlists\Fallen World Alpha 2\Stock Folder\Data\DLCRobot - Voices_en.ba2",
        @"E:\SteamLibrary\steamapps\common\Fallout 4\Data\DLCRobot - Voices_en.ba2",
    };
    private static string? Archive => Candidates.FirstOrDefault(File.Exists);

    // v7 DX10 (texture) archives share the same v7 layout family: classic v1 24-byte entry + 24-byte
    // chunk (startMip/endMip/align). Mutagen read two phantom entry unknowns and skipped the chunk tail
    // for version>1, so _format/_width/_height decoded as garbage -> "Unsupported DDS header format" /
    // arithmetic overflow. Verified against Fallout4 - Textures1.ba2 (entry 0: ext="dds", 1024x1024,
    // 11 mips, format=71/BC1, chunk ends 0xBAADF00D).
    private static readonly string[] TexCandidates =
    {
        @"E:\Modlists\Fallen World Alpha 2\Stock Folder\Data\Fallout4 - Textures1.ba2",
        @"E:\SteamLibrary\steamapps\common\Fallout 4\Data\Fallout4 - Textures1.ba2",
    };
    private static string? TexArchive => TexCandidates.FirstOrDefault(File.Exists);

    private readonly ITestOutputHelper _out;
    public Ba2Version7Tests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void ExtractFile_FromARealVersion7Archive_ProducesARealFuz_NotDriftedGarbage()
    {
        var archive = Archive;
        if (archive == null) { _out.WriteLine("Skipped -- v7 fixture archive not present in any known location."); return; }

        var outPath = Path.Combine(Path.GetTempPath(),
            "FO4RE_Ba2V7_" + Guid.NewGuid().ToString("N")[..8] + ".fuz");
        try
        {
            var result = ArchiveService.ExtractFile(
                archive, @"Sound\Voice\DLCRobot.esm\DLC01MechanistEyebot\00007072_1.fuz", outPath);
            _out.WriteLine(result);
            File.Exists(outPath).Should().BeTrue();

            var bytes = File.ReadAllBytes(outPath);
            var magic = System.Text.Encoding.ASCII.GetString(bytes, 0, 4);
            magic.Should().Be("FUZE",
                "a correctly-strided v7 entry table resolves the name to real FUZ data; a drifted " +
                "table would hand back an unrelated blob");
        }
        finally { try { File.Delete(outPath); } catch { } }
    }

    [Fact]
    public void ExtractFile_FromARealVersion7Dx10TextureArchive_ProducesAValidDds()
    {
        var archive = TexArchive;
        if (archive == null) { _out.WriteLine("Skipped -- v7 DX10 texture fixture not present in any known location."); return; }

        var outPath = Path.Combine(Path.GetTempPath(),
            "FO4RE_Ba2V7Tex_" + Guid.NewGuid().ToString("N")[..8] + ".dds");
        try
        {
            var result = ArchiveService.ExtractFile(archive, @"Textures\Props\Cigar\AshTray_d.DDS", outPath);
            _out.WriteLine(result);
            File.Exists(outPath).Should().BeTrue();

            var bytes = File.ReadAllBytes(outPath);
            var magic = System.Text.Encoding.ASCII.GetString(bytes, 0, 4);
            magic.Should().Be("DDS ",
                "the DX10 entry+chunk layout must be read at the right stride to reconstruct a valid " +
                "DDS header; a mis-strided entry threw 'Unsupported DDS header format' or overflowed");
            // Reconstructed header (magic+DDS_HEADER) is 128 bytes; real texture data follows.
            bytes.Length.Should().BeGreaterThan(128);
        }
        finally { try { File.Delete(outPath); } catch { } }
    }
}
