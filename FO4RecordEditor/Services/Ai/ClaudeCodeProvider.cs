using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using FO4RecordEditor.Models;

namespace FO4RecordEditor.Services;

/// <summary>
/// AI backend that drives the local Claude Code CLI (`claude -p`) instead of the
/// Anthropic HTTP API, so the user can use their Claude Code sign-in with no API key.
/// The conversation is flattened into a single print-mode prompt fed via stdin.
/// </summary>
public sealed class ClaudeCodeProvider : IAIProvider
{
    private readonly string _exePath;
    private readonly string? _mcpUrl;
    private readonly string _mcpServerName;
    private readonly string? _model;

    public string Name => "Claude Code";

    public ClaudeCodeProvider(string exePath = "claude", string? mcpUrl = null,
        string mcpServerName = "fo4editor", string? model = null)
    {
        _exePath = string.IsNullOrWhiteSpace(exePath) ? "claude" : exePath;
        _mcpUrl = mcpUrl;
        _mcpServerName = mcpServerName;
        _model = string.IsNullOrWhiteSpace(model) ? null : model;
    }

    public async IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var prompt = new StringBuilder();
        if (_mcpUrl != null)
        {
            // Same guidance the API agent gets (efficiency first), so both paths behave identically.
            // The tools are exposed to Claude Code as mcp__<server>__<name>.
            prompt.AppendLine($"Your plugin tools are exposed as mcp__{_mcpServerName}__<name> " +
                              $"(e.g. mcp__{_mcpServerName}__scan_conflicts).\n");
            prompt.AppendLine(AiGuidance.System);
            prompt.AppendLine();
        }
        foreach (var m in messages)
        {
            switch (m.Role)
            {
                case ChatRole.System:    prompt.AppendLine(m.Content).AppendLine(); break;
                case ChatRole.User:      prompt.AppendLine("User: " + m.Content); break;
                case ChatRole.Assistant: prompt.AppendLine("Assistant: " + m.Content); break;
            }
        }

        // Route through cmd.exe so npm shims (claude.cmd / claude.ps1) resolve via PATHEXT --
        // Process.Start cannot launch a .cmd/.ps1 directly on Windows.
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath(),
            // claude emits UTF-8; without this the OEM code page mangles characters
            // like the em dash into "ΓÇö".
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("/c");
        psi.ArgumentList.Add(_exePath);                   // "claude" or a full path
        psi.ArgumentList.Add("-p");                       // print (non-interactive) mode
        // Stream JSON events instead of buffering the whole answer to the end. In text mode the
        // CLI emits nothing until it finishes, so a long run looks frozen ("thinking… 600s"); with
        // stream-json we surface each tool call and text chunk live (and keep the idle timer fed).
        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("stream-json");
        psi.ArgumentList.Add("--verbose");                // required with stream-json in -p mode
        if (_model != null)
        {
            psi.ArgumentList.Add("--model");
            psi.ArgumentList.Add(_model);
        }

        // Register the in-process plugin MCP server so Claude Code can read live plugin data.
        if (_mcpUrl != null)
        {
            var cfg = $"{{\"mcpServers\":{{\"{_mcpServerName}\":{{\"type\":\"http\",\"url\":\"{_mcpUrl}\"}}}}}}";
            psi.ArgumentList.Add("--mcp-config");
            psi.ArgumentList.Add(cfg);
            psi.ArgumentList.Add("--allowedTools");
            // Auto-approve this server's tools + Read (so pasted/attached images can be viewed) in -p mode.
            psi.ArgumentList.Add($"mcp__{_mcpServerName} Read");
        }

        Process? proc = null;
        string? startError = null;
        try
        {
            proc = Process.Start(psi);
        }
        catch (Exception ex)
        {
            startError = ex.Message;
        }

        if (proc == null)
        {
            yield return $"[Claude Code not available: {startError}. " +
                         "Install the Claude Code CLI and ensure `claude` is on your PATH, " +
                         "or set its full path in Settings.]";
            yield break;
        }

        // Kill the CLI if the request is cancelled / times out, so it doesn't linger.
        using var killReg = ct.Register(() => { try { proc.Kill(entireProcessTree: true); } catch { } });

