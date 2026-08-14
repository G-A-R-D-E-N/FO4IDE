using System.Collections.Concurrent;
using System.Threading.Channels;
using Newtonsoft.Json;

namespace FO4RecordEditor.Server;

/// <summary>
/// The server-sent-events equivalent of CoreWebView2.PostWebMessageAsJson: one fan-out point that
/// every open page receives. Bounded and drop-oldest, because a page that stops reading must never
/// be able to stall the thread that raised the event (chat streaming raises a lot of them).
/// </summary>
public sealed class EventStream
{
    private readonly ConcurrentDictionary<Channel<string>, byte> _subscribers = new();

    public Channel<string> Subscribe()
    {
        var ch = Channel.CreateBounded<string>(
            new BoundedChannelOptions(512) { FullMode = BoundedChannelFullMode.DropOldest });
        _subscribers[ch] = 0;
        return ch;
    }

    public void Unsubscribe(Channel<string> ch)
    {
        _subscribers.TryRemove(ch, out _);
        ch.Writer.TryComplete();
    }

    public void Push(object payload)
    {
        // Newlines would split one SSE frame into several, so the JSON goes out on a single line.
        var json = JsonConvert.SerializeObject(payload).Replace("\r", "").Replace("\n", "");
        foreach (var ch in _subscribers.Keys) ch.Writer.TryWrite(json);
    }
}
