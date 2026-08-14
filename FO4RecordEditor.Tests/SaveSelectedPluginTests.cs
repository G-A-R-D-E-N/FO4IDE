using FluentAssertions;
using FO4RecordEditor.Models;
using FO4RecordEditor.ViewModels;
using Xunit;

namespace FO4RecordEditor.Tests;

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
