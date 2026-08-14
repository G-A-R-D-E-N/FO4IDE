namespace FO4RecordEditor.Models;

public sealed record ConflictEntry(
    string FormKey,
    string EditorID,
    string Type,
    IReadOnlyList<string> Plugins,
    string Winner,
    bool InvolvesMod,
    bool Suppressed = false)
{
    public string PluginsText => string.Join(", ", Plugins);
    public int Count => Plugins.Count;
}
