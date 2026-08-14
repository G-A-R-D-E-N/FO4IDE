using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FO4RecordEditor.Models;
using FO4RecordEditor.ViewModels;
using Newtonsoft.Json;

namespace FO4RecordEditor.Services;

[ClassInterface(ClassInterfaceType.AutoDual)]
[ComVisible(true)]
public class ChatInterop
{
    private readonly ShellViewModel _shell;
    private readonly Action<object> _post;
    private readonly ChatHistoryService _history = new();

    private readonly Dictionary<Guid, ChatSession> _live = new();
    private readonly Dictionary<Guid, CancellationTokenSource> _running = new();
    private readonly SemaphoreSlim _agentGate = new(1, 1);
    private ChatSession _current = new();

    public ChatInterop(ShellViewModel shell, Action<object> post)
    {
        _shell = shell;
        _post = post;
        _live[_current.Id] = _current;
    }

    private ChatSession Live(ChatSession s) { _live[s.Id] = s; return s; }
    private ChatSession? ResolveLive(string id)
    {
        if (!Guid.TryParse(id, out var gid)) return null;
        if (_live.TryGetValue(gid, out var s)) return s;
        var loaded = _history.LoadAll().FirstOrDefault(x => x.Id == gid);
        if (loaded != null) _live[gid] = loaded;
        return loaded;
    }

    public bool IsAgentReady() => _shell.Agent != null;

    public string ListSessions() => JsonConvert.SerializeObject(
        _history.LoadAll().Select(SessionMeta).ToArray());

    public string NewSession()
    {
        _current = Live(new ChatSession());
        return JsonConvert.SerializeObject(SessionDto(_current));
    }

    public string LoadSession(string id)
    {
        var session = ResolveLive(id);
        if (session == null) return NewSession();
        _current = session;
        return JsonConvert.SerializeObject(SessionDto(session));
    }

    public string ForkSession(string id)
    {
        DebugLog.Interop(nameof(ForkSession), id);
        var src = ResolveLive(id);
        var meaningful = src?.Messages.Where(m => !string.IsNullOrWhiteSpace(m.Text)).ToList();
        if (src == null || meaningful == null || meaningful.Count == 0)
            return NewSession();

        const int keepRecent = 12;
        var recent = meaningful.Skip(Math.Max(0, meaningful.Count - keepRecent)).ToList();
        var dropped = meaningful.Count - recent.Count;

        var fork = Live(new ChatSession { Name = "Fork: " + src.Name });
        fork.Messages.Add(new SessionMessage
        {
            IsUser = false,
            Text = $"📋 Forked from \"{src.Name}\" -- continuing with the most recent {recent.Count} message(s)" +
                   (dropped > 0 ? $"; the earlier {dropped} were dropped to save usage." : "."),
        });
        foreach (var m in recent)
            fork.Messages.Add(new SessionMessage { IsUser = m.IsUser, Text = m.Text });

        _history.Save(fork);
        _current = fork;
        return JsonConvert.SerializeObject(SessionDto(fork));
    }

    public string RenameSession(string id, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Provide a name.";
        var session = ResolveLive(id);
        if (session == null) return "Chat not found.";
        session.Name = name.Trim();
        _history.Save(session);
        return "ok";
    }

    public string DeleteSession(string id)
    {
        if (Guid.TryParse(id, out var gid))
        {
            if (_running.TryGetValue(gid, out var cts)) { cts.Cancel(); _running.Remove(gid); }
            _live.Remove(gid);
            _history.Delete(gid);
            if (gid == _current.Id) NewSession();
        }
        return ListSessions();
    }

    private static object SessionMeta(ChatSession s) => new
    {
        id = s.Id.ToString(),
        name = s.Name,
        createdAt = s.CreatedAt,
        count = s.Messages.Count,
    };

    private static object SessionDto(ChatSession s) => new
    {
        id = s.Id.ToString(),
        name = s.Name,
        messages = s.Messages.Select(m => new { isUser = m.IsUser, text = m.Text }).ToArray(),
    };

