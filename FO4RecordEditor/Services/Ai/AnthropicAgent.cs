using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FO4RecordEditor.Services;

/// <summary>
/// Agentic Anthropic client with tool use. Runs the read/act loop: send the user's
/// question with the plugin tools, execute any tool calls the model makes against the
/// loaded data, feed results back, and repeat until the model produces a final answer.
/// Non-streaming per turn (reliable tool_use parsing); text is delivered via onText.
/// </summary>
public sealed class AnthropicAgent
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly PluginToolExecutor _executor;
    private readonly List<object> _history = new();

    private const int MaxToolIterations = 8;

    public AnthropicAgent(string apiKey, string model, PluginToolExecutor executor)
    {
        _apiKey = apiKey;
        _model = model;
        _executor = executor;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    public void Reset() => _history.Clear();

    /// <summary>Replace the agent's working history with a rebuilt set of turns (used by /compact,
    /// which keeps recent messages verbatim after a summary). A leading non-user turn is prefixed
    /// with a synthetic user message so the Messages API's required user/assistant alternation holds.</summary>
    public void LoadHistory(IReadOnlyList<(bool IsUser, string Text)> turns)
    {
        _history.Clear();
        bool first = true;
        foreach (var (isUser, text) in turns)
        {
            if (string.IsNullOrEmpty(text)) continue;
            if (first && !isUser)
                _history.Add(new { role = "user", content = "(continuing from a compacted summary)" });
            _history.Add(new { role = isUser ? "user" : "assistant", content = text });
            first = false;
        }
    }

    /// <param name="onText">Invoked with each text chunk the model emits.</param>
    /// <param name="onToolStatus">Invoked when the model calls a tool (for a UI status line).</param>
    public async Task<string> RunAsync(
        string userMessage, string? systemContext,
        Action<string> onText, Action<string>? onToolStatus = null, CancellationToken ct = default,
        Action<string>? onUsage = null)
    {
        _history.Add(new { role = "user", content = userMessage });
        var finalText = new StringBuilder();

        // Prompt-cache the stable prefix (tools + system). Within one question the tool loop
        // re-sends this prefix up to 8x, and across questions the tool block is identical -- cached
        // tokens cost ~0.1x, which is the main lever for cutting API spend.
        var systemBlocks = string.IsNullOrWhiteSpace(systemContext) ? null : new object[]
        {
            new { type = "text", text = systemContext, cache_control = new { type = "ephemeral" } }
        };

        long totalIn = 0, totalCacheRead = 0, totalCacheWrite = 0, totalOut = 0;

        for (int iteration = 0; iteration < MaxToolIterations; iteration++)
        {
            var payload = new
            {
                model = _model,
                max_tokens = 4096,
                system = systemBlocks,
                tools = PluginToolExecutor.ToolDefinitionsCached(),
                messages = _history,
            };

            string respJson = await PostAsync(payload, ct);
            using var doc = JsonDocument.Parse(respJson);
            var rootEl = doc.RootElement;

            if (rootEl.TryGetProperty("type", out var tp) && tp.GetString() == "error")
            {
                var msg = rootEl.TryGetProperty("error", out var errEl) &&
                          errEl.TryGetProperty("message", out var mEl) ? mEl.GetString() : respJson;
                var errText = $"[AI error] {msg}";
                onText(errText);
                return errText;
            }

            if (rootEl.TryGetProperty("usage", out var usageEl))
            {
                long U(string k) => usageEl.TryGetProperty(k, out var v) && v.TryGetInt64(out var n) ? n : 0;
                totalIn += U("input_tokens");
                totalCacheRead += U("cache_read_input_tokens");
                totalCacheWrite += U("cache_creation_input_tokens");
                totalOut += U("output_tokens");
            }

            var contentArr = rootEl.GetProperty("content");
            var stopReason = rootEl.TryGetProperty("stop_reason", out var sr) ? sr.GetString() : null;

            // Record the assistant turn verbatim so tool_use ids round-trip correctly.
            _history.Add(new { role = "assistant", content = contentArr.Clone() });

            var toolResults = new List<object>();
            foreach (var block in contentArr.EnumerateArray())
            {
                var btype = block.GetProperty("type").GetString();
                if (btype == "text")
                {
                    var txt = block.GetProperty("text").GetString() ?? "";
                    finalText.Append(txt);
                    onText(txt);
                }
                else if (btype == "tool_use")
                {
                    var id = block.GetProperty("id").GetString()!;
                    var name = block.GetProperty("name").GetString()!;
                    var input = block.GetProperty("input").GetRawText();
                    onToolStatus?.Invoke($"{name} {input}");

                    string result;
                    try { result = _executor.Execute(name, input); }
                    catch (Exception ex) { result = $"Tool error: {ex.Message}"; }

                    if (result.Length > 12000) result = result[..12000] + "\n...(truncated)";
                    toolResults.Add(new { type = "tool_result", tool_use_id = id, content = result });
                }
            }

            if (stopReason == "tool_use" && toolResults.Count > 0)
            {
                _history.Add(new { role = "user", content = toolResults });
                continue; // model wants to act on the tool results
            }
            break; // final answer produced
        }

        // Report token usage so the user can see cost (and that caching is working): cached input
        // tokens bill at ~0.1x, so a high "cached" share means most of the prefix was reused.
        if (onUsage != null && (totalIn + totalCacheRead + totalCacheWrite + totalOut) > 0)
        {
            long billedIn = totalIn + totalCacheWrite;
            onUsage($"Tokens -- in {billedIn:N0} (+{totalCacheRead:N0} cached @0.1x), out {totalOut:N0}");
        }

        return finalText.ToString();
    }

    private async Task<string> PostAsync(object payload, CancellationToken ct)
    {
        // Same guard as AnthropicProvider: a blank key produced an upstream 401 reading
        // "x-api-key header is required", which points at the wrong thing. Say what is actually wrong.
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            return JsonSerializer.Serialize(new
            {
                type = "error",
                error = new
                {
                    type = "not_configured",
                    message = "No Anthropic API key is configured, so the request was not sent. "
                        + "Add one in Settings > AI (Anthropic API Key), or switch the AI provider to "
                        + "Claude Code or Ollama, which need no key.",
                },
            });
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        req.Headers.Add("x-api-key", _apiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");
        req.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, ct);
        return await resp.Content.ReadAsStringAsync(ct);
    }
}
