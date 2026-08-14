using System.Collections.Concurrent;
using System.Threading.Channels;
using Newtonsoft.Json;

namespace FO4RecordEditor.Server;






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

        var json = JsonConvert.SerializeObject(payload).Replace("\r", "").Replace("\n", "");
        foreach (var ch in _subscribers.Keys) ch.Writer.TryWrite(json);
    }
}
