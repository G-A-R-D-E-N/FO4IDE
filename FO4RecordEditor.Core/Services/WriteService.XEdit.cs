using System.Collections;
using System.Reflection;
using System.Text.Json;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;

namespace FO4RecordEditor.Services;

public static partial class WriteService
{

    public static string CopyAsNewRecord(object? env, string sourcePlugin, string id, string targetPlugin, string? newEditorId)
    {
        if (!ResolveFk(env, id, out var fk))
            return ToolError.Fail($"'{id}' is not a FormKey and no loaded record has that EditorID.");

        var src = string.IsNullOrWhiteSpace(sourcePlugin)
            ? MutagenLoader.GetRecordContexts(env, fk).LastOrDefault().rec
            : MutagenLoader.GetRecordVersion(env, sourcePlugin, fk);
        if (src == null) return ToolError.Fail($"Could not find {fk} in '{sourcePlugin}'.");

        var mod = EnsureOpen(targetPlugin, env, out var openMsg);
        if (mod == null)
        {
            var (createName, _) = NormalizePlugin(targetPlugin);
            CreatePlugin(targetPlugin);
            mod = GetMutable(createName);
            if (mod == null) return ToolError.Fail(openMsg.Length > 0 ? openMsg : $"Could not open or create '{targetPlugin}'.");
        }
        var (name, _) = NormalizePlugin(targetPlugin);

        FormKey newFk;
        try { newFk = mod.GetNextFormKey(); }
        catch (Exception ex) { return ToolError.Fail($"Could not allocate a new FormID in {name}: {ex.Message}"); }

        IMajorRecord dup;
        try { dup = src.Duplicate(newFk); }
        catch (Exception ex) { return ToolError.Fail($"Could not duplicate {fk}: {ex.Message}"); }

        if (!string.IsNullOrWhiteSpace(newEditorId)) dup.EditorID = newEditorId;
        else if (!string.IsNullOrWhiteSpace(dup.EditorID)) dup.EditorID = dup.EditorID + "DUP";

        if (!TryAddToGroup(mod, src.Registration.GetterType, dup, out var addErr))
            return ToolError.Fail($"Could not add the duplicate to {name}: {addErr}");

        MutagenLoader.InvalidateModIndex(name);
        NotifyChanged(name);
        return $"Copied {fk} into {name} as NEW record {newFk} ('{dup.EditorID}'). save_plugin to persist.";
    }

    private static bool TryAddToGroup(IFallout4Mod mod, Type getterType, IMajorRecord rec, out string err)
    {
        foreach (var prop in mod.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var pt = prop.PropertyType;
            if (!pt.IsGenericType) continue;
            var args = pt.GetGenericArguments();
            if (args.Length != 1) continue;
            if (!getterType.IsAssignableFrom(args[0])) continue;
            if (!args[0].IsInstanceOfType(rec)) continue;

            var group = prop.GetValue(mod);
            if (group is not IGroup) continue;

            var add = pt.GetMethod("Add", new[] { args[0] })
                      ?? pt.GetMethods().FirstOrDefault(m => m.Name == "Add" && m.GetParameters().Length == 1);
            if (add == null) continue;
            try { add.Invoke(group, new object[] { rec }); err = ""; return true; }
            catch (Exception ex) { err = ex.InnerException?.Message ?? ex.Message; return false; }
        }
        err = $"no group in the mod holds {getterType.Name}";
        return false;
    }

    public static string RemoveIdenticalToMaster(object? env, string plugin, bool apply, int limit = 2000)
    {
        var mod = EnsureOpen(plugin, env, out var openMsg);
        if (mod == null) return ToolError.Fail(openMsg);
        var (name, _) = NormalizePlugin(plugin);

        var itm = new List<(FormKey fk, string edid, string type, string overrides)>();
        int scanned = 0;

        foreach (var rec in mod.EnumerateMajorRecords().ToList())
        {
            scanned++;
            var contexts = MutagenLoader.GetRecordContexts(env, rec.FormKey);
            int mine = contexts.FindIndex(c => string.Equals(c.plugin, name, StringComparison.OrdinalIgnoreCase));
            if (mine <= 0) continue;

            var previous = contexts[mine - 1];
            if (!RecordsAreIdentical(previous.rec, rec)) continue;

            itm.Add((rec.FormKey, rec.EditorID ?? "", rec.Registration.Name, previous.plugin));
            if (itm.Count >= limit) break;
        }

        if (itm.Count == 0)
            return $"No identical-to-master records in {name} ({scanned} record(s) checked).";

        if (!apply)
        {
            var preview = string.Join("\n", itm.Take(50).Select(i =>
                $"  {i.type} {i.fk} '{i.edid}' -- identical to {i.overrides}"));
            var more = itm.Count > 50 ? $"\n  ... and {itm.Count - 50} more" : "";
            return $"DRY RUN: {itm.Count} identical-to-master record(s) in {name} of {scanned} checked.\n" +
                   preview + more + "\nRe-run with apply=true to remove them.";
        }

        int removed = 0;
        var failures = new List<string>();
        foreach (var i in itm)
        {
            if (TryRemoveRecord(mod, i.fk, out var err)) removed++;
            else failures.Add($"{i.fk}: {err}");
        }

        MutagenLoader.InvalidateModIndex(name);
        NotifyChanged(name);
        var msg = $"Removed {removed} identical-to-master record(s) from {name}. save_plugin to persist.";
        if (failures.Count > 0) msg += $" {failures.Count} could not be removed: {string.Join("; ", failures.Take(5))}.";
        return msg;
    }

