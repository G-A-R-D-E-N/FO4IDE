using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.CompilerServices;
using FO4RecordEditor.Models;

namespace FO4RecordEditor.Services;

public sealed class GeminiProvider : IAIProvider
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly string _apiKey;
    private readonly string _model;
    private readonly PluginToolExecutor? _executor;
    private const int MaxToolIterations = 8;

    public string Name => "Gemini";

    public GeminiProvider(string apiKey, string model, PluginToolExecutor? executor)
    {
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? "gemini-2.0-flash" : model;
        _executor = executor;
    }

    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            yield return "[Gemini: no API key. Set it in Settings → AI Provider → Gemini.]";
            yield break;
        }

        var sys = string.Join("\n\n", messages.Where(m => m.Role == ChatRole.System).Select(m => m.Content));
        var contents = new List<object>();
        foreach (var m in messages.Where(m => m.Role != ChatRole.System))
            contents.Add(new { role = m.Role == ChatRole.User ? "user" : "model", parts = new[] { new { text = m.Content } } });

        object? tools = _executor != null
            ? new[] { new { functionDeclarations = PluginToolExecutor.GeminiToolDefinitions() } }
            : null;

        for (int iter = 0; iter < MaxToolIterations; iter++)
        {
            var payload = new Dictionary<string, object?>
            {
                ["contents"] = contents,
                ["tools"] = tools,
                ["systemInstruction"] = string.IsNullOrWhiteSpace(sys) ? null : new { parts = new[] { new { text = sys } } },
            };

            string respJson = await Post(payload, ct);

            using var doc = JsonDocument.Parse(respJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var err))
            {
                var msg = err.TryGetProperty("message", out var em) ? em.GetString() : respJson;
                yield return $"[Gemini error] {msg}";
                yield break;
            }
            if (!root.TryGetProperty("candidates", out var cands) || cands.GetArrayLength() == 0)
                yield break;

            var content = cands[0].GetProperty("content");
            contents.Add(content.Clone());

            var funcResponses = new List<object>();
            if (content.TryGetProperty("parts", out var parts))
            {
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var txt))
                    {
                        yield return txt.GetString() ?? "";
                    }
                    else if (part.TryGetProperty("functionCall", out var fc))
                    {
                        var name = fc.GetProperty("name").GetString() ?? "";
                        var args = fc.TryGetProperty("args", out var a) ? a.GetRawText() : "{}";
                        yield return $"\n🔧 {name}({Trunc(args, 120)})\n";

                        string result;
                        try { result = _executor!.Execute(name, args); }
                        catch (Exception ex) { result = "Tool error: " + ex.Message; }

                        yield return $"↳ {Trunc(OneLine(result), 160)}\n";
                        funcResponses.Add(new { functionResponse = new { name, response = new { result } } });
                    }
                }
            }

            if (funcResponses.Count > 0)
            {
                contents.Add(new { role = "user", parts = funcResponses });
                continue;
            }
            break;
        }
    }

    private async Task<string> Post(object payload, CancellationToken ct)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json"),
        };
        using var resp = await _http.SendAsync(req, ct);
        return await resp.Content.ReadAsStringAsync(ct);
    }

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max] + "…";
    private static string OneLine(string s) => s.Replace("\r", " ").Replace("\n", " ").Trim();
}
