using System;
using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;

namespace FO4RecordEditor.Tests;

// MutagenLoader._modIndexCache used to grow unbounded: every plugin a sweep (search_all /
// scan_conflicts / list_records across a 650+-plugin load order) touched cached a full per-mod index
// forever, driving multi-GB long-session memory growth. It is now an LRU capped at
// MaxCachedModIndexes, with actively-edited plugins (EditableMods) protected from eviction. These
// tests pin that behavior down against the real StoreModIndex/EvictModIndexLru path via the internal
// seams -- no real mod enumeration needed, so they're deterministic and fast.
//
// The tests mutate MutagenLoader's shared static cache + EditableMods + the cap; each resets that
// state on entry and the class restores the default cap on dispose. They are grouped in one
// non-collection-parallel class so they don't race each other.
[Collection("MutagenLoaderCache")]
public class MutagenLoaderCacheTests : IDisposable
{
    private readonly int _originalCap = MutagenLoader.MaxCachedModIndexes;
    private readonly long _originalRecordCap = MutagenLoader.MaxCachedIndexRecords;

    public MutagenLoaderCacheTests()
    {
        MutagenLoader.ClearModIndexCacheForTest();
        MutagenLoader.EditableMods.Clear();
    }

    public void Dispose()
    {
        MutagenLoader.MaxCachedModIndexes = _originalCap;
        MutagenLoader.MaxCachedIndexRecords = _originalRecordCap;
        MutagenLoader.ClearModIndexCacheForTest();
        MutagenLoader.EditableMods.Clear();
    }

    [Fact]
    public void Cache_is_bounded_by_the_cap_and_evicts_least_recently_used()
    {
        MutagenLoader.MaxCachedModIndexes = 5;

        // Seed well past the cap. Each Seed goes through the real store+evict path.
        for (int i = 0; i < 20; i++)
            MutagenLoader.SeedModIndexForTest($"Mod{i:D2}.esp", new object());

        // Single-threaded, so eviction settles exactly at the cap.
        MutagenLoader.ModIndexCacheCount.Should().Be(5);

        // The last 5 seeded are the most-recently-used and must survive; the early ones are gone.
        for (int i = 15; i < 20; i++)
            MutagenLoader.ModIndexCacheContains($"Mod{i:D2}.esp").Should().BeTrue($"Mod{i:D2} was seeded last");
        for (int i = 0; i < 15; i++)
            MutagenLoader.ModIndexCacheContains($"Mod{i:D2}.esp").Should().BeFalse($"Mod{i:D2} is the oldest");
    }

    [Fact]
    public void Actively_edited_plugins_are_never_evicted_even_when_oldest()
    {
        MutagenLoader.MaxCachedModIndexes = 3;

        // Seed the edited plugin FIRST so it is the least-recently-used by tick, then flood the cache.
        MutagenLoader.EditableMods["MyPatch.esp"] = new object();
        MutagenLoader.SeedModIndexForTest("MyPatch.esp", new object());
        for (int i = 0; i < 20; i++)
            MutagenLoader.SeedModIndexForTest($"Vanilla{i:D2}.esp", new object());

        // Despite being the oldest, the in-edit plugin's index is protected from eviction.
        MutagenLoader.ModIndexCacheContains("MyPatch.esp").Should().BeTrue("EditableMods entries are never evicted");
    }

    // An entry cap alone is not a memory bound, which is why MaxCachedIndexRecords exists. Per-plugin
    // cost varies about 47,000x on a real load order: the median plugin holds 33 records (~40 KB)
    // while Fallout4.esm holds 1,549,276 (~1836 MB). Four big plugins can therefore blow any sane
    // memory budget while sitting far below a 64-entry cap, so eviction has to count records.
    [Fact]
    public void Cache_evicts_on_the_record_budget_even_when_far_under_the_entry_cap()
    {
        MutagenLoader.MaxCachedModIndexes = 1000;      // deliberately not the binding constraint
        MutagenLoader.MaxCachedIndexRecords = 100_000;

        for (int i = 0; i < 6; i++)
            MutagenLoader.SeedModIndexForTest($"Big{i:D2}.esp", new object(), retainedRecords: 40_000);

        MutagenLoader.ModIndexCacheCount.Should().BeLessThan(6, "the record budget must bite before the entry cap");
        MutagenLoader.ModIndexRetainedRecords.Should().BeLessThanOrEqualTo(100_000);

        // LRU order still holds: the most recently seeded survives.
        MutagenLoader.ModIndexCacheContains("Big05.esp").Should().BeTrue();
        MutagenLoader.ModIndexCacheContains("Big00.esp").Should().BeFalse();
    }

    // A plugin that is only ever type-queried retains nothing, so it must not consume the budget.
    [Fact]
    public void Indexes_that_retain_no_records_do_not_count_against_the_record_budget()
    {
        MutagenLoader.MaxCachedModIndexes = 1000;
        MutagenLoader.MaxCachedIndexRecords = 10;

        for (int i = 0; i < 50; i++)
            MutagenLoader.SeedModIndexForTest($"Counts{i:D2}.esp", new object(), retainedRecords: 0);

        MutagenLoader.ModIndexCacheCount.Should().Be(50, "counts-only indexes hold no records to evict");
        MutagenLoader.ModIndexRetainedRecords.Should().Be(0);
    }

    // The record budget must not be able to evict a plugin the user is actively editing, for the
    // same reason the entry cap cannot: live overrides and EditorID resolution filter on "is this
    // plugin indexed", so dropping it can make an in-progress edit silently stop resolving.
    [Fact]
    public void Actively_edited_plugins_survive_the_record_budget_too()
    {
        MutagenLoader.MaxCachedModIndexes = 1000;
        MutagenLoader.MaxCachedIndexRecords = 1_000;

        MutagenLoader.EditableMods["MyPatch.esp"] = new object();
        MutagenLoader.SeedModIndexForTest("MyPatch.esp", new object(), retainedRecords: 500_000);
        for (int i = 0; i < 5; i++)
            MutagenLoader.SeedModIndexForTest($"Other{i:D2}.esp", new object(), retainedRecords: 5_000);

        MutagenLoader.ModIndexCacheContains("MyPatch.esp").Should().BeTrue();
    }
}
