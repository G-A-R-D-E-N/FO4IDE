using System.Collections;
using FO4RecordEditor.Models;
using Mutagen.Bethesda.Plugins;

namespace FO4RecordEditor.Services;

/// <summary>
/// Scans the entire loaded load order for record conflicts (FormKeys touched by 2+ plugins).
/// Memory-efficient: tracks the plugin list + winning record info per FormKey, not the records.
/// </summary>
public static class ConflictScanner
{
    // Single source of truth -- also gates the write layer, see ProtectedPlugins.
    private static readonly IReadOnlySet<string> VanillaMasters = ProtectedPlugins.VanillaMasters;

    // The full load-order scan is expensive, so cache it and reuse for the UI, the tree tinting, and
    // the AI's scan_conflicts tool. Invalidated on env (re)load and whenever a plugin is edited.
    private static List<ConflictEntry>? _cache;
    private static readonly object _cacheLock = new();
    public static bool HasCache => _cache != null;
    public static void InvalidateCache() { lock (_cacheLock) _cache = null; }

    /// <summary>Cached scan: scans once, then returns the same list until invalidated. Serialized so
    /// the UI's Scan Conflicts button and the AI's scan_conflicts tool can't both scan at once.</summary>
    public static List<ConflictEntry> ScanCached(object env, Action<string>? progress = null)
    {
        lock (_cacheLock)
            return _cache ??= Scan(env, progress);
    }

    public static Task<List<ConflictEntry>> ScanAsync(object env, Action<string>? progress = null) =>
        Task.Run(() => ScanCached(env, progress));

    public static List<ConflictEntry> Scan(object env, Action<string>? progress = null)
    {
        dynamic e = env;
        var order = new List<(string name, Mutagen.Bethesda.Fallout4.IFallout4ModGetter mod)>();
        foreach (var l in (IEnumerable)e.LoadOrder.ListedOrder)
        {
            dynamic ld = l;
            if (ld.Mod is Mutagen.Bethesda.Fallout4.IFallout4ModGetter m)
                order.Add(((string)ld.ModKey.FileName.String, m));
        }

        // Hot-load AI/in-editor patches: an EditableMods plugin replaces its on-disk version in place
        // (so its edits count) or, if new, is appended as the last (winning) plugin. This makes a
        // freshly-authored patch show up in the conflict scan without reloading the modlist.
        foreach (var kv in FO4RecordEditor.Services.MutagenLoader.EditableMods)
        {
            if (kv.Value is not Mutagen.Bethesda.Fallout4.IFallout4ModGetter em) continue;
            int at = order.FindIndex(o => string.Equals(o.name, kv.Key, StringComparison.OrdinalIgnoreCase));
            if (at >= 0) order[at] = (kv.Key, em);
            else order.Add((kv.Key, em));
        }

        // Pass 1 (cheap): which FormKeys are touched by 2+ distinct plugins?
        var firstPlugin = new Dictionary<FormKey, string>();
        var multi = new HashSet<FormKey>();
        foreach (var (name, mod) in order)
        {
            progress?.Invoke($"Scanning {name}...");
            foreach (var rec in mod.EnumerateMajorRecords())
            {
                if (!firstPlugin.TryGetValue(rec.FormKey, out var fp)) firstPlugin[rec.FormKey] = name;
                else if (!string.Equals(fp, name, StringComparison.OrdinalIgnoreCase)) multi.Add(rec.FormKey);
            }
        }

        // Pass 2: collect only the override records for those FormKeys (bounded to candidates).
        var recs = new Dictionary<FormKey, List<(string plugin, Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter rec)>>();
        foreach (var (name, mod) in order)
        {
            progress?.Invoke($"Comparing overrides in {name}...");
            foreach (var rec in mod.EnumerateMajorRecords())
            {
                if (!multi.Contains(rec.FormKey)) continue;
                if (!recs.TryGetValue(rec.FormKey, out var lst)) { lst = new(); recs[rec.FormKey] = lst; }
                lst.Add((name, rec));
            }
        }

        progress?.Invoke("Determining real conflicts...");
        var result = new List<ConflictEntry>();
        foreach (var kv in recs)
        {
            var entries = kv.Value;
            // Only the versions that DIFFER in content are real conflicts; identical
            // overrides (the benign Cell/NavMesh noise) are dropped, exactly like xEdit.
            var first = entries[0].rec;
            bool differs = entries.Any(en => !RecordsEqual(en.rec, first));
            if (!differs) continue;

            var plugins = entries.Select(en => en.plugin).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (plugins.Count < 2) continue;

            var win = entries[^1];
            bool involvesMod = plugins.Any(p => !VanillaMasters.Contains(p));
            bool suppressed = ModGroupsService.IsSuppressed(plugins);
            result.Add(new ConflictEntry(kv.Key.ToString(), win.rec.EditorID ?? "",
                win.rec.Registration.Name, plugins, win.plugin, involvesMod, suppressed));
        }

        return result
            .OrderByDescending(c => c.InvolvesMod)
            .ThenBy(c => c.Type, StringComparer.Ordinal)
            .ThenBy(c => c.EditorID, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool RecordsEqual(
        Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter a,
        Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter b)
    {
        try { return a.Equals(b); } catch { return false; }
    }
}
