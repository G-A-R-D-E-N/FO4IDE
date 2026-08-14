using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FO4RecordEditor.Models;

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

    public FieldEditKind EditKind { get; set; } = FieldEditKind.Text;
    public string[]? EnumOptions { get; set; }

    public string? RefType { get; set; }

    public string? RefTypes { get; set; }

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

    public bool IsSummary { get; set; }

    public string DisplayValue => (IsLeaf || IsSummary) ? Value : $"({Children.Count} items)";

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

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string name = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
