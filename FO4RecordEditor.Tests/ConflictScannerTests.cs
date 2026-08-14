using FluentAssertions;
using FO4RecordEditor.Services;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Noggog;
using Xunit;

namespace FO4RecordEditor.Tests;

// Characterization tests for ConflictScanner.Scan, pinning the "only real conflicts, like xEdit"
// behavior. The whole filter rests on ConflictScanner.RecordsEqual (IMajorRecordGetter.Equals)
// giving deep VALUE equality. If Mutagen ever returns reference equality here, the first test
// (identical overrides) flips to reporting a phantom conflict -- this test would catch that.
public class ConflictScannerTests
{
    // Two plugins (A then B) both define the SAME FormKey weapon, with caller-supplied content.
    private static object BuildEnv(out FormKey fk, string editorIdA, string[] keywordsA,
                                                   string editorIdB, string[] keywordsB)
    {
        // ConflictScanner deliberately overlays process-global editable mods onto the supplied
        // environment. This fixture owns exactly A and B, so isolate it from plugins authored by
        // earlier tests instead of relying on test-class execution order.
        MutagenLoader.EditableMods.Clear();
        ConflictScanner.InvalidateCache();

        var a = new Fallout4Mod(ModKey.FromNameAndExtension("A.esp"), Fallout4Release.Fallout4);
        fk = new FormKey(a.ModKey, 0x800);

        var wa = new Weapon(fk, Fallout4Release.Fallout4) { EditorID = editorIdA, Keywords = new() };
        foreach (var k in keywordsA) wa.Keywords!.Add(new FormLink<IKeywordGetter>(FormKey.Factory(k)));
        a.Weapons.Add(wa);

        var b = new Fallout4Mod(ModKey.FromNameAndExtension("B.esp"), Fallout4Release.Fallout4);
        var wb = new Weapon(fk, Fallout4Release.Fallout4) { EditorID = editorIdB, Keywords = new() };  // override of A
        foreach (var k in keywordsB) wb.Keywords!.Add(new FormLink<IKeywordGetter>(FormKey.Factory(k)));
        b.Weapons.Add(wb);

        var listings = new List<IModListingGetter<IFallout4ModGetter>>
        {
            new ModListing<IFallout4ModGetter>(a.ModKey, a, enabled: true, ghostSuffix: string.Empty),
            new ModListing<IFallout4ModGetter>(b.ModKey, b, enabled: true, ghostSuffix: string.Empty),
        };
        var lo = new LoadOrder<IModListingGetter<IFallout4ModGetter>>(listings, disposeItems: false);
        var lc = lo.ToImmutableLinkCache<IFallout4Mod, IFallout4ModGetter>();
        return new Mo2ProfileLoader.Mo2GameEnvironment
        {
            LoadOrder = lo,
            LinkCache = lc,
            DataFolderPath = new DirectoryPath(System.IO.Path.GetTempPath()),
            PluginPaths = new Dictionary<string, string>(),
        };
    }

    [Fact]
    public void Identical_overrides_are_not_reported_as_conflicts()
    {
        var kw = new[] { "000001:Fallout4.esm", "000002:Fallout4.esm" };
        var env = BuildEnv(out _, "TestWeap", kw, "TestWeap", kw);

        var conflicts = ConflictScanner.Scan(env);

        conflicts.Should().BeEmpty("two plugins overriding a record with identical content is benign, " +
                                   "like xEdit's identical-to-master filtering");
    }

    [Fact]
    public void Differing_overrides_are_reported_as_a_conflict()
    {
        var env = BuildEnv(out var fk,
            "TestWeap", new[] { "000001:Fallout4.esm", "000002:Fallout4.esm" },
            "TestWeap", new[] { "000001:Fallout4.esm", "0000FF:Fallout4.esm" });

        var conflicts = ConflictScanner.Scan(env);

        conflicts.Should().ContainSingle()
            .Which.FormKey.Should().Be(fk.ToString());
    }
}
