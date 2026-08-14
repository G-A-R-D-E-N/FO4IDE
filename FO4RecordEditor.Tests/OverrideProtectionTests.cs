using System.Text.Json;
using FluentAssertions;
using FO4RecordEditor.Services;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Xunit;

namespace FO4RecordEditor.Tests;

public class OverrideProtectionTests
{
    [Fact]
    public void CopyAsOverride_ConfirmedOverwriteReplacesFieldsInsteadOfReturningTheOldOverride()
    {
        var source = $"OverwriteSrc_{Guid.NewGuid():N}.esp";
        var patch = $"OverwritePatch_{Guid.NewGuid():N}.esp";

        WriteService.CreatePlugin(source);
        WriteService.CreateRecord(source, "BOOK", "Overwrite_Book", env: null);
        WriteService.SetField(source, "Overwrite_Book", "Name", "Source v1", env: null)
            .Should().Contain("Set Name");

        var sourceBook = WriteService.GetMutable(source)!.Books.Single(b => b.EditorID == "Overwrite_Book");
        WriteService.CopyAsOverride(null, source, sourceBook.FormKey.ToString(), patch, overwrite: false)
            .Should().StartWith("Copied");

        WriteService.SetField(patch, "Overwrite_Book", "Name", "Patch edit", env: null)
            .Should().Contain("Set Name");
        WriteService.SetField(source, "Overwrite_Book", "Name", "Source v2", env: null)
            .Should().Contain("Set Name");

        WriteService.CopyAsOverride(null, source, sourceBook.FormKey.ToString(), patch, overwrite: false)
            .Should().StartWith("EXISTS:");
        WriteService.GetMutable(patch)!.Books.Single(b => b.FormKey == sourceBook.FormKey)
            .Name!.String.Should().Be("Patch edit", "a refused overwrite must leave the target untouched");

        WriteService.CopyAsOverride(null, source, sourceBook.FormKey.ToString(), patch, overwrite: true)
            .Should().StartWith("Copied").And.Contain("replacing");
        WriteService.GetMutable(patch)!.Books.Single(b => b.FormKey == sourceBook.FormKey)
            .Name!.String.Should().Be("Source v2", "an approved overwrite must be a fresh copy of the selected source version");
    }

    [Fact]
    public void CopyAsOverride_SameSourceAndTargetIsRejectedBeforeRemovingTheRecord()
    {
        var plugin = $"SameSourceTarget_{Guid.NewGuid():N}.esp";
        WriteService.CreatePlugin(plugin);
        WriteService.CreateRecord(plugin, "BOOK", "SameSourceBook", env: null);
        var book = WriteService.GetMutable(plugin)!.Books.Single();
        book.Name = "Original";

        WriteService.CopyAsOverride(null, plugin, book.FormKey.ToString(), plugin, overwrite: true)
            .Should().Contain("source and target are both");

        WriteService.GetMutable(plugin)!.Books.Single(b => b.FormKey == book.FormKey)
            .Name!.String.Should().Be("Original");
    }

    [Fact]
    public void DeepCopyAsOverride_CopiesTheExplicitlySelectedOverrideVersion()
    {
        var origin = $"DeepVersionOrigin_{Guid.NewGuid():N}.esp";
        var selected = $"DeepVersionSelected_{Guid.NewGuid():N}.esp";
        var patch = $"DeepVersionPatch_{Guid.NewGuid():N}.esp";

        WriteService.CreatePlugin(origin);
        WriteService.CreateRecord(origin, "BOOK", "DeepVersionBook", env: null);
        var formKey = WriteService.GetMutable(origin)!.Books.Single().FormKey;
        WriteService.SetField(origin, "DeepVersionBook", "Name", "Origin version", env: null);
        WriteService.CopyAsOverride(null, origin, formKey.ToString(), selected, overwrite: false)
            .Should().StartWith("Copied");
        WriteService.SetField(selected, "DeepVersionBook", "Name", "Selected override", env: null);
        WriteService.CreateRecord(selected, "KYWD", "SelectedDependency", env: null);
        var selectedMod = WriteService.GetMutable(selected)!;
        var selectedKeyword = selectedMod.Keywords.Single(k => k.EditorID == "SelectedDependency");
        WriteService.AddListItem(selected, "DeepVersionBook", "Keywords",
                selectedKeyword.FormKey.ToString(), env: null)
            .Should().Contain("Added");

        WriteService.DeepCopyAsOverride(null, selected, formKey.ToString(), patch,
                apply: true, overwrite: false)
            .Should().StartWith("Deep-copied 2 of 2");

        var target = WriteService.GetMutable(patch)!;
        target.Books.Single(b => b.FormKey == formKey).Name!.String.Should().Be("Selected override",
            "deep copy must use the plugin version named in the confirmation prompt");
        target.Keywords.Select(k => k.FormKey).Should().Contain(selectedKeyword.FormKey,
            "dependencies owned by the selected override plugin must follow that selected version");
    }

