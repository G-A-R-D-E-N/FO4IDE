namespace FO4RecordEditor.Models;

/// <summary>One conflicting record: a FormKey defined/overridden by 2+ plugins.</summary>
public sealed record ConflictEntry(
    string FormKey,
    string EditorID,
    string Type,
    IReadOnlyList<string> Plugins,   // every plugin that touches it, in load order
    string Winner,                   // the winning plugin (last in load order)
    bool InvolvesMod,                // true if a non-vanilla/DLC plugin is involved
    bool Suppressed = false)         // every touching plugin belongs to one declared ModGroup
{
    public string PluginsText => string.Join(", ", Plugins);
    public int Count => Plugins.Count;
}