    private static bool RecordsAreIdentical(IMajorRecordGetter a, IMajorRecordGetter b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Registration.GetterType != b.Registration.GetterType) return false;
        try { return a.Equals(b); }
        catch { return false; }
    }

    private static bool TryRemoveRecord(IFallout4Mod mod, FormKey fk, out string err)
    {
        foreach (var groupProp in mod.GetType().GetProperties()
                     .Where(p => typeof(IGroup).IsAssignableFrom(p.PropertyType)))
        {
            if (groupProp.GetValue(mod) is not IGroup group) continue;
            var contains = ((IEnumerable)group).Cast<IMajorRecordGetter>().Any(r => r.FormKey == fk);
            if (!contains) continue;
            return TryRemoveFromGroup(group, fk, out err);
        }
        err = "could not locate the group holding it";
        return false;
    }

    public static string CreateMergedPatch(object? env, string plugins, string patchPlugin, bool apply, int limit = 5000)
    {
        if (string.IsNullOrWhiteSpace(patchPlugin))
            return ToolError.Fail("Choose a patch plugin to write the merged records into.");

        var selected = (plugins ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var order = LoadOrderMods(env)
            .Where(m => selected.Count == 0 || selected.Contains(m.name))
            .ToList();

        var (patchName, _) = NormalizePlugin(patchPlugin);
        order.RemoveAll(m => string.Equals(m.name, patchName, StringComparison.OrdinalIgnoreCase));

        if (order.Count < 2)
            return ToolError.Fail($"A merged patch needs at least two source plugins; matched {order.Count}.");

        var owner = new Dictionary<FormKey, string>();
        var winner = new Dictionary<FormKey, string>();
        var contested = new HashSet<FormKey>();
        foreach (var (name, mod) in order)
        {
            foreach (var rec in mod.EnumerateMajorRecords())
            {
                var fk = rec.FormKey;
                if (owner.TryGetValue(fk, out var first))
                {
                    if (!string.Equals(first, name, StringComparison.OrdinalIgnoreCase)) contested.Add(fk);
                }
                else owner[fk] = name;
                winner[fk] = name;
            }
        }

        if (contested.Count == 0)
            return $"No records are touched by two or more of the {order.Count} selected plugin(s); nothing to merge.";

        var work = contested.Take(limit).ToList();
        if (!apply)
        {
            var byWinner = work.GroupBy(fk => winner[fk])
                .OrderByDescending(g => g.Count())
                .Select(g => $"  {g.Key}: {g.Count()} record(s) win");
            var capped = contested.Count > limit ? $" (capped at {limit} of {contested.Count})" : "";
            return $"DRY RUN: {work.Count} conflicting record(s){capped} across {order.Count} plugin(s) would be " +
                   $"forwarded into {patchName}.\n" + string.Join("\n", byWinner) +
                   "\nRe-run with apply=true to write them.";
        }

        int ok = 0;
        var failures = new List<string>();
        foreach (var fk in work)
        {
            var msg = ResolveConflict(env, fk.ToString(), winner[fk], patchPlugin);
            if (msg.StartsWith("Copied", StringComparison.OrdinalIgnoreCase)) ok++;
            else failures.Add($"{fk}: {msg}");
        }

        var result = $"Merged patch {patchName}: forwarded {ok} of {work.Count} conflicting record(s). " +
                     $"Ensure {patchName} loads last, then save_plugin to persist.";
        if (failures.Count > 0) result += $" {failures.Count} failed: {string.Join("; ", failures.Take(5))}.";
        return result;
    }

    private static List<(string name, IFallout4ModGetter mod)> LoadOrderMods(object? env)
    {
        var order = new List<(string name, IFallout4ModGetter mod)>();
        if (env != null)
        {
            try
            {
                foreach (var l in ((IEnumerable)((dynamic)env).LoadOrder.ListedOrder).Cast<dynamic>())
                    if (l.Mod is IFallout4ModGetter m)
                        order.Add(((string)l.ModKey.FileName.String, m));
            }
            catch { }
        }
        foreach (var kv in MutagenLoader.EditableMods)
        {
            if (kv.Value is not IFallout4ModGetter em) continue;
            int at = order.FindIndex(o => string.Equals(o.name, kv.Key, StringComparison.OrdinalIgnoreCase));
            if (at >= 0) order[at] = (kv.Key, em);
            else order.Add((kv.Key, em));
        }
        return order;
    }
}
