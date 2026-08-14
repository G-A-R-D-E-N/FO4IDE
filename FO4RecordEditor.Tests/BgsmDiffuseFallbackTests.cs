using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;
using Xunit.Abstractions;

namespace FO4RecordEditor.Tests;











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



            var url = TextureService.GetTexturePngDataUrl(
                nifPath: Path.Combine(dataRoot, "nonexistent.nif"),
                relTexPath: @"Template\DefaultTemplate_Wet.bgsm");

            url.Should().BeEmpty("this real material has no diffuse texture at all -- returning '' " +
                "is honest, not a bug to paper over with a guessed fallback");
        }
        finally { TextureService.SetSessionRoots(Array.Empty<string>()); }
    }
}