        using (proc)
        {
            // Write the prompt to stdin on a BACKGROUND task so we read stdout concurrently. Writing the
            // whole prompt first and only then reading deadlocks on a large prompt (a long chat): the CLI
            // fills its stdout pipe -- which we aren't draining yet -- and stops reading stdin, while we're
            // still blocked writing stdin. Both wait forever and the chat appears stuck on "thinking…".
            var writeTask = Task.Run(async () =>
            {
                try { await proc.StandardInput.WriteAsync(prompt.ToString()); proc.StandardInput.Close(); }
                catch { /* the CLI may exit before we finish writing; ignore */ }
            });

            // Drain stderr from the START, not after the stdout loop. Same shape as the stdin
            // deadlock above: stderr is redirected, so once the CLI fills that pipe it blocks on
            // write and stops producing stdout -- the read loop below then never sees EOF and the
            // chat hangs on "thinking…" forever.
            var errTask = proc.StandardError.ReadToEndAsync(ct);

            string? line;
            while ((line = await proc.StandardOutput.ReadLineAsync(ct)) != null)
                foreach (var chunk in ParseStreamLine(line))
                    yield return chunk;

            await proc.WaitForExitAsync(ct);
            try { await writeTask; } catch { /* ignore */ }

            if (proc.ExitCode != 0)
            {
                var err = "";
                try { err = await errTask; } catch { /* ignore */ }
                if (!string.IsNullOrWhiteSpace(err))
                    yield return $"\n[Claude Code error] {err.Trim()}";
            }
        }
    }

    /// <summary>
    /// Translate one NDJSON line from `claude -p --output-format stream-json` into chunks to show
    /// what Claude is doing: assistant text as-is, reasoning as a "💭" line, each tool call with its
    /// arguments ("🔧 get_record(plugin=…, id=…)"), and tool results truncated ("↳ …"). Skips the
    /// init/result envelope and surfaces errors. Unparseable lines pass through verbatim.
    /// </summary>
    internal static IEnumerable<string> ParseStreamLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) yield break;

        JsonDocument? doc = null;
        try { doc = JsonDocument.Parse(line); }
        catch { /* not JSON -- pass it through below */ }
        if (doc == null) { yield return line + "\n"; yield break; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var typeEl)) yield break;

            switch (typeEl.GetString())
            {
                case "assistant":
                    if (TryContent(root, out var aContent))
                        foreach (var block in aContent.EnumerateArray())
                        {
                            var bt = block.TryGetProperty("type", out var b) ? b.GetString() : null;
                            if (bt == "text" && block.TryGetProperty("text", out var t))
                            {
                                var s = t.GetString();
                                if (!string.IsNullOrEmpty(s)) yield return s;
                            }
                            else if (bt == "thinking" && block.TryGetProperty("thinking", out var th))
                            {
                                var s = th.GetString();
                                if (!string.IsNullOrWhiteSpace(s))
                                    yield return $"\n_💭 {Truncate(OneLine(s!), 200)}_\n";
                            }
                            else if (bt == "tool_use" && block.TryGetProperty("name", out var n))
                            {
                                var args = block.TryGetProperty("input", out var inp) ? FormatArgs(inp) : "";
                                yield return $"\n🔧 {ShortToolName(n.GetString())}({args})\n";
                            }
                        }
                    break;

                case "user":   // tool results come back as a user turn
                    if (TryContent(root, out var uContent))
                        foreach (var block in uContent.EnumerateArray())
                        {
                            if ((block.TryGetProperty("type", out var b) ? b.GetString() : null) != "tool_result")
                                continue;
                            var text = ExtractResultText(block);
                            if (!string.IsNullOrWhiteSpace(text))
                                yield return $"↳ {Truncate(OneLine(text), 160)}\n";
                        }
                    break;

                case "result":
                    if (root.TryGetProperty("is_error", out var e) && e.GetBoolean() &&
                        root.TryGetProperty("result", out var r))
                        yield return $"\n[Claude Code error] {r.GetString()}";
                    break;
            }
        }
    }

    private static bool TryContent(JsonElement root, out JsonElement content)
    {
        content = default;
        return root.TryGetProperty("message", out var m) &&
               m.TryGetProperty("content", out content) &&
               content.ValueKind == JsonValueKind.Array;
    }

    // "mcp__fo4editor__get_record" -> "get_record"
    private static string ShortToolName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "tool";
        var i = name.LastIndexOf("__", StringComparison.Ordinal);
        return i >= 0 ? name[(i + 2)..] : name;
    }

    // {"plugin":"X.esp","id":"abc"} -> "plugin=X.esp, id=abc"
    private static string FormatArgs(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object) return "";
        var parts = new List<string>();
        foreach (var p in input.EnumerateObject())
        {
            var v = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.ToString();
            parts.Add($"{p.Name}={Truncate(OneLine(v), 60)}");
        }
        return string.Join(", ", parts);
    }

    private static string ExtractResultText(JsonElement toolResult)
    {
        if (!toolResult.TryGetProperty("content", out var c)) return "";
        if (c.ValueKind == JsonValueKind.String) return c.GetString() ?? "";
        if (c.ValueKind == JsonValueKind.Array)
            foreach (var part in c.EnumerateArray())
                if (part.TryGetProperty("text", out var t)) return t.GetString() ?? "";
        return "";
    }

    private static string OneLine(string s) => s.Replace("\r", " ").Replace("\n", " ").Trim();
    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
