using System.IO;
using FluentAssertions;
using FO4RecordEditor.Services;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Noggog;
using Xunit;

namespace FO4RecordEditor.Tests;

public class GlobalStateIsolationTests
{
    private static Mo2ProfileLoader.Mo2GameEnvironment BuildEnv(string modName, string editorId, out FormKey fk)
    {
        var mod = new Fallout4Mod(ModKey.FromNameAndExtension(modName), Fallout4Release.Fallout4);
        fk = new FormKey(mod.ModKey, 0x800);
        mod.Weapons.Add(new Weapon(fk, Fallout4Release.Fallout4) { EditorID = editorId });

        var listings = new List<IModListingGetter<IFallout4ModGetter>>
        {
            new ModListing<IFallout4ModGetter>(mod.ModKey, mod, enabled: true, ghostSuffix: string.Empty),
        };
        var lo = new LoadOrder<IModListingGetter<IFallout4ModGetter>>(listings, disposeItems: false);
        return new Mo2ProfileLoader.Mo2GameEnvironment
        {
            LoadOrder = lo,
            LinkCache = lo.ToImmutableLinkCache<IFallout4Mod, IFallout4ModGetter>(),
            DataFolderPath = new DirectoryPath(Path.GetTempPath()),
            PluginPaths = new Dictionary<string, string>(),
        };
    }

    [Fact]
    public void AfterARestore_ResolutionReturnsToThePreExistingEnvironment()
    {

        var previous = BuildEnv("Prev.esp", editorId: "PrevWeapon", out var prevFk);
        MutagenLoader.LinkCache = previous.LinkCache;

        try
        {
            FormKey leakFk;
            using (var isolation = new GlobalStateIsolation())
            {

                var leaked = BuildEnv("Leak.esp", editorId: "LeakWeapon", out leakFk);
                MutagenLoader.LinkCache = leaked.LinkCache;

                MutagenLoader.DescribeFormKey(null, leakFk.ToString()).Should().Contain("LeakWeapon");
            }

            MutagenLoader.DescribeFormKey(null, prevFk.ToString()).Should().Contain("PrevWeapon");
            MutagenLoader.DescribeFormKey(null, leakFk.ToString()).Should().NotContain("LeakWeapon");
        }
        finally
        {
            MutagenLoader.LinkCache = null;
        }
    }

    [Fact]
    public void Dispose_RestoresTheRegistryDictionariesCapsAndCaches()
    {

        var pristine = new GlobalStateIsolation();

        try
        {

            MutagenLoader.LooseMods["A.esp"] = "loose-mod";
            MutagenLoader.LooseModPaths["A.esp"] = @"C:\mods\A.esp";
            MutagenLoader.PluginSourcePaths["A.esp"] = @"C:\mods\A.esp";
            MutagenLoader.EditableMods["MyPatch.esp"] = "editable-copy";
            MutagenLoader.MasterIsEsl["Fallout4.esm"] = false;
            MutagenLoader.MasterIsEsl["MyPatch.esp"] = true;
            MutagenLoader.MaxCachedModIndexes = 7;
            MutagenLoader.MaxCachedIndexRecords = 12_345;

            using (var isolation = new GlobalStateIsolation())
            {

                MutagenLoader.LooseMods["B.esp"] = "leaked";
                MutagenLoader.LooseMods.TryRemove("A.esp", out _);
                MutagenLoader.LooseModPaths["B.esp"] = @"C:\mods\B.esp";
                MutagenLoader.PluginSourcePaths["B.esp"] = @"C:\mods\B.esp";
                MutagenLoader.EditableMods["B.esp"] = "leaked";
                MutagenLoader.MasterIsEsl["B.esp"] = true;
                MutagenLoader.MasterIsEsl.Remove("Fallout4.esm");
                MutagenLoader.MaxCachedModIndexes = 99;
                MutagenLoader.MaxCachedIndexRecords = 99;
                MutagenLoader.SeedModIndexForTest("B.esp", new object());
                ConflictScanner.ScanCached(BuildEnv("Prev.esp", "PrevWeapon", out _));

                MutagenLoader.LooseMods.Keys.Should().BeEquivalentTo("B.esp");
                MutagenLoader.LooseModPaths.Keys.Should().BeEquivalentTo("B.esp");
                MutagenLoader.PluginSourcePaths.Keys.Should().BeEquivalentTo("B.esp");
                MutagenLoader.EditableMods.Keys.Should().BeEquivalentTo("B.esp");
                MutagenLoader.MasterIsEsl.Keys.Should().BeEquivalentTo("B.esp");
                MutagenLoader.MaxCachedModIndexes.Should().Be(99);
                MutagenLoader.MaxCachedIndexRecords.Should().Be(99);
                MutagenLoader.ModIndexCacheCount.Should().BeGreaterThan(0);
                ConflictScanner.HasCache.Should().BeTrue();
            }

            MutagenLoader.LooseMods.Should().BeEquivalentTo(new[] { new KeyValuePair<string, object>("A.esp", "loose-mod") });
            MutagenLoader.LooseModPaths.Should().BeEquivalentTo(new[] { new KeyValuePair<string, string>("A.esp", @"C:\mods\A.esp") });
            MutagenLoader.PluginSourcePaths.Should().BeEquivalentTo(new[] { new KeyValuePair<string, string>("A.esp", @"C:\mods\A.esp") });
            MutagenLoader.EditableMods.Should().BeEquivalentTo(new[] { new KeyValuePair<string, object>("MyPatch.esp", "editable-copy") });
            MutagenLoader.MasterIsEsl.Should().BeEquivalentTo(new[]
            {
                new KeyValuePair<string, bool>("Fallout4.esm", false),
                new KeyValuePair<string, bool>("MyPatch.esp", true),
            });
            MutagenLoader.MaxCachedModIndexes.Should().Be(7);
            MutagenLoader.MaxCachedIndexRecords.Should().Be(12_345);
            MutagenLoader.ModIndexCacheCount.Should().Be(0);
            ConflictScanner.HasCache.Should().BeFalse();
        }
        finally
        {

            pristine.Dispose();
        }
    }
}
