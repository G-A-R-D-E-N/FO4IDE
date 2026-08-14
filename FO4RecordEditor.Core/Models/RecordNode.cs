using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FO4RecordEditor.Models;

/// <summary>How the value cell should be edited: a free textbox, a checkbox (bool), a dropdown
/// (enum), or a record picker (Ref -- a FormLink to another record).</summary>
public enum FieldEditKind { Text, Bool, Enum, Ref }

public class RecordNode : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;

    public string Key { get; init; } = "";
    public string? FilePath { get; set; }
    public RecordNode? Parent { get; set; }
    public ObservableCollection<RecordNode> Children { get; } = [];

    public Dictionary<string, string> Values { get; } = new();

    // Editor hint for the value cell: a bool field renders as a checkbox, an enum as a dropdown
    // (xEdit-style), everything else as a textbox. Set by the walker from the field's CLR type.
    public FieldEditKind EditKind { get; set; } = FieldEditKind.Text;
    public string[]? EnumOptions { get; set; }

    // For a FormLink (EditKind == Ref): a short display label for the target (e.g. "Keyword").
    public string? RefType { get; set; }
    // Comma-separated list of EVERY concrete record class the link may point at (e.g. an "item"
    // link -> "Ammunition,Armor,Ingestible,MiscItem,Weapon,..."). The picker filters on this set.
    public string? RefTypes { get; set; }

    // True for a record row in the Explorer tree. Records are shown as leaves there (their
    // fields live in the center property grid), so the tree binds TreeChildren, not Children.
    public bool IsRecordNode { get; set; }

    public IEnumerable<RecordNode> TreeChildren => IsRecordNode ? System.Array.Empty<RecordNode>() : Children;

    private ConflictStatus _conflict;
    public ConflictStatus ConflictStatus
    {
        get => _conflict;
        set { if (_conflict == value) return; _conflict = value; Notify(); }
    }

    public string Value
    {
        get => Values.Values.LastOrDefault() ?? "";
        set
        {
            if (Values.Count == 0) Values[""] = value;
            else if (Values.Values.LastOrDefault() == value) return;
            else Values[Values.Keys.Last()] = value;

            Notify();
            Notify(nameof(DisplayValue));
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; Notify(); }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; Notify(); }
    }

    public bool IsLeaf => Children.Count == 0;

    // A node that has a complete one-line summary (e.g. a condition "GetGlobalValue(...) == 0" or a
    // component "Adhesive x3") AND keeps its sub-fields as children for editing. Flattened views
    // (conflict grid, get_record) show the summary line and stop; the Record tree still expands the
    // children so individual fields stay editable.
    public bool IsSummary { get; set; }

    public string DisplayValue => (IsLeaf || IsSummary) ? Value : $"({Children.Count} items)";

    // ---- helpers used by scripts ----------------------------------------

    public RecordNode? GetChild(string key) =>
        Children.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));

    public string? GetValue(string key) => GetChild(key)?.Value;

    public bool SetValue(string key, string value)
    {
        var child = GetChild(key);
        if (child == null) return false;
        child.Value = value;
        return true;
    }

    public IEnumerable<RecordNode> Descendants()
    {
        var stack = new Stack<RecordNode>(Children.Reverse());
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;
            foreach (var child in current.Children.Reverse())
            {
                stack.Push(child);
            }
        }
    }

    public IEnumerable<RecordNode> SelfAndDescendants() =>
        Enumerable.Repeat(this, 1).Concat(Descendants());

    // Deep path access: "Conditions[0].Function"
    public RecordNode? Navigate(string path)
    {
        RecordNode? cur = this;
        foreach (var part in path.Split('.'))
        {
            if (cur == null) return null;
            if (part.EndsWith(']'))
            {
                var bracket = part.IndexOf('[');
                if (bracket == -1) return null;
                var name = part[..bracket];
                if (!int.TryParse(part[(bracket + 1)..^1], out var idx)) return null;
                cur = string.IsNullOrEmpty(name) ? cur : cur.GetChild(name);
                if (cur == null || idx < 0 || idx >= cur.Children.Count) return null;
                cur = cur.Children[idx];
            }
            else
            {
                cur = cur.GetChild(part);
            }
        }
        return cur;
    }

    public string? GetPath(string path) => Navigate(path)?.Value;

    // ---- INotifyPropertyChanged -----------------------------------------

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string name = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