    private static readonly (string Name, string Args, string Help)[] _commands =
    {
        ("/help",    "",     "List these commands."),
        ("/clear",   "",     "Start a fresh conversation (alias: /new, /reset)."),
        ("/compact", "",     "Summarize the conversation to cut context size and cost."),
        ("/cost",    "",     "Show this conversation's size and a rough token estimate."),
        ("/model",   "[id]", "Show the current model, or switch it (e.g. /model claude-sonnet-4-6)."),
        ("/retry",   "",     "Resend your last message."),
        ("/stop",    "",     "Stop the current response."),
    };

    public string GetCommands() => JsonConvert.SerializeObject(
        _commands.Select(c => new { name = c.Name, args = c.Args, help = c.Help }).ToArray());

    public void CancelMessage(string sessionId)
    {
        if (Guid.TryParse(sessionId, out var gid) && _running.TryGetValue(gid, out var cts))
            cts.Cancel();
    }

    public void ResetChat() => NewSession();

    public async Task SendMessage(string sessionId, string text, string imagesJson)
    {
        var session = ResolveLive(sessionId) ?? Live(new ChatSession());
        text = (text ?? "").Trim();
        var imagePaths = SaveAttachedImages(imagesJson);
        if (string.IsNullOrEmpty(text) && imagePaths.Count == 0) return;

        if (text.StartsWith('/'))
        {
            await HandleCommand(session, text);
            return;
        }

        if (session.Messages.Count == 0 && text.Length > 2)
        {
            session.Name = text.Length > 30 ? text[..30] + "…" : text;
            _post(new { Type = "SessionRenamed", Id = session.Id.ToString(), Name = session.Name });
        }

        session.Messages.Add(new SessionMessage { IsUser = true, Text = text });
        _history.Save(session);

        var prompt = text;
        if (imagePaths.Count > 0)
            prompt = (text.Length > 0 ? text + "\n\n" : "") +
                     $"[The user attached {imagePaths.Count} image(s). View them with your Read tool: " +
                     string.Join(", ", imagePaths) + "]";

        await RunForSession(session, prompt);
    }

