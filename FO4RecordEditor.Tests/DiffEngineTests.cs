using FluentAssertions;
using FO4RecordEditor.Models;
using FO4RecordEditor.Services;
using FO4RecordEditor.Tests.Helpers;
using Xunit;

namespace FO4RecordEditor.Tests;

public class DiffEngineTests
{
    [Fact]
    public void Compare_DetectsModifiedAddedRemoved()
    {
        var a = GraphTestBuilder.Record("WEAP", "001:T.esp", "Gun",
            ("Damage", "50"), ("OldField", "x"));
        var b = GraphTestBuilder.Record("WEAP", "001:T.esp", "Gun",
            ("Damage", "120"), ("NewField", "y"));

        var rows = new DiffEngine().Compare(a, b);

        rows.Should().Contain(r => r.Path == "Damage" && r.Kind == DiffKind.Modified
            && r.ValueA == "50" && r.ValueB == "120");
        rows.Should().Contain(r => r.Path == "OldField" && r.Kind == DiffKind.Removed);
        rows.Should().Contain(r => r.Path == "NewField" && r.Kind == DiffKind.Added);
    }

    [Fact]
    public void Compare_OnlyChanges_ExcludesUnchanged()
    {
        var a = GraphTestBuilder.Record("WEAP", "001:T.esp", "Gun", ("Damage", "50"));
        var b = GraphTestBuilder.Record("WEAP", "001:T.esp", "Gun", ("Damage", "50"));
        new DiffEngine().Compare(a, b, changesOnly: true).Should().BeEmpty();
    }
}
