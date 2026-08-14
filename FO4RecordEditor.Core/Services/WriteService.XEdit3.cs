using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;

namespace FO4RecordEditor.Services;

public static partial class WriteService
{

    public static string DeepCopyAsOverride(object? env, string sourcePlugin, string id, string patchPlugin,
        bool apply, bool overwrite = false, int cap = 200)
    {
        if (!ResolveFk(env, id, out var rootFk))
            return ToolError.Fail($"'{id}' is not a FormKey and no loaded record has that EditorID.");

        var rootVersions = MutagenLoader.GetRecordContexts(env, rootFk);
        string sourceName;
        IMajorRecordGetter? root;
        if (string.IsNullOrWhiteSpace(sourcePlugin))
        {
            var selected = rootVersions.LastOrDefault();
            root = selected.rec;
            sourceName = selected.plugin ?? rootFk.ModKey.FileName.String;
        }
        else
        {
            (sourceName, _) = NormalizePlugin(sourcePlugin);
            root = MutagenLoader.GetRecordVersion(env, sourceName, rootFk);
        }
        if (root == null) return ToolError.Fail($"Could not find {rootFk} in '{sourceName}'.");

        if (root.Registration.Name is "Cell" or "Worldspace")
            return ToolError.Fail(
                $"{root.Registration.Name} does not support deep copy here: xEdit's version copies its placed-" +
                "reference tree (CELL) or cell blocks (WRLD), which this tool cannot reproduce -- see " +
                "compact_to_esl's cell-nested-record refusal for why. Copy the cell with copy_as_override and " +
                "its placed objects individually with create_placed_object instead.");

        var owner = ModKey.FromNameAndExtension(sourceName);

        var visited = new HashSet<FormKey> { rootFk };
        var queue = new Queue<IMajorRecordGetter>();
        queue.Enqueue(root);
        var toCopy = new List<IMajorRecordGetter> { root };

        while (queue.Count > 0 && toCopy.Count < cap)
        {
            var cur = queue.Dequeue();
            foreach (var link in cur.EnumerateFormLinks())
            {
                var lfk = link.FormKey;
                if (lfk.IsNull || lfk.ModKey != owner || !visited.Add(lfk)) continue;

                var target = MutagenLoader.GetRecordVersion(env, sourceName, lfk);
                if (target == null) continue;

                toCopy.Add(target);
                queue.Enqueue(target);
                if (toCopy.Count >= cap) break;
            }
        }

        if (!TryFindExistingOverrides(env, patchPlugin, toCopy.Select(r => r.FormKey),
                out var existing, out var inspectError))
            return ToolError.Fail(inspectError);

        if (!apply)
        {
            var byType = toCopy.GroupBy(r => r.Registration.Name).OrderByDescending(g => g.Count())
                .Select(g => $"{g.Key} x{g.Count()}");
            var capped = queue.Count > 0 ? $" (capped at {cap}; more remained in the queue)" : "";
            var overwriteNote = existing.Count > 0
                ? $" {existing.Count} already exist in the target and will require a separate overwrite confirmation."
                : "";
            return $"DRY RUN: deep copy of {rootFk} would bring {toCopy.Count} record(s) into {patchPlugin}{capped}: " +
                   string.Join(", ", byType) + "." + overwriteNote + " Re-run with apply=true.";
        }

        if (existing.Count > 0 && !overwrite)
        {
            var sample = string.Join(", ", existing.Take(5).Select(e => $"{e.editorId} ({e.formKey})"));
            var more = existing.Count > 5 ? $" and {existing.Count - 5} more" : "";
            var (targetName, _) = NormalizePlugin(patchPlugin);
            return $"{OverwritePrompt} '{targetName}' already contains {existing.Count} of the {toCopy.Count} " +
                   $"record(s) in this deep copy: {sample}{more}. Overwrite all existing records with " +
                   $"fresh copies from '{sourceName}'?";
        }

        int ok = 0;
        var failures = new List<string>();

        foreach (var rec in toCopy.AsEnumerable().Reverse())
        {
            var msg = ResolveConflict(env, rec.FormKey.ToString(), sourceName, patchPlugin, overwrite);
            if (msg.StartsWith("Copied", StringComparison.OrdinalIgnoreCase)) ok++;
            else failures.Add($"{rec.FormKey} ({rec.Registration.Name}): {msg}");
        }

        var (patchName, _) = NormalizePlugin(patchPlugin);
        var result = $"Deep-copied {ok} of {toCopy.Count} record(s) rooted at {rootFk} into {patchName}. " +
                     $"Ensure {patchName} loads after the conflicting plugins, then save it.";
        if (failures.Count > 0) result += $" {failures.Count} failed: {string.Join("; ", failures.Take(5))}.";
        return result;
    }

