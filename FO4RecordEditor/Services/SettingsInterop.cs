using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using FO4RecordEditor.ViewModels;
using Newtonsoft.Json;

namespace FO4RecordEditor.Services;

/// <summary>
/// WebView2 host object backing the React Settings panel. Reads/writes the same AppSettings the old
/// WPF SettingsDialog edited, then persists and rebuilds the AI provider so changes take effect live.
/// </summary>
[ClassInterface(ClassInterfaceType.AutoDual)]
[ComVisible(true)]
public class SettingsInterop
{
    private readonly ShellViewModel _shell;

    public SettingsInterop(ShellViewModel shell) => _shell = shell;

    /// <summary>The editable settings as JSON. OutputFolder is resolved to its effective default
    /// when blank (matching the old dialog), so the field always shows where saves will land.</summary>
    public string GetSettings()
    {
        var s = _shell.Settings.Current;
        return JsonConvert.SerializeObject(new
        {
            s.AiProvider,
            s.AnthropicApiKey,
            s.Model,
            s.GeminiApiKey,
            s.GeminiModel,
            s.ClaudeCodePath,
            s.OllamaUrl,
            s.OllamaModel,
            OutputFolder = string.IsNullOrWhiteSpace(s.OutputFolder)
                ? WriteService.DefaultOutputDir
                : s.OutputFolder,
            s.DataFolder,
            s.Mo2InstancePath,
            s.CkWikiPath,
            s.TexconvPath,
            s.PapyrusCompilerPath,
            s.PapyrusBaseImports,
            s.NiftoolPath,
            s.FfmpegPath,
            s.XwmaEncodePath,
            s.Archive2Path,
            s.ReadLargePluginsIntoMemory,
        });
    }

    /// <summary>Apply, persist, and rebuild the AI provider. Returns a human-readable result.</summary>
    public string SaveSettings(string json)
    {
        DebugLog.Interop(nameof(SaveSettings));
        try
        {
            var dto = JsonConvert.DeserializeObject<SettingsDto>(json);
            if (dto == null) return "Invalid settings payload.";

            var s = _shell.Settings.Current;
            if (dto.AiProvider != null)      s.AiProvider = dto.AiProvider;
            if (dto.AnthropicApiKey != null) s.AnthropicApiKey = dto.AnthropicApiKey;
            if (dto.Model != null)           s.Model = dto.Model;
            if (dto.GeminiApiKey != null)    s.GeminiApiKey = dto.GeminiApiKey;
            if (dto.GeminiModel != null)     s.GeminiModel = dto.GeminiModel;
            if (dto.ClaudeCodePath != null)  s.ClaudeCodePath = dto.ClaudeCodePath;
            if (dto.OllamaUrl != null)       s.OllamaUrl = dto.OllamaUrl;
            if (dto.OllamaModel != null)     s.OllamaModel = dto.OllamaModel;
            if (dto.OutputFolder != null)    s.OutputFolder = dto.OutputFolder.Trim();
            if (dto.DataFolder != null)      s.DataFolder = dto.DataFolder.Trim();
            if (dto.CkWikiPath != null)      s.CkWikiPath = dto.CkWikiPath.Trim();
            if (dto.TexconvPath != null)         s.TexconvPath = dto.TexconvPath.Trim();
            if (dto.PapyrusCompilerPath != null) s.PapyrusCompilerPath = dto.PapyrusCompilerPath.Trim();
            if (dto.PapyrusBaseImports != null)  s.PapyrusBaseImports = dto.PapyrusBaseImports.Trim();
            if (dto.NiftoolPath != null)         s.NiftoolPath = dto.NiftoolPath.Trim();
            if (dto.FfmpegPath != null)          s.FfmpegPath = dto.FfmpegPath.Trim();
            if (dto.XwmaEncodePath != null)      s.XwmaEncodePath = dto.XwmaEncodePath.Trim();
            if (dto.Archive2Path != null)        s.Archive2Path = dto.Archive2Path.Trim();
            if (dto.ReadLargePluginsIntoMemory.HasValue) s.ReadLargePluginsIntoMemory = dto.ReadLargePluginsIntoMemory.Value;

            _shell.Settings.Save();
            _shell.RebuildProvider();
            // ToolPaths caches AppSettings in a static field it never invalidated on its own -- without
            // this, editing these three here would silently require an app restart to take effect,
            // unlike every other field on this panel.
            ToolPaths.Invalidate();
            return "Settings saved.";
        }
        catch (Exception ex)
        {
            DebugLog.Exception("SaveSettings", ex);
            return "Save failed: " + ex.Message;
        }
    }

    /// <summary>Native folder picker for the path fields. Returns the chosen path, or "" if cancelled.</summary>
    public string BrowseFolder(string title, string current)
    {
        var seed = !string.IsNullOrWhiteSpace(current) && System.IO.Directory.Exists(current) ? current : "";
        return HostServices.PickFolder(string.IsNullOrWhiteSpace(title) ? "Choose a folder" : title, seed);
    }

    /// <summary>Native file picker for the .exe path fields (texconv, PapyrusCompiler). Returns the
    /// chosen path, or "" if cancelled.</summary>
    public string BrowseFile(string title, string filter, string current)
    {
        var seed = !string.IsNullOrWhiteSpace(current) && System.IO.File.Exists(current)
            ? System.IO.Path.GetDirectoryName(current) ?? "" : "";
        return HostServices.PickFile(title, filter, seed);
    }

    /// <summary>Run `&lt;path&gt; --version` to verify the Claude Code CLI is reachable.</summary>
    public async Task<string> TestClaude(string path)
    {
        path = string.IsNullOrWhiteSpace(path) ? "claude" : path;
        try
        {
            var psi = new ProcessStartInfo { FileName = "cmd.exe" };
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(path);
            psi.ArgumentList.Add("--version");

            // Was: read stdout to EOF, then WaitForExitAsync, with stderr redirected but never
            // drained and no timeout at all. A child that wrote enough to stderr to fill that pipe
            // blocked forever, and this is on the settings dialog's button -- it froze the UI.
            var run = await ProcessRunner.RunAsync(psi, TimeSpan.FromSeconds(30));

            if (!run.Started) return "✗ Could not start the Claude Code CLI.";
            if (run.TimedOut) return "✗ `claude --version` timed out after 30s.";
            return run.ExitCode == 0
                ? $"✓ Found: {run.StdOut.Trim()}"
                : "✗ `claude` ran but returned an error.";
        }
        catch (Exception ex)
        {
            return $"✗ Not found: {ex.Message}. Install the Claude Code CLI or set the full path.";
        }
    }

    private sealed class SettingsDto
    {
        public string? AiProvider { get; set; }
        public string? AnthropicApiKey { get; set; }
        public string? Model { get; set; }
        public string? GeminiApiKey { get; set; }
        public string? GeminiModel { get; set; }
        public string? ClaudeCodePath { get; set; }
        public string? OllamaUrl { get; set; }
        public string? OllamaModel { get; set; }
        public string? OutputFolder { get; set; }
        public string? DataFolder { get; set; }
        public string? CkWikiPath { get; set; }
        public string? TexconvPath { get; set; }
        public string? PapyrusCompilerPath { get; set; }
        public string? PapyrusBaseImports { get; set; }
        public string? NiftoolPath { get; set; }
        public string? FfmpegPath { get; set; }
        public string? XwmaEncodePath { get; set; }
        public string? Archive2Path { get; set; }
        public bool? ReadLargePluginsIntoMemory { get; set; }
    }
}
