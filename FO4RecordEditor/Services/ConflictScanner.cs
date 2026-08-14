using System.Collections;
using FO4RecordEditor.Models;
using Mutagen.Bethesda.Plugins;

namespace FO4RecordEditor.Services;





public static class ConflictScanner
{

    private static readonly IReadOnlySet<string> VanillaMasters = ProtectedPlugins.VanillaMasters;



    private static List<ConflictEntry>? _cache;
    private static readonly object _cacheLock = new();
    public static bool HasCache => _cache != null;
    public static void InvalidateCache() { lock (_cacheLock) _cache = null; }



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




        foreach (var kv in FO4RecordEditor.Services.MutagenLoader.EditableMods)
        {
            if (kv.Value is not Mutagen.Bethesda.Fallout4.IFallout4ModGetter em) continue;
            int at = order.FindIndex(o => string.Equals(o.name, kv.Key, StringComparison.OrdinalIgnoreCase));
            if (at >= 0) order[at] = (kv.Key, em);
            else order.Add((kv.Key, em));
        }


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
