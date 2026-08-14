using System.Text.RegularExpressions;
using FO4RecordEditor.Models;

namespace FO4RecordEditor.Services;

public sealed class KnowledgeGraph
{
    public sealed record Conflict(string FormKey, IReadOnlyList<RecordEntry> Overrides);

    private static readonly Regex FormKeyRe =
        new(@"^[0-9A-Fa-f]{6}:.+\.(esp|esm|esl)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly Dictionary<string, RecordEntry> _byFormKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<RecordEntry>> _byEditorId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<RecordEntry>> _byType = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Reference>> _inbound = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Reference>> _outbound = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<RecordEntry>> _overrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Reference> _allRefs = new();

    public int RecordCount => _byFormKey.Count;
    public int ReferenceCount => _allRefs.Count;
    public IReadOnlyCollection<RecordEntry> AllRecords => _byFormKey.Values;

    public void Index(RecordNode pluginRoot)
    {
        var plugin = pluginRoot.Key;
        foreach (var node in pluginRoot.SelfAndDescendants())
        {
            var fk = node.GetValue("FormKey");
            if (fk == null || node.IsLeaf) continue;
            var type = node.GetValue("Type") ?? "";
            var eid = node.GetValue("EditorID") ?? "";

            var entry = new RecordEntry(fk, eid, type, plugin, node, IsWinningOverride: true);

            Add(_overrides, fk, entry);
            _byFormKey[fk] = entry;
            Add(_byEditorId, eid, entry);
            Add(_byType, type, entry);
            IndexReferences(node, fk);
        }
    }

    public async Task IndexAsync(IEnumerable<RecordNode> plugins, IProgress<string>? progress = null)
    {
        await Task.Run(() =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var p in plugins)
            {
                progress?.Report($"Indexing {p.Key}...");
                Index(p);
            }
            sw.Stop();
            System.Diagnostics.Debug.WriteLine($"[PERF] KnowledgeGraph Indexing took {sw.ElapsedMilliseconds}ms");
            progress?.Report($"Indexed {RecordCount} records, {ReferenceCount} references.");
        });
    }

    private void IndexReferences(RecordNode record, string fromFk)
    {
        var descendants = record.Descendants().ToList();
        foreach (var leaf in descendants)
        {
            if (!leaf.IsLeaf) continue;
            var v = leaf.Value;

            if (string.IsNullOrEmpty(v) || v.Length < 10 || !v.Contains(':')) continue;
            if (!FormKeyRe.IsMatch(v)) continue;
            if (v.Equals(fromFk, StringComparison.OrdinalIgnoreCase)) continue;
            var kind = ClassifyKind(leaf.Key);
            var reference = new Reference(fromFk, v, leaf.Key, kind);
            _allRefs.Add(reference);
            Add(_inbound, v, reference);
            Add(_outbound, fromFk, reference);
        }
    }

    private static ReferenceKind ClassifyKind(string field)
    {
        var f = field.ToLowerInvariant();
        if (f.Contains("keyword")) return ReferenceKind.Keyword;
        if (f.Contains("actorvalue")) return ReferenceKind.ActorValue;
        if (f.Contains("script")) return ReferenceKind.Script;
        if (f.Contains("reference") || f.StartsWith("[")) return ReferenceKind.LeveledListEntry;
        return ReferenceKind.Generic;
    }

    public RecordEntry? GetByFormKey(string fk) =>
        _byFormKey.TryGetValue(fk, out var e) ? e : null;

    public IReadOnlyList<RecordEntry> GetByEditorID(string eid) =>
        _byEditorId.TryGetValue(eid, out var l) ? l : Array.Empty<RecordEntry>();

    public IReadOnlyList<RecordEntry> GetByType(string type) =>
        _byType.TryGetValue(type, out var l) ? l : Array.Empty<RecordEntry>();

    public IReadOnlyList<Reference> GetReferencesTo(string fk) =>
        _inbound.TryGetValue(fk, out var l) ? l : Array.Empty<Reference>();

    public IReadOnlyList<Reference> GetReferencesFrom(string fk) =>
        _outbound.TryGetValue(fk, out var l) ? l : Array.Empty<Reference>();

    public IReadOnlyList<Conflict> GetConflicts() =>
        _overrides.Where(kv => kv.Value.Count > 1)
                  .Select(kv => new Conflict(kv.Key, kv.Value))
                  .ToList();

    public IReadOnlyList<RecordEntry> GetNeighborhood(string fk)
    {
        var result = new List<RecordEntry>();
        var self = GetByFormKey(fk);
        if (self != null) result.Add(self);
        foreach (var r in GetReferencesFrom(fk))
        {
            var e = GetByFormKey(r.ToFormKey);
            if (e != null) result.Add(e);
        }
        return result.DistinctBy(e => e.FormKey).ToList();
    }

    public ImpactReport AnalyzeImpact(string fk)
    {
        var target = GetByFormKey(fk);
        var inbound = GetReferencesTo(fk);
        var affected = inbound.Select(r => GetByFormKey(r.FromFormKey))
                              .Where(e => e != null).Select(e => e!)
                              .DistinctBy(e => e.FormKey).ToList();
        return new ImpactReport(fk, target?.EditorID ?? "(unknown)", inbound, affected);
    }

    public void Clear()
    {
        _byFormKey.Clear(); _byEditorId.Clear(); _byType.Clear();
        _inbound.Clear(); _outbound.Clear(); _allRefs.Clear(); _overrides.Clear();
    }

    private static void Add<T>(Dictionary<string, List<T>> dict, string key, T val)
    {
        if (!dict.TryGetValue(key, out var l)) { l = new(); dict[key] = l; }
        l.Add(val);
    }
}
