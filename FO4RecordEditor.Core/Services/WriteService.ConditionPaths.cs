using System.Collections;
using System.Reflection;
using System.Text.Json;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins.Records;

namespace FO4RecordEditor.Services;








public static partial class WriteService
{

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


        if (cur is IList target && ElementIsCondition(target)) { list = target; return true; }


        if (cur is IConditionGetter && lastList != null) { list = lastList; return true; }


        if (cur is IPerkConditionGetter)
        {
            var inner = cur.GetType().GetProperty("Conditions")?.GetValue(cur);
            if (inner is IList il2) { list = il2; return true; }
        }


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



    private static bool ElementIsCondition(IList list) => ElementIs<IConditionGetter>(list);

    private static bool ElementIs<T>(IList list)
    {
        var t = list.GetType();
        foreach (var iface in t.GetInterfaces())
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IList<>))
                return typeof(T).IsAssignableFrom(iface.GetGenericArguments()[0]);
        return list.Count > 0 && list[0] is T;
    }






    public static Mutagen.Bethesda.Fallout4.IFallout4Mod? GetMutableFor(string plugin, object? env, out string msg) =>
        EnsureOpen(plugin, env, out msg);


    public static Mutagen.Bethesda.Fallout4.IFallout4MajorRecord? FindRecordIn(
        Mutagen.Bethesda.Fallout4.IFallout4Mod mod, string recordId) => FindMutableRecord(mod, recordId);


    public static (string name, string? path) SplitPlugin(string plugin) => NormalizePlugin(plugin);


    public static void RaiseChanged(string name) => NotifyChanged(name);


    public static bool TryResolveFormKey(object? env, string value, out Mutagen.Bethesda.Plugins.FormKey fk) =>
        ResolveFk(env, value, out fk);
}
