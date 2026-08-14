using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using FO4RecordEditor.Models;

namespace FO4RecordEditor.Services;

public sealed class OllamaProvider : IAIProvider
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _model;

    public string Name => "Ollama";

    public OllamaProvider(string baseUrl = "http://localhost:11434", string model = "llama3.1")
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _model = model;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var payload = new
        {
            model = _model,
            stream = true,
            messages = messages.Select(m => new
            {
                role = m.Role switch
                {
                    ChatRole.System => "system",
                    ChatRole.User => "user",
                    _ => "assistant"
                },
                content = m.Content
            }).ToArray()
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/chat")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        HttpResponseMessage? resp = null;
        string? error = null;
        try { resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct); }
        catch (Exception ex) { error = $"[Ollama unreachable: {ex.Message}]"; }

        if (error != null || resp == null)
        {
            yield return error ?? "[Ollama unreachable]";
            yield break;
        }

        using (resp)
        using (var stream = await resp.Content.ReadAsStreamAsync(ct))
        using (var reader = new StreamReader(stream))
        {
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line)) continue;
                string? text = null;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    if (doc.RootElement.TryGetProperty("message", out var msg) &&
                        msg.TryGetProperty("content", out var c))
                        text = c.GetString();
                }
                catch { }
                if (!string.IsNullOrEmpty(text)) yield return text;
            }
        }
    }
}
