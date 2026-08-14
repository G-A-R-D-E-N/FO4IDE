namespace FO4RecordEditor.Services;

public static class AiProviderFactory
{
    public static IAIProvider Create(AppSettings s) => s.AiProvider switch
    {
        "ollama"     => new OllamaProvider(s.OllamaUrl, s.OllamaModel),
        "claudecode" => new ClaudeCodeProvider(s.ClaudeCodePath),
        _            => new AnthropicProvider(s.AnthropicApiKey, s.Model),
    };
}
