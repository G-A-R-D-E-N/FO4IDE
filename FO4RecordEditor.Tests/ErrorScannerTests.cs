using FluentAssertions;
using FO4RecordEditor.Models;
using FO4RecordEditor.Services;
using FO4RecordEditor.Tests.Helpers;
using Xunit;

namespace FO4RecordEditor.Tests;

public class ErrorScannerTests
{
    [Fact]
    public void Scan_FlagsNullActorValue()
    {
        var rec = GraphTestBuilder.Record("COBJ", "001001:Test.esp", "MyRecipe",
            ("ActorValue", "NULL - Null Reference [00000000]"));
        var g = new KnowledgeGraph();
        g.Index(GraphTestBuilder.Plugin("Test.esp", rec));

        var errors = new ErrorScanner().Scan(g);
        errors.Should().Contain(e => e.Category == ErrorCategory.NullActorValue
                                  && e.FormKey == "001001:Test.esp");
    }

    [Fact]
    public void Scan_FlagsBrokenReference()
    {
        var rec = GraphTestBuilder.Record("WEAP", "001001:Test.esp", "MyGun",
            ("Keyword1", "00BEEF:Missing.esp"));
        var g = new KnowledgeGraph();
        g.Index(GraphTestBuilder.Plugin("Test.esp", rec));

        var errors = new ErrorScanner().Scan(g);
        errors.Should().Contain(e => e.Category == ErrorCategory.BrokenReference
                                  && e.Description.Contains("00BEEF:Missing.esp"));
    }

    [Fact]
    public void Scan_DoesNotFlagVanillaReference()
    {
        var rec = GraphTestBuilder.Record("WEAP", "001001:Test.esp", "MyGun",
            ("Keyword1", "0004A0:Fallout4.esm"));
        var g = new KnowledgeGraph();
        g.Index(GraphTestBuilder.Plugin("Test.esp", rec));

        var errors = new ErrorScanner().Scan(g);
        errors.Should().NotContain(e => e.Category == ErrorCategory.BrokenReference);
    }

    [Fact]
    public void Scan_FlagsLeveledListEntryWithNoReference()
    {
        var ll = GraphTestBuilder.Record("LVLI", "002000:Test.esp", "MyList");
        var entries = new RecordNode { Key = "Entries", Parent = ll };
        var e0 = new RecordNode { Key = "[0]", Parent = entries };
        GraphTestBuilder.AddLeaf(e0, "Reference", "");
        GraphTestBuilder.AddLeaf(e0, "Count", "1");
        entries.Children.Add(e0);
        ll.Children.Add(entries);

        var g = new KnowledgeGraph();
        g.Index(GraphTestBuilder.Plugin("Test.esp", ll));
        var errors = new ErrorScanner().Scan(g);
        errors.Should().Contain(e => e.Category == ErrorCategory.InvalidLeveledList);
    }
}
