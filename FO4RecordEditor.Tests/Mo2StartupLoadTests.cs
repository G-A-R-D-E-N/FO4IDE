using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;

namespace FO4RecordEditor.Tests;










public class Mo2StartupLoadTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fo4re-mo2-" + Guid.NewGuid().ToString("N")[..8]);

    private readonly GlobalStateIsolation _state = new();

    public void Dispose()
    {
        _state.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }








    private static string? FindSmallPlugin()
    {
        var data = TestDataRoots.DataRoot;
        if (data == null || !Directory.Exists(data)) return null;
        return Directory.EnumerateFiles(data, "*.es*", SearchOption.TopDirectoryOnly)
            .Where(f => f.EndsWith(".esl", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".esp", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal)
            .FirstOrDefault(f => new FileInfo(f).Length is > 4096 and < 60 * 1024);
    }

    private void WriteProfile(params string[] pluginNames)
    {
        var profileDir = Path.Combine(_root, "profiles", "Test");
        Directory.CreateDirectory(profileDir);
        File.WriteAllLines(Path.Combine(profileDir, "plugins.txt"), pluginNames.Select(p => "*" + p));
        File.WriteAllLines(Path.Combine(profileDir, "modlist.txt"), new[] { "+TestMod" });
        Directory.CreateDirectory(Path.Combine(_root, "mods", "TestMod"));
        Directory.CreateDirectory(Path.Combine(_root, "EmptyData"));
    }

    private string ModFile(string name) => Path.Combine(_root, "mods", "TestMod", name);

    [Fact]
    public void A_plugin_that_fails_to_parse_is_reported_with_a_reason_not_silently_dropped()
    {
        var source = FindSmallPlugin();
        if (source == null)
        {
            TestDataRoots.FixturesRequired.Should().BeFalse("FO4RE_REQUIRE_FIXTURES=1 but no small plugin was found to build the fixture from");
            return;
        }

        var ext = Path.GetExtension(source);
        string good = "Good" + ext, corrupt = "Corrupt" + ext;
        WriteProfile(good, corrupt);
        File.Copy(source, ModFile(good), overwrite: true);




        var bytes = File.ReadAllBytes(source);
        for (int i = 256; i < bytes.Length; i++) bytes[i] = 0xFF;
        File.WriteAllBytes(ModFile(corrupt), bytes);

        var (_, loaded) = Mo2ProfileLoader.Load(_root, "Test", Path.Combine(_root, "EmptyData"));

        loaded.Should().Contain(good, "the valid plugin must still load");
        loaded.Should().NotContain(corrupt);

        Mo2ProfileLoader.FailedToLoad.Should().ContainSingle()
            .Which.name.Should().Be(corrupt);
        Mo2ProfileLoader.FailedToLoad[0].reason.Should().NotBeNullOrWhiteSpace("the reason is the whole point -- it distinguishes corrupt from missing");
    }

    [Fact]
    public void A_clean_load_reports_no_failures()
    {
        var source = FindSmallPlugin();
        if (source == null)
        {
            TestDataRoots.FixturesRequired.Should().BeFalse("FO4RE_REQUIRE_FIXTURES=1 but no small plugin was found to build the fixture from");
            return;
        }

        var good = "Good" + Path.GetExtension(source);
        WriteProfile(good);
        File.Copy(source, ModFile(good), overwrite: true);

        var (_, loaded) = Mo2ProfileLoader.Load(_root, "Test", Path.Combine(_root, "EmptyData"));

        loaded.Should().Contain(good);
        Mo2ProfileLoader.FailedToLoad.Should().BeEmpty();
    }
















    [Fact]
    public void Saving_over_a_plugin_the_environment_holds_open_replaces_it_in_place()
    {
        var source = FindSmallPlugin();
        if (source == null)
        {
            TestDataRoots.FixturesRequired.Should().BeFalse("FO4RE_REQUIRE_FIXTURES=1 but no small plugin was found to build the fixture from");
            return;
        }

        var name = "SwapTarget" + Path.GetExtension(source);
        WriteProfile(name);
        File.Copy(source, ModFile(name), overwrite: true);

        var (env, loaded) = Mo2ProfileLoader.Load(_root, "Test", Path.Combine(_root, "EmptyData"));
        loaded.Should().Contain(name);

        var mo2 = (Mo2ProfileLoader.Mo2GameEnvironment)env;
        var cacheBefore = mo2.LinkCache;

        WriteService.OpenPlugin(name, env).Should().NotStartWith("", "the fixture plugin should open for editing");

        var result = WriteService.SavePlugin(name, null, env);

        result.Should().StartWith("Saved", "the in-place swap should succeed, not fall back");
        File.Exists(ModFile(name) + ".new").Should().BeFalse("a .new fallback means the swap did not happen");
        File.Exists(ModFile(name)).Should().BeTrue();
        Directory.EnumerateFiles(Path.GetDirectoryName(ModFile(name))!, "*.tmp").Should().BeEmpty("the temp file must be consumed");



        var modKey = Mutagen.Bethesda.Plugins.ModKey.FromNameAndExtension(name);
        var listing = mo2.LoadOrder.ListedOrder.SingleOrDefault(l => l.ModKey == modKey);
        listing.Should().NotBeNull("the plugin must still be in the load order after the swap");
        listing!.Mod.Should().NotBeNull("the plugin must be reopened after the swap");
        mo2.LinkCache.Should().NotBeSameAs(cacheBefore, "a stale link cache would be reading a disposed overlay");
        MutagenLoader.LinkCache.Should().BeSameAs(mo2.LinkCache, "both holders must be updated together");


        using var reread = Mutagen.Bethesda.Fallout4.Fallout4Mod.CreateFromBinaryOverlay(
            ModFile(name), Mutagen.Bethesda.Fallout4.Fallout4Release.Fallout4);
        reread.ModKey.FileName.String.Should().Be(name);
    }





    [Fact]
    public void A_missing_plugin_is_not_reported_as_a_load_failure()
    {
        WriteProfile("NotThere.esp");

        var (_, loaded) = Mo2ProfileLoader.Load(_root, "Test", Path.Combine(_root, "EmptyData"));

        loaded.Should().NotContain("NotThere.esp");
        Mo2ProfileLoader.FailedToLoad.Should().BeEmpty("absent is not the same as corrupt");
    }
}
