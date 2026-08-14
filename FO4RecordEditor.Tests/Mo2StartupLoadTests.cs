using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;

namespace FO4RecordEditor.Tests;

/// <summary>
/// A plugin that is found on disk but will not parse must be reported, not dropped.
/// </summary>
/// <remarks>
/// It used to be counted alongside genuinely missing plugins in a "could not be resolved" line with
/// no reason given, so a corrupt plugin was indistinguishable from an absent one. On the reference
/// modlist that silently removed real plugins from the load order, which every downstream answer --
/// conflict scans, patches, master lists -- was then computed without.
/// </remarks>
public class Mo2StartupLoadTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fo4re-mo2-" + Guid.NewGuid().ToString("N")[..8]);

    private readonly GlobalStateIsolation _state = new();

    public void Dispose()
    {
        _state.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    /// <summary>
    /// A real, small, genuinely parseable plugin to build the fixture from. Taken from the top level
    /// of the resolved Data folder -- the Creation Club .esl files there are a few KB and always
    /// present alongside the base game, which makes this portable to any install. Deliberately not
    /// derived by walking up to an MO2 mods folder: that assumed a modlist layout the Data folder
    /// does not have to sit inside.
    /// </summary>
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

        // Keep the TES4 header intact so the file is recognisably a plugin, then replace the record
        // data after it with garbage so parsing gets far enough in to fail rather than being rejected
        // outright. Truncating alone can stop cleanly at a record boundary and parse "successfully".
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

    /// <summary>
    /// Saving over a plugin the environment holds open must replace it in place, not fall back to
    /// writing a <c>.new</c> file beside it.
    /// </summary>
    /// <remarks>
    /// Every plugin in the load order is now an mmap overlay, so this path is the norm rather than
    /// the exception: the save releases that one plugin's overlay, swaps the file, and reopens it.
    /// <para>
    /// What this proves on Linux is the state machine -- that the swap completes, the plugin is still
    /// in the load order afterwards with a live mod, the link cache is rebuilt rather than left
    /// holding the disposed overlay, and no <c>.new</c> fallback file is produced. It does NOT prove
    /// the handle-release itself, because replacing a mapped file only fails on Windows; on Linux the
    /// direct write would have succeeded regardless.
    /// </para>
    /// </remarks>
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

        // The environment must still be coherent: the plugin is back in the load order with a live
        // mod, and the link cache was rebuilt rather than left pointing at the disposed overlay.
        var modKey = Mutagen.Bethesda.Plugins.ModKey.FromNameAndExtension(name);
        var listing = mo2.LoadOrder.ListedOrder.SingleOrDefault(l => l.ModKey == modKey);
        listing.Should().NotBeNull("the plugin must still be in the load order after the swap");
        listing!.Mod.Should().NotBeNull("the plugin must be reopened after the swap");
        mo2.LinkCache.Should().NotBeSameAs(cacheBefore, "a stale link cache would be reading a disposed overlay");
        MutagenLoader.LinkCache.Should().BeSameAs(mo2.LinkCache, "both holders must be updated together");

        // And the file on disk is still a valid plugin.
        using var reread = Mutagen.Bethesda.Fallout4.Fallout4Mod.CreateFromBinaryOverlay(
            ModFile(name), Mutagen.Bethesda.Fallout4.Fallout4Release.Fallout4);
        reread.ModKey.FileName.String.Should().Be(name);
    }

    /// <summary>
    /// A plugin that is simply absent stays in the "could not be resolved" bucket -- it is a
    /// different problem from one that was found and would not parse, and must not be conflated.
    /// </summary>
    [Fact]
    public void A_missing_plugin_is_not_reported_as_a_load_failure()
    {
        WriteProfile("NotThere.esp");

        var (_, loaded) = Mo2ProfileLoader.Load(_root, "Test", Path.Combine(_root, "EmptyData"));

        loaded.Should().NotContain("NotThere.esp");
        Mo2ProfileLoader.FailedToLoad.Should().BeEmpty("absent is not the same as corrupt");
    }
}
