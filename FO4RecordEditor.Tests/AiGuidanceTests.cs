using System.Linq;
using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;
using Xunit.Abstractions;

namespace FO4RecordEditor.Tests;

public class AiGuidanceTests
{
    private readonly ITestOutputHelper _out;
    public AiGuidanceTests(ITestOutputHelper o) => _out = o;

    private static string[] ToolNames() => PluginToolExecutor.ToolDefinitions()
        .Select(t => (string)t.GetType().GetProperty("name")!.GetValue(t)!)
        .ToArray();

    [Fact]
    public void EveryRegisteredToolIsMentionedInTheGuidance()
    {
        var missing = ToolNames().Where(n => !AiGuidance.System.Contains(n, StringComparison.Ordinal)).ToList();

        if (missing.Count > 0)
            _out.WriteLine("Not mentioned in AiGuidance.System:\n  " + string.Join("\n  ", missing));

        missing.Should().BeEmpty(
            "every tool must appear in AiGuidance's routing map -- the model reads that prose before " +
            "its tool schemas, and an omitted tool reads as one that does not exist");
    }

    [Fact]
    public void GuidanceDoesNotReferToToolsThatNoLongerExist()
    {
        var known = ToolNames().ToHashSet(StringComparer.Ordinal);

        var referenced = System.Text.RegularExpressions.Regex
            .Matches(AiGuidance.System, @"\b[a-z][a-z0-9]*(?:_[a-z0-9]+)+\b")
            .Select(m => m.Value)
            .Distinct()
            .ToList();

        var notTools = new[]
        {
            "dry_run", "patch_plugin", "source_plugin", "chance_none", "form_list",
            "new_id", "base_object", "map_marker", "record_type", "object_bounds",
            "hide_grouped",
        }.ToHashSet(StringComparer.Ordinal);

        var stale = referenced.Where(r => !known.Contains(r) && !notTools.Contains(r)).ToList();

        if (stale.Count > 0)
            _out.WriteLine("Tool-shaped tokens in AiGuidance that are not registered tools:\n  " +
                           string.Join("\n  ", stale));

        stale.Should().BeEmpty(
            "the guidance must not name a tool that does not exist; if one of these is prose, add it " +
            "to the notTools allowlist in this test");
    }
}
