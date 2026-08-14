namespace FO4RecordEditor.Models;
public sealed record DiffRow(string Path, string? ValueA, string? ValueB, DiffKind Kind);
