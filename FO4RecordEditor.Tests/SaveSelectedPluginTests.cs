using FluentAssertions;
using FO4RecordEditor.Models;
using FO4RecordEditor.ViewModels;
using Xunit;

namespace FO4RecordEditor.Tests;

// MutagenLoader.SaveEsp was a stub: the write was commented out and the field-mapping loop was
// empty, but it called ClearDirty() and the caller logged "Saved {plugin}." unconditionally. Ctrl+S
// and the "save" command therefore reported success, cleared the dirty flags, and wrote nothing.
// It is deleted; SaveSelectedPlugin must now return an honest message instead.
public class SaveSelectedPluginTests
{
    [Fact]
    public void NothingSelected_SaysSo_RatherThanClaimingASave()
    {
        var shell = new ShellViewModel { SelectedNode = null };

        var result = shell.SaveSelectedPlugin();

        result.Should().Contain("Nothing selected");
        result.Should().NotContain("Saved ");
    }

    [Fact]
    public void PluginBinary_IsRefusedAndPointsAtTheWorkingPath()
    {
        var shell = new ShellViewModel
        {
            SelectedNode = new RecordNode { Key = "MyMod.esp", FilePath = @"C:\mods\MyMod.esp" },
        };

        var result = shell.SaveSelectedPlugin();

        result.Should().Contain("not supported");
        // Must name a route that actually works, not just fail: the editor's own Save action and
        // the save_plugin tool.
        result.Should().Contain("Save action").And.Contain("save_plugin");
        result.Should().NotContain("Saved ");
    }

    [Fact]
    public void NodeWithNoFile_IsRefused()
    {
        var shell = new ShellViewModel { SelectedNode = new RecordNode { Key = "orphan" } };

        shell.SaveSelectedPlugin().Should().Contain("no backing file");
    }
}