    private static System.Collections.Generic.List<string> SaveAttachedImages(string? imagesJson)
    {
        var paths = new System.Collections.Generic.List<string>();
        if (string.IsNullOrWhiteSpace(imagesJson) || imagesJson == "[]") return paths;
        try
        {
            var arr = JsonConvert.DeserializeObject<System.Collections.Generic.List<string>>(imagesJson);
            if (arr == null) return paths;
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FO4RecordEditor_ChatImages");
            System.IO.Directory.CreateDirectory(dir);
            foreach (var entry in arr)
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                var data = entry;
                var isJpeg = data.Contains("image/jpeg", StringComparison.OrdinalIgnoreCase);
                var comma = data.IndexOf(',');
                if (data.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0)
                    data = data[(comma + 1)..];
                var bytes = Convert.FromBase64String(data);
                var path = System.IO.Path.Combine(dir, Guid.NewGuid().ToString("N") + (isJpeg ? ".jpg" : ".png"));
                System.IO.File.WriteAllBytes(path, bytes);
                paths.Add(path);
            }
        }
        catch (Exception ex) { DebugLog.Exception("SaveAttachedImages", ex); }
        return paths;
    }

    private async Task RunForSession(ChatSession session, string prompt)
    {
        var sid = session.Id.ToString();

        if (_running.TryGetValue(session.Id, out var prev)) prev.Cancel();
        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        _running[session.Id] = cts;
        var ct = cts.Token;
        var full = new StringBuilder();
        try
        {
            var loadedPlugins = MutagenLoader.QueryLoadedPlugins(_shell.GameEnvironment);
            var ctx = _shell.Context.BuildForQuestion(prompt, _shell.SelectedNode, loadedPlugins);
            _shell.Log.Log(LogCategory.AI, LogLevel.Info, "Prompt sent", prompt);

            if (_shell.Agent != null)
            {

                await _agentGate.WaitAsync(ct);
                try
                {
                    _shell.Agent.LoadHistory(session.Messages.Take(session.Messages.Count - 1)
                        .Select(m => (m.IsUser, m.Text)).ToList());
                    await _shell.Agent.RunAsync(prompt, ctx + AgentToolsPrompt,
                        onText: chunk => { full.Append(chunk); _post(new { Type = "AiToken", SessionId = sid, Text = chunk }); },
                        onToolStatus: s => _post(new { Type = "AiToolStatus", SessionId = sid, Text = s }),
                        ct: ct,
                        onUsage: usage => _shell.Log.Log(LogCategory.AI, LogLevel.Info, usage));
                }
                finally { _agentGate.Release(); }
            }
            else
            {

                await _shell.Chat.StreamOneShot(BuildMessages(session, ctx, prompt),
                    token => { full.Append(token); _post(new { Type = "AiToken", SessionId = sid, Text = token }); }, ct);
            }

            session.Messages.Add(new SessionMessage { IsUser = false, Text = full.ToString() });
            _history.Save(session);
            _post(new { Type = "AiDone", SessionId = sid });
            await MaybeAutoCompact(session);
        }
        catch (OperationCanceledException)
        {
            session.Messages.Add(new SessionMessage { IsUser = false, Text = full.ToString() });
            _history.Save(session);
            _post(new { Type = "AiDone", SessionId = sid, Stopped = true });
        }
        catch (Exception ex)
        {
            _post(new { Type = "AiError", SessionId = sid, Text = ex.Message });
            _shell.Log.Log(LogCategory.AI, LogLevel.Error, "AI error", ex.Message);
        }
        finally
        {
            if (_running.TryGetValue(session.Id, out var c) && ReferenceEquals(c, cts)) _running.Remove(session.Id);
        }
    }

    private static List<ChatMessage> BuildMessages(ChatSession session, string systemCtx, string promptOverride)
    {
        var msgs = new List<ChatMessage> { new(ChatRole.System, systemCtx) };
        for (int i = 0; i < session.Messages.Count; i++)
        {
            var m = session.Messages[i];
            var content = (i == session.Messages.Count - 1 && m.IsUser) ? promptOverride : m.Text;
            msgs.Add(new ChatMessage(m.IsUser ? ChatRole.User : ChatRole.Assistant, content));
        }
        return msgs;
    }

    private async Task HandleCommand(ChatSession session, string text)
    {
        var sid = session.Id.ToString();
        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLowerInvariant();
        var arg = parts.Length > 1 ? parts[1].Trim() : "";
        switch (cmd)
        {
            case "/help": case "/?": case "/commands":
                _post(new { Type = "AiInfo", SessionId = sid, Text = HelpText() });
                break;
            case "/clear": case "/new": case "/reset":
                session.Messages.Clear();
                _history.Save(session);
                _post(new { Type = "AiClear", SessionId = sid });
                break;
            case "/cost": case "/usage": case "/tokens":
                _post(new { Type = "AiInfo", SessionId = sid, Text = UsageText(session) });
                break;
            case "/model":
                _post(new { Type = "AiInfo", SessionId = sid, Text = SetOrShowModel(arg) });
                break;
            case "/retry":
                await RetryLast(session);
                break;
            case "/stop":
                if (_running.TryGetValue(session.Id, out var cts)) cts.Cancel();
                _post(new { Type = "AiInfo", SessionId = sid, Text = "Stopped." });
                break;
            case "/compact":
                await Compact(session);
                break;
            default:
                _post(new { Type = "AiInfo", SessionId = sid, Text = $"Unknown command `{cmd}`. Type `/help` for the list." });
                break;
        }
    }

    private static string HelpText()
    {
        var sb = new StringBuilder("Slash commands:\n");
        foreach (var (name, args, help) in _commands)
            sb.Append("  ").Append(name).Append(string.IsNullOrEmpty(args) ? "" : " " + args)
              .Append(" -- ").Append(help).Append('\n');
        return sb.ToString().TrimEnd();
    }

    private static string UsageText(ChatSession session)
    {
        var chars = session.Messages.Sum(m => m.Text?.Length ?? 0);
        var tokens = chars / 4;
        return $"This chat: {session.Messages.Count} message(s), ~{chars:N0} chars (≈{tokens:N0} tokens). " +
               "The whole transcript is re-sent each turn -- use /compact if it gets large.";
    }

    private string SetOrShowModel(string arg)
    {
        if (string.IsNullOrWhiteSpace(arg))
            return $"Current model: {_shell.Settings.Current.Model} (provider: {_shell.Settings.Current.AiProvider}).";
        _shell.Settings.Current.Model = arg;
        _shell.Settings.Save();
        _shell.RebuildProvider();
        return $"Model switched to {arg}.";
    }

    private async Task RetryLast(ChatSession session)
    {
        var lastUser = session.Messages.LastOrDefault(m => m.IsUser);
        if (lastUser == null) { _post(new { Type = "AiInfo", SessionId = session.Id.ToString(), Text = "Nothing to retry." }); return; }

        if (session.Messages.Count > 0 && !session.Messages[^1].IsUser)
            session.Messages.RemoveAt(session.Messages.Count - 1);
        _post(new { Type = "AiRetry", SessionId = session.Id.ToString(), Text = lastUser.Text });
        await RunForSession(session, lastUser.Text);
    }

    private const int AutoCompactBudgetChars = 80_000;

    private async Task MaybeAutoCompact(ChatSession session)
    {
        var chars = session.Messages.Sum(m => m.Text?.Length ?? 0);

        if (chars < AutoCompactBudgetChars || session.Messages.Count <= 7) return;
        try { await Compact(session); } catch {  }
    }

    private async Task Compact(ChatSession session)
    {
        var sid = session.Id.ToString();
        var msgs = session.Messages.Where(m => !string.IsNullOrWhiteSpace(m.Text)).ToList();
        const int keepRecent = 6;
        if (msgs.Count <= keepRecent + 1)
        {
            _post(new { Type = "AiInfo", SessionId = sid, Text = $"Only {msgs.Count} message(s) -- nothing old enough to compact yet." });
            return;
        }

        var older = msgs.Take(msgs.Count - keepRecent).ToList();
        var recent = msgs.Skip(msgs.Count - keepRecent).ToList();
        var transcript = string.Join("\n\n", older.Select(m => (m.IsUser ? "User: " : "Assistant: ") + m.Text));

        _post(new { Type = "AiInfo", SessionId = sid, Text = $"Compacting {older.Count} older message(s)…" });
        try
        {
            var summary = await _shell.Chat.SummarizeTextAsync(transcript, CompactInstruction);
            if (string.IsNullOrWhiteSpace(summary))
            {
                _post(new { Type = "AiInfo", SessionId = sid, Text = "Compact produced no summary -- conversation left unchanged." });
                return;
            }

            var summaryMsg = new SessionMessage
            {
                IsUser = false,
                Text = $"🗜️ Earlier conversation compacted ({older.Count} summarized; last {recent.Count} kept):\n\n" + summary,
            };
            session.Messages.Clear();
            session.Messages.Add(summaryMsg);
            session.Messages.AddRange(recent);
            _history.Save(session);

            _post(new { Type = "AiReload", SessionId = sid, Session = SessionDto(session) });
        }
        catch (OperationCanceledException) { _post(new { Type = "AiInfo", SessionId = sid, Text = "Compact cancelled." }); }
        catch (Exception ex) { _post(new { Type = "AiInfo", SessionId = sid, Text = "Compact error: " + ex.Message }); }
    }

    private const string CompactInstruction =
        "Summarize the following conversation between a user and an AI assistant about editing Fallout 4 " +
        "plugin records. Preserve every concrete decision, FormKey/EditorID, plugin name, and unresolved " +
        "task. Be concise but lose no actionable detail. Write it as notes the assistant can continue from.";

    private const string AgentToolsPrompt = "\n\n" + AiGuidance.System;
}
