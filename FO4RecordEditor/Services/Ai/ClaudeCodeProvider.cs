using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using FO4RecordEditor.Models;

namespace FO4RecordEditor.Services;

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

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath(),

            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("/c");
        psi.ArgumentList.Add(_exePath);
        psi.ArgumentList.Add("-p");

        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("stream-json");
        psi.ArgumentList.Add("--verbose");
        if (_model != null)
        {
            psi.ArgumentList.Add("--model");
            psi.ArgumentList.Add(_model);
        }

        if (_mcpUrl != null)
        {
            var cfg = $"{{\"mcpServers\":{{\"{_mcpServerName}\":{{\"type\":\"http\",\"url\":\"{_mcpUrl}\"}}}}}}";
            psi.ArgumentList.Add("--mcp-config");
            psi.ArgumentList.Add(cfg);
            psi.ArgumentList.Add("--allowedTools");

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

        using var killReg = ct.Register(() => { try { proc.Kill(entireProcessTree: true); } catch { } });

        using (proc)
        {

            var writeTask = Task.Run(async () =>
            {
                try { await proc.StandardInput.WriteAsync(prompt.ToString()); proc.StandardInput.Close(); }
                catch {  }
            });

            var errTask = proc.StandardError.ReadToEndAsync(ct);

            string? line;
            while ((line = await proc.StandardOutput.ReadLineAsync(ct)) != null)
                foreach (var chunk in ParseStreamLine(line))
                    yield return chunk;

            await proc.WaitForExitAsync(ct);
            try { await writeTask; } catch {  }

            if (proc.ExitCode != 0)
            {
                var err = "";
                try { err = await errTask; } catch {  }
                if (!string.IsNullOrWhiteSpace(err))
                    yield return $"\n[Claude Code error] {err.Trim()}";
            }
        }
    }

    internal static IEnumerable<string> ParseStreamLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) yield break;

        JsonDocument? doc = null;
        try { doc = JsonDocument.Parse(line); }
        catch {  }
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

                case "user":
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

    private static string ShortToolName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "tool";
        var i = name.LastIndexOf("__", StringComparison.Ordinal);
        return i >= 0 ? name[(i + 2)..] : name;
    }

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
