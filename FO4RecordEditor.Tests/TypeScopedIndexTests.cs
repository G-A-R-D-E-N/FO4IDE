using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services;
using Mutagen.Bethesda.Fallout4;
using Xunit;

namespace FO4RecordEditor.Tests;

/// <summary>
/// The per-mod index is lazy and type-scoped: it eagerly stores only per-signature COUNTS, and
/// materializes a signature's records the first time that signature is asked for. These tests pin
/// down that it still answers exactly what the old eager full index answered.
/// </summary>
/// <remarks>
/// Why this needs real data: the trap being guarded against is invisible on a synthetic mod.
/// Type-scoped enumeration must be handed the GETTER INTERFACE (<c>IWeaponGetter</c>). Handing it the
/// concrete record class does not throw -- it silently returns the wrong set. Measured against
/// Fallout4.esm: <c>typeof(Weapon)</c> yields 0 records where 252 exist, <c>typeof(PlacedObject)</c>
/// yields 0 where 1,244,528 exist, and <c>typeof(Cell)</c> yields 5 where 40,165 exist. A binary
/// overlay's records are <c>WeaponBinaryOverlay</c> and friends, which implement the interface but
/// are not the class.
/// <para>
/// The second trap, also only visible on real data: for polymorphic on-disk record families the
/// scoped enumeration returns the WHOLE family rather than the requested subtype. On Fallout4.esm
/// 17 of 147 signatures behave this way -- every <c>GameSetting*</c> variant yields all 2,039 GMSTs,
/// every <c>Global*</c> yields all 1,346 GLOBs, the <c>ObjectModification</c> family yields all
/// 2,409 OMODs. It is always a superset, never a subset, which is why filtering on the exact
/// registration name is the correct fix rather than a workaround.
/// </para>
/// </remarks>
[Collection("MutagenLoaderCache")]
public class TypeScopedIndexTests : IDisposable
{
    private readonly string? _plugin = TestDataRoots.Archive("Fallout4.esm");
    private const string ModName = "Fallout4.esm";

    public TypeScopedIndexTests()
    {
        MutagenLoader.ClearModIndexCacheForTest();
        MutagenLoader.EditableMods.Clear();
    }

    public void Dispose()
    {
        MutagenLoader.ClearModIndexCacheForTest();
        MutagenLoader.LooseMods.TryRemove(ModName, out _);
        MutagenLoader.LooseModPaths.TryRemove(ModName, out _);
    }

    private bool Skip()
    {
        if (_plugin != null) return false;
        TestDataRoots.FixturesRequired.Should().BeFalse("FO4RE_REQUIRE_FIXTURES=1 but Fallout4.esm was not found");
        return true;
    }

    /// <summary>Ground truth built the old way: one eager walk, bucketed by signature.</summary>
    private static (Dictionary<string, int> counts, Dictionary<string, List<string>> firstRows) Truth(string path)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var rows = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        using var mod = Fallout4Mod.CreateFromBinaryOverlay(path, Fallout4Release.Fallout4);
        foreach (var rec in mod.EnumerateMajorRecords())
        {
            var sig = rec.Registration.Name;
            counts[sig] = counts.TryGetValue(sig, out var n) ? n + 1 : 1;
            if (!rows.TryGetValue(sig, out var l)) { l = new(); rows[sig] = l; }
            if (l.Count < 25) l.Add($"{rec.FormKey}|{rec.EditorID ?? ""}");
        }
        return (counts, rows);
    }

    private void Install()
    {
        MutagenLoader.LooseMods[ModName] = Fallout4Mod.CreateFromBinaryOverlay(_plugin!, Fallout4Release.Fallout4);
        MutagenLoader.LooseModPaths[ModName] = _plugin!;
    }

    [Fact]
    public void Every_signature_count_matches_an_eager_enumeration()
    {
        if (Skip()) return;
        var (truth, _) = Truth(_plugin!);
        Install();

        var reported = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in MutagenLoader.QueryRecordTypes(null, ModName))
        {
            var i = entry.LastIndexOf(" (", StringComparison.Ordinal);
            reported[entry[..i]] = int.Parse(entry[(i + 2)..^1]);
        }

        reported.Should().BeEquivalentTo(truth);
    }

    [Theory]
    // A small ordinary type, the largest one in the game, and three members of polymorphic
    // families that the registration-name filter exists to keep honest.
    [InlineData("Weapon")]
    [InlineData("PlacedObject")]
    [InlineData("GameSettingFloat")]
    [InlineData("GlobalShort")]
    [InlineData("ObjectModification")]
    public void Type_scoped_listing_matches_an_eager_enumeration(string sig)
    {
        if (Skip()) return;
        var (truthCounts, truthRows) = Truth(_plugin!);
        truthCounts.Should().ContainKey(sig, "the fixture should contain this record type");
        Install();

        MutagenLoader.CountRecordsOfType(null, ModName, sig).Should().Be(truthCounts[sig]);

        var rows = MutagenLoader.QueryRecordsOfType(null, ModName, sig, limit: 25, offset: 0)
            .Select(r => $"{r.formKey}|{r.editorId}")
            .ToList();
        rows.Should().Equal(truthRows[sig].Take(rows.Count));
    }

    [Fact]
    public void Listing_one_small_type_does_not_materialize_the_whole_plugin()
    {
        if (Skip()) return;
        Install();

        // 252 WEAP out of 1,549,276 records, 80% of which are PlacedObject. Retaining the whole
        // plugin here is the 1836 MB regression this design exists to prevent, so assert that the
        // records held afterwards are on the order of the type asked for, not the plugin.
        MutagenLoader.QueryRecordsOfType(null, ModName, "Weapon", limit: 25, offset: 0).Should().NotBeEmpty();

        MutagenLoader.ModIndexRetainedRecords.Should().BeLessThan(10_000,
            "only the requested signature's records may be retained, not all 1.5M");
    }

    [Fact]
    public void Asking_only_for_record_types_retains_no_records_at_all()
    {
        if (Skip()) return;
        Install();

        MutagenLoader.QueryRecordTypes(null, ModName).Should().NotBeEmpty();

        MutagenLoader.ModIndexRetainedRecords.Should().Be(0,
            "the counts-only walk deliberately keeps nothing alive");
    }

    [Fact]
    public void Searching_a_plugin_does_not_pin_its_records()
    {
        if (Skip()) return;
        Install();

        MutagenLoader.QuerySearch(null, ModName, "Steel", limit: 20);

        MutagenLoader.ModIndexRetainedRecords.Should().Be(0,
            "a whole-plugin search streams and keeps only its hits");
    }

    [Fact]
    public void An_absent_signature_returns_empty_rather_than_throwing()
    {
        if (Skip()) return;
        Install();

        MutagenLoader.CountRecordsOfType(null, ModName, "NoSuchRecordType").Should().Be(0);
        MutagenLoader.QueryRecordsOfType(null, ModName, "NoSuchRecordType", 10).Should().BeEmpty();
    }
}
