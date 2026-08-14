using FluentAssertions;
using FO4RecordEditor.Models;
using FO4RecordEditor.Services;
using FO4RecordEditor.Tests.Helpers;
using Xunit;

namespace FO4RecordEditor.Tests;

public class KnowledgeGraphTests
{
    private static RecordNode SamplePlugin()
    {
        var keyword = GraphTestBuilder.Record("KYWD", "001000:Test.esp", "MyKeyword");
        var weapon  = GraphTestBuilder.Record("WEAP", "001001:Test.esp", "MyGun",
            ("Damage", "50"), ("Keyword1", "001000:Test.esp"));
        return GraphTestBuilder.Plugin("Test.esp", keyword, weapon);
    }

    [Fact]
    public void Index_RegistersRecords_ByFormKeyAndEditorID()
    {
        var g = new KnowledgeGraph();
        g.Index(SamplePlugin());
        g.GetByFormKey("001001:Test.esp")!.EditorID.Should().Be("MyGun");
        g.GetByEditorID("MyKeyword").Should().ContainSingle();
        g.GetByType("WEAP").Should().ContainSingle();
        g.RecordCount.Should().Be(2);
    }

    [Fact]
    public void GetReferencesTo_FindsInboundLinks()
    {
        var g = new KnowledgeGraph();
        g.Index(SamplePlugin());
        var inbound = g.GetReferencesTo("001000:Test.esp");
        inbound.Should().ContainSingle();
        inbound[0].FromFormKey.Should().Be("001001:Test.esp");
        inbound[0].FieldPath.Should().Be("Keyword1");
    }

    [Fact]
    public void AnalyzeImpact_ListsAffectedRecords()
    {
        var g = new KnowledgeGraph();
        g.Index(SamplePlugin());
        var report = g.AnalyzeImpact("001000:Test.esp");
        report.TargetEditorID.Should().Be("MyKeyword");
        report.AffectedRecords.Should().ContainSingle()
              .Which.EditorID.Should().Be("MyGun");
    }

    [Fact]
    public void Index_TwoPluginsSameFormKey_TracksConflict()
    {
        var g = new KnowledgeGraph();
        g.Index(GraphTestBuilder.Plugin("A.esp",
            GraphTestBuilder.Record("WEAP", "000800:Master.esm", "Gun", ("Damage", "10"))));
        g.Index(GraphTestBuilder.Plugin("B.esp",
            GraphTestBuilder.Record("WEAP", "000800:Master.esm", "Gun", ("Damage", "99"))));
        g.GetConflicts().Should().ContainSingle()
            .Which.FormKey.Should().Be("000800:Master.esm");
        var winner = g.GetByFormKey("000800:Master.esm")!;
        winner.SourcePlugin.Should().Be("B.esp");
        winner.IsWinningOverride.Should().BeTrue();
    }

    [Fact]
    public void GetNeighborhood_ReturnsRecordPlusOneHop()
    {
        var g = new KnowledgeGraph();
        g.Index(SamplePlugin());
        var hood = g.GetNeighborhood("001001:Test.esp");
        hood.Select(e => e.EditorID).Should().Contain(new[] { "MyGun", "MyKeyword" });
    }
}
