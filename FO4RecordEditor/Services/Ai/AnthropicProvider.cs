using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using FO4RecordEditor.Models;

namespace FO4RecordEditor.Services;

public sealed class AnthropicProvider : IAIProvider
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;

    public string Name => "Anthropic";

    public AnthropicProvider(string apiKey, string model = "claude-opus-4-8")
    {
        _apiKey = apiKey;
        _model = model;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Refuse before the network, not after. With a blank key this sent x-api-key: "" and the user
        // got Anthropic's own 401 -- '{"message":"x-api-key header is required"}' -- which reads like
        // the app forgot to attach the header, when in fact no key was ever configured. The provider
        // is also the fallback in AiProviderFactory, so an unset AiProvider lands here by default.
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            yield return "No Anthropic API key is configured, so the request was not sent. "
                + "Add one in Settings > AI (Anthropic API Key), or switch the AI provider to "
                + "Claude Code or Ollama, which need no key.";
            yield break;
        }

        var system = string.Join("\n\n",
            messages.Where(m => m.Role == ChatRole.System).Select(m => m.Content));
        var turns = messages.Where(m => m.Role != ChatRole.System)
            .Select(m => new
            {
                role = m.Role == ChatRole.User ? "user" : "assistant",
                content = m.Content
            }).ToArray();

        var payload = new
        {
            model = _model,
            max_tokens = 4096,
            stream = true,
            system = string.IsNullOrWhiteSpace(system) ? null : system,
            messages = turns,
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        req.Headers.Add("x-api-key", _apiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");
        req.Content = new StringContent(
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { DefaultIgnoreCondition =
                System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }),
            Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            yield return $"[AI error {(int)resp.StatusCode}] {err}";
            yield break;
        }

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data:")) continue;
            var json = line["data:".Length..].Trim();
            if (json == "[DONE]") break;

            string? text = null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("type", out var t) &&
                    t.GetString() == "content_block_delta" &&
                    doc.RootElement.TryGetProperty("delta", out var d) &&
                    d.TryGetProperty("text", out var txt))
                    text = txt.GetString();
            }
            catch { }
            if (text != null) yield return text;
        }
    }
}