    public static string ChangeReferencingRecords(object? env, string fromId, string toId, string patchPlugin, bool apply, int cap = 300)
    {
        if (!ResolveFk(env, fromId, out var fromFk))
            return ToolError.Fail($"'{fromId}' is not a FormKey and no loaded record has that EditorID.");
        if (!ResolveFk(env, toId, out var toFk))
            return ToolError.Fail($"'{toId}' is not a FormKey and no loaded record has that EditorID.");
        if (fromFk == toFk) return ToolError.Fail("'from' and 'to' are the same record.");

        var referencing = MutagenLoader.GetReferencedBy(env, fromFk.ToString(), cap);
        if (referencing.Count == 0)
            return $"Nothing references {fromFk}; nothing to change.";

        if (!apply)
        {
            var preview = referencing.Take(50)
                .Select(r => $"  {r.Type} {r.FormKey} '{r.EditorID}' in {r.Plugin}");
            var more = referencing.Count > 50 ? $"\n  ... and {referencing.Count - 50} more" : "";
            return $"DRY RUN: {referencing.Count} record(s) reference {fromFk}; would repoint them at {toFk} in " +
                   $"{patchPlugin}:\n" + string.Join("\n", preview) + more + "\nRe-run with apply=true.";
        }

        var (patchName, patchPath) = NormalizePlugin(patchPlugin);
        if (GetMutable(patchName) == null)
        {
            bool existsOnDisk = (patchPath != null && File.Exists(patchPath))
                || MutagenLoader.LooseModPaths.ContainsKey(patchName)
                || FindPluginPath(patchName, env) != null;
            if (existsOnDisk) OpenPlugin(patchPlugin, env);
            else CreatePlugin(patchPlugin);
        }
        var patch = GetMutable(patchName);
        if (patch == null) return ToolError.Fail($"Could not open or create patch plugin '{patchName}'.");

        var targetIndex = LoadOrderIndexOf(env, patchName);
        if (targetIndex >= 0)
        {
            foreach (var reference in referencing)
            {
                if (!FormKey.TryFactory(reference.FormKey, out var referenceKey)) continue;
                if (FindMutableRecord(patch, referenceKey.ToString()) == null) continue;

                var sourceIndex = LoadOrderIndexOf(env, reference.Plugin);
                if (sourceIndex >= 0 && targetIndex < sourceIndex)
                {
                    return ToolError.Fail(
                        $"Refused before changing any records: '{patchName}' is at load order {targetIndex:X2}, " +
                        $"but '{reference.Plugin}' is at {sourceIndex:X2} and carries a later version of " +
                        $"{referenceKey}. Editing the earlier target override would not change the winning record. " +
                        $"Move '{patchName}' after '{reference.Plugin}' or choose a later patch plugin.");
                }
            }
        }

        var remap = new Dictionary<FormKey, FormKey> { [fromFk] = toFk };
        int ok = 0;
        var failures = new List<string>();

        foreach (var r in referencing)
        {
            if (!FormKey.TryFactory(r.FormKey, out var rfk)) { failures.Add($"{r.FormKey}: not a FormKey"); continue; }

            var ovr = FindMutableRecord(patch, rfk.ToString());
            if (ovr == null)
            {
                var copyMsg = ResolveConflict(env, rfk.ToString(), r.Plugin, patchPlugin);
                if (!copyMsg.StartsWith("Copied", StringComparison.OrdinalIgnoreCase))
                { failures.Add($"{rfk} ({r.Plugin}): {copyMsg}"); continue; }
                ovr = FindMutableRecord(patch, rfk.ToString());
            }

            if (ovr is not IFormLinkContainer container)
            { failures.Add($"{rfk}: target record is not a FormLink container"); continue; }

            try { container.RemapLinks(remap); ok++; }
            catch (Exception ex) { failures.Add($"{rfk}: remap failed ({ex.Message})"); }
        }

        MutagenLoader.InvalidateModIndex(patchName); NotifyChanged(patchName);
        var result = $"Repointed {ok} of {referencing.Count} referencing record(s) from {fromFk} to {toFk} in {patchName}. " +
                     $"Ensure {patchName} loads after the plugins it copied from, then save_plugin.";
        if (failures.Count > 0) result += $" {failures.Count} failed: {string.Join("; ", failures.Take(5))}.";
        return result;
    }
}
