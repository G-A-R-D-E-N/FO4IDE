namespace FO4RecordEditor.Services;

public sealed class AppSettings
{
    public string AnthropicApiKey { get; set; } = "";
    // Default to a current model. claude-3-7-sonnet was retired (Feb 2026) and 404s on the API /
    // is rejected by the CLI. A full id works for both the Anthropic API and the Claude Code CLI.
    public string Model { get; set; } = "claude-opus-4-8";
    public string AiProvider { get; set; } = "anthropic";   // anthropic | claudecode | gemini | ollama
    public string GeminiApiKey { get; set; } = "";
    public string GeminiModel { get; set; } = "gemini-2.0-flash";
    public string OllamaUrl { get; set; } = "http://localhost:11434";
    public string OllamaModel { get; set; } = "llama3.1";
    public string ClaudeCodePath { get; set; } = "claude";  // CLI on PATH, or full path
    public string LastFolder { get; set; } = "";
    public string SpriggitExePath { get; set; } = "";
    public string OutputFolder { get; set; } = "";   // where save_plugin writes (blank = <app>/Output)
    public string DataFolder { get; set; } = "";   // Load Env data-folder override (blank = auto-detect / MO2 VFS)
    public string Mo2InstancePath { get; set; } = "";   // MO2 instance folder (the one with mods\ and profiles\)
    public string Fallout4Path { get; set; } = "";   // FO4 install root (blank = registry / Steam auto-detect)
    public string NiftoolPath { get; set; } = "";   // niftool.exe (blank = bundled copy next to the exe)
    public string TexconvPath { get; set; } = "";   // Texconvx64.exe (blank = bundled copy next to the exe)
    public string PapyrusCompilerPath { get; set; } = "";   // CK PapyrusCompiler.exe (blank = from the FO4 install)
    public string PapyrusBaseImports { get; set; } = "";   // extra base-script roots, ';'-separated, highest first
    public string CkWikiPath { get; set; } = "";   // offline Creation Kit Wiki HTML mirror root override (blank = use the bundled copy)
    public string FfmpegPath { get; set; } = "";   // ffmpeg.exe (blank = bundled copy next to the exe)
    public string XwmaEncodePath { get; set; } = "";   // xWMAEncode.exe (blank = bundled copy next to the exe)
    public string Archive2Path { get; set; } = "";   // Archive2.exe (blank = auto-detect from the Fallout 4/CK install)
    // Read every plugin fully into memory instead of opening the large ones as binary overlays.
    // OFF by default: overlays are faster and keep record data off the managed heap, and that is what
    // the tool has always done. Turn it on when saving over a loaded plugin fails with the ".new file
    // beside it" message -- an overlay keeps an OPEN FILE HANDLE for as long as the environment lives
    // (verified on the real Fallout4.esm via /proc/self/fd), and on Windows an open handle is what
    // blocks an in-place overwrite.
    //
    // Measured cost, so the default is not a guess: on a real 657-plugin load order the 39 plugins
    // over 1 MB are 197 MB on disk but ~2.2 GB once read into managed record objects (about 11x).
    // That is why this defaults OFF rather than on.
    public bool ReadLargePluginsIntoMemory { get; set; } = false;

    public double SidePanelWidth { get; set; } = 320;
    public double AiPanelWidth { get; set; } = 420;
    public double BottomPanelHeight { get; set; } = 200;
}
