using FluentAssertions;
using FO4RecordEditor.Models;
using FO4RecordEditor.Services;
using FO4RecordEditor.Tests.Helpers;
using Xunit;

namespace FO4RecordEditor.Tests;

public class AIContextBuilderTests
{
    [Fact]
    public void Build_IncludesRecordFieldsAndNeighborhood()
    {
        var keyword = GraphTestBuilder.Record("KYWD", "001000:Test.esp", "MyKeyword");
        var weapon  = GraphTestBuilder.Record("WEAP", "001001:Test.esp", "MyGun",
            ("Damage", "50"), ("Keyword1", "001000:Test.esp"));
        var g = new KnowledgeGraph();
        g.Index(GraphTestBuilder.Plugin("Test.esp", keyword, weapon));

        var ctx = new AIContextBuilder(g).BuildForRecord(g.GetByFormKey("001001:Test.esp")!);

        ctx.Should().Contain("MyGun");
        ctx.Should().Contain("Damage");
        ctx.Should().Contain("MyKeyword");
    }
}
