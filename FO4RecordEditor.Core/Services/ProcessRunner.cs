using System.Diagnostics;
using System.IO;

namespace FO4RecordEditor.Services;

public readonly record struct ProcessResult(bool Started, bool TimedOut, int ExitCode, string StdOut, string StdErr)
{

    public string Combined => (StdOut + "\n" + StdErr).Replace("\r", "").Trim();
}

public static class ProcessRunner
{

    public static ProcessResult Run(ProcessStartInfo psi, TimeSpan timeout)
    {
        PrepareForCapture(psi);

        using var p = Process.Start(psi);
        if (p == null) return new ProcessResult(false, false, -1, "", "");

        var so = p.StandardOutput.ReadToEndAsync();
        var se = p.StandardError.ReadToEndAsync();

        if (!p.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            DrainRemaining(so, se);
            return new ProcessResult(true, true, -1, TextOf(so), TextOf(se));
        }

        DrainRemaining(so, se);
        return new ProcessResult(true, false, p.ExitCode, TextOf(so), TextOf(se));
    }

    public static async Task<ProcessResult> RunAsync(
        ProcessStartInfo psi, TimeSpan timeout, CancellationToken ct = default)
    {
        PrepareForCapture(psi);

        using var p = Process.Start(psi);
        if (p == null) return new ProcessResult(false, false, -1, "", "");

        var so = p.StandardOutput.ReadToEndAsync(ct);
        var se = p.StandardError.ReadToEndAsync(ct);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await p.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            DrainRemaining(so, se);
            return new ProcessResult(true, true, -1, TextOf(so), TextOf(se));
        }

        DrainRemaining(so, se);
        return new ProcessResult(true, false, p.ExitCode, TextOf(so), TextOf(se));
    }

    private static void ShimWindowsExe(ProcessStartInfo psi)
    {
        if (OperatingSystem.IsWindows()) return;
        if (!psi.FileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return;

        var wine = Environment.GetEnvironmentVariable("FO4RE_WINE");
        if (wine != null && wine.Length == 0) return;
        if (string.IsNullOrWhiteSpace(wine)) wine = "wine";

        if (!string.IsNullOrEmpty(psi.Arguments))
            psi.Arguments = Quote(psi.FileName) + " " + psi.Arguments;
        else
            psi.ArgumentList.Insert(0, psi.FileName);

        psi.FileName = wine;

        psi.Environment.Remove("DOTNET_ROOT");
    }

    private static string Quote(string s) => s.Contains(' ') ? "\"" + s + "\"" : s;

    private static void PrepareForCapture(ProcessStartInfo psi)
    {
        ShimWindowsExe(psi);
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
    }

    private static void DrainRemaining(Task<string> so, Task<string> se)
    {
        try { Task.WaitAll(new Task[] { so, se }, 10_000); } catch { }
    }

    private static string TextOf(Task<string> t) => t.IsCompletedSuccessfully ? t.Result : "";
}
