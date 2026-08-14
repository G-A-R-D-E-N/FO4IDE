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

/// <summary>
/// WebView2 host object bridging the React AI panel to the live Claude backend (the Anthropic agent
/// with plugin tool-use, or the plain streaming chat). Owns the chat sessions (persisted under
/// %AppData%\FO4RecordEditor\Chats via ChatHistoryService) and the slash commands. Reply tokens,
/// tool-status lines, info notes, and session events are pushed to the page via the injected
/// <c>post</c> callback (which marshals to the UI thread before PostWebMessageAsJson).
/// </summary>
[ClassInterface(ClassInterfaceType.AutoDual)]
[ComVisible(true)]
public class ChatInterop
{
    private readonly ShellViewModel _shell;
    private readonly Action<object> _post;
    private readonly ChatHistoryService _history = new();
    // Each session is its own conversation with its own in-flight request, so multiple chats can run
    // at once and never bleed into each other. _live keeps a stable object per session id (so streamed
    // replies land on the right one); _running tracks the cancellation token per session.
    private readonly Dictionary<Guid, ChatSession> _live = new();
    private readonly Dictionary<Guid, CancellationTokenSource> _running = new();
    private readonly SemaphoreSlim _agentGate = new(1, 1);   // serializes the (stateful) Anthropic agent
    private ChatSession _current = new();

    public ChatInterop(ShellViewModel shell, Action<object> post)
    {
        _shell = shell;
        _post = post;
        _live[_current.Id] = _current;
    }

    // Keep a single stable instance per session id, so concurrent appends/streams target the same object.
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

    // ---- sessions --------------------------------------------------------

    /// <summary>All saved chats (newest first) as JSON: [{id, name, createdAt, count}].</summary>
    public string ListSessions() => JsonConvert.SerializeObject(
        _history.LoadAll().Select(SessionMeta).ToArray());

    /// <summary>Create a new empty chat and return it. (No shared history to reset -- each session
    /// carries its own conversation, built fresh per send.)</summary>
    public string NewSession()
    {
        _current = Live(new ChatSession());
        return JsonConvert.SerializeObject(SessionDto(_current));
    }

    /// <summary>Switch to a saved chat: return the full session ({id, name, messages:[{isUser,text}]})
    /// so the panel can render the transcript. A chat that is mid-stream keeps streaming in the
    /// background; switching to it just shows its current state.</summary>
    public string LoadSession(string id)
    {
        var session = ResolveLive(id);
        if (session == null) return NewSession();
        _current = session;
        return JsonConvert.SerializeObject(SessionDto(session));
    }

    /// <summary>Fork a chat into a NEW one seeded with its most recent messages (older bulk dropped to
    /// save usage). Instant and reliable -- it does NOT call the model (summarizing a huge transcript
    /// through the provider is slow/can hang). For a compressed summary, run /compact first.</summary>
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

    // ---- commands --------------------------------------------------------

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

    /// <summary>The slash commands as JSON: [{name, args, help}].</summary>
    public string GetCommands() => JsonConvert.SerializeObject(
        _commands.Select(c => new { name = c.Name, args = c.Args, help = c.Help }).ToArray());

    /// <summary>Stop the in-flight response for a specific chat (by session id).</summary>
    public void CancelMessage(string sessionId)
    {
        if (Guid.TryParse(sessionId, out var gid) && _running.TryGetValue(gid, out var cts))
            cts.Cancel();
    }

    public void ResetChat() => NewSession();

    // ---- send ------------------------------------------------------------

    /// <summary>
    /// Send a user message (or a slash command) to a SPECIFIC chat session and stream the reply back,
    /// tagged with that session's id. Multiple sessions can run at once without bleeding into each other.
    /// Web messages carry SessionId: {Type:"AiToken"|"AiToolStatus"|"AiInfo"|"AiError",SessionId,Text},
    /// {Type:"AiDone",SessionId[,Stopped]}, {Type:"AiClear",SessionId}, {Type:"SessionRenamed",Id,Name}.
    /// </summary>
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

        // Auto-name a brand-new chat from its first message.
        if (session.Messages.Count == 0 && text.Length > 2)
        {
            session.Name = text.Length > 30 ? text[..30] + "…" : text;
            _post(new { Type = "SessionRenamed", Id = session.Id.ToString(), Name = session.Name });
        }

        session.Messages.Add(new SessionMessage { IsUser = true, Text = text });
        _history.Save(session);

        // The session/transcript keeps the clean text; the AI prompt gets the image file paths appended
        // so it can view them (Claude Code reads them with its Read tool; auto-approved above).
        var prompt = text;
        if (imagePaths.Count > 0)
            prompt = (text.Length > 0 ? text + "\n\n" : "") +
                     $"[The user attached {imagePaths.Count} image(s). View them with your Read tool: " +
                     string.Join(", ", imagePaths) + "]";

        await RunForSession(session, prompt);
    }

    // Decode base64 image attachments (data URLs or raw base64) to temp PNG/JPG files. Returns paths.
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

    // Run the AI for ONE session and stream the reply tagged with that session's id. The reply is
    // persisted to the captured session object, so switching chats mid-stream never misroutes it.
    private async Task RunForSession(ChatSession session, string prompt)
    {
        var sid = session.Id.ToString();
        // Cancel only THIS session's previous run (other sessions keep streaming).
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
                // The Anthropic agent holds shared tool-loop state, so serialize its runs. Load THIS
                // session's prior turns, then run (RunAsync appends the prompt as the user turn).
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
                // Claude Code / Ollama: one-shot stream built from THIS session's messages -- fully
                // concurrent (each call is independent; no shared history).
                await _shell.Chat.StreamOneShot(BuildMessages(session, ctx, prompt),
                    token => { full.Append(token); _post(new { Type = "AiToken", SessionId = sid, Text = token }); }, ct);
            }

            session.Messages.Add(new SessionMessage { IsUser = false, Text = full.ToString() });
            _history.Save(session);
            _post(new { Type = "AiDone", SessionId = sid });
            await MaybeAutoCompact(session);   // keep the re-sent transcript from growing without bound
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

    // Build the provider message list for a session: system context + the session's turns, with the
    // last user turn replaced by the prompt (which may carry image references).
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
        var tokens = chars / 4;   // rough heuristic
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
        // Drop the previous assistant reply (if any) so the re-run replaces it.
        if (session.Messages.Count > 0 && !session.Messages[^1].IsUser)
            session.Messages.RemoveAt(session.Messages.Count - 1);
        _post(new { Type = "AiRetry", SessionId = session.Id.ToString(), Text = lastUser.Text });
        await RunForSession(session, lastUser.Text);
    }

    // The whole transcript is re-sent every turn, so its size is the main cost driver. Once a chat
    // grows past this, auto-compact the older messages so usage stops climbing turn over turn.
    private const int AutoCompactBudgetChars = 80_000;

    private async Task MaybeAutoCompact(ChatSession session)
    {
        var chars = session.Messages.Sum(m => m.Text?.Length ?? 0);
        // Need enough messages that compacting (which keeps the last 6 verbatim) actually helps.
        if (chars < AutoCompactBudgetChars || session.Messages.Count <= 7) return;
        try { await Compact(session); } catch { /* never let auto-compact break the turn */ }
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

            // Tell the panel to re-render this session's compacted transcript.
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
