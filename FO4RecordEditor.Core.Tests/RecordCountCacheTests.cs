using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;

namespace FO4RecordEditor.Core.Tests;

[Collection("MutagenLoaderCache")]
public class RecordCountCacheTests : IDisposable
{
    private readonly string _dir;
    private readonly string? _originalOverride;

    public RecordCountCacheTests()
    {
        _originalOverride = Environment.GetEnvironmentVariable("FO4RE_COUNT_CACHE");
        _dir = Path.Combine(Path.GetTempPath(), "fo4re-counts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable("FO4RE_COUNT_CACHE", Path.Combine(_dir, "record-counts.json"));
        RecordCountCache.ResetForTest();
        MutagenLoader.EditableMods.Clear();
        MutagenLoader.LooseModPaths.Clear();
        MutagenLoader.PluginSourcePaths.Clear();
    }

    public void Dispose()
    {
        RecordCountCache.ResetForTest();
        Environment.SetEnvironmentVariable("FO4RE_COUNT_CACHE", _originalOverride);
        MutagenLoader.EditableMods.Clear();
        MutagenLoader.LooseModPaths.Clear();
        MutagenLoader.PluginSourcePaths.Clear();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string MakePlugin(string name, string content = "esp")
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static Dictionary<string, int> Counts(params (string sig, int n)[] pairs)
    {
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (sig, n) in pairs) d[sig] = n;
        return d;
    }

    [Fact]
    public void Counts_round_trip_for_an_unchanged_plugin()
    {
        var path = MakePlugin("Round.esp");
        RecordCountCache.Put(path, Counts(("Weapon", 252), ("Cell", 3), ("PlacedObject", 1_244_528)));

        RecordCountCache.TryGet(path, out var got).Should().BeTrue();
        got.Should().HaveCount(3);
        got["Weapon"].Should().Be(252);
        got["PlacedObject"].Should().Be(1_244_528);
    }

    [Fact]
    public void Counts_survive_a_process_restart()
    {
        var path = MakePlugin("Persisted.esp");
        RecordCountCache.Put(path, Counts(("Weapon", 7)));
        RecordCountCache.Flush();

        RecordCountCache.ResetInMemoryForTest();

        RecordCountCache.TryGet(path, out var got).Should().BeTrue("the whole point is surviving a restart");
        got["Weapon"].Should().Be(7);
    }

    [Fact]
    public void A_plugin_whose_size_changed_is_a_miss()
    {
        var path = MakePlugin("Resized.esp", "esp");
        RecordCountCache.Put(path, Counts(("Weapon", 1)));

        File.WriteAllText(path, "esp-with-more-records");

        RecordCountCache.TryGet(path, out _).Should().BeFalse();
    }

    [Fact]
    public void A_plugin_rewritten_to_the_same_size_is_still_a_miss()
    {
        var path = MakePlugin("Rewritten.esp", "aaaa");
        RecordCountCache.Put(path, Counts(("Weapon", 1)));

        var stamp = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, "bbbb");
        File.SetLastWriteTimeUtc(path, stamp.AddSeconds(1));

        new FileInfo(path).Length.Should().Be(4, "this test is only meaningful if the size is unchanged");
        RecordCountCache.TryGet(path, out _).Should().BeFalse();
    }

    [Fact]
    public void A_plugin_that_no_longer_exists_is_a_miss()
    {
        var path = MakePlugin("Deleted.esp");
        RecordCountCache.Put(path, Counts(("Weapon", 1)));

        File.Delete(path);

        RecordCountCache.TryGet(path, out _).Should().BeFalse();
    }

    [Fact]
    public void A_plugin_that_was_never_stored_is_a_miss()
    {
        RecordCountCache.TryGet(MakePlugin("Unknown.esp"), out _).Should().BeFalse();
    }

    [Fact]
    public void Storing_counts_for_a_missing_file_stores_nothing()
    {
        RecordCountCache.Put(Path.Combine(_dir, "Nope.esp"), Counts(("Weapon", 1)));
        RecordCountCache.Count.Should().Be(0);
    }

    [Fact]
    public void A_corrupt_cache_file_is_discarded_rather_than_throwing()
    {
        var path = MakePlugin("Corrupt.esp");
        File.WriteAllText(Environment.GetEnvironmentVariable("FO4RE_COUNT_CACHE")!, "{ this is not json");

        RecordCountCache.TryGet(path, out _).Should().BeFalse();
        RecordCountCache.Count.Should().Be(0);
    }

    [Fact]
    public void A_cache_file_from_another_version_is_discarded()
    {
        var path = MakePlugin("Versioned.esp");
        RecordCountCache.Put(path, Counts(("Weapon", 1)));
        RecordCountCache.Flush();

        var file = Environment.GetEnvironmentVariable("FO4RE_COUNT_CACHE")!;
        File.WriteAllText(file, File.ReadAllText(file).Replace("\"version\": 1", "\"version\": 99"));
        RecordCountCache.ResetInMemoryForTest();

        RecordCountCache.TryGet(path, out _).Should().BeFalse();
    }

    [Fact]
    public void Disabling_the_cache_turns_off_both_halves()
    {
        var path = MakePlugin("Disabled.esp");
        RecordCountCache.Put(path, Counts(("Weapon", 1)));

        RecordCountCache.Enabled = false;
        try
        {
            RecordCountCache.TryGet(path, out _).Should().BeFalse();
            RecordCountCache.Put(MakePlugin("Other.esp"), Counts(("Weapon", 2)));
            RecordCountCache.Count.Should().Be(1, "the Put while disabled must not have landed");
        }
        finally { RecordCountCache.Enabled = true; }
    }

    [Fact]
    public void An_actively_edited_plugin_is_never_cached()
    {
        MutagenLoader.PluginSourcePaths["MyPatch.esp"] = MakePlugin("MyPatch.esp");
        MutagenLoader.EditableMods["MyPatch.esp"] = new object();

        MutagenLoader.CountsCacheKeyFor("MyPatch.esp").Should().BeNull();
    }

    [Fact]
    public void A_loose_opened_plugin_is_keyed_by_the_path_it_was_opened_from()
    {
        var loose = MakePlugin("Loose.esp");
        MutagenLoader.LooseModPaths["Loose.esp"] = loose;

        MutagenLoader.CountsCacheKeyFor("Loose.esp").Should().Be(loose);
    }

    [Fact]
    public void A_load_order_plugin_is_keyed_by_where_the_loader_read_it()
    {
        var path = MakePlugin("FromModlist.esp");
        MutagenLoader.PluginSourcePaths["FromModlist.esp"] = path;

        MutagenLoader.CountsCacheKeyFor("FromModlist.esp").Should().Be(path);
    }

    [Fact]
    public void A_plugin_of_unknown_origin_is_not_cached()
    {
        MutagenLoader.CountsCacheKeyFor("WhoKnows.esp").Should().BeNull("a walk is cheaper than a wrong count");
    }
}
