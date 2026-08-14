using System.Diagnostics;
using FO4RecordEditor.Services;

namespace FO4RecordEditor.Server;

public static class LinuxHostServices
{
    private static readonly object UiLock = new();
    private static string? _picker;

    public static void Install()
    {
        HostServices.ShowFileDialog = Show;
        HostServices.ShowMessage = ShowMessage;

        HostServices.InvokeOnUiThread = a => { lock (UiLock) a(); };
    }

    private static string Picker => _picker ??=
        Which("zenity") ? "zenity" : Which("kdialog") ? "kdialog" : "";

    private static bool Which(string exe)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':'))
            if (dir.Length > 0 && File.Exists(Path.Combine(dir, exe))) return true;
        return false;
    }

    private static string Show(FileDialogRequest r)
    {
        try
        {
            return Picker switch
            {
                "zenity" => Run("zenity", ZenityArgs(r)),
                "kdialog" => Run("kdialog", KdialogArgs(r)),
                _ => "",
            };
        }
        catch (Exception ex)
        {
            DebugLog.Exception("HostServices.ShowFileDialog", ex);
            return "";
        }
    }

    private static List<string> ZenityArgs(FileDialogRequest r)
    {
        var a = new List<string> { "--file-selection", "--title=" + r.Title };
        if (r.Kind == FileDialogKind.OpenFolder) a.Add("--directory");
        if (r.Kind == FileDialogKind.SaveFile) { a.Add("--save"); a.Add("--confirm-overwrite"); }
        if (!string.IsNullOrWhiteSpace(r.InitialDirectory))
            a.Add("--filename=" + r.InitialDirectory.TrimEnd('/') + "/");
        foreach (var f in ParseFilter(r.Filter)) a.Add("--file-filter=" + f);
        return a;
    }

    private static List<string> KdialogArgs(FileDialogRequest r)
    {
        var start = string.IsNullOrWhiteSpace(r.InitialDirectory) ? "." : r.InitialDirectory;
        return r.Kind switch
        {
            FileDialogKind.OpenFolder => ["--title", r.Title, "--getexistingdirectory", start],
            FileDialogKind.SaveFile => ["--title", r.Title, "--getsavefilename", start],
            _ => ["--title", r.Title, "--getopenfilename", start],
        };
    }

    private static IEnumerable<string> ParseFilter(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) yield break;
        var parts = filter.Split('|');
        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            var patterns = parts[i + 1].Replace(';', ' ');
            yield return $"{parts[i]} | {patterns}";
        }
    }

    private static void ShowMessage(string text)
    {
        try
        {
            if (Picker == "zenity") Run("zenity", ["--info", "--no-wrap", "--text=" + text]);
            else if (Picker == "kdialog") Run("kdialog", ["--msgbox", text]);
            else Console.Error.WriteLine("[FO4IDE] " + text);
        }
        catch (Exception ex) { DebugLog.Exception("HostServices.ShowMessage", ex); }
    }

    private static string Run(string exe, List<string> args)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi);
        if (p == null) return "";
        var outp = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode == 0 ? outp.Trim() : "";
    }
}
