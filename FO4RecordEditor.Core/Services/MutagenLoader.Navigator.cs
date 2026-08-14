using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FO4RecordEditor.Services.Rendering;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;

namespace FO4RecordEditor.Services;

/// <summary>
/// Queries backing the redesigned navigator, workspace header and detail rail. These answer
/// "what is in this load order" and "what does this record look like from every angle", which the
/// per-plugin drill-down API above was never shaped to do.
/// </summary>
public static partial class MutagenLoader
{
    public sealed record ActivePluginDto(string Name, string Kind, int LoadOrder, bool Editable, long Size);
    public sealed record RecordTypeDto(string Type, string FriendlyName, int Count);
    public sealed record BreadcrumbNodeDto(string Kind, string Label, string FormKey);
    public sealed record RecordDetailsDto(
        string FormKey, string FormId, string EditorId, string Signature, string ClassName,
        string BaseForm, string BaseFormKey, string File, string Winner, int OverrideCount);
    public sealed record PluginMatrixRowDto(
        string Plugin, int LoadOrder, string Kind, int Changes, int Conflicts,
        bool IsOverride, bool IsWinner, string LastModified);
    public sealed record DependencyDto(string Kind, string FormKey, string EditorId, string Type, string Plugin);
    public sealed record HistoryEntryDto(string Plugin, int LoadOrder, string Action, int ChangedFields, string LastModified);
    public sealed record LoadOrderSummaryDto(int TotalRecords, int PluginCount, IReadOnlyList<string> Plugins);

    /// <summary>
    /// A plugin's kind, from its own header flags rather than its extension: an .esp carrying the
    /// Master flag really is a master to the game, and .esl-flagged plugins occupy the FE slot.
    /// </summary>
    private static string PluginKind(object? mod, string name)
    {
        try
        {
            dynamic m = mod!;
            bool master = (bool)m.ModHeader.Flags.HasFlag(Mutagen.Bethesda.Fallout4.Fallout4ModHeader.HeaderFlag.Master);
            // The ESL/light flag is spelled "Small" in the header, and it wins over Master for
            // display: an .esm carrying it still lands in the FE slot.
            bool light = (bool)m.ModHeader.Flags.HasFlag(Mutagen.Bethesda.Fallout4.Fallout4ModHeader.HeaderFlag.Small);
            if (light) return "light";
            if (master) return "master";
        }
        catch { /* fall through to the extension */ }
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext == ".esm" ? "master" : ext == ".esl" ? "light" : "plugin";
    }

    /// <summary>The load order as the navigator's File Browser shows it, newest override last.</summary>
    public static List<ActivePluginDto> GetActivePlugins(object? envObj)
    {
        var result = new List<ActivePluginDto>();
        int i = 0;
        foreach (var (name, mod) in AllLoadedMods(envObj))
        {
            long size = 0;
            if (LooseModPaths.TryGetValue(name, out var path))
            {
                try { size = new FileInfo(path).Length; } catch { }
            }
            result.Add(new ActivePluginDto(name, PluginKind(mod, name), i++,
                EditableMods.ContainsKey(name), size));
        }
        return result;
    }

