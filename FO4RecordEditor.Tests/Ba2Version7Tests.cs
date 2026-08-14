using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;
using Xunit.Abstractions;

namespace FO4RecordEditor.Tests;














public class Ba2Version7Tests
{
    private static readonly string[] Candidates =
    {
        @"E:\Modlists\Fallen World Alpha 2\Stock Folder\Data\DLCRobot - Voices_en.ba2",
        @"E:\SteamLibrary\steamapps\common\Fallout 4\Data\DLCRobot - Voices_en.ba2",
    };
    private static string? Archive => Candidates.FirstOrDefault(File.Exists);






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

            bytes.Length.Should().BeGreaterThan(128);
        }
        finally { try { File.Delete(outPath); } catch { } }
    }
}
