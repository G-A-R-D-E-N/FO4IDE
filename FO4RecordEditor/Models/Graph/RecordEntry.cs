namespace FO4RecordEditor.Models;

public sealed record RecordEntry(
    string FormKey, string EditorID, string Type,
    string SourcePlugin, RecordNode Node, bool IsWinningOverride);
