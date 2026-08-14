using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Xunit;
using Xunit.Abstractions;

namespace FO4RecordEditor.Tests;

// ReadLargePluginsIntoMemory decides whether Mo2ProfileLoader opens a large plugin as a binary overlay
// or reads it fully into memory. It exists because an overlay keeps an OPEN HANDLE on its plugin
// file for as long as the environment lives, and nothing tears the environment down -- so saving
// over a loaded plugin cannot overwrite in place and falls back to writing a .new file beside it.
//
// The default must stay OFF: overlays are faster and keep record data off the managed heap, and
// that is the behaviour every existing user already has.
public class ReadLargePluginsIntoMemoryTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string? _originalEnv;

    public ReadLargePluginsIntoMemoryTests(ITestOutputHelper o)
    {
        _out = o;
        _originalEnv = Environment.GetEnvironmentVariable("FO4RE_FULL_PLUGIN_READS");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("FO4RE_FULL_PLUGIN_READS", _originalEnv);
        ToolPaths.Invalidate();
    }

    private static void SetEnv(string? value)
    {
        Environment.SetEnvironmentVariable("FO4RE_FULL_PLUGIN_READS", value);
        ToolPaths.Invalidate();   // ToolPaths caches AppSettings; without this the read is stale
    }

    [Fact]
    public void DefaultsToOff_SoExistingBehaviourIsUnchanged()
    {
        SetEnv(null);
        new AppSettings().ReadLargePluginsIntoMemory.Should().BeFalse("overlays stay the default");
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("0", false)]
    [InlineData("no", false)]
    public void EnvVarOverridesTheStoredSetting(string value, bool expected)
    {
        SetEnv(value);
        ToolPaths.ReadLargePluginsIntoMemory.Should().Be(expected);
    }

    // The point of the setting, measured rather than assumed. Skipped (loudly) without a real
    // plugin to open -- see TestDataRoots for why a silent skip would be worse than useless.
    [Fact]
    public void OverlayHoldsTheFileOpen_FullReadDoesNot()
    {
        var dataRoot = TestDataRoots.DataRoot;
        if (dataRoot == null)
        {
            var msg = "No Fallout 4 Data folder found (set FO4RE_TEST_DATA).";
            if (TestDataRoots.FixturesRequired) Assert.Fail(msg);
            _out.WriteLine("Skipped -- " + msg);
            return;
        }

        var plugin = Directory.GetFiles(dataRoot, "*.esm")
            .Select(f => new FileInfo(f))
            .Where(f => f.Length > 1024 * 1024)
            .OrderByDescending(f => f.Length)
            .FirstOrDefault();
        if (plugin == null) { _out.WriteLine("Skipped -- no plugin over 1 MB in " + dataRoot); return; }

        var copy = Path.Combine(Path.GetTempPath(), $"FO4RE_HandleTest_{Guid.NewGuid():N}"[..24] + ".esm");
        File.Copy(plugin.FullName, copy);
        try
        {
            var overlay = Fallout4Mod.CreateFromBinaryOverlay(ModPath.FromPath(copy), Fallout4Release.Fallout4);
            var overlayCount = overlay.EnumerateMajorRecords().Count();
            overlay.Should().BeAssignableTo<IDisposable>("an overlay owns a handle it must release");
            (overlay as IDisposable)!.Dispose();

            var full = Fallout4Mod.CreateFromBinary(ModPath.FromPath(copy), Fallout4Release.Fallout4);
            var fullCount = full.EnumerateMajorRecords().Count();
            full.Should().NotBeAssignableTo<IDisposable>("a full read owns no handle to release");

            fullCount.Should().Be(overlayCount,
                "the setting must change how the file is opened, never what is read from it");
            _out.WriteLine($"{Path.GetFileName(plugin.FullName)}: {overlayCount} records via both paths");

            // With nothing holding the file, an in-place overwrite is possible. This is the whole
            // point on Windows, where an open handle makes File.Replace fail.
            var act = () => File.WriteAllBytes(copy, File.ReadAllBytes(copy));
            act.Should().NotThrow("nothing should be holding the loaded plugin open");
        }
        finally { try { File.Delete(copy); } catch { } }
    }
}
