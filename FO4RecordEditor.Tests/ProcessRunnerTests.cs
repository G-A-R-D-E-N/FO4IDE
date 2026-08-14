using System.Diagnostics;
using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;
using Xunit.Abstractions;

namespace FO4RecordEditor.Tests;

public class ProcessRunnerTests
{
    private readonly ITestOutputHelper _out;
    public ProcessRunnerTests(ITestOutputHelper o) => _out = o;

    private static ProcessStartInfo ChattyOnStderr() => PowerShell(
        "$e=[Console]::Error; 1..2000 | ForEach-Object { $e.WriteLine('x' * 200) }; " +
        "[Console]::Out.WriteLine('STDOUT-MARKER')");

    private static ProcessStartInfo PowerShell(string command)
    {
        var psi = new ProcessStartInfo { FileName = "powershell.exe" };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(command);
        return psi;
    }

    [Fact]
    public void ChildFloodingStderr_DoesNotDeadlock_AndBothStreamsAreCaptured()
    {
        var sw = Stopwatch.StartNew();
        var r = ProcessRunner.Run(ChattyOnStderr(), TimeSpan.FromSeconds(60));
        sw.Stop();
        _out.WriteLine($"elapsed={sw.Elapsed.TotalSeconds:0.0}s stdout={r.StdOut.Length} stderr={r.StdErr.Length}");

        r.Started.Should().BeTrue();
        r.TimedOut.Should().BeFalse("draining both streams concurrently must let the child finish");
        r.StdOut.Should().Contain("STDOUT-MARKER");
        r.StdErr.Length.Should().BeGreaterThan(100_000, "the whole stderr flood must be captured, not truncated");
    }

    [Fact]
    public async Task ChildFloodingStderr_DoesNotDeadlock_Async()
    {
        var r = await ProcessRunner.RunAsync(ChattyOnStderr(), TimeSpan.FromSeconds(60));

        r.TimedOut.Should().BeFalse();
        r.StdOut.Should().Contain("STDOUT-MARKER");
        r.StdErr.Length.Should().BeGreaterThan(100_000);
    }

    [Fact]
    public void HangingChild_IsKilledAtTheTimeout()
    {
        var sw = Stopwatch.StartNew();
        var r = ProcessRunner.Run(PowerShell("Start-Sleep -Seconds 120"), TimeSpan.FromSeconds(3));
        sw.Stop();
        _out.WriteLine($"elapsed={sw.Elapsed.TotalSeconds:0.0}s timedOut={r.TimedOut}");

        r.TimedOut.Should().BeTrue();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(45), "the timeout must actually fire");
    }

    [Fact]
    public async Task HangingChild_IsKilledAtTheTimeout_Async()
    {
        var sw = Stopwatch.StartNew();
        var r = await ProcessRunner.RunAsync(PowerShell("Start-Sleep -Seconds 120"), TimeSpan.FromSeconds(3));
        sw.Stop();

        r.TimedOut.Should().BeTrue();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(45));
    }

    [Fact]
    public void ExitCodeAndStreams_AreReported()
    {
        var r = ProcessRunner.Run(PowerShell("Write-Output 'out'; [Console]::Error.WriteLine('err'); exit 3"),
            TimeSpan.FromSeconds(30));

        r.Started.Should().BeTrue();
        r.TimedOut.Should().BeFalse();
        r.ExitCode.Should().Be(3);
        r.StdOut.Should().Contain("out");
        r.StdErr.Should().Contain("err");
        r.Combined.Should().Contain("out").And.Contain("err");
    }

    [Fact]
    public void MissingExecutable_ReportsNotStarted_RatherThanThrowing()
    {
        var act = () => ProcessRunner.Run(
            new ProcessStartInfo { FileName = "definitely_not_a_real_program_xyz.exe" },
            TimeSpan.FromSeconds(5));

        act.Should().Throw<System.ComponentModel.Win32Exception>();
    }
}
