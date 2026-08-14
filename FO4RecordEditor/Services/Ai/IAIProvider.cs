using FO4RecordEditor.Models;

namespace FO4RecordEditor.Services;

public interface IAIProvider
{
    string Name { get; }
    IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages, CancellationToken ct = default);
}
