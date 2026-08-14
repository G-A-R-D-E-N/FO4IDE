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

    public int TurnCount => _history.Count;

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

            _history.Add(new ChatMessage(ChatRole.Assistant, full.ToString()));
        }
        return full.ToString();
    }
}
