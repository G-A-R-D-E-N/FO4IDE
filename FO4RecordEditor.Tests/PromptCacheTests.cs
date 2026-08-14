using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using FO4RecordEditor.Services;

namespace FO4RecordEditor.Tests;

public class PromptCacheTests
{
    private static readonly JsonSerializerOptions Opts =
        new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    [Fact]
    public void CachedToolDefinitions_PutOneBreakpointOnTheLastTool()
    {
        var tools = PluginToolExecutor.ToolDefinitionsCached();
        var json = JsonSerializer.Serialize(tools, Opts);



        var occurrences = json.Split("cache_control").Length - 1;
        occurrences.Should().Be(1, "only the last tool carries a cache_control breakpoint");
        json.Should().Contain("\"type\":\"ephemeral\"");


        var lastName = PluginToolExecutor.ToolDefinitionsCached().Length;
        lastName.Should().BeGreaterThan(0);
        json.LastIndexOf("cache_control").Should().BeGreaterThan(json.Length / 2,
            "the breakpoint sits in the final tool, in the back half of the array");
    }
}
