using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;

namespace FO4RecordEditor.Core.Tests;

// #87: the per-signature counts-walk retains nothing after #85, but it still reads every plugin and
// costs 7.0 s across a real 663-plugin load order on every process start. RecordCountCache persists
// the counts (~147 numbers per plugin, ~100 KB for the whole order) keyed by the plugin's path, size
// and last-write time.
//
// The invalidation half is the part most likely to go wrong -- a stale hit means the record tree and
// every pagination header report counts for a plugin that no longer has them -- so most of these
// tests are about a hit NOT being served.
//
// Each test points FO4RE_COUNT_CACHE at its own temp file, so nothing here touches the user's real
// cache beside settings.json. They share MutagenLoader's static maps, hence the collection.
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
        RecordCountCache.Flush();          // the debounced timer would otherwise not have fired yet

        // Drop everything in memory: the same state a fresh process starts in, with only the file.
        RecordCountCache.ResetInMemoryForTest();

        RecordCountCache.TryGet(path, out var got).Should().BeTrue("the whole point is surviving a restart");
        got["Weapon"].Should().Be(7);
    }

    // The plugin was rewritten to a different length: the counts behind it can no longer be assumed.
    [Fact]
    public void A_plugin_whose_size_changed_is_a_miss()
    {
        var path = MakePlugin("Resized.esp", "esp");
        RecordCountCache.Put(path, Counts(("Weapon", 1)));

        File.WriteAllText(path, "esp-with-more-records");

        RecordCountCache.TryGet(path, out _).Should().BeFalse();
    }

    // The same length written again -- the case a size-only check would get wrong, and the exact shape
    // of an in-place save that swaps one record for another.
    [Fact]
    public void A_plugin_rewritten_to_the_same_size_is_still_a_miss()
    {
        var path = MakePlugin("Rewritten.esp", "aaaa");
        RecordCountCache.Put(path, Counts(("Weapon", 1)));

        var stamp = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, "bbbb");
        File.SetLastWriteTimeUtc(path, stamp.AddSeconds(1));   // filesystem timestamp granularity

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

    // Storing counts for a file that is not there would key an entry nothing can ever validate.
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

    // ---- which plugins are eligible at all ----

    // An editable copy is mutable in memory: add or delete a record and its counts stop matching the
    // file on disk, while the file's size and write time do not move. There is nothing to validate
    // against, so it must never be cached under that plugin's key.
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

    // Two MO2 profiles can resolve the same plugin name to different mod folders, so the load order's
    // own record of where it read each plugin is what keys the entry -- not the name.
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
