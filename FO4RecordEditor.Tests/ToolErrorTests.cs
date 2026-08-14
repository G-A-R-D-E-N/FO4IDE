using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;

namespace FO4RecordEditor.Tests;


public class ToolErrorTests
{
    [Fact]
    public void Fail_MarksText_AndUnwrapStripsTheMarker()
    {
        var marked = ToolError.Fail("Record 'X' not found in Y.esp.");

        ToolError.IsMarked(marked).Should().BeTrue();

        var result = ToolError.Unwrap(marked);
        result.IsError.Should().BeTrue();
        result.Text.Should().Be("Record 'X' not found in Y.esp.");
    }

    [Fact]
    public void Fail_IsIdempotent()
    {
        var once = ToolError.Fail("boom");
        ToolError.Fail(once).Should().Be(once);
    }

    [Fact]
    public void PlainText_IsNotAnError()
    {
        var result = ToolError.Unwrap("Set EditorID = 'Foo'");
        result.IsError.Should().BeFalse();
        result.Text.Should().Be("Set EditorID = 'Foo'");
    }



    [Theory]
    [InlineData("No conflicts found")]
    [InlineData("No problems found for 000800:Foo.esp.")]
    [InlineData("Nothing references 000800:Foo.esp.")]
    [InlineData("No records of type 'KYWD' in Foo.esp.")]
    [InlineData("No deleted records.")]
    public void EmptySuccessMessages_AreNotErrors(string text)
    {
        ToolError.Unwrap(text).IsError.Should().BeFalse();
    }


    [Theory]
    [InlineData("Could not resolve 'Foo'.")]
    [InlineData("Failed to open 'Foo.esp' for editing: denied")]
    [InlineData("Cannot write 'out.esp': denied")]
    [InlineData("Invalid FormKey.")]
    [InlineData("Unknown tool: bogus")]
    [InlineData("Tool error: boom")]
    [InlineData("No environment loaded. Use 'Load Env' or 'Open MO2' first.")]
    public void LegacyFailureOpeners_AreDetected(string text)
    {
        ToolError.Unwrap(text).IsError.Should().BeTrue();
    }



    [Fact]
    public void FailurePhraseLaterInBody_DoesNotFlipASuccessfulRead()
    {
        var dump = "EditorID: TestRecord\nNote: Could not resolve 'X' (historical comment)";
        ToolError.Unwrap(dump).IsError.Should().BeFalse();
    }
}
