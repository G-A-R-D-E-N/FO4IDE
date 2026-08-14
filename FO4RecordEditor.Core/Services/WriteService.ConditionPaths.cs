using System.Collections;
using System.Reflection;
using System.Text.Json;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins.Records;

namespace FO4RecordEditor.Services;

/// <summary>
/// Conditions are not always the record's own list. A magic effect keeps its own
/// (<c>Effects[0].Conditions</c>), and a perk keeps them two deep behind the tab wrapper
/// (<c>Effects[0].Conditions[0].Conditions</c>, where the outer list is PerkConditions holding the
/// run-on tab index). The grid hands back whatever row path the user right-clicked, so the resolver
/// here turns any of those into the one editable <c>Condition</c> list and reads or replaces it.
/// </summary>
public static partial class WriteService
{
    /// <summary>Read the Condition list at a grid row path, in set_conditions' own JSON schema.</summary>
    public static string GetConditionsAtPath(object? env, string plugin, string recordId, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Equals("Conditions", StringComparison.OrdinalIgnoreCase))
            return GetConditionsJson(env, plugin, recordId);

        var rec = FindReadableRecord(env, plugin, recordId);
        if (rec == null) return ToolError.Fail($"Record '{recordId}' not found.");

        if (!TryResolveConditionList(rec, path, out var list, out var err)) return ToolError.Fail(err);

