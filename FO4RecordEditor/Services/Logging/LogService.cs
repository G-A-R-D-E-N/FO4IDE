using System.Collections.ObjectModel;
using FO4RecordEditor.Models;

namespace FO4RecordEditor.Services;

public sealed class LogService
{
    public ObservableCollection<LogEntry> Entries { get; } = [];
    public event Action<LogEntry>? EntryAdded;

    public void Log(LogCategory cat, LogLevel level, string message, string? detail = null)
    {
        var entry = new LogEntry(DateTime.Now, level, cat, message, detail);
        Entries.Add(entry);
        EntryAdded?.Invoke(entry);

        DebugLog.Write(level.ToString(), cat.ToString(), message, detail);
    }

    public IReadOnlyList<LogEntry> Filter(LogCategory? cat, LogLevel minLevel) =>
        Entries.Where(e => (cat == null || e.Category == cat) && e.Level >= minLevel).ToList();

    public string Export() =>
        string.Join(Environment.NewLine, Entries.Select(e =>
            $"{e.Timestamp:HH:mm:ss.fff} [{e.Level,-8}] [{e.Category,-7}] {e.Message}" +
            (e.Detail != null ? $"  | {e.Detail}" : "")));

    public void Clear() => Entries.Clear();
}
