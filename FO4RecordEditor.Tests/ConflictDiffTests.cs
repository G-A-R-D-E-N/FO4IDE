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

public class ConflictDiffTests
{
    private static object BuildEnv(out string weaponFk, string[] keywordsA, string[] keywordsB)
    {
        var a = new Fallout4Mod(ModKey.FromNameAndExtension("A.esp"), Fallout4Release.Fallout4);
        var fk = new FormKey(a.ModKey, 0x800);
        weaponFk = fk.ToString();

        var wa = new Weapon(fk, Fallout4Release.Fallout4) { EditorID = "TestWeap", Keywords = new() };
        foreach (var k in keywordsA) wa.Keywords!.Add(new FormLink<IKeywordGetter>(FormKey.Factory(k)));
        a.Weapons.Add(wa);

        var b = new Fallout4Mod(ModKey.FromNameAndExtension("B.esp"), Fallout4Release.Fallout4);
        var wb = new Weapon(fk, Fallout4Release.Fallout4) { EditorID = "TestWeap", Keywords = new() };
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
    public void Reordered_keywords_are_not_flagged_as_a_conflict()
    {
        var env = BuildEnv(out var fk,
            keywordsA: new[] { "000001:Fallout4.esm", "000002:Fallout4.esm", "000003:Fallout4.esm" },
            keywordsB: new[] { "000003:Fallout4.esm", "000001:Fallout4.esm", "000002:Fallout4.esm" });

        var matrix = MutagenLoader.BuildConflictMatrix(env, fk);
        matrix.Should().NotBeNull();
        matrix!.Rows.Where(r => r.Field.StartsWith("Keywords")).Should().NotBeEmpty()
            .And.OnlyContain(r => !r.Differs);
    }

    [Fact]
    public void Genuinely_different_keywords_still_conflict()
    {
        var env = BuildEnv(out var fk,
            keywordsA: new[] { "000001:Fallout4.esm", "000002:Fallout4.esm", "000003:Fallout4.esm" },
            keywordsB: new[] { "000001:Fallout4.esm", "000002:Fallout4.esm", "0000FF:Fallout4.esm" });

        var matrix = MutagenLoader.BuildConflictMatrix(env, fk);
        matrix!.Rows.Where(r => r.Field.StartsWith("Keywords")).Should().Contain(r => r.Differs);
    }
}