        var outp = new List<Dictionary<string, object?>>();
        foreach (var item in list!)
            if (item is IConditionGetter c) outp.Add(DescribeCondition(c));
        return JsonSerializer.Serialize(outp);
    }

    /// <summary>
    /// Replace the Condition list at a grid row path. Same JSON schema as set_conditions, and the
    /// same all-or-nothing contract: the list is rebuilt from what you pass.
    /// </summary>
    public static string SetConditionsAtPath(string plugin, string recordId, string path, string conditionsJson, object? env)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Equals("Conditions", StringComparison.OrdinalIgnoreCase))
            return SetConditions(plugin, recordId, conditionsJson, env);

        var mod = EnsureOpen(plugin, env, out var openMsg); if (mod == null) return ToolError.Fail(openMsg);
        var rec = FindMutableRecord(mod, recordId);
        if (rec == null) return ToolError.Fail($"Record '{recordId}' not found in {plugin}.");

        if (!TryResolveConditionList(rec, path, out var list, out var err)) return ToolError.Fail(err);

        var built = new List<Condition>();
        var failures = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(conditionsJson) ? "[]" : conditionsJson);
            int idx = 0;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                idx++;
                var cond = BuildConditionFromJson(el, env, out var cerr);
                if (cond == null) { failures.Add($"#{idx}: {cerr}"); continue; }
                built.Add(cond);
            }
        }
        catch (Exception ex) { return ToolError.Fail($"Could not parse conditions JSON: {ex.Message}."); }

        list!.Clear();
        foreach (var c in built) list.Add(c);

        var (name, _) = NormalizePlugin(plugin);
        MutagenLoader.InvalidateModIndex(name); NotifyChanged(name);
        var msg = $"Set {built.Count} condition(s) at {path} on {recordId} in {name}.";
        if (failures.Count > 0) msg += $" Skipped {failures.Count}: {string.Join("; ", failures)}.";
        return msg + " save_plugin to persist.";
    }

    // The record to read from: the in-editor copy of the named plugin if one is open, else that
    // plugin's on-disk version, else the winning version.
    private static IMajorRecordGetter? FindReadableRecord(object? env, string plugin, string recordId)
    {
        if (!string.IsNullOrWhiteSpace(plugin))
        {
            var (name, _) = NormalizePlugin(plugin);
            var mutable = GetMutable(name);
            if (mutable != null && FindMutableRecord(mutable, recordId) is { } m) return m;
            if (ResolveFk(env, recordId, out var pfk) &&
                MutagenLoader.GetRecordVersion(env, name, pfk) is { } v) return v;
        }
        return ResolveFk(env, recordId, out var fk)
            ? MutagenLoader.GetRecordContexts(env, fk).LastOrDefault().rec
            : null;
    }

    /// <summary>
    /// Walk a grid row path ("Effects[0].Conditions[1].Conditions[2]") down to the IList of
    /// Condition it names, tolerating the three shapes a user can right-click: the list itself, one
    /// entry in it, or a perk's tab wrapper (whose own Conditions list is the real target).
    /// </summary>
    private static bool TryResolveConditionList(object record, string path, out IList? list, out string error)
    {
        list = null; error = "";
        object? cur = record;
        IList? lastList = null;

        foreach (var seg in SplitPath(path))
        {
            if (cur == null) { error = $"'{path}' does not resolve on {record.GetType().Name}."; return false; }

            var prop = cur.GetType().GetProperty(seg.name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop == null) { error = $"No field '{seg.name}' on {cur.GetType().Name} (path '{path}')."; return false; }
            cur = prop.GetValue(cur);

            if (seg.index is { } i)
            {
                if (cur is not IList il) { error = $"'{seg.name}' is not a list, so '{seg.name}[{i}]' is meaningless."; return false; }
                if (i < 0 || i >= il.Count) { error = $"'{seg.name}[{i}]' is out of range ({il.Count} item(s))."; return false; }
                lastList = il;
                cur = il[i];
            }
        }

        // The list itself, e.g. "...Conditions".
        if (cur is IList target && ElementIsCondition(target)) { list = target; return true; }

        // One entry in a Condition list, e.g. "...Conditions[2]": edit the list holding it.
        if (cur is IConditionGetter && lastList != null) { list = lastList; return true; }

        // A perk's tab wrapper, e.g. "Effects[0].Conditions[0]": its own Conditions are the target.
        if (cur is IPerkConditionGetter)
        {
            var inner = cur.GetType().GetProperty("Conditions")?.GetValue(cur);
            if (inner is IList il2) { list = il2; return true; }
        }

        // The perk tab list itself, which holds wrappers rather than conditions.
        if (cur is IList wrappers && wrappers.Count >= 0 && ElementIs<IPerkConditionGetter>(wrappers))
        {
            error = $"'{path}' is the perk's condition-TAB list, not a condition list. Expand a " +
                    "Condition [n] under it and edit that -- each tab holds its own conditions.";
            return false;
        }

        error = $"'{path}' does not name a condition list.";
        return false;
    }

    private static IEnumerable<(string name, int? index)> SplitPath(string path)
    {
        foreach (var raw in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var seg = raw.Trim();
            int br = seg.IndexOf('[');
            if (br < 0) { yield return (seg, null); continue; }
            var name = seg[..br];
            var idxText = seg[(br + 1)..].TrimEnd(']');
            yield return (name, int.TryParse(idxText, out var i) ? i : null);
        }
    }

    // Element type by declared generic argument first (an empty list still answers correctly),
    // falling back to the first item for a non-generic IList.
    private static bool ElementIsCondition(IList list) => ElementIs<IConditionGetter>(list);

    private static bool ElementIs<T>(IList list)
    {
        var t = list.GetType();
        foreach (var iface in t.GetInterfaces())
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IList<>))
                return typeof(T).IsAssignableFrom(iface.GetGenericArguments()[0]);
        return list.Count > 0 && list[0] is T;
    }

    // ── shims for ElementService ────────────────────────────────────────────────────────────────
    // ElementService needs the same open/find/notify plumbing every write path here uses. These
    // expose it without widening the private helpers themselves.

    /// <summary>Open the plugin for editing and hand back the mutable mod (null with a reason).</summary>
    public static Mutagen.Bethesda.Fallout4.IFallout4Mod? GetMutableFor(string plugin, object? env, out string msg) =>
        EnsureOpen(plugin, env, out msg);

    /// <summary>Find a record by FormKey or EditorID inside an already-open mutable mod.</summary>
    public static Mutagen.Bethesda.Fallout4.IFallout4MajorRecord? FindRecordIn(
        Mutagen.Bethesda.Fallout4.IFallout4Mod mod, string recordId) => FindMutableRecord(mod, recordId);

    /// <summary>Split "MyMod.esp" or a full path into (name, explicitPath).</summary>
    public static (string name, string? path) SplitPlugin(string plugin) => NormalizePlugin(plugin);

    /// <summary>Fire the PluginChanged notification so the UI and caches see the edit.</summary>
    public static void RaiseChanged(string name) => NotifyChanged(name);

    /// <summary>Resolve a FormKey string or EditorID against the load order.</summary>
    public static bool TryResolveFormKey(object? env, string value, out Mutagen.Bethesda.Plugins.FormKey fk) =>
        ResolveFk(env, value, out fk);
}
