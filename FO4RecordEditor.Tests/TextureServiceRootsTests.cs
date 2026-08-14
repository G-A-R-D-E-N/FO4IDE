using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;

namespace FO4RecordEditor.Tests;

// Regression coverage for TextureService.AddDataRoots. A NIF resolved from TextureService's own
// BA2-extraction temp cache (used for every mesh the Cell Viewer pulls out of an archive) has no
// ancestor "Data" folder -- climbing from it used to walk all the way to a drive root (the 24-level
// guard), which then made EnsureRootScanned recursively enumerate the ENTIRE drive for .ba2 files and
// hit inaccessible OS junction points (C:\Config.Msi, C:\Users\Default User, ...) that threw
// mid-enumeration and silently failed whatever texture triggered it -- the direct cause of meshes
// rendering flat gray in a live cell. Confirmed from a real debug log showing exactly those
// UnauthorizedAccessExceptions, one per ancestor level climbed.
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
        // Must STOP at "Data" -- must not keep climbing into Fallout4/Games/C:\ (the drive-root-scan hazard).
        roots.Should().NotContain(Path.Combine("C:\\Games", "Fallout4"));
        roots.Should().NotContain("C:\\Games");
    }
}
