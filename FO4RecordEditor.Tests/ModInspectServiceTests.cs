using System.IO;
using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;

namespace FO4RecordEditor.Tests;

public class ModInspectServiceTests
{
    private static string MakeModFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ModCatalogTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "Meshes", "Weapons"));
        Directory.CreateDirectory(Path.Combine(root, "Textures", "Weapons"));
        Directory.CreateDirectory(Path.Combine(root, "Scripts", "Source"));
        Directory.CreateDirectory(Path.Combine(root, "Sound", "Voice", "MyMod.esp", "PlayerVoice"));
        Directory.CreateDirectory(Path.Combine(root, "Sound", "FX", "AnimTextData"));

        File.WriteAllText(Path.Combine(root, "Meshes", "Weapons", "gun.nif"), "x");
        File.WriteAllText(Path.Combine(root, "Meshes", "Weapons", "gun2.nif"), "x");
        File.WriteAllText(Path.Combine(root, "Textures", "Weapons", "gun_d.dds"), "x");
        File.WriteAllText(Path.Combine(root, "MyMod.esp"), "x");
        File.WriteAllText(Path.Combine(root, "Scripts", "MyScript.pex"), "x");
        File.WriteAllText(Path.Combine(root, "Scripts", "Source", "MyScript.psc"), "x");  // must be excluded
        File.WriteAllText(Path.Combine(root, "Sound", "Voice", "MyMod.esp", "PlayerVoice", "line1.fuz"), "x");
        File.WriteAllText(Path.Combine(root, "Sound", "FX", "AnimTextData", "data1.txt"), "x");
        File.WriteAllText(Path.Combine(root, "readme.txt"), "x");   // not categorized -> "other"

        return root;
    }

    [Fact]
    public void CatalogFolder_BucketsFilesByExtension()
    {
        var root = MakeModFolder();
        try
        {
            var result = ModInspectService.CatalogFolder(root);
            result.Should().Contain("meshes: 2");
            result.Should().Contain("textures: 1");
            result.Should().Contain("plugins: 1");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void CatalogFolder_ExcludesDecompiledScriptsSource()
    {
        var root = MakeModFolder();
        try
        {
            var result = ModInspectService.CatalogFolder(root);
            // Scripts/MyScript.pex counts; Scripts/Source/MyScript.psc must NOT.
            result.Should().Contain("scripts_pex: 1");
            result.Should().NotContain("scripts_psc:", "the only .psc lives under Scripts\\Source, which must be excluded");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void CatalogFolder_SpecialCasesVoiceAndAnimTextData()
    {
        var root = MakeModFolder();
        try
        {
            var result = ModInspectService.CatalogFolder(root);
            result.Should().Contain("voice: 1").And.Contain("PlayerVoice");
            result.Should().Contain("anim_text_data: 1");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void CatalogFolder_BucketsUnrecognizedExtensionsAsOther()
    {
        var root = MakeModFolder();
        try
        {
            ModInspectService.CatalogFolder(root).Should().Contain("other/unrecognized: 1");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void CatalogFolder_MissingFolder_ReturnsFriendlyError()
    {
        ModInspectService.CatalogFolder(@"C:\does\not\exist\here").Should().Contain("not found");
    }
}
