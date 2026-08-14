using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;

namespace FO4RecordEditor.Tests;

public class TextureServiceRootsTests
{
    [Fact]
    public void AddDataRoots_SkipsTheClimbEntirely_ForOurOwnTempCachePath()
    {
        var path = Path.Combine(TextureService.TexCacheDir, "ba2_deadbeef.nif");
        var roots = new List<string>();

        TextureService.AddDataRoots(roots, path);

        roots.Should().BeEmpty("a temp-cache path has no ancestor Data folder -- climbing it only " +
            "reaches drive-root/user-profile junctions, never something useful");
    }

    [Fact]
    public void AddDataRoots_ClimbsNormally_ForARealDataTreePath_AndStopsAtData()
    {
        var path = Path.Combine("C:\\Games", "Fallout4", "Data", "Meshes", "Clutter", "Rock01.nif");
        var roots = new List<string>();

        TextureService.AddDataRoots(roots, path);

        roots.Should().Contain(Path.Combine("C:\\Games", "Fallout4", "Data", "Meshes", "Clutter"));
        roots.Should().Contain(Path.Combine("C:\\Games", "Fallout4", "Data", "Meshes"));
        roots.Should().Contain(Path.Combine("C:\\Games", "Fallout4", "Data"));

        roots.Should().NotContain(Path.Combine("C:\\Games", "Fallout4"));
        roots.Should().NotContain("C:\\Games");
    }
}
