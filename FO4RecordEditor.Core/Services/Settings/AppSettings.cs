namespace FO4RecordEditor.Services;

public sealed class AppSettings
{
    public string AnthropicApiKey { get; set; } = "";

    public string Model { get; set; } = "claude-opus-4-8";
    public string AiProvider { get; set; } = "anthropic";
    public string GeminiApiKey { get; set; } = "";
    public string GeminiModel { get; set; } = "gemini-2.0-flash";
    public string OllamaUrl { get; set; } = "http://localhost:11434";
    public string OllamaModel { get; set; } = "llama3.1";
    public string ClaudeCodePath { get; set; } = "claude";
    public string LastFolder { get; set; } = "";
    public string SpriggitExePath { get; set; } = "";
    public string OutputFolder { get; set; } = "";
    public string DataFolder { get; set; } = "";
    public string Mo2InstancePath { get; set; } = "";
    public string Fallout4Path { get; set; } = "";
    public string NiftoolPath { get; set; } = "";
    public string TexconvPath { get; set; } = "";
    public string PapyrusCompilerPath { get; set; } = "";
    public string PapyrusBaseImports { get; set; } = "";
    public string CkWikiPath { get; set; } = "";
    public string FfmpegPath { get; set; } = "";
    public string XwmaEncodePath { get; set; } = "";
    public string Archive2Path { get; set; } = "";

    public bool ReadLargePluginsIntoMemory { get; set; } = false;

    public double SidePanelWidth { get; set; } = 320;
    public double AiPanelWidth { get; set; } = 420;
    public double BottomPanelHeight { get; set; } = 200;
}
