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

/// <summary>
/// Regression guard for the process-global leak fix: a class that loads an environment into
/// MutagenLoader.LinkCache without restoring it leaves its records answering every later FormLink
/// resolution in the run. These tests prove GlobalStateIsolation's dispose really restores
/// resolution (and the registry snapshot), not just the pointer.
/// </summary>
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
        // The environment a later test class would legitimately resolve against.
        var previous = BuildEnv("Prev.esp", editorId: "PrevWeapon", out var prevFk);
        MutagenLoader.LinkCache = previous.LinkCache;

        try
        {
            FormKey leakFk;
            using (var isolation = new GlobalStateIsolation())
            {
                // Simulate a leaky prior class: load a different environment while the scope is open.
                var leaked = BuildEnv("Leak.esp", editorId: "LeakWeapon", out leakFk);
                MutagenLoader.LinkCache = leaked.LinkCache;

                // The leak is live while the scope is open, so the post-restore assertions cannot
                // pass vacuously.
                MutagenLoader.DescribeFormKey(null, leakFk.ToString()).Should().Contain("LeakWeapon");
            }   // dispose restores the pre-existing cache

            // After the restore, resolution consults the legitimate environment again.
            MutagenLoader.DescribeFormKey(null, prevFk.ToString()).Should().Contain("PrevWeapon");
            MutagenLoader.DescribeFormKey(null, leakFk.ToString()).Should().NotContain("LeakWeapon");
        }
        finally
        {
            MutagenLoader.LinkCache = null;
        }
    }

    /// <summary>
    /// Pins the rest of the snapshot contract: every registry dictionary, both LRU caps, the
    /// per-mod index cache, and ConflictScanner's cache.
    /// </summary>
    [Fact]
    public void Dispose_RestoresTheRegistryDictionariesCapsAndCaches()
    {
        // Captured before the seeded state; disposed last to return the process to defaults.
        var pristine = new GlobalStateIsolation();

        try
        {
            // Pre-existing state a later class would legitimately see.
            MutagenLoader.LooseMods["A.esp"] = "loose-mod";
            MutagenLoader.LooseModPaths["A.esp"] = @"C:\mods\A.esp";
            MutagenLoader.PluginSourcePaths["A.esp"] = @"C:\mods\A.esp";
            MutagenLoader.EditableMods["MyPatch.esp"] = "editable-copy";
            MutagenLoader.MasterIsEsl["Fallout4.esm"] = false;
            MutagenLoader.MasterIsEsl["MyPatch.esp"] = true;
            MutagenLoader.MaxCachedModIndexes = 7;
            MutagenLoader.MaxCachedIndexRecords = 12_345;

            using (var isolation = new GlobalStateIsolation())   // captures the seeded state
            {
                // A leaky class mutates everything in scope: adds, removes, and changes caps.
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
                ConflictScanner.ScanCached(BuildEnv("Prev.esp", "PrevWeapon", out _));   // populates its cache

                // The mutations are live while the scope is open, so the restores cannot pass
                // vacuously.
                MutagenLoader.LooseMods.Keys.Should().BeEquivalentTo("B.esp");
                MutagenLoader.LooseModPaths.Keys.Should().BeEquivalentTo("B.esp");
                MutagenLoader.PluginSourcePaths.Keys.Should().BeEquivalentTo("B.esp");
                MutagenLoader.EditableMods.Keys.Should().BeEquivalentTo("B.esp");
                MutagenLoader.MasterIsEsl.Keys.Should().BeEquivalentTo("B.esp");
                MutagenLoader.MaxCachedModIndexes.Should().Be(99);
                MutagenLoader.MaxCachedIndexRecords.Should().Be(99);
                MutagenLoader.ModIndexCacheCount.Should().BeGreaterThan(0);
                ConflictScanner.HasCache.Should().BeTrue();
            }   // dispose restores the seeded state

            // Registries and caps are back to the pre-existing state; the caches were cleared.
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
            // Hand the process back to pristine defaults.
            pristine.Dispose();
        }
    }
}
