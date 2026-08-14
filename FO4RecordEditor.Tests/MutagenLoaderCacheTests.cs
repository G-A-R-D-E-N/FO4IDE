using System;
using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;

namespace FO4RecordEditor.Tests;

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

        for (int i = 0; i < 20; i++)
            MutagenLoader.SeedModIndexForTest($"Mod{i:D2}.esp", new object());

        MutagenLoader.ModIndexCacheCount.Should().Be(5);

        for (int i = 15; i < 20; i++)
            MutagenLoader.ModIndexCacheContains($"Mod{i:D2}.esp").Should().BeTrue($"Mod{i:D2} was seeded last");
        for (int i = 0; i < 15; i++)
            MutagenLoader.ModIndexCacheContains($"Mod{i:D2}.esp").Should().BeFalse($"Mod{i:D2} is the oldest");
    }

    [Fact]
    public void Actively_edited_plugins_are_never_evicted_even_when_oldest()
    {
        MutagenLoader.MaxCachedModIndexes = 3;

        MutagenLoader.EditableMods["MyPatch.esp"] = new object();
        MutagenLoader.SeedModIndexForTest("MyPatch.esp", new object());
        for (int i = 0; i < 20; i++)
            MutagenLoader.SeedModIndexForTest($"Vanilla{i:D2}.esp", new object());

        MutagenLoader.ModIndexCacheContains("MyPatch.esp").Should().BeTrue("EditableMods entries are never evicted");
    }

    [Fact]
    public void Cache_evicts_on_the_record_budget_even_when_far_under_the_entry_cap()
    {
        MutagenLoader.MaxCachedModIndexes = 1000;
        MutagenLoader.MaxCachedIndexRecords = 100_000;

        for (int i = 0; i < 6; i++)
            MutagenLoader.SeedModIndexForTest($"Big{i:D2}.esp", new object(), retainedRecords: 40_000);

        MutagenLoader.ModIndexCacheCount.Should().BeLessThan(6, "the record budget must bite before the entry cap");
        MutagenLoader.ModIndexRetainedRecords.Should().BeLessThanOrEqualTo(100_000);

        MutagenLoader.ModIndexCacheContains("Big05.esp").Should().BeTrue();
        MutagenLoader.ModIndexCacheContains("Big00.esp").Should().BeFalse();
    }

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
