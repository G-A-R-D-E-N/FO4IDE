namespace FO4RecordEditor.Models;
public sealed record AppCommand(string Id, string Title, string Category, Action Execute);
