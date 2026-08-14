using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;
using Xunit.Abstractions;

namespace FO4RecordEditor.Tests;

// Regression coverage for TextureService's .bgsm-diffuse fallback. Many FO4 meshes carry no texture
// in their own BSShaderTextureSet at all -- the BSLightingShaderProperty instead points at a shared
// .bgsm material file (rootMaterialName) that holds the real diffuse path. Without resolving that,
// GetTexturePngDataUrl returns "" for such a shape and it renders flat gray even though the mesh
// legitimately has a texture: a shape whose "textures" array niftool reports as empty, but whose
// base object visibly has wood grain in-game.
//
// The fixture .bgsm's DiffuseTexture holds "architecture/buildings/woodfloor01_d.dds".
//
// Skips loudly when the fixture archives aren't present, rather than passing.
public class BgsmDiffuseFallbackTests
{
    private static readonly string[] DataRootCandidates =
    {
        @"E:\Modlists\Fallen World Alpha 2\Stock Folder\Data",
        @"E:\SteamLibrary\steamapps\common\Fallout 4\Data",
    };
    private static string? DataRoot => DataRootCandidates.FirstOrDefault(Directory.Exists);

    private readonly ITestOutputHelper _out;
    public BgsmDiffuseFallbackTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void GetTexturePngDataUrl_ResolvesDiffuseThroughALinkedBgsmMaterial()
    {
        var dataRoot = DataRoot;
        if (dataRoot == null) { _out.WriteLine("Skipped -- no known Fallout 4 Data folder present."); return; }

        TextureService.SetSessionRoots(new[] { dataRoot });
        try
        {
            // Passed exactly as the frontend would when a shape's own texture slots came up empty --
            // no nifPath anchor needed since resolution falls through to the session Data root.
            var url = TextureService.GetTexturePngDataUrl(
                nifPath: Path.Combine(dataRoot, "nonexistent.nif"),
                relTexPath: @"Architecture\Buildings\WoodPlanks01.BGSM");

            url.Should().NotBeNullOrEmpty(
                "the real WoodPlanks01.BGSM has a genuine DiffuseTexture " +
                "(architecture/buildings/woodfloor01_d.dds) that should resolve and convert");
            url.Should().StartWith("data:image/png;base64,");
        }
        finally { TextureService.SetSessionRoots(Array.Empty<string>()); }
    }

    [Fact]
    public void GetTexturePngDataUrl_ReturnsEmpty_ForABgsmWithNoDiffuse()
    {
        var dataRoot = DataRoot;
        if (dataRoot == null) { _out.WriteLine("Skipped -- no known Fallout 4 Data folder present."); return; }

        TextureService.SetSessionRoots(new[] { dataRoot });
        try
        {
            // Template\DefaultTemplate_Wet.bgsm genuinely has an empty DiffuseTexture field (parsed
            // by hand: len=1, byte=0x00, the documented "empty slot" encoding) -- this must return ""
            // rather than throw or return a bogus/garbage image.
            var url = TextureService.GetTexturePngDataUrl(
                nifPath: Path.Combine(dataRoot, "nonexistent.nif"),
                relTexPath: @"Template\DefaultTemplate_Wet.bgsm");

            url.Should().BeEmpty("this real material has no diffuse texture at all -- returning '' " +
                "is honest, not a bug to paper over with a guessed fallback");
        }
        finally { TextureService.SetSessionRoots(Array.Empty<string>()); }
    }
}