    [Fact]
    public void CopyAsOverrideMany_PreflightsTheWholeBatchBeforeWritingAnything()
    {
        var source = $"BatchOverwriteSrc_{Guid.NewGuid():N}.esp";
        var patch = $"BatchOverwritePatch_{Guid.NewGuid():N}.esp";

        WriteService.CreatePlugin(source);
        WriteService.CreateRecord(source, "BOOK", "Batch_First", env: null);
        WriteService.CreateRecord(source, "BOOK", "Batch_Second", env: null);
        var books = WriteService.GetMutable(source)!.Books.ToDictionary(b => b.EditorID!);

        WriteService.CopyAsOverride(null, source, books["Batch_First"].FormKey.ToString(), patch, overwrite: false)
            .Should().StartWith("Copied");

        var itemsJson = JsonSerializer.Serialize(new[]
        {
            new { formKey = books["Batch_First"].FormKey.ToString(), source },
            new { formKey = books["Batch_Second"].FormKey.ToString(), source },
        });

        using (var refused = JsonDocument.Parse(
                   WriteService.CopyAsOverrideMany(null, itemsJson, patch, overwrite: false)))
        {
            var root = refused.RootElement;
            root.GetProperty("requiresOverwrite").GetBoolean().Should().BeTrue();
            root.GetProperty("existing").GetArrayLength().Should().Be(1);
            root.GetProperty("ok").GetInt32().Should().Be(0);
            root.GetProperty("failed").GetInt32().Should().Be(0);
        }

        var afterRefusal = WriteService.GetMutable(patch)!;
        afterRefusal.Books.Select(b => b.EditorID).Should().BeEquivalentTo(
            new[] { "Batch_First" },
            "the non-conflicting second record must not be copied before the overwrite prompt is answered");

        using (var confirmed = JsonDocument.Parse(
                   WriteService.CopyAsOverrideMany(null, itemsJson, patch, overwrite: true)))
        {
            var root = confirmed.RootElement;
            root.GetProperty("requiresOverwrite").GetBoolean().Should().BeFalse();
            root.GetProperty("ok").GetInt32().Should().Be(2);
            root.GetProperty("failed").GetInt32().Should().Be(0);
        }

        WriteService.GetMutable(patch)!.Books.Select(b => b.EditorID)
            .Should().BeEquivalentTo("Batch_First", "Batch_Second");
    }

    [Fact]
    public void DeepCopyAsOverride_PreflightsExistingRecordsBeforeCopyingDependencies()
    {
        var source = $"DeepOverwriteSrc_{Guid.NewGuid():N}.esp";
        var patch = $"DeepOverwritePatch_{Guid.NewGuid():N}.esp";

        WriteService.CreatePlugin(source);
        WriteService.CreateRecord(source, "KYWD", "Deep_Keyword", env: null);
        WriteService.CreateRecord(source, "WEAP", "Deep_Weapon", env: null);
        var sourceMod = WriteService.GetMutable(source)!;
        var keyword = sourceMod.Keywords.Single(k => k.EditorID == "Deep_Keyword");
        var weapon = sourceMod.Weapons.Single(w => w.EditorID == "Deep_Weapon");
        WriteService.AddListItem(source, "Deep_Weapon", "Keywords", keyword.FormKey.ToString(), env: null)
            .Should().Contain("Added");

        WriteService.CopyAsOverride(null, source, weapon.FormKey.ToString(), patch, overwrite: false)
            .Should().StartWith("Copied");
        WriteService.GetMutable(patch)!.Keywords.Should().BeEmpty();

        WriteService.DeepCopyAsOverride(null, source, weapon.FormKey.ToString(), patch,
                apply: true, overwrite: false)
            .Should().StartWith("EXISTS:");
        WriteService.GetMutable(patch)!.Keywords.Should().BeEmpty(
            "the dependency must not be copied before the root collision is confirmed");

        WriteService.DeepCopyAsOverride(null, source, weapon.FormKey.ToString(), patch,
                apply: true, overwrite: true)
            .Should().StartWith("Deep-copied 2 of 2");

        var target = WriteService.GetMutable(patch)!;
        target.Keywords.Select(k => k.FormKey).Should().Contain(keyword.FormKey);
        target.Weapons.Single(w => w.FormKey == weapon.FormKey).Keywords!
            .Select(k => k.FormKey).Should().Contain(keyword.FormKey);
    }
}
