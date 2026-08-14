using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using FO4RecordEditor.ViewModels;

namespace FO4RecordEditor.Services;

// WebView2 host object for the Papyrus panel: compile .psc -> .pex (CK compiler), decompile
// .pex -> .psc (our in-process decompiler), and look up functions/scripts in the offline CK Wiki
// mirror, and parse .psc source for the Analyze editor. Same COM rules as AppInterop: AutoDual,
// ComVisible, only string/bool/int params and
// string/Task<string> returns. Native file pickers run on the UI thread (host-object calls are
// dispatched there); compile/decompile/lookup run on a background task so the long process call
// (or a big wiki directory scan) never blocks the UI. The wiki root is read live from Settings on
// each lookup call (MastersInterop's pattern) rather than captured at construction, so changing it
// in the Settings panel takes effect on the next lookup with no restart.
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

    /// <summary>
    /// Compiles source, through whichever engine <paramref name="engine"/> selects.
    /// </summary>
    /// <param name="engine">
    /// <c>auto</c> (empty counts as auto), <c>builtin</c> or <c>creationkit</c>. Auto prefers an
    /// installed Creation Kit, so an existing setup keeps behaving exactly as it did, and uses the
    /// built-in compiler when there is none.
    /// </param>
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

    /// <summary>Look up a function's Syntax/Parameters/Return Value from the offline CK Wiki mirror
    /// (bundled with the app; Settings -> CK Wiki Path overrides it). See PapyrusWikiService.LookupFunction.</summary>
    public Task<string> LookupFunction(string script, string functionName) =>
        Task.Run(() =>
        {
            try { return PapyrusWikiService.LookupFunction(ToolPaths.CkWiki() ?? "", script, functionName); }
            catch (Exception ex) { DebugLog.Exception("Papyrus.LookupFunction", ex); return "Error: " + ex.Message; }
        });

    /// <summary>Look up a script's Extends/Global Functions/Member Functions/Events from the offline
    /// CK Wiki mirror (bundled with the app). See PapyrusWikiService.LookupScriptInfo.</summary>
    public Task<string> LookupScriptInfo(string script) =>
        Task.Run(() =>
        {
            try { return PapyrusWikiService.LookupScriptInfo(ToolPaths.CkWiki() ?? "", script); }
            catch (Exception ex) { DebugLog.Exception("Papyrus.LookupScriptInfo", ex); return "Error: " + ex.Message; }
        });

    // ---------------------------------------------------------------------------------------------
    // Source analysis for the Analyze mode. These take the editor BUFFER, not a path: "errors as you
    // type" is about unsaved text, and re-reading the file would report the last save instead of
    // what is on screen. They return JSON rather than the MCP tools' prose, because the panel needs
    // positions it can select on. None of this touches the Creation Kit -- see
    // PapyrusAnalysisService: it parses, it does not compile.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Diagnostics plus the outline for a buffer, in one call. See PapyrusAnalysisService.AnalyzeJson.</summary>
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

    /// <summary>The declaration of the symbol at a 0-based offset. See PapyrusAnalysisService.SymbolAtJson.</summary>
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

    /// <summary>Read a .psc into the editor. Returns the text, or a string starting with "ERR:".</summary>
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

    /// <summary>
    /// Write the editor buffer back to a .psc. Returns "" on success, or a message starting with "ERR:".
    /// </summary>
    /// <remarks>
    /// Refuses anything that is not a .psc. This is the only write path the Papyrus panel has, and
    /// the panel hands it whatever path is in the source box -- which the user may have typed, or
    /// dragged in, or left pointing at a .pex from a previous decompile. Overwriting a compiled
    /// script with source text would destroy it silently.
    /// </remarks>
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

    /// <summary>Open a folder (or the folder containing a file) in Windows Explorer. Returns "" on success.</summary>
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

    /// <summary>Write a dropped file's bytes to a temp path and return that path, so drag-and-drop can
    /// set a usable source (WebView2 does not expose dropped files' real OS paths to JS). Returns the
    /// temp path, or a string starting with "ERR:" on failure.</summary>
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
