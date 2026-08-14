using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;

namespace FO4RecordEditor.Tests;

public class CommandRegistryTests
{
    [Fact]
    public void Search_FuzzyMatchesSubsequence()
    {
        var r = new CommandRegistry();
        r.Register("scan", "Run Error Scan", "Errors", () => { });
        r.Register("compile", "Compile Plugin", "Build", () => { });
        r.Search("res").Select(c => c.Id).Should().Contain("scan");
        r.Search("comp").Select(c => c.Id).Should().Contain("compile");
    }
}
