namespace FO4RecordEditor.Models;
public sealed record LogEntry(
    DateTime Timestamp, LogLevel Level, LogCategory Category,
    string Message, string? Detail);
