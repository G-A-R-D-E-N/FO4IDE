using System.IO;
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

public sealed class ChangeReferencingRecordsReviewTests
{
    [Fact]
    public void ChangeReferencingRecords_RemapsAnOverrideAlreadyPresentInTheTargetPatch()
    {
        var source = $"ReferenceChangeSource_{Guid.NewGuid():N}.esp";
        var patch = $"ReferenceChangePatch_{Guid.NewGuid():N}.esp";

        WriteService.CreatePlugin(source).Should().Contain("Created");
        WriteService.CreateRecord(source, "KYWD", "ReferenceChangeOld", env: null)
            .Should().Contain("Created");
        WriteService.CreateRecord(source, "KYWD", "ReferenceChangeNew", env: null)
            .Should().Contain("Created");
        WriteService.CreateRecord(source, "WEAP", "ReferenceChangeWeapon", env: null)
            .Should().Contain("Created");

        var sourceMod = WriteService.GetMutable(source)!;
        var oldKeyword = sourceMod.Keywords.Single(k => k.EditorID == "ReferenceChangeOld");
        var newKeyword = sourceMod.Keywords.Single(k => k.EditorID == "ReferenceChangeNew");
        var weapon = sourceMod.Weapons.Single(w => w.EditorID == "ReferenceChangeWeapon");
        WriteService.AddListItem(source, weapon.FormKey.ToString(), "Keywords", oldKeyword.FormKey.ToString(), env: null)
            .Should().Contain("Added");

        WriteService.CopyAsOverride(null, source, weapon.FormKey.ToString(), patch, overwrite: false)
            .Should().StartWith("Copied");
        var targetWeapon = WriteService.GetMutable(patch)!.Weapons.Single(w => w.FormKey == weapon.FormKey);
        targetWeapon.Name = "Existing target edit must survive";

        var env = BuildEnvironment(sourceMod, WriteService.GetMutable(patch)!);
        var result = WriteService.ChangeReferencingRecords(
            env,
            oldKeyword.FormKey.ToString(),
            newKeyword.FormKey.ToString(),
            patch,
            apply: true);

        result.Should().Contain("Repointed").And.NotContain("EXISTS:");
        var after = WriteService.GetMutable(patch)!.Weapons.Single(w => w.FormKey == weapon.FormKey);
        after.Name!.String.Should().Be("Existing target edit must survive");
        after.Keywords!.Select(k => k.FormKey).Should().Contain(newKeyword.FormKey);
        after.Keywords!.Select(k => k.FormKey).Should().NotContain(oldKeyword.FormKey);
    }

    [Fact]
    public void ChangeReferencingRecords_RefusesBeforeMutationWhenTargetLoadsBeforeALaterVersion()
    {
        var source = $"ReferenceOrderSource_{Guid.NewGuid():N}.esp";
        var patch = $"ReferenceOrderPatch_{Guid.NewGuid():N}.esp";
        var later = $"ReferenceOrderLater_{Guid.NewGuid():N}.esp";

        WriteService.CreatePlugin(source).Should().Contain("Created");
        WriteService.CreateRecord(source, "KYWD", "ReferenceOrderOld", env: null)
            .Should().Contain("Created");
        WriteService.CreateRecord(source, "KYWD", "ReferenceOrderNew", env: null)
            .Should().Contain("Created");
        WriteService.CreateRecord(source, "WEAP", "ReferenceOrderWeapon", env: null)
            .Should().Contain("Created");

        var sourceMod = WriteService.GetMutable(source)!;
        var oldKeyword = sourceMod.Keywords.Single(k => k.EditorID == "ReferenceOrderOld");
        var newKeyword = sourceMod.Keywords.Single(k => k.EditorID == "ReferenceOrderNew");
        var weapon = sourceMod.Weapons.Single(w => w.EditorID == "ReferenceOrderWeapon");
        WriteService.AddListItem(source, weapon.FormKey.ToString(), "Keywords", oldKeyword.FormKey.ToString(), env: null)
            .Should().Contain("Added");

        WriteService.CopyAsOverride(null, source, weapon.FormKey.ToString(), patch, overwrite: false)
            .Should().StartWith("Copied");
        WriteService.CopyAsOverride(null, source, weapon.FormKey.ToString(), later, overwrite: false)
            .Should().StartWith("Copied");

        var patchMod = WriteService.GetMutable(patch)!;
        var laterMod = WriteService.GetMutable(later)!;
        var targetWeapon = patchMod.Weapons.Single(w => w.FormKey == weapon.FormKey);
        targetWeapon.Name = "Earlier target must remain unchanged";

        var env = BuildEnvironment(sourceMod, patchMod, laterMod);
        var result = WriteService.ChangeReferencingRecords(
            env,
            oldKeyword.FormKey.ToString(),
            newKeyword.FormKey.ToString(),
            patch,
            apply: true);

        result.Should().Contain("Refused before changing any records")
            .And.Contain(later)
            .And.Contain("earlier target override would not change the winning record");
        targetWeapon.Name!.String.Should().Be("Earlier target must remain unchanged");
        targetWeapon.Keywords!.Select(k => k.FormKey).Should().Contain(oldKeyword.FormKey);
        targetWeapon.Keywords!.Select(k => k.FormKey).Should().NotContain(newKeyword.FormKey);
    }

    private static Mo2ProfileLoader.Mo2GameEnvironment BuildEnvironment(params IFallout4ModGetter[] mods)
    {
        var listings = mods
            .Select(mod => (IModListingGetter<IFallout4ModGetter>)new ModListing<IFallout4ModGetter>(
                mod.ModKey,
                mod,
                enabled: true,
                ghostSuffix: string.Empty))
            .ToList();
        var loadOrder = new LoadOrder<IModListingGetter<IFallout4ModGetter>>(listings, disposeItems: false);
        var linkCache = loadOrder.ToImmutableLinkCache<IFallout4Mod, IFallout4ModGetter>();
        return new Mo2ProfileLoader.Mo2GameEnvironment
        {
            LoadOrder = loadOrder,
            LinkCache = linkCache,
            DataFolderPath = new DirectoryPath(Path.GetTempPath()),
            PluginPaths = new Dictionary<string, string>(),
        };
    }
}
