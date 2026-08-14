using System.IO;
using System.Text.Json;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;

namespace FO4RecordEditor.Services;

/// <summary>
/// The second batch of xEdit-parity operations: declaring a master, renumbering a whole plugin, and
/// the circular-leveled-list check that belongs beside scan_broken_refs.
/// </summary>
public static partial class WriteService
{
    // ── Add Masters ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Declare a master on a plugin (xEdit "Add Masters..."). Until a file is a declared master, a
    /// reference into it has nowhere valid to point, so this is a prerequisite for authoring the
    /// reference, not a cleanup step.
    ///
    /// Note that save_plugin RE-DERIVES the master list from the links the plugin actually contains,
    /// so a master added here and then never referenced will not survive the save. That is the
    /// intended behaviour: use this to unblock authoring, then write the reference.
    /// </summary>
    public static string AddMasters(string plugin, string[] masters, object? env)
    {
        var mod = EnsureOpen(plugin, env, out var openMsg);
        if (mod == null) return ToolError.Fail(openMsg);
        var (name, _) = NormalizePlugin(plugin);
        if (ProtectedPlugins.IsProtected(name)) return ToolError.Fail(ProtectedPlugins.RefusalMessage(name));

        if (masters == null || masters.Length == 0)
            return ToolError.Fail("Provide 'masters' as one or more plugin filenames to declare, e.g. ['DLCCoast.esm'].");

        var current = mod.MasterReferences.Select(m => m.Master.FileName.String).ToList();
        var have = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);

        var added = new List<string>();
        var already = new List<string>();
        var unknown = new List<string>();

        // Only accept a file the load order actually knows about: a master naming a plugin that is
        // not present makes every FormID resolved through it dangle, which is exactly the crash the
        // Dependencies tab flags.
        var loaded = new HashSet<string>(LoadOrderMods(env).Select(m => m.name), StringComparer.OrdinalIgnoreCase);

        foreach (var raw in masters)
        {
            var m = raw?.Trim() ?? "";
            if (m.Length == 0) continue;
            if (string.Equals(m, name, StringComparison.OrdinalIgnoreCase))
                return ToolError.Fail($"A plugin cannot master itself ('{name}').");
            if (have.Contains(m)) { already.Add(m); continue; }
            if (!loaded.Contains(m)) { unknown.Add(m); continue; }
            mod.MasterReferences.Add(new MasterReference { Master = ModKey.FromFileName(m) });
            have.Add(m);
            added.Add(m);
        }

        if (added.Count == 0 && unknown.Count > 0)
            return ToolError.Fail($"Not loaded in this load order, so refusing to declare as master(s): " +
                                  $"[{string.Join(", ", unknown)}]. Load the modlist or the game Data folder first.");

        if (added.Count > 0) { MutagenLoader.InvalidateModIndex(name); NotifyChanged(name); }

