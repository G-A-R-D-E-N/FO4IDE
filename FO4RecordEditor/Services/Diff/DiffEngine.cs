using FO4RecordEditor.Models;

namespace FO4RecordEditor.Services;

public sealed class DiffEngine
{
    public IReadOnlyList<DiffRow> Compare(RecordNode a, RecordNode b, bool changesOnly = false)
    {
        var leavesA = Flatten(a);
        var leavesB = Flatten(b);
        var paths = leavesA.Keys.Union(leavesB.Keys).OrderBy(p => p);
        var rows = new List<DiffRow>();

        foreach (var path in paths)
        {
            leavesA.TryGetValue(path, out var va);
            leavesB.TryGetValue(path, out var vb);
            var kind = (va, vb) switch
            {
                (null, not null) => DiffKind.Added,
                (not null, null) => DiffKind.Removed,
                _ when va != vb  => DiffKind.Modified,
                _                => DiffKind.Unchanged,
            };
            if (changesOnly && kind == DiffKind.Unchanged) continue;
            rows.Add(new DiffRow(path, va, vb, kind));
        }
        return rows;
    }

    public static (int added, int removed, int modified) Summary(IReadOnlyList<DiffRow> rows) =>
        (rows.Count(r => r.Kind == DiffKind.Added),
         rows.Count(r => r.Kind == DiffKind.Removed),
         rows.Count(r => r.Kind == DiffKind.Modified));

    private static Dictionary<string, string> Flatten(RecordNode root)
    {
        var dict = new Dictionary<string, string>();
        void Walk(RecordNode n, string prefix)
        {
            foreach (var c in n.Children)
            {
                var path = string.IsNullOrEmpty(prefix) ? c.Key : $"{prefix}.{c.Key}";
                if (c.IsLeaf) dict[path] = c.Value;
                else Walk(c, path);
            }
        }
        Walk(root, "");
        return dict;
    }
}
