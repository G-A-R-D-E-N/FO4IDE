using FO4RecordEditor.Models;

namespace FO4RecordEditor.Services;

public sealed class ChatService
{
    private readonly List<ChatMessage> _history = new();
    private IAIProvider _provider;

    public ChatService(IAIProvider provider) => _provider = provider;
    public void SetProvider(IAIProvider provider) => _provider = provider;
    public IReadOnlyList<ChatMessage> History => _history;
    public void Reset() => _history.Clear();
    
    public void LoadHistory(IEnumerable<ChatMessage> history)
    {
        _history.Clear();
        _history.AddRange(history);
    }

    /// <summary>Number of stored turns (user + assistant messages).</summary>
    public int TurnCount => _history.Count;

    /// <summary>
    /// Summarize a block of conversation text via the active provider WITHOUT mutating history.
    /// Used by /compact, which summarizes only the OLDER messages and keeps the recent ones verbatim;
    /// the caller rebuilds history afterwards. Returns the summary (empty if the provider gave nothing).
    /// </summary>
    public async Task<string> SummarizeTextAsync(string transcript, string instruction, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(transcript)) return "";

        var outgoing = new List<ChatMessage>
        {
            new(ChatRole.User, instruction + "\n\n---- CONVERSATION TO SUMMARIZE ----\n" + transcript)
        };
        var sb = new System.Text.StringBuilder();
        await foreach (var token in _provider.StreamAsync(outgoing, ct))
            sb.Append(token);
        return sb.ToString().Trim();
    }

    /// <summary>Stream a fully-built message list through the provider WITHOUT touching the shared
    /// _history. Lets several chat sessions run at once, each with its own conversation, instead of
    /// sharing one history (which would interleave/bleed between chats).</summary>
    public async Task StreamOneShot(IReadOnlyList<ChatMessage> messages, Action<string> onToken, CancellationToken ct = default)
    {
        await foreach (var token in _provider.StreamAsync(messages, ct))
            onToken(token);
    }

    public async Task<string> SendAsync(
        string userMessage, string? systemContext,
        Action<string> onToken, CancellationToken ct = default)
    {
        var outgoing = new List<ChatMessage>();
        if (systemContext != null) outgoing.Add(new ChatMessage(ChatRole.System, systemContext));
        outgoing.AddRange(_history);
        outgoing.Add(new ChatMessage(ChatRole.User, userMessage));

        _history.Add(new ChatMessage(ChatRole.User, userMessage));

        var full = new System.Text.StringBuilder();
        try
        {
            await foreach (var token in _provider.StreamAsync(outgoing, ct))
            {
                full.Append(token);
                onToken(token);
            }
        }
        finally
        {
            // Always commit an assistant turn (even on cancel/throw with partial text)
            // so the user/assistant role alternation the Messages API requires is preserved.
            // Cancellation is the normal "stop generating" path, not an edge case.
            _history.Add(new ChatMessage(ChatRole.Assistant, full.ToString()));
        }
        return full.ToString();
    }
}
