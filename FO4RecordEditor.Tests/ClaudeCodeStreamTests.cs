using FluentAssertions;
using FO4RecordEditor.Services;

namespace FO4RecordEditor.Tests;

public class ClaudeCodeStreamTests
{
    [Fact]
    public void AssistantText_IsStreamedAsIs()
    {
        var line = """{"type":"assistant","message":{"content":[{"type":"text","text":"Hello there"}]}}""";
        ClaudeCodeProvider.ParseStreamLine(line).Should().ContainSingle().Which.Should().Be("Hello there");
    }

    [Fact]
    public void ToolUse_ShowsShortNameAndArgs()
    {
        var line = """{"type":"assistant","message":{"content":[{"type":"tool_use","name":"mcp__fo4editor__get_record","input":{"plugin":"Combat AI Empowered.esp","id":"fSneakRunningMult"}}]}}""";
        var chunks = ClaudeCodeProvider.ParseStreamLine(line).ToList();
        chunks.Should().ContainSingle();
        chunks[0].Should().Contain("🔧")
            .And.Contain("get_record")
            .And.NotContain("mcp__fo4editor__")
            .And.Contain("plugin=Combat AI Empowered.esp")
            .And.Contain("id=fSneakRunningMult");
    }

    [Fact]
    public void Thinking_IsShown()
    {
        var line = """{"type":"assistant","message":{"content":[{"type":"thinking","thinking":"Let me check the recipe components first"}]}}""";
        ClaudeCodeProvider.ParseStreamLine(line).Should().ContainSingle()
            .Which.Should().Contain("💭").And.Contain("check the recipe components");
    }

    [Fact]
    public void ToolResult_IsShownTruncated()
    {
        var line = """{"type":"user","message":{"content":[{"type":"tool_result","content":[{"type":"text","text":"FWC_Research_Deliverer\nComponents: 2"}]}]}}""";
        ClaudeCodeProvider.ParseStreamLine(line).Should().ContainSingle()
            .Which.Should().Contain("↳").And.Contain("FWC_Research_Deliverer");
    }

    [Fact]
    public void InitAndSuccessResult_ProduceNoUserText()
    {
        ClaudeCodeProvider.ParseStreamLine("""{"type":"system","subtype":"init","tools":[]}""").Should().BeEmpty();
        ClaudeCodeProvider.ParseStreamLine("""{"type":"result","subtype":"success","is_error":false,"result":"done"}""").Should().BeEmpty();
    }

    [Fact]
    public void ErrorResult_IsSurfaced()
    {
        var line = """{"type":"result","is_error":true,"result":"MCP connection failed"}""";
        ClaudeCodeProvider.ParseStreamLine(line).Should().ContainSingle()
            .Which.Should().Contain("MCP connection failed");
    }

    [Fact]
    public void NonJsonLine_PassesThroughVerbatim()
    {
        ClaudeCodeProvider.ParseStreamLine("warning: something").Should().ContainSingle()
            .Which.Should().Contain("warning: something");
        ClaudeCodeProvider.ParseStreamLine("   ").Should().BeEmpty();
    }
}