        var parts = new List<string>();
        if (added.Count > 0) parts.Add($"declared [{string.Join(", ", added)}]");
        if (already.Count > 0) parts.Add($"already declared [{string.Join(", ", already)}]");
        if (unknown.Count > 0) parts.Add($"SKIPPED, not loaded [{string.Join(", ", unknown)}]");
        return $"{name} masters: {string.Join("; ", parts)}. Now [{string.Join(", ", mod.MasterReferences.Select(m => m.Master.FileName.String))}]. " +
               "save_plugin re-derives the master list from actual references, so write the reference before saving.";
    }

    // ── Renumber FormIDs across a whole plugin ──────────────────────────────────────────────────

    /// <summary>
    /// xEdit's "Renumber FormIDs from...": give every one of the plugin's own records a new object
    /// id, running upward from a base, and repoint every reference. This is how you resolve a
    /// FormID collision with another mod, or move a plugin's records out of a range someone else
    /// claims. renumber_formid does one record, which is not a real option for hundreds.
    ///
    /// Defaults to a dry run.
    /// </summary>
    public static string RenumberPluginFormIds(string plugin, string startHex, bool apply, object? env)
    {
        var mod = EnsureOpen(plugin, env, out var openMsg);
        if (mod == null) return ToolError.Fail(openMsg);
        var (name, _) = NormalizePlugin(plugin);
        if (ProtectedPlugins.IsProtected(name)) return ToolError.Fail(ProtectedPlugins.RefusalMessage(name));

        var startText = (startHex ?? "").Trim();
        if (startText.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) startText = startText[2..];
        if (!uint.TryParse(startText, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var start))
            return ToolError.Fail($"'{startHex}' is not a hex object id. Pass something like '000800' or '001000'.");
        if (start == 0) return ToolError.Fail("Object id 0 is reserved; start at 0x000800 or higher.");
        if (start > 0xFFFFFF) return ToolError.Fail("Object ids are 24 bit; the base must be <= 0xFFFFFF.");

        var native = mod.EnumerateMajorRecords()
            .Where(r => r.FormKey.ModKey == mod.ModKey)
            .OrderBy(r => r.FormKey.ID)
            .ToList();
        if (native.Count == 0) return $"'{name}' has no records of its own to renumber.";
        if (start + (ulong)native.Count - 1 > 0xFFFFFF)
            return ToolError.Fail($"'{name}' has {native.Count} record(s); starting at {start:X6} would run past 0xFFFFFF.");

        var remap = new Dictionary<FormKey, FormKey>();
        uint next = start;
        foreach (var r in native)
        {
            var target = new FormKey(mod.ModKey, next);
            if (r.FormKey != target) remap[r.FormKey] = target;
            next++;
        }

        if (remap.Count == 0)
            return $"'{name}' is already numbered consecutively from {start:X6}; nothing to do.";

        if (!apply)
            return $"DRY RUN: would renumber {remap.Count} of {native.Count} record(s) in {name} into " +
                   $"{start:X6}-{next - 1:X6}, repointing every in-plugin reference. " +
                   "References from OTHER plugins into this one will break -- they point at the old ids. " +
                   "Re-run with apply=true.";

        if (!TryRekeyRecords(mod, remap, "renumber_plugin_formids", name,
                "Renumber in xEdit instead, which can re-key cell-nested records.", out var err))
            return err;

        MutagenLoader.InvalidateModIndex(name); NotifyChanged(name);
        return $"Renumbered {remap.Count} record(s) in {name} into {start:X6}-{next - 1:X6} and repointed every " +
               "in-plugin reference. References from other plugins into this one now dangle. save_plugin to persist.";
    }

    // ── Create SEQ file ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// xEdit's "Create SEQ File". A plugin with start-game-enabled quests needs a matching
    /// <c>Data/Seq/&lt;plugin&gt;.seq</c> or those quests silently never start on an existing save,
    /// so this is a required build step for quest mods rather than an optimisation.
    ///
    /// The file is a bare array of little-endian 4-byte FormIDs, no header. Which quests belong in
    /// it follows xEdit: the start-game-enabled flag is set here AND the version this record
    /// overrides does not already have it, so a plugin does not re-declare quests its master
    /// already starts.
    ///
    /// FormIDs are written with the plugin's OWN file-local index (its master count), which is how
    /// the on-disk record already encodes them and matches the Creation Kit's own output. The
    /// engine resolves them against the plugin the file is named for, which is why a SEQ file does
    /// not need regenerating when load order changes.
    /// </summary>
    public static string CreateSeqFile(object? env, string plugin, string? outputDir)
    {
        var (name, _) = NormalizePlugin(plugin);

        IFallout4ModGetter? mod = GetMutable(name);
        mod ??= LoadOrderMods(env).FirstOrDefault(m => string.Equals(m.name, name, StringComparison.OrdinalIgnoreCase)).mod;
        if (mod == null) return ToolError.Fail($"'{name}' is not loaded. Open the modlist or open_plugin it first.");

        var masterCount = mod.ModHeader.MasterReferences.Count;
        var ids = new List<uint>();
        var included = new List<string>();

        foreach (var q in mod.Quests)
        {
            if (q.Data is not { } data || !data.Flags.HasFlag(Quest.Flag.StartGameEnabled)) continue;

            // Skip a quest whose previous version already starts at game start: re-declaring it is
            // what makes a patch's SEQ file fight its master's.
            var contexts = MutagenLoader.GetRecordContexts(env, q.FormKey);
            int mine = contexts.FindIndex(c => string.Equals(c.plugin, name, StringComparison.OrdinalIgnoreCase));
            if (mine > 0 && contexts[mine - 1].rec is IQuestGetter prev &&
                prev.Data is { } prevData && prevData.Flags.HasFlag(Quest.Flag.StartGameEnabled))
                continue;

            ids.Add(((uint)masterCount << 24) | (q.FormKey.ID & 0x00FFFFFF));
            included.Add($"{q.EditorID ?? "(no EditorID)"} [{q.FormKey}]");
        }

        if (ids.Count == 0)
            return $"'{name}' has no start-game-enabled quests of its own, so it does not need a SEQ file.";

        var dir = string.IsNullOrWhiteSpace(outputDir) ? Path.Combine(DefaultOutputDir, "Seq") : outputDir!;
        var path = Path.Combine(dir, Path.ChangeExtension(name, ".seq"));
        try
        {
            Directory.CreateDirectory(dir);
            var bytes = new byte[ids.Count * 4];
            for (int i = 0; i < ids.Count; i++)
                BitConverter.TryWriteBytes(bytes.AsSpan(i * 4, 4), ids[i]);
            File.WriteAllBytes(path, bytes);
        }
        catch (Exception ex) { return ToolError.Fail($"Could not write '{path}': {ex.Message}"); }

        return $"Wrote {path} with {ids.Count} start-game-enabled quest(s):\n  " +
               string.Join("\n  ", included.Take(50)) +
               (included.Count > 50 ? $"\n  ... and {included.Count - 50} more" : "") +
               "\nDeploy it as Data/Seq/" + Path.ChangeExtension(name, ".seq") + " alongside the plugin.";
    }

    // ── Check for circular leveled lists ────────────────────────────────────────────────────────

    /// <summary>
    /// xEdit's "Check for Circular Leveled Lists". A leveled list that contains itself, directly or
    /// through another list, hangs or crashes the engine when it tries to resolve it. Walks the
    /// WINNING version of every LVLI/LVLN across the load order, since that is what the game sees.
    /// Read-only.
    /// </summary>
    public static string CheckCircularLeveledLists(object? env, string plugin, int limit = 200)
    {
        var (filterName, _) = string.IsNullOrWhiteSpace(plugin) ? ("", (string?)null) : NormalizePlugin(plugin);

        // Winning version per FormKey: later plugins overwrite earlier ones.
        var edges = new Dictionary<FormKey, List<FormKey>>();
        var owner = new Dictionary<FormKey, (string plugin, string edid, string type)>();

        foreach (var (modName, mod) in LoadOrderMods(env))
        {
            foreach (var rec in mod.EnumerateMajorRecords())
            {
                List<FormKey>? children = null;
                switch (rec)
                {
                    case ILeveledItemGetter li:
                        children = li.Entries?.Select(e => e.Data?.Reference.FormKey ?? FormKey.Null).ToList();
                        break;
                    case ILeveledNpcGetter ln:
                        children = ln.Entries?.Select(e => e.Data?.Reference.FormKey ?? FormKey.Null).ToList();
                        break;
                }
                if (children == null) continue;
                edges[rec.FormKey] = children.Where(c => !c.IsNull).ToList();
                owner[rec.FormKey] = (modName, rec.EditorID ?? "", rec.Registration.Name);
            }
        }

        if (edges.Count == 0) return "No leveled lists found in the load order.";

        // Iterative colour-marked DFS: 1 = on the current path, 2 = fully explored and clean.
        var state = new Dictionary<FormKey, int>();
        var cycles = new List<string>();
        var reported = new HashSet<string>();

        foreach (var root in edges.Keys)
        {
            if (state.GetValueOrDefault(root) != 0) continue;
            var path = new List<FormKey>();
            var stack = new Stack<(FormKey node, int childIndex)>();
            stack.Push((root, 0));
            state[root] = 1;
            path.Add(root);

            while (stack.Count > 0)
            {
                var (node, ci) = stack.Pop();
                var kids = edges.TryGetValue(node, out var k) ? k : new List<FormKey>();
                if (ci >= kids.Count)
                {
                    state[node] = 2;
                    if (path.Count > 0) path.RemoveAt(path.Count - 1);
                    continue;
                }
                stack.Push((node, ci + 1));
                var child = kids[ci];
                if (!edges.ContainsKey(child)) continue;          // not a list, so not a cycle candidate
                var cs = state.GetValueOrDefault(child);
                if (cs == 1)
                {
                    int at = path.IndexOf(child);
                    var loop = at >= 0 ? path.Skip(at).Append(child).ToList() : new List<FormKey> { node, child };
                    var key = string.Join("->", loop.Select(f => f.ToString()));
                    if (reported.Add(key))
                        cycles.Add(string.Join(" -> ", loop.Select(f =>
                            owner.TryGetValue(f, out var o) && o.edid.Length > 0 ? $"{o.edid} [{f}]" : f.ToString())));
                    if (cycles.Count >= limit) goto done;
                }
                else if (cs == 0)
                {
                    state[child] = 1;
                    path.Add(child);
                    stack.Push((child, 0));
                }
            }
        }
    done:

        if (filterName.Length > 0)
            cycles = cycles.Where(c => c.Contains(filterName, StringComparison.OrdinalIgnoreCase)).ToList();

        if (cycles.Count == 0)
            return $"No circular leveled lists across {edges.Count} leveled list(s)" +
                   (filterName.Length > 0 ? $" (filtered to {filterName})" : "") + ".";

        return $"{cycles.Count} circular leveled list(s) found across {edges.Count} checked. Each of these " +
               "hangs or crashes the engine when it resolves:\n  " + string.Join("\n  ", cycles.Take(limit));
    }

    /// <summary>JSON form of <see cref="CheckCircularLeveledLists"/>, for the UI.</summary>
    public static string CheckCircularLeveledListsJson(object? env, string plugin, int limit = 200) =>
        JsonSerializer.Serialize(new { report = CheckCircularLeveledLists(env, plugin, limit) });
}