    /// <summary>
    /// Record types present across the whole load order, with counts. The old tree was
    /// plugin -> type -> record; the navigator is type-first, so the counts have to be summed
    /// across plugins rather than read from one mod's index.
    /// </summary>
    public static List<RecordTypeDto> GetRecordTypeIndex(object? envObj)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, mod) in AllLoadedMods(envObj))
        {
            ModIndex idx;
            try { idx = GetModIndex(mod, name); } catch { continue; }
            foreach (var kv in idx.Counts)
                counts[kv.Key] = counts.TryGetValue(kv.Key, out var n) ? n + kv.Value : kv.Value;
        }
        return counts
            .Select(kv => new RecordTypeDto(kv.Key, FriendlyNames.Label(kv.Key), kv.Value))
            .OrderBy(t => t.FriendlyName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Records of one type across the load order, winning version only, for the navigator's lazy
    /// expand. Deduplicated by FormKey because an overridden record appears in every plugin that
    /// touches it and the navigator wants one row per record, not one per override.
    /// </summary>
    public static List<SearchHit> GetRecordsOfTypeAcrossLoadOrder(
        object? envObj, string sig, string filter, int limit = 500, int offset = 0)
    {
        var seen = new HashSet<FormKey>();
        var hits = new List<SearchHit>();
        var needle = filter?.Trim() ?? "";
        int skipped = 0;

        foreach (var (name, mod) in AllLoadedMods(envObj))
        {
            ModIndex idx;
            try { idx = GetModIndex(mod, name); } catch { continue; }
            var recs = RecordsOfSig(idx, sig);
            if (recs.Count == 0) continue;

            foreach (var r in recs)
            {
                if (!seen.Add(r.FormKey)) continue;
                if (needle.Length > 0)
                {
                    var eid = r.EditorID ?? "";
                    if (eid.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0 &&
                        r.FormKey.ToString().IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                }
                if (skipped++ < offset) continue;
                hits.Add(new SearchHit(r.FormKey.ToString(), r.EditorID ?? "", sig, name));
                if (hits.Count >= limit) return hits;
            }
        }
        return hits;
    }

    /// <summary>
    /// The containment breadcrumb. Only placed references sit in a real worldspace/cell hierarchy;
    /// everything else gets the short "which plugin owns the winning version" path, so the header
    /// never renders an empty strip.
    /// </summary>
    public static List<BreadcrumbNodeDto> GetContainmentPath(object? envObj, string formKeyStr)
    {
        var path = new List<BreadcrumbNodeDto>();
        if (envObj == null || !FormKey.TryFactory(formKeyStr, out var fk)) return path;

        var contexts = GetRecordContexts(envObj, fk);
        if (contexts.Count == 0) return path;

        try
        {
            var lc = (Mutagen.Bethesda.Plugins.Cache.ILinkCache)((dynamic)envObj).LinkCache;
            // ResolveSimpleContext walks Parent links up the CELL/WRLD tree; a non-placed record
            // simply has none, which is why this is a best-effort chain and not a required one.
            var ctx = lc.ResolveAllSimpleContexts<IMajorRecordGetter>(fk).FirstOrDefault();
            var chain = new List<BreadcrumbNodeDto>();
            var parent = ctx?.Parent;
            while (parent?.Record is IMajorRecordGetter pr)
            {
                chain.Add(new BreadcrumbNodeDto(
                    pr.GetType().Name.Replace("Binary", "").Replace("Overlay", ""),
                    Describe(pr), pr.FormKey.ToString()));
                parent = parent.Parent;
            }
            chain.Reverse();
            path.AddRange(chain);
        }
        catch { /* no hierarchy for this record type */ }

        var self = contexts[^1].rec;
        path.Add(new BreadcrumbNodeDto("Record", Describe(self), self.FormKey.ToString()));
        return path;
    }

    private static string Describe(IMajorRecordGetter r)
    {
        var eid = r.EditorID;
        var id = r.FormKey.ID.ToString("X8");
        return string.IsNullOrWhiteSpace(eid) ? id : $"{id} <{eid}>";
    }

    /// <summary>Header fields for the detail rail.</summary>
    public static RecordDetailsDto? GetRecordDetails(object? envObj, string formKeyStr)
    {
        if (!FormKey.TryFactory(formKeyStr, out var fk)) return null;
        var contexts = GetRecordContexts(envObj, fk);
        if (contexts.Count == 0) return null;

        var (winnerPlugin, winner) = contexts[^1];
        var sig = SignatureOf(winner);

        // A placed reference's "Base Form" is what it is an instance of, which is the single most
        // useful jump target in the rail. Non-REFR records have no base and report themselves.
        string baseLabel = "", baseKey = "";
        try
        {
            dynamic d = winner;
            FormKey bk = d.Base.FormKey;
            if (!bk.IsNull)
            {
                baseKey = bk.ToString();
                var baseCtx = GetRecordContexts(envObj, bk);
                baseLabel = baseCtx.Count > 0 ? Describe(baseCtx[^1].rec) : bk.ID.ToString("X8");
            }
        }
        catch { /* not a placed reference */ }

        return new RecordDetailsDto(
            FormKey: winner.FormKey.ToString(),
            FormId: winner.FormKey.ID.ToString("X8"),
            EditorId: winner.EditorID ?? "",
            Signature: sig,
            ClassName: FriendlyNames.Label(sig),
            BaseForm: baseLabel,
            BaseFormKey: baseKey,
            File: winnerPlugin,
            Winner: winnerPlugin,
            OverrideCount: Math.Max(0, contexts.Count - 1));
    }

    /// <summary>
    /// The record's type name. Registration.Name is the same source BuildConflictMatrix uses for
    /// ConflictMatrix.Type, so the rail and the grid agree. The obvious-looking
    /// Registration.TriggeringRecordType.Type throws on binary overlays, which silently fell back
    /// to the CLR type name and produced "WeaponBinaryOverlay" instead of "Weapon".
    /// </summary>
    private static string SignatureOf(IMajorRecordGetter r)
    {
        try { return (string)((dynamic)r).Registration.Name; }
        catch { return r.GetType().Name.Replace("BinaryOverlay", "").Replace("Binary", ""); }
    }

    /// <summary>
    /// "Plugins Affecting This Record": one row per plugin carrying a version, with how many fields
    /// it changed and how many of those actually conflict. Changes and conflicts are read off the
    /// same conflict matrix the grid renders, so the numbers cannot disagree with the grid.
    /// </summary>
    public static List<PluginMatrixRowDto> GetRecordPluginMatrix(object? envObj, string formKeyStr)
    {
        var rows = new List<PluginMatrixRowDto>();
        var matrix = BuildConflictMatrix(envObj, formKeyStr);
        if (matrix == null) return rows;

        for (int col = 0; col < matrix.Plugins.Count; col++)
        {
            var plugin = matrix.Plugins[col];
            // Two counts that cannot contradict each other: how many fields this plugin sets, and
            // how many of those lose to a later plugin. An earlier attempt counted a "master"
            // status as a conflict but not a change, so a base column reported more conflicts than
            // changes, which reads as a bug even though both numbers were individually defensible.
            int winnerCol = matrix.Plugins.Count - 1;
            int changes = 0, conflicts = 0;
            foreach (var row in matrix.Rows)
            {
                if (col >= row.Statuses.Count) continue;
                if (row.Statuses[col] == "notdefined") continue;
                changes++;
                var mine = col < row.Values.Count ? row.Values[col] : "";
                var winning = winnerCol < row.Values.Count ? row.Values[winnerCol] : "";
                if (row.Differs && CanonValue(mine) != CanonValue(winning)) conflicts++;
            }

            string mtime = "";
            if (LooseModPaths.TryGetValue(plugin, out var path))
            {
                try { mtime = new FileInfo(path).LastWriteTimeUtc.ToString("u"); } catch { }
            }

            object? mod = ResolveMod(plugin, envObj);
            rows.Add(new PluginMatrixRowDto(
                Plugin: plugin,
                LoadOrder: col,
                Kind: PluginKind(mod, plugin),
                Changes: changes,
                Conflicts: conflicts,
                IsOverride: col > 0,
                IsWinner: string.Equals(plugin, matrix.Winner, StringComparison.OrdinalIgnoreCase),
                LastModified: mtime));
        }
        return rows;
    }

    /// <summary>
    /// What the winning version needs to resolve: every outgoing FormLink, plus the masters it
    /// implies. A dangling link is reported with an empty EditorId and Plugin rather than dropped,
    /// because "this dependency does not resolve" is exactly what the panel exists to surface.
    /// </summary>
    public static List<DependencyDto> GetDependencies(object? envObj, string formKeyStr, int cap = 300)
    {
        var deps = new List<DependencyDto>();
        if (!FormKey.TryFactory(formKeyStr, out var fk)) return deps;
        var contexts = GetRecordContexts(envObj, fk);
        if (contexts.Count == 0) return deps;

        var seen = new HashSet<FormKey>();
        foreach (var link in contexts[^1].rec.EnumerateFormLinks())
        {
            if (link.FormKey.IsNull || !seen.Add(link.FormKey)) continue;
            var target = GetRecordContexts(envObj, link.FormKey);
            if (target.Count == 0)
            {
                deps.Add(new DependencyDto("missing", link.FormKey.ToString(), "", "", ""));
            }
            else
            {
                var (plugin, rec) = target[^1];
                deps.Add(new DependencyDto("link", link.FormKey.ToString(),
                    rec.EditorID ?? "", SignatureOf(rec), plugin));
            }
            if (deps.Count >= cap) break;
        }
        return deps;
    }

    /// <summary>
    /// The override timeline. There is no real version history in a plugin file, so this is the
    /// honest approximation the plan called for: one entry per plugin that carries the record, in
    /// load order, labelled by what it did to it.
    /// </summary>
    public static List<HistoryEntryDto> GetHistory(object? envObj, string formKeyStr)
    {
        var entries = new List<HistoryEntryDto>();
        foreach (var row in GetRecordPluginMatrix(envObj, formKeyStr))
        {
            entries.Add(new HistoryEntryDto(
                Plugin: row.Plugin,
                LoadOrder: row.LoadOrder,
                Action: row.LoadOrder == 0 ? "created" : row.Conflicts > 0 ? "conflicting override" : "override",
                ChangedFields: row.Changes,
                LastModified: row.LastModified));
        }
        return entries;
    }

    /// <summary>Totals for the status bar. The record count is the real total, not the plugin count.</summary>
    public static LoadOrderSummaryDto GetLoadOrderSummary(object? envObj)
    {
        var names = new List<string>();
        int total = 0;
        foreach (var (name, mod) in AllLoadedMods(envObj))
        {
            names.Add(name);
            try { total += GetModIndex(mod, name).Counts.Values.Sum(); } catch { }
        }
        return new LoadOrderSummaryDto(total, names.Count, names);
    }
}
