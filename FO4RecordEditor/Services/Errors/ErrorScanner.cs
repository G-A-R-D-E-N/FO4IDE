using FO4RecordEditor.Models;

namespace FO4RecordEditor.Services;

public sealed class ErrorScanner
{
    private static readonly HashSet<string> KnownMasters = new(StringComparer.OrdinalIgnoreCase)
    {
        "Fallout4.esm", "DLCRobot.esm", "DLCworkshop01.esm", "DLCCoast.esm",
        "DLCworkshop02.esm", "DLCworkshop03.esm", "DLCNukaWorld.esm",
        "DLCUltraHighResolution.esm",
    };

    public IReadOnlyList<PluginError> Scan(KnowledgeGraph graph)
    {
        var errors = new List<PluginError>();
        foreach (var rec in graph.AllRecords)
        {
            ScanNullActorValues(rec, errors);
            ScanBrokenRefs(rec, graph, errors);
            ScanLeveledLists(rec, errors);
        }
        return errors;
    }

    public Task<IReadOnlyList<PluginError>> ScanAsync(KnowledgeGraph graph) =>
        Task.Run(() => Scan(graph));

    public static Dictionary<ErrorSeverity, int> CountBySeverity(IReadOnlyList<PluginError> errors) =>
        errors.GroupBy(e => e.Severity).ToDictionary(g => g.Key, g => g.Count());

    private static void ScanNullActorValues(RecordEntry rec, List<PluginError> errors)
    {
        foreach (var leaf in rec.Node.Descendants().Where(n => n.IsLeaf))
        {
            if (!leaf.Key.Equals("ActorValue", StringComparison.OrdinalIgnoreCase)) continue;
            var v = leaf.Value;
            if (string.IsNullOrWhiteSpace(v) || v.Contains("NULL") || v.Contains("000000"))
                errors.Add(new PluginError(ErrorSeverity.Critical, ErrorCategory.NullActorValue,
                    rec.SourcePlugin, rec.FormKey, leaf.Key,
                    "Condition has NULL/empty ActorValue (always evaluates 0 -> recipe blocked).",
                    FixAvailable: true));
        }
    }

    private static void ScanBrokenRefs(RecordEntry rec, KnowledgeGraph graph, List<PluginError> errors)
    {
        foreach (var r in graph.GetReferencesFrom(rec.FormKey))
        {
            var plugin = r.ToFormKey.Split(':').Last();
            if (KnownMasters.Contains(plugin)) continue;
            if (graph.GetByFormKey(r.ToFormKey) != null) continue;
            errors.Add(new PluginError(ErrorSeverity.Error, ErrorCategory.BrokenReference,
                rec.SourcePlugin, rec.FormKey, r.FieldPath,
                $"References {r.ToFormKey} which is not loaded.", FixAvailable: false));
        }
    }

    private static void ScanLeveledLists(RecordEntry rec, List<PluginError> errors)
    {
        if (!rec.Type.Equals("LVLI", StringComparison.OrdinalIgnoreCase) &&
            !rec.Type.Equals("LVLN", StringComparison.OrdinalIgnoreCase)) return;
        var entries = rec.Node.GetChild("Entries");
        if (entries == null) return;
        foreach (var entry in entries.Children)
        {
            var refVal = entry.GetValue("Reference");
            if (string.IsNullOrWhiteSpace(refVal))
                errors.Add(new PluginError(ErrorSeverity.Warning, ErrorCategory.InvalidLeveledList,
                    rec.SourcePlugin, rec.FormKey, $"Entries.{entry.Key}",
                    "Leveled list entry has no reference (dead slot).", FixAvailable: true));
        }
    }
}
