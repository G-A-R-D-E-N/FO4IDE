using System.IO;
using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;

namespace FO4RecordEditor.Tests;

// The write layer used to open and overwrite any resolvable plugin, so set_field("Fallout4.esm", ...)
// followed by save_plugin("Fallout4.esm") would overwrite the user's game master -- a file that can
// only be restored by a Steam re-validate or reinstall.
public class ProtectedPluginsTests
{
    [Theory]
    [InlineData("Fallout4.esm")]
    [InlineData("fallout4.esm")]
    [InlineData("DLCCoast.esm")]
    [InlineData("DLCNukaWorld.esm")]
    [InlineData("DLCUltraHighResolution.esm")]
    public void VanillaMasters_AreProtected(string name) =>
        ProtectedPlugins.IsProtected(name).Should().BeTrue();

    [Theory]
    [InlineData("MyPatch.esp")]
    [InlineData("Fallout4Patch.esp")]
    [InlineData("NotFallout4.esm")]
    [InlineData("")]
    [InlineData(null)]
    public void OtherPlugins_AreNot(string? name) =>
        ProtectedPlugins.IsProtected(name).Should().BeFalse();

    [Fact]
    public void ValidateSavePath_AcceptsPluginExtensions()
    {
        var dir = Path.GetTempPath();
        ProtectedPlugins.ValidateSavePath(Path.Combine(dir, "MyPatch.esp")).Should().BeNull();
        ProtectedPlugins.ValidateSavePath(Path.Combine(dir, "MyPatch.esm")).Should().BeNull();
        ProtectedPlugins.ValidateSavePath(Path.Combine(dir, "MyPatch.esl")).Should().BeNull();
    }

    [Fact]
    public void ValidateSavePath_RejectsNonPluginExtension() =>
        ProtectedPlugins.ValidateSavePath(Path.Combine(Path.GetTempPath(), "notes.txt"))
            .Should().NotBeNull().And.Contain(".esp");

    [Fact]
    public void ValidateSavePath_RejectsVanillaMasterDestination() =>
        ProtectedPlugins.ValidateSavePath(Path.Combine(Path.GetTempPath(), "Fallout4.esm"))
            .Should().NotBeNull().And.Contain("write-protected");

    // The check runs on the normalized path, so '..' cannot smuggle a protected name past it.
    [Fact]
    public void ValidateSavePath_NormalizesBeforeCheckingTheName() =>
        ProtectedPlugins.ValidateSavePath(@"C:\mods\subdir\..\Fallout4.esm")
            .Should().NotBeNull().And.Contain("write-protected");

    [Fact]
    public void OpenPlugin_RefusesAVanillaMaster()
    {
        var exec = new PluginToolExecutor(() => null);
        var result = exec.ExecuteWithStatus("open_plugin", "{\"plugin\":\"Fallout4.esm\"}");

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("write-protected");
    }

    [Fact]
    public void SavePlugin_RefusesAVanillaMasterTarget()
    {
        var exec = new PluginToolExecutor(() => null);
        var result = exec.ExecuteWithStatus("save_plugin", "{\"plugin\":\"Fallout4.esm\"}");

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("write-protected");
    }

    // A real plugin must not be writable to an arbitrary destination filename.
    [Fact]
    public void SavePlugin_RefusesAVanillaMasterDestinationPath()
    {
        var plugin = $"GuardTest_{Guid.NewGuid():N}.esp";
        var exec = new PluginToolExecutor(() => null);
        exec.Execute("create_plugin", $"{{\"name\":\"{plugin}\"}}").Should().Contain("Created new plugin");

        var dest = Path.Combine(Path.GetTempPath(), "Fallout4.esm").Replace("\\", "\\\\");
        var result = exec.ExecuteWithStatus("save_plugin",
            $"{{\"plugin\":\"{plugin}\",\"path\":\"{dest}\"}}");

        result.IsError.Should().BeTrue();
        result.Text.Should().Contain("write-protected");
        File.Exists(Path.Combine(Path.GetTempPath(), "Fallout4.esm")).Should().BeFalse();
    }
}
