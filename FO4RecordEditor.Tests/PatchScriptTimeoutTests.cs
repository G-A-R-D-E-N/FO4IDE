using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;
using Xunit.Abstractions;

namespace FO4RecordEditor.Tests;

// run_script had no CancellationToken, so `while(true){}` wedged the thread forever -- and for the
// stdio MCP server that thread IS the server.
public class PatchScriptTimeoutTests
{
    private readonly ITestOutputHelper _out;
    public PatchScriptTimeoutTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void InfiniteLoop_IsAbandonedAtTheTimeout()
    {
        var original = PatchScriptRunner.ScriptTimeout;
        PatchScriptRunner.ScriptTimeout = TimeSpan.FromSeconds(3);
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = PatchScriptRunner.Run(
                "while(true){}", null, $"TimeoutTest_{Guid.NewGuid():N}.esp", dryRun: true);
            sw.Stop();

            _out.WriteLine($"elapsed={sw.Elapsed.TotalSeconds:0.0}s\n{result}");

            result.Should().Contain("was abandoned");
            sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(60));
        }
        finally { PatchScriptRunner.ScriptTimeout = original; }
    }

    [Fact]
    public void NormalScript_StillRunsToCompletion()
    {
        var result = PatchScriptRunner.Run(
            "var x = 1 + 1;", null, $"TimeoutTest_{Guid.NewGuid():N}.esp", dryRun: true);

        result.Should().NotContain("was abandoned");
        result.Should().Contain("DRY RUN");
    }
}
