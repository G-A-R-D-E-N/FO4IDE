using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using FO4RecordEditor.ViewModels;

namespace FO4RecordEditor.Services;

[ClassInterface(ClassInterfaceType.AutoDual)]
[ComVisible(true)]
public class PapyrusInterop
{
    private readonly ShellViewModel _shell;
    public PapyrusInterop(ShellViewModel shell) => _shell = shell;

    public string BrowseForFile(string title, string filter)
    {
        return HostServices.PickFile(title, filter);
    }

    public string BrowseForFolder(string title)
    {
        return HostServices.PickFolder(title);
    }

    public Task<string> Compile(string source, string output, string imports, string flags,
        bool all, bool optimize, bool release, string compilerPath, string engine) =>
        Task.Run(() =>
        {
            try
            {
                return PapyrusService.Compile(
                    source, N(output), N(imports), N(flags), all, optimize, release, N(compilerPath), N(engine));
            }
            catch (Exception ex) { DebugLog.Exception("Papyrus.Compile", ex); return "Error: " + ex.Message; }
        });

    public Task<string> Decompile(string source, string output, bool assembly, bool write) =>
        Task.Run(() =>
        {
            try { return PapyrusService.Decompile(source, N(output), assembly, write); }
            catch (Exception ex) { DebugLog.Exception("Papyrus.Decompile", ex); return "Error: " + ex.Message; }
        });

    public Task<string> LookupFunction(string script, string functionName) =>
        Task.Run(() =>
        {
            try { return PapyrusWikiService.LookupFunction(ToolPaths.CkWiki() ?? "", script, functionName); }
            catch (Exception ex) { DebugLog.Exception("Papyrus.LookupFunction", ex); return "Error: " + ex.Message; }
        });

    public Task<string> LookupScriptInfo(string script) =>
        Task.Run(() =>
        {
            try { return PapyrusWikiService.LookupScriptInfo(ToolPaths.CkWiki() ?? "", script); }
            catch (Exception ex) { DebugLog.Exception("Papyrus.LookupScriptInfo", ex); return "Error: " + ex.Message; }
        });

    public Task<string> Analyze(string text, string path) =>
        Task.Run(() =>
        {
            try { return Papyrus.PapyrusAnalysisService.AnalyzeJson(text ?? "", N(path)); }
            catch (Exception ex)
            {
                DebugLog.Exception("Papyrus.Analyze", ex);
                return "{\"error\":" + Newtonsoft.Json.JsonConvert.ToString(ex.Message) + "}";
            }
        });

    public Task<string> SymbolAt(string text, string path, int offset, string imports) =>
        Task.Run(() =>
        {
            try { return Papyrus.PapyrusAnalysisService.SymbolAtJson(text ?? "", N(path), offset, N(imports)); }
            catch (Exception ex)
            {
                DebugLog.Exception("Papyrus.SymbolAt", ex);
                return "{\"error\":" + Newtonsoft.Json.JsonConvert.ToString(ex.Message) + "}";
            }
        });

    public Task<string> ReadScript(string path) =>
        Task.Run(() =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return "ERR:No path.";
                if (!File.Exists(path)) return "ERR:Not found: " + path;
                return File.ReadAllText(path);
            }
            catch (Exception ex) { DebugLog.Exception("Papyrus.ReadScript", ex); return "ERR:" + ex.Message; }
        });

    public Task<string> WriteScript(string path, string text) =>
        Task.Run(() =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return "ERR:No path.";
                if (!path.EndsWith(".psc", StringComparison.OrdinalIgnoreCase))
                    return "ERR:Refusing to write '" + Path.GetFileName(path) + "' -- this saves Papyrus source, so the path must end in .psc.";
                var dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (dir != null) Directory.CreateDirectory(dir);
                File.WriteAllText(path, text ?? "");
                return "";
            }
            catch (Exception ex) { DebugLog.Exception("Papyrus.WriteScript", ex); return "ERR:" + ex.Message; }
        });

    public string OpenFolder(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return "No path.";
            path = path.Trim().Trim('"');
            if (File.Exists(path)) path = Path.GetDirectoryName(path) ?? path;
            if (!Directory.Exists(path)) return "Folder not found: " + path;
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            return "";
        }
        catch (Exception ex) { return "Error: " + ex.Message; }
    }

    public string StageDroppedFile(string name, string base64)
    {
        try
        {
            var safe = Path.GetFileName(string.IsNullOrWhiteSpace(name) ? "dropped.bin" : name);
            var dir = Path.Combine(Path.GetTempPath(), "FO4RE_Drop_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, safe);
            File.WriteAllBytes(path, Convert.FromBase64String(base64));
            return path;
        }
        catch (Exception ex) { return "ERR:" + ex.Message; }
    }

    private static string? N(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
