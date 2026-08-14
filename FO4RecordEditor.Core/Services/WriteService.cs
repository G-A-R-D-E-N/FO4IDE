using System.IO;
using System.Reflection;
using System.Text.Json;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Strings;
// Only ever call JsonConvert.SerializeObject from Newtonsoft here, never a bare JsonSerializer.* --
// System.Text.Json.JsonSerializer is already used throughout this file and the two types collide.
using JsonConvert = Newtonsoft.Json.JsonConvert;

namespace FO4RecordEditor.Services;

/// <summary>
/// Lets the AI author plugins: create a new mutable mod, add records, set scalar fields,
/// and write the ESP. Mutable mods are mirrored into MutagenLoader.LooseMods so the read
/// tools (list/search/get) immediately see what was created. Changes live in memory until
/// save_plugin is called.
/// </summary>
public static partial class WriteService
{
    private static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

    private static readonly Dictionary<string, IFallout4Mod> _mutable = new(StringComparer.OrdinalIgnoreCase);
    // Original on-disk path of an opened plugin, so save_plugin can overwrite it in place.
    private static readonly Dictionary<string, string> _sourcePath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>User-configured output folder (from Settings). Blank => &lt;app&gt;/Output.</summary>
    public static string? OutputFolderOverride { get; set; }

    public static string DefaultOutputDir =>
        !string.IsNullOrWhiteSpace(OutputFolderOverride)
            ? OutputFolderOverride!
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output");

    /// <summary>The loaded MO2 instance's overwrite folder (&lt;instance&gt;\overwrite). When set, NEW
    /// patches save here so MO2 loads them automatically at top priority -- no manual mod install.</summary>
    public static string? Mo2OverwriteFolder { get; set; }

    /// <summary>Where a brand-new patch (no source file) saves by default: the MO2 overwrite folder
    /// when a modlist is loaded, otherwise the configured Output folder.</summary>
    public static string NewPatchDir =>
        !string.IsNullOrWhiteSpace(Mo2OverwriteFolder) ? Mo2OverwriteFolder! : DefaultOutputDir;

    public static IFallout4Mod? GetMutable(string name) =>
        _mutable.TryGetValue(name, out var m) ? m : null;

    internal static bool TryGetSourcePath(string name, out string path) =>
        _sourcePath.TryGetValue(name, out path!);

    private static void Register(string name, IFallout4Mod mod)
    {
        _mutable[name] = mod;
        MutagenLoader.EditableMods[name] = mod;  // prioritized for ALL reads (hot loading)
        // so list_plugins / the tree still see it. Via ReplaceLooseMod because opening a plugin that
        // was already loaded read-only displaces a memory-mapped overlay, whose file handle stays
        // open until it is disposed -- and that handle is on the very file save_plugin writes back to.
        MutagenLoader.ReplaceLooseMod(name, mod);
    }

    // Accept a bare file name ("Patch.esp") OR a full path ("D:\mods\X\Patch.esp"). Returns the file
    // name used as the registry key / ModKey, plus the explicit on-disk path when one was supplied.
    private static (string name, string? explicitPath) NormalizePlugin(string plugin)
    {
        if (string.IsNullOrWhiteSpace(plugin)) return (plugin, null);
        if ((plugin.Contains('\\') || plugin.Contains('/')) && Path.IsPathRooted(plugin))
            return (Path.GetFileName(plugin), plugin);
        return (plugin, null);
    }

    public static string OpenPlugin(string plugin, object? env)
    {
        if (string.IsNullOrWhiteSpace(plugin)) return "Provide a plugin name.";
        var (name, explicitPath) = NormalizePlugin(plugin);
        // Never hand out a mutable vanilla master -- save_plugin defaults to overwriting the file a
        // plugin was opened from, so opening one is already one typo away from destroying it.
        if (ProtectedPlugins.IsProtected(name)) return ToolError.Fail(ProtectedPlugins.RefusalMessage(name));
        if (_mutable.ContainsKey(name)) return $"'{name}' is already open for editing.";

        string? filePath = explicitPath ?? FindPluginPath(name, env);
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return $"Could not locate '{name}' on disk. Pass a full path (e.g. 'D:\\...\\{name}'), load the game environment, or open the file via File > Open.";

        try
        {
            var mod = Fallout4Mod.CreateFromBinary(ModPath.FromPath(filePath), Fallout4Release.Fallout4);
            Register(name, mod);
            _sourcePath[name] = filePath;     // save_plugin overwrites the original by default
            MutagenLoader.LooseModPaths[name] = filePath; // also backs raw integrity scans
            NotifyChanged(name);
            var count = mod.EnumerateMajorRecords().Count();
            return $"Opened '{name}' for editing ({count} records). Use create_record, set_field, and save_plugin.";
        }
        catch (Exception ex)
        {
            return $"Failed to open '{name}' for editing: {ex.Message}";
        }
    }

    private static string? FindPluginPath(string plugin, object? env)
    {
        // 1. Loose mod opened via file picker
        if (MutagenLoader.LooseModPaths.TryGetValue(plugin, out var loosePath))
            return loosePath;

        if (env != null)
        {
            // 2. MO2 environment (Mo2ProfileLoader.Mo2GameEnvironment) -- its LoadOrder entries are
            // plain Mutagen ModListing objects with no Path property, and DataFolderPath here is only
            // the vanilla game Data folder, so neither of the two checks below can ever find a plugin
            // that actually lives in an MO2 mods\ or overwrite\ folder (i.e. almost anything in a real
            // modlist). PluginPaths is the loader's own record of where it actually read each plugin
            // from; check it first. Wrapped in try/catch because a non-MO2 env (plain GameEnvironmentState
            // from Load Env) has no such property and dynamic dispatch throws on the miss.
            try
            {
                dynamic dynEnv = env;
                IReadOnlyDictionary<string, string> pluginPaths = dynEnv.PluginPaths;
                if (pluginPaths.TryGetValue(plugin, out var mo2Path)) return mo2Path;
            }
            catch { }

            // 3. Game environment load order (the vanilla Load Env / GameEnvironmentState path --
            // ListedOrder entries here DO carry a real Path).
            try
            {
                dynamic dynEnv = env;
                foreach (dynamic l in (System.Collections.IEnumerable)dynEnv.LoadOrder.ListedOrder)
                {
                    if (!string.Equals((string)l.ModKey.FileName.String, plugin, StringComparison.OrdinalIgnoreCase))
                        continue;
                    // Try direct Path property first
                    try { var p = (string?)l.Path?.Path; if (p != null) return p; } catch { }
                    // Fall back to DataFolderPath + filename
                    try
                    {
                        var dataDir = (string)dynEnv.DataFolderPath.Path;
                        return Path.Combine(dataDir, plugin);
                    }
                    catch { }
                    break;
                }
            }
            catch { }
        }

        return null;
    }

    public static string CreatePlugin(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Provide a plugin name, e.g. 'MyMod.esp'.";
        // A full path is allowed -- use its file name as the ModKey and remember the path to save to.
        var (fileName, explicitPath) = NormalizePlugin(name);
        if (!fileName.EndsWith(".esp", StringComparison.OrdinalIgnoreCase) &&
            !fileName.EndsWith(".esm", StringComparison.OrdinalIgnoreCase) &&
            !fileName.EndsWith(".esl", StringComparison.OrdinalIgnoreCase))
            fileName += ".esp";

        var mod = new Fallout4Mod(ModKey.FromNameAndExtension(fileName), Fallout4Release.Fallout4);
        Register(fileName, mod);
        if (explicitPath != null) _sourcePath[fileName] = explicitPath;   // save_plugin writes here
        NotifyChanged(fileName);
        return $"Created new plugin '{fileName}'. Add records with create_record, then call save_plugin to write it.";
    }

    /// <summary>Raised (with the plugin file name) when a plugin is created/opened/modified,
    /// so the UI can show or refresh it in the Explorer tree.</summary>
    public static event Action<string>? PluginChanged;
    private static void NotifyChanged(string name) => PluginChanged?.Invoke(name);

    /// <summary>Public entry point for NotifyChanged -- a C# event can only be raised from within its
    /// declaring type, so a sibling class (e.g. CellService, for #60's cleanup tool) needs this to
    /// tell the Explorer tree a patch plugin changed instead of reaching into WriteService's own
    /// private NotifyChanged.</summary>
    public static void NotifyPluginChanged(string name) => NotifyChanged(name);

    public static string CreateRecord(string plugin, string sig, string editorId, object? env)
    {
        var mod = EnsureOpen(plugin, env, out var openMsg);
        if (mod == null) return openMsg;
        if (string.IsNullOrWhiteSpace(editorId)) return "Provide an editorId for the new record.";

        var fk = NextFreeFormKey(mod);
        if (fk == null)
            return $"'{plugin}' is a light plugin (ESL) and its FormID range (0x800-0xFFF) is full. " +
                   $"Save it as a .esp (FormID restriction lifted) to add more records.";

        var rec = AddNewBySig(mod, sig, editorId, fk.Value);
        if (rec == null)
            return $"Record type '{sig}' is not supported for creation yet. Supported: " + SupportedTypes;

        MutagenLoader.InvalidateModIndex(plugin);
        NotifyChanged(plugin);
        return $"Created {sig} '{editorId}' [{rec.FormKey}] in {plugin}. Set fields with set_field, then save_plugin.";
    }

    public static string SetField(string plugin, string recordId, string field, string value, object? env)
    {
        var mod = EnsureOpen(plugin, env, out var openMsg);
        if (mod == null) return openMsg;

        var rec = FindMutableRecord(mod, recordId);
        if (rec == null) return ToolError.Fail($"Record '{recordId}' not found in {plugin}.");

        if (TrySet(rec, field, value, env, out var msg))
        {
            MutagenLoader.InvalidateModIndex(plugin);
            NotifyChanged(plugin);
            return $"Set {field} = '{value}' on {recordId} in {plugin}.";
        }
        return msg;
    }

    // Return the plugin as a mutable mod, auto-opening an already-loaded (read-only) plugin
    // from disk if needed. Created plugins are already mutable. This lets the AI edit an
    // existing plugin without a separate open_plugin step.
    private static IFallout4Mod? EnsureOpen(string plugin, object? env, out string msg)
    {
        msg = "";
        var (name, _) = NormalizePlugin(plugin);
        var mod = GetMutable(name);
        if (mod != null) return mod;

        var openResult = OpenPlugin(plugin, env);   // normalizes + loads mutable, or explains why not
        mod = GetMutable(name);
        if (mod == null) msg = openResult;
        return mod;
    }

    public static string AddListItem(string plugin, string recordId, string field, string value, object? env)
    {
        var mod = EnsureOpen(plugin, env, out var openMsg);
        if (mod == null) return openMsg;

        var rec = FindMutableRecord(mod, recordId);
        if (rec == null) return ToolError.Fail($"Record '{recordId}' not found in {plugin}.");

        if (!AddListItemToRecord(rec, field, value, env, out var msg)) return msg;
        MutagenLoader.InvalidateModIndex(plugin);
        NotifyChanged(plugin);
        return $"Added {value} to {field} on {recordId} in {plugin} ({msg}).";
    }

    // Record-level core of AddListItem: append a FormLink to a list field on an already-resolved
    // record. No index invalidation / change notification -- the caller batches those. Returns false
    // with a reason in 'msg' on failure, or true with a short success note (e.g. "now 4 item(s)").
    private static bool AddListItemToRecord(object rec, string field, string value, object? env, out string msg)
    {
        var prop = rec.GetType().GetProperty(field,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (prop == null) { msg = $"No field '{field}' on {rec.GetType().Name}."; return false; }

        // The list may be null on a freshly created record -- initialise it.
        var listObj = prop.CanRead ? prop.GetValue(rec) : null;
        if (listObj == null)
        {
            if (!prop.CanWrite) { msg = $"Field '{field}' is an uninitialised list and can't be created."; return false; }
            var listType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            try { listObj = System.Activator.CreateInstance(listType); } catch { listObj = null; }
            if (listObj == null) { msg = $"Could not initialise list '{field}'."; return false; }
            prop.SetValue(rec, listObj);
        }
        if (listObj is not System.Collections.IList list) { msg = $"Field '{field}' is not a list."; return false; }

        // Find the FormLink target type of the list elements (e.g. IKeywordGetter).
        var elemType = prop.PropertyType.IsGenericType ? prop.PropertyType.GetGenericArguments().FirstOrDefault() : null;
        Type? linkTarget = null;
        if (elemType != null && elemType.IsGenericType)
        {
            var gtd = elemType.GetGenericTypeDefinition();
            if (gtd == typeof(IFormLink<>) || gtd == typeof(IFormLinkGetter<>) ||
                gtd == typeof(IFormLinkNullable<>) || gtd == typeof(IFormLinkNullableGetter<>))
                linkTarget = elemType.GetGenericArguments()[0];
        }
        if (linkTarget == null)
        {
            msg = $"Field '{field}' is a list of {elemType?.Name ?? "?"}; add_list_item supports FormLink lists " +
                  $"(e.g. Keywords, FormList Items) for now.";
            return false;
        }

        if (!FormKey.TryFactory(value, out var fk))
        {
            fk = MutagenLoader.ResolveEditorIdToFormKey(env, value);
            if (fk.IsNull) { msg = $"'{value}' is not a FormKey and no loaded record has that EditorID."; return false; }
        }

        try
        {
            var link = System.Activator.CreateInstance(typeof(FormLink<>).MakeGenericType(linkTarget), fk);
            list.Add(link);
            msg = $"now {list.Count} item(s)";
            return true;
        }
        catch (Exception ex)
        {
            msg = $"Could not add to {field}: {ex.Message}";
            return false;
        }
    }

    // ---- FormID compaction to ESL range -----------------------------------------------

    public static string CompactToEsl(string plugin, object? env)
    {
        var mod = EnsureOpen(plugin, env, out var msg); if (mod == null) return msg;

        // Native records whose object IDs fall outside the ESL range 0x800-0xFFF.
        var native = mod.EnumerateMajorRecords().Where(r => r.FormKey.ModKey == mod.ModKey).ToList();
        var inRange = new HashSet<uint>(native.Select(r => r.FormKey.ID).Where(id => id is >= 0x800 and <= 0xFFF));

        var remap = new Dictionary<FormKey, FormKey>();
        uint next = 0x800;
        foreach (var r in native.Where(r => r.FormKey.ID > 0xFFF).OrderBy(r => r.FormKey.ID))
        {
            while (inRange.Contains(next)) next++;
            if (next > 0xFFF)
                return $"Cannot compact '{plugin}': it has more than {0xFFF - 0x800 + 1} records, which won't fit the ESL range.";
            remap[r.FormKey] = new FormKey(mod.ModKey, next);
            inRange.Add(next);
            next++;
        }

        if (remap.Count == 0)
            return $"'{plugin}' is already ESL-compatible (all native FormIDs within 0x800-0xFFF).";

        if (!TryRekeyRecords(mod, remap, "compact_to_esl", plugin,
                "Compact in xEdit instead, or keep cell-placed records within 0x800-0xFFF when authoring them.",
                out var rekeyError))
            return rekeyError;

        MutagenLoader.InvalidateModIndex(plugin); NotifyChanged(plugin);
        return $"Compacted {plugin}: remapped {remap.Count} record(s) into the ESL range (0x800-0xFFF) and fixed " +
               $"all references. Now save_plugin to a path ending in .esl (and the plugin can be a light master). " +
               $"This is in memory until you save.";
    }

    /// <summary>
    /// Read-only precursor to CompactToEsl: reports whether a plugin would fit the ESL/Small range
    /// and what compaction would need to do, without mutating anything. Runs the same record-count
    /// arithmetic as CompactToEsl (lines above) plus the reachability check CompactToEsl only
    /// discovers mid-mutation via TryRekeyRecords, so a caller can rule out both failure modes before
    /// deciding to run the destructive operation.
    /// </summary>
    public static string CheckEslEligibility(string plugin, object? env)
    {
        var mod = EnsureOpen(plugin, env, out var msg); if (mod == null) return msg;

        var native = mod.EnumerateMajorRecords().Where(r => r.FormKey.ModKey == mod.ModKey).ToList();
        var inRange = new HashSet<uint>(native.Select(r => r.FormKey.ID).Where(id => id is >= 0x800 and <= 0xFFF));
        var outOfRange = native.Where(r => r.FormKey.ID > 0xFFF).ToList();

        if (outOfRange.Count == 0)
            return $"'{plugin}' is already ESL-compatible: all {native.Count} native record(s) sit within 0x800-0xFFF. " +
                   "compact_to_esl would be a no-op; save_plugin to a path ending in .esl directly.";

        var capacity = 0xFFF - 0x800 + 1;
        if (native.Count > capacity)
            return $"'{plugin}' CANNOT fit the ESL range: {native.Count} native record(s) exceed the " +
                   $"{capacity}-slot capacity (0x800-0xFFF). compact_to_esl would refuse this plugin.";

        // Same reachability check TryRekeyRecords performs, run here read-only against every
        // out-of-range record instead of just the ones a specific remap dict happens to touch.
        var topLevelGroups = mod.GetType().GetProperties()
            .Where(p => typeof(Mutagen.Bethesda.Plugins.Records.IGroup).IsAssignableFrom(p.PropertyType))
            .Select(p => p.GetValue(mod) as Mutagen.Bethesda.Plugins.Records.IGroup)
            .Where(g => g != null).Select(g => g!).ToList();
        var reachable = new HashSet<FormKey>(topLevelGroups
            .SelectMany(g => ((System.Collections.IEnumerable)g)
                .Cast<Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter>())
            .Select(r => r.FormKey));
        var unreachable = outOfRange.Where(r => !reachable.Contains(r.FormKey)).ToList();
        if (unreachable.Count > 0)
        {
            var byType = string.Join(", ", unreachable.GroupBy(r => r.GetType().Name).Select(g => $"{g.Key} x{g.Count()}"));
            return $"'{plugin}' would BLOCK on compact_to_esl: {unreachable.Count} out-of-range record(s) live in " +
                   $"nested groups this tool cannot re-key [{byType}] (cells / placed objects / worldspace sub-cells). " +
                   $"{outOfRange.Count - unreachable.Count} other out-of-range record(s) are otherwise fine. " +
                   "Keep cell-placed records within 0x800-0xFFF when authoring them, or compact in xEdit instead.";
        }

        return $"'{plugin}' is ESL-ELIGIBLE: {outOfRange.Count} of {native.Count} native record(s) sit outside " +
               $"0x800-0xFFF and would be remapped by compact_to_esl (dry, no failure modes detected). " +
               "Run compact_to_esl to actually perform the remap.";
    }

    /// <summary>
    /// Give a set of the plugin's own records new FormKeys and repoint every reference onto them.
    /// Shared by compact_to_esl and renumber_plugin_formids, which differ only in how they choose
    /// the new ids.
    ///
    /// Two failure modes are checked in a specific order, and both matter:
    /// - A record that no top-level group can reach (cells / placed objects / worldspace sub-cells,
    ///   because Cells is a Fallout4ListGroup&lt;CellBlock&gt; and does not implement IGroup) is
    ///   refused BEFORE anything is mutated, or RemapLinks would repoint references onto FormKeys
    ///   that no record owns.
    /// - A duplicate that could not have its original removed aborts BEFORE RemapLinks, because the
    ///   plugin then holds duplicate FormKeys and repointing would scatter references across them.
    /// </summary>
    private static bool TryRekeyRecords(IFallout4Mod mod, Dictionary<FormKey, FormKey> remap,
        string opName, string plugin, string refusalHint, out string error)
    {
        var topLevelGroups = mod.GetType().GetProperties()
            .Where(p => typeof(Mutagen.Bethesda.Plugins.Records.IGroup).IsAssignableFrom(p.PropertyType))
            .Select(p => p.GetValue(mod) as Mutagen.Bethesda.Plugins.Records.IGroup)
            .Where(g => g != null).Select(g => g!).ToList();

        var reachable = new HashSet<FormKey>(topLevelGroups
            .SelectMany(g => ((System.Collections.IEnumerable)g)
                .Cast<Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter>())
            .Select(r => r.FormKey));
        var unreachable = remap.Keys.Where(k => !reachable.Contains(k)).ToList();
        if (unreachable.Count > 0)
        {
            var unreachableSet = new HashSet<FormKey>(unreachable);
            var byType = string.Join(", ", mod.EnumerateMajorRecords()
                .Where(r => unreachableSet.Contains(r.FormKey))
                .GroupBy(r => r.GetType().Name).Select(g => $"{g.Key} x{g.Count()}"));
            error = ToolError.Fail(
                $"{opName} cannot renumber '{plugin}': {unreachable.Count} record(s) live in nested groups " +
                $"(cells / placed objects / worldspace sub-cells) that this tool cannot re-key [{byType}]. " +
                "Nothing was modified -- references were NOT repointed, so the plugin is unchanged. " + refusalHint);
            return false;
        }

        var rekeyFailures = new List<string>();
        foreach (var group in topLevelGroups)
        {
            var toRekey = ((System.Collections.IEnumerable)group).Cast<Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter>()
                .Where(r => remap.ContainsKey(r.FormKey)).ToList();
            foreach (var rg in toRekey)
            {
                group.DuplicateInAsNewUntypedRecord((Mutagen.Bethesda.Plugins.Records.IMajorRecord)rg, remap[rg.FormKey]);
                if (!TryRemoveFromGroup(group, rg.FormKey, out var removeErr)) rekeyFailures.Add(removeErr);
            }
        }

        if (rekeyFailures.Count > 0)
        {
            error = ToolError.Fail(
                $"{opName} FAILED on '{plugin}': {rekeyFailures.Count} record(s) were duplicated but the " +
                "original could not be removed, so the plugin now holds duplicate FormKeys in memory. " +
                "It was NOT saved and references were NOT repointed -- run reload_plugin to discard these " +
                "in-memory changes before doing anything else.\n  " +
                string.Join("\n  ", rekeyFailures.Take(10)));
            return false;
        }

        mod.RemapLinks(remap);
        error = "";
        return true;
    }

    /// <summary>
    /// Set or clear the header's ESL ("light master" / Small) bit directly. compact_to_esl only
    /// renumbers FormIDs into the 0x800-0xFFF range and tells the caller to save with a .esl path;
    /// it never touches this bit. Whether a plugin actually behaves as light comes from this header
    /// flag (what xEdit's "ESL flag" checkbox sets), not from the file extension or the FormID range
    /// by itself -- a .esp with the bit set is just as light as a renamed .esl. In memory until
    /// save_plugin.
    /// </summary>
    public static string SetLightFlag(string plugin, bool light, object? env)
    {
        var mod = EnsureOpen(plugin, env, out var msg); if (mod == null) return msg;
        var (name, _) = NormalizePlugin(plugin);

        bool wasLight = mod.ModHeader.Flags.HasFlag(Fallout4ModHeader.HeaderFlag.Small);
        if (light == wasLight)
            return $"'{name}' already {(light ? "has" : "does not have")} the ESL (Small) flag set.";

        if (light)
        {
            // Setting the bit does not renumber anything -- warn rather than silently ship a plugin
            // that claims to be light while still holding FormIDs the light range can't represent.
            var outOfRange = mod.EnumerateMajorRecords()
                .Count(r => r.FormKey.ModKey == mod.ModKey && r.FormKey.ID > 0xFFF);
            mod.ModHeader.Flags |= Fallout4ModHeader.HeaderFlag.Small;
            MutagenLoader.InvalidateModIndex(name); NotifyChanged(name);
            var warn = outOfRange > 0
                ? $" WARNING: {outOfRange} native record(s) still have a FormID above 0xFFF, so this plugin " +
                  "is NOT actually ESL-safe yet. Call compact_to_esl first, or the light-master FormID " +
                  "encoding will truncate those records' high bytes at runtime."
                : "";
            return $"Set the ESL (Small) flag on '{name}'.{warn} In memory until save_plugin.";
        }

        mod.ModHeader.Flags &= ~Fallout4ModHeader.HeaderFlag.Small;
        MutagenLoader.InvalidateModIndex(name); NotifyChanged(name);
        return $"Cleared the ESL (Small) flag on '{name}'. In memory until save_plugin.";
    }

    /// <summary>
    /// Set or clear a plugin's header Localized flag -- the bit that makes Mutagen automatically read/
    /// write this plugin's translated text through Strings\&lt;plugin&gt;_&lt;lang&gt;.STRINGS/.DLSTRINGS/
    /// .ILSTRINGS instead of storing it inline (Fallout4Mod_Generated.cs's CreateFromBinary and its write
    /// path both key off this exact flag; see the #56 investigation for the full trace). Same shape as
    /// SetLightFlag above, minus the FormID-range warning: there is no numeric precondition here, but
    /// setting the flag with no Strings\ folder next to the plugin means every TranslatedString field
    /// will read back empty until one exists, so that case is warned rather than silently shipped.
    /// </summary>
    public static string SetLocalizedFlag(string plugin, bool localized, object? env)
    {
        var mod = EnsureOpen(plugin, env, out var msg); if (mod == null) return msg;
        var (name, path) = NormalizePlugin(plugin);

        bool wasLocalized = mod.ModHeader.Flags.HasFlag(Fallout4ModHeader.HeaderFlag.Localized);
        if (localized == wasLocalized)
            return $"'{name}' already {(localized ? "has" : "does not have")} the Localized flag set.";

        if (localized)
        {
            mod.ModHeader.Flags |= Fallout4ModHeader.HeaderFlag.Localized;
            MutagenLoader.InvalidateModIndex(name); NotifyChanged(name);

            string? pluginDir = null;
            try { pluginDir = System.IO.Path.GetDirectoryName(path ?? FindPluginPath(name, env)); } catch { }
            bool hasStringsFolder = pluginDir != null &&
                System.IO.Directory.Exists(System.IO.Path.Combine(pluginDir, "Strings"));
            var warn = hasStringsFolder ? "" :
                $" WARNING: no Strings\\ folder found next to '{name}' yet -- every TranslatedString field " +
                "will read back empty until save_plugin writes one (or you add it manually before reloading).";
            return $"Set the Localized flag on '{name}'.{warn} In memory until save_plugin.";
        }

        mod.ModHeader.Flags &= ~Fallout4ModHeader.HeaderFlag.Localized;
        MutagenLoader.InvalidateModIndex(name); NotifyChanged(name);
        return $"Cleared the Localized flag on '{name}': translated text now stores inline instead of via " +
               "Strings files. In memory until save_plugin.";
    }

    /// <summary>
    /// Change one record's FormID within its plugin (xEdit "Change FormID") and (by default) repoint
    /// every reference to it that lives in the SAME plugin. References in OTHER plugins are not
    /// rewritten (that needs a patch); the message notes they may exist so the caller / AI can fix them.
    /// In memory until save_plugin. newIdHex is the 6-digit object id, e.g. "000F99" or "0xF99".
    ///
    /// <paramref name="repointRefs"/> MUST be false when resolving a DUPLICATE FormKey (two records
    /// illegally sharing one id): references to the shared id are ambiguous, so repointing them would
    /// drag the OTHER record's referrers onto the moved record. With it false, the moved record simply
    /// takes a fresh id and existing references stay pointing at the id (now owned solely by the twin).
    /// The record is targeted by exact type+identity so the correct twin is moved, not the first match.
    /// </summary>
    public static string RenumberFormId(string plugin, string recordId, string newIdHex, object? env,
        bool repointRefs = true)
    {
        var mod = EnsureOpen(plugin, env, out var msg); if (mod == null) return msg;
        var rec = FindMutableRecord(mod, recordId);
        if (rec == null) return ToolError.Fail($"Record '{recordId}' not found in {plugin}.");

        var hex = (newIdHex ?? "").Trim();
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex[2..];
        if (!uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, Inv, out var newId))
            return $"'{newIdHex}' is not a valid hex FormID (use the 6-digit object id, e.g. '000F99').";
        if (newId == 0) return "FormID 000000 is the null id and can't be assigned.";

        var oldFk = rec.FormKey;
        var newFk = new FormKey(mod.ModKey, newId);
        if (newFk == oldFk) return "The new FormID is the same as the current one.";
        if (mod.EnumerateMajorRecords().Any(r => r.FormKey == newFk))
            return $"FormID {newId:X6} is already used by another record in {plugin}. Pick a free id.";

        // Re-key the SPECIFIC record we resolved (match by FormKey AND record type, so when two records
        // illegally share oldFk we move the intended twin -- not whichever group enumerates first).
        bool rekeyed = false;
        foreach (var groupProp in mod.GetType().GetProperties()
                     .Where(p => typeof(Mutagen.Bethesda.Plugins.Records.IGroup).IsAssignableFrom(p.PropertyType)))
        {
            if (groupProp.GetValue(mod) is not Mutagen.Bethesda.Plugins.Records.IGroup group) continue;
            var match = ((System.Collections.IEnumerable)group)
                .Cast<Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter>()
                .FirstOrDefault(r => r.FormKey == oldFk && r.GetType() == rec.GetType());
            if (match == null) continue;
            group.DuplicateInAsNewUntypedRecord((Mutagen.Bethesda.Plugins.Records.IMajorRecord)match, newFk);
            // The duplicate already exists; a silent no-op here would leave both records.
            if (!TryRemoveFromGroup(group, oldFk, out var removeErr))
                return ToolError.Fail(
                    $"renumber_formid FAILED on '{plugin}': {recordId} was duplicated to {newFk} but the original " +
                    $"could not be removed, so the plugin now holds both in memory. It was NOT saved -- run " +
                    $"reload_plugin to discard this change. Reason: {removeErr}");
            rekeyed = true;
            break;
        }
        if (!rekeyed)
            return ToolError.Fail($"Could not locate the group holding {oldFk} ({rec.GetType().Name}) in {plugin}.");

        // Repoint in-plugin references to the new id -- UNLESS we're splitting a duplicate FormKey, in
        // which case the shared references belong to the twin and must stay on the old id.
        if (repointRefs)
            mod.RemapLinks(new Dictionary<FormKey, FormKey> { [oldFk] = newFk });
        MutagenLoader.InvalidateModIndex(plugin);
        NotifyChanged(plugin);

        var refNote = repointRefs
            ? $"and fixed in-plugin references. NOTE: references to {oldFk} in OTHER plugins are NOT updated. " +
              "Check 'Referenced By' (or ask the AI to find and patch them)."
            : $"WITHOUT repointing references (duplicate-FormKey split): every existing reference to {oldFk} " +
              "stays pointing at the record that keeps that id. Verify that was the intent.";
        return $"Renumbered {rec.EditorID} ({rec.GetType().Name}) from {oldFk} to {newFk} in {plugin} {refNote} " +
               "save_plugin to persist.";
    }

    // ---- cleaning (xEdit-style UDR) ----------------------------------------------------

    // FO4 "Initially Disabled" major-record flag.
    private const int InitiallyDisabledFlag = 0x0000_0800;

    public static string CleanPlugin(string plugin, object? env)
    {
        var mod = EnsureOpen(plugin, env, out var msg); if (mod == null) return msg;

        int undeleted = 0, skipped = 0;
        foreach (var recG in mod.EnumerateMajorRecords().ToList())
        {
            if (!recG.IsDeleted) continue;
            if (recG is IFallout4MajorRecord rec)
            {
                // Undelete and Disable Reference: clear the deleted flag, mark initially
                // disabled so the engine treats it as a hidden ref instead of a hard delete.
                rec.IsDeleted = false;
                try { rec.MajorRecordFlagsRaw |= InitiallyDisabledFlag; } catch { }
                undeleted++;
            }
            else skipped++;
        }

        if (undeleted > 0) { MutagenLoader.InvalidateModIndex(plugin); NotifyChanged(plugin); }
        return $"Cleaned {plugin}: undeleted + disabled {undeleted} record(s)" +
               (skipped > 0 ? $"; {skipped} deleted record(s) could not be processed" : "") +
               ". Call save_plugin to persist.";
    }

    // ---- VMAD scripts (e.g. wire a holotape to its terminal program) -------------------

    public static string AttachScript(string plugin, string recordId, string scriptName, object? env)
    {
        var mod = EnsureOpen(plugin, env, out var msg); if (mod == null) return msg;
        var rec = FindMutableRecord(mod, recordId); if (rec == null) return ToolError.Fail($"Record '{recordId}' not found in {plugin}.");

        var vmad = GetOrCreateVmad(rec, out var vmadMsg); if (vmad == null) return vmadMsg;
        var scripts = (System.Collections.IList)vmad.GetType().GetProperty("Scripts")!.GetValue(vmad)!;
        foreach (var s in scripts)
            if (string.Equals((string?)s!.GetType().GetProperty("Name")!.GetValue(s), scriptName, StringComparison.OrdinalIgnoreCase))
                return $"Script '{scriptName}' is already attached to {recordId}.";

        scripts.Add(new ScriptEntry { Name = scriptName });
        MutagenLoader.InvalidateModIndex(plugin); NotifyChanged(plugin);
        return $"Attached script '{scriptName}' to {recordId} in {plugin}. Configure it with set_script_property.";
    }

    public static string SetScriptProperty(string plugin, string recordId, string scriptName,
        string propName, string value, string? type, object? env)
    {
        var mod = EnsureOpen(plugin, env, out var msg); if (mod == null) return msg;
        var rec = FindMutableRecord(mod, recordId); if (rec == null) return ToolError.Fail($"Record '{recordId}' not found in {plugin}.");

        var vmad = rec.GetType().GetProperty("VirtualMachineAdapter")?.GetValue(rec);
        if (vmad == null) return $"{recordId} has no scripts. Call attach_script first.";
        var scripts = (System.Collections.IList)vmad.GetType().GetProperty("Scripts")!.GetValue(vmad)!;

        object? script = null;
        foreach (var s in scripts)
            if (string.Equals((string?)s!.GetType().GetProperty("Name")!.GetValue(s), scriptName, StringComparison.OrdinalIgnoreCase))
            { script = s; break; }
        if (script == null) return $"Script '{scriptName}' is not attached to {recordId}. Call attach_script first.";

        var props = (System.Collections.IList)script.GetType().GetProperty("Properties")!.GetValue(script)!;
        for (int i = props.Count - 1; i >= 0; i--)
            if (string.Equals((string?)props[i]!.GetType().GetProperty("Name")!.GetValue(props[i]), propName, StringComparison.OrdinalIgnoreCase))
                props.RemoveAt(i);

        var kind = (type ?? InferScriptKind(value, env)).ToLowerInvariant();
        object scriptProp;
        try
        {
            switch (kind)
            {
                case "object": case "form": case "formlink":
                {
                    var op = new ScriptObjectProperty { Name = propName };
                    var link = op.GetType().GetProperty("Object")!.GetValue(op)!;
                    var setTo = link.GetType().GetMethod("SetTo", new[] { typeof(FormKey) })!;
                    // Explicit null. Without this, "none" is not a parseable FormKey, falls through to
                    // the EditorID resolver, and ends up written as a DANGLING 0x00FFFFFF form -- a
                    // broken reference that still reported success. Clearing a property has to be a
                    // real null FormLink.
                    if (string.IsNullOrWhiteSpace(value) ||
                        value.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                        value.Equals("null", StringComparison.OrdinalIgnoreCase))
                    {
                        setTo.Invoke(link, new object[] { FormKey.Null });
                        scriptProp = op;
                        break;
                    }
                    if (!FormKey.TryFactory(value, out var fk))
                    {
                        fk = MutagenLoader.ResolveEditorIdToFormKey(env, value);
                        if (fk.IsNull) return $"'{value}' is not a FormKey and no loaded record has that EditorID.";
                    }
                    setTo.Invoke(link, new object[] { fk });
                    scriptProp = op;
                    break;
                }
                case "int":    scriptProp = new ScriptIntProperty    { Name = propName, Data = int.Parse(value, Inv) };   break;
                case "float":  scriptProp = new ScriptFloatProperty  { Name = propName, Data = float.Parse(value, Inv) }; break;
                case "bool":   scriptProp = new ScriptBoolProperty   { Name = propName, Data = bool.Parse(value) };  break;
                default:       scriptProp = new ScriptStringProperty { Name = propName, Data = value };              break;
            }
        }
        catch (Exception ex) { return $"Could not set script property: {ex.Message}"; }

        props.Add(scriptProp);
        MutagenLoader.InvalidateModIndex(plugin); NotifyChanged(plugin);
        return $"Set script property '{propName}' = '{value}' ({kind}) on script '{scriptName}' of {recordId}.";
    }

    private static object? GetOrCreateVmad(object rec, out string msg)
    {
        msg = "";
        var prop = rec.GetType().GetProperty("VirtualMachineAdapter");
        if (prop == null) { msg = $"{rec.GetType().Name} does not support scripts."; return null; }
        var vmad = prop.GetValue(rec);
        if (vmad == null)
        {
            var vt = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            try { vmad = System.Activator.CreateInstance(vt); } catch { vmad = null; }
            if (vmad == null) { msg = "Could not initialise the script adapter."; return null; }
            vt.GetProperty("Version")?.SetValue(vmad, (short)6);    // FO4 VMAD version/format
            vt.GetProperty("ObjectFormat")?.SetValue(vmad, (ushort)2);
            prop.SetValue(rec, vmad);
        }
        return vmad;
    }

    private static string InferScriptKind(string value, object? env)
    {
        if (FormKey.TryFactory(value, out _)) return "object";
        if (!MutagenLoader.ResolveEditorIdToFormKey(env, value).IsNull) return "object";
        if (bool.TryParse(value, out _)) return "bool";
        if (int.TryParse(value, out _)) return "int";
        if (float.TryParse(value, out _)) return "float";
        return "string";
    }

    /// <summary>
    /// Copy the plugin's original on-disk file to a timestamped .bak file beside it.
    /// Safe to call multiple times; each call produces a uniquely named backup.
    /// </summary>
    public static string BackupPlugin(string plugin)
    {
        var (name, _) = NormalizePlugin(plugin);
        if (!_sourcePath.TryGetValue(name, out var sourcePath) || string.IsNullOrWhiteSpace(sourcePath))
            return $"'{name}' has no on-disk source path -- it may be a newly created plugin that hasn't been saved yet. No backup needed.";
        if (!File.Exists(sourcePath))
            return $"Source file not found on disk at '{sourcePath}'; cannot back it up.";
        var ts = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var bakPath = sourcePath + $".{ts}.bak";
        try
        {
            File.Copy(sourcePath, bakPath, overwrite: false);
            return $"Backup created: {bakPath}";
        }
        catch (Exception ex)
        {
            return $"Backup failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Writes a plugin to disk. <paramref name="env"/> is REQUIRED -- do not give it a default.
    /// It is what orders the MAST table by real load order; without it Mutagen emits its own order,
    /// which can put a dependent ESM before its dependency and hang the game on load with no crash
    /// log. It carried a `= null` default for a long time and callers kept quietly omitting it
    /// (five inside this file, then three more in the GUI). Pass null explicitly if you genuinely
    /// have no environment -- that at least shows up in review.
    /// </summary>
    /// <summary>The success message for a completed save, shared by the overlay-swap and direct
    /// write paths so both report identically.</summary>
    private static string DescribeSave(string name, string path, string? orderingUnavailable)
    {
        var inOverwrite = !string.IsNullOrWhiteSpace(Mo2OverwriteFolder) &&
            path.StartsWith(Mo2OverwriteFolder!, StringComparison.OrdinalIgnoreCase);
        var baseMsg = inOverwrite
            ? $"Saved {name} to the MO2 overwrite folder ({path}). MO2 loads it automatically at top " +
              "priority -- just enable it in the plugin list if it isn't already checked, and place it " +
              "after the plugins it patches."
            : $"Saved {name} to {path}.";

        // Only matters with 2+ masters: a single-master plugin has nothing to mis-order. Warn
        // rather than fail, because the write did succeed and may well be fine.
        if (orderingUnavailable != null && ReadMasterNames(path).Count > 1)
            baseMsg += $" WARNING: master order was NOT set from the load order ({orderingUnavailable}), " +
                       "so the MAST table is in Mutagen's own order. A multi-master plugin written this way " +
                       "can list a dependent ESM before its dependency, which makes the game hang on load " +
                       "with no crash log. Verify the master order, or re-save with the environment loaded.";
        return baseMsg;
    }

    public static string SavePlugin(string plugin, string? path, object? env)
    {
        var (name, explicitPath) = NormalizePlugin(plugin);
        if (ProtectedPlugins.IsProtected(name)) return ToolError.Fail(ProtectedPlugins.RefusalMessage(name));

        var mod = GetMutable(name);
        if (mod == null) return ToolError.Fail($"Plugin '{name}' is not open for editing.");

        // Default: overwrite the original file the plugin was opened from (modify in place).
        // Newly created plugins (no source) fall back to the configured Output folder.
        // New patches (no source file) save into the MO2 overwrite folder when a modlist is loaded, so
        // MO2 picks them up automatically; plugins opened from disk overwrite their original file.
        if (string.IsNullOrWhiteSpace(path))
            path = explicitPath ?? (_sourcePath.TryGetValue(name, out var sp) ? sp : Path.Combine(NewPatchDir, name));

        // The destination is unvalidated user/agent input, and below we create its parent directory
        // and then File.Replace/File.Move over whatever is there. Check extension and vanilla-master
        // name on the NORMALIZED path before any of that happens.
        var pathProblem = ProtectedPlugins.ValidateSavePath(path);
        if (pathProblem != null) return ToolError.Fail(pathProblem);

        // Order the MAST entries by the real load order. Without this Mutagen emits them in whatever
        // order it built the set, which can put a dependent ESM before the one it depends on (observed:
        // DLCCoast.esm written BEFORE Fallout4.esm). The game does not error on that - it HANGS on load
        // forever with no crash log. This is the cause of the long-standing "save_plugin scrambles
        // master order" freeze.
        var loOrdering = TryGetLoadOrderOrdering(env, out var orderingUnavailable);

        var prms = new BinaryWriteParameters
        {
            // Build the master list from the records' FormLinks (self-contained, no load order needed).
            MastersListContent = MastersListContentOption.Iterate,
            // We write to a temp file whose name (Plugin.esp.xxxx.tmp) is not a valid ModKey; the mod
            // already carries the correct ModKey, so skip Mutagen's filename<->ModKey check.
            ModKey = ModKeyOption.NoCheck,
            MastersListOrdering = loOrdering,
        };
        try { Directory.CreateDirectory(Path.GetDirectoryName(path)!); } catch { }

        // FunctionConditionData's generated FormLink metadata now includes custom ActorValue
        // parameters, and the local Mutagen writer serializes Form-category parameters directly from
        // ParameterOneRecord. Keep Iterate as the single source of truth for both MAST discovery and
        // FormID remapping instead of maintaining a second hand-written integer encoder.
        var writeParams = prms;

        // Write to a temp file first, then swap it in. A direct in-place write fails when the file is
        // memory-mapped -- every plugin loaded via 'Open MO2' is an overlay the editor itself locks,
        // so we never write straight over the live file.
        var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try { mod.WriteToBinary(tmp, writeParams); }
        catch (Exception ex) { try { File.Delete(tmp); } catch { } return ToolError.Fail($"Save failed while writing: {ex.Message}"); }

        try { EnsureFallout4MasterWritten(mod, tmp, loOrdering); }
        catch (Exception ex) { try { File.Delete(tmp); } catch { } return ToolError.Fail($"Save failed while enforcing the Fallout4.esm master: {ex.Message}"); }

        // Every plugin in an MO2 load order is an mmap overlay, so the target is normally held open by
        // this editor and a plain File.Replace would fail. Release just this plugin's overlay, swap,
        // and reopen it. Falls through to the direct write below when there is no environment holding
        // it (a brand new patch, a plugin outside the load order, or no modlist loaded at all).
        if (Mo2ProfileLoader.TryReplaceLoadedPluginFile(env, name, tmp, path, out var swapError))
        {
            DuplicateFormIdScanner.Invalidate(path);
            NotifyChanged(name);
            return DescribeSave(name, path, orderingUnavailable);
        }
        _ = swapError;   // not surfaced: the direct path below reports its own, more specific failure

        try
        {
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
            DuplicateFormIdScanner.Invalidate(path);
            NotifyChanged(name);
            return DescribeSave(name, path, orderingUnavailable);
        }
        catch (Exception)
        {
            // The target is locked. This is almost always THIS editor's own overlay from 'Open MO2'
            // (not MO2 and not xEdit). Keep the freshly-written copy beside it so no work is lost.
            var sideways = path + ".new";
            try { if (File.Exists(sideways)) File.Delete(sideways); File.Move(tmp, sideways); }
            catch { sideways = tmp; }
            return $"Wrote {name} to '{sideways}', but could NOT overwrite '{path}'. That file is locked by " +
                   $"THIS editor: every plugin loaded via 'Open MO2' is memory-mapped, so the editor holds it " +
                   $"open (closing MO2 will NOT help). To apply it: close this editor and rename " +
                   $"'{Path.GetFileName(sideways)}' to '{Path.GetFileName(path)}', or save the patch under a " +
                   $"NEW plugin name that isn't in the loaded list.";
        }
    }

    /// <summary>
    /// Fallout 4 patch plugins are expected to always declare Fallout4.esm as a master, even when
    /// this save's content happens not to reference anything in it yet (e.g. a freshly created,
    /// still-empty patch) -- so xEdit/FO4Edit and the game's own plugin list always show the base
    /// game dependency a Fallout 4 patch is assumed to have.
    ///
    /// MastersListContentOption.Iterate (the normal, correct master-computation mode SavePlugin
    /// uses) derives the master set PURELY from FormLinks/override-FormKeys actually present in the
    /// record data (see ModHeaderWriteLogic.AddMasterCollectionActions) -- it does not seed from
    /// whatever mod.MasterReferences already says, so mutating that list before the write has no
    /// effect under Iterate; it would just be silently recomputed away. Checking what Iterate would
    /// naturally produce requires re-deriving its exact traversal, which risks drifting out of sync
    /// with Mutagen's own logic -- instead this checks the ACTUAL master list Mutagen just wrote to
    /// disk (ReadMasterNames reads the real TES4 header bytes), and only if Fallout4.esm is
    /// genuinely absent does it rewrite with MastersListContentOption.NoCheck, which DOES honor
    /// mod.MasterReferences verbatim. The rewrite still serializes every FormLink against the new
    /// header, so record bytes and the forced master order stay synchronized.
    /// </summary>
    private static void EnsureFallout4MasterWritten(IFallout4Mod mod, string writtenPath, AMastersListOrderingOption? loOrdering)
    {
        const string fo4 = "Fallout4.esm";
        if (string.Equals(mod.ModKey.FileName.String, fo4, StringComparison.OrdinalIgnoreCase))
            return;   // Fallout4.esm itself is never its own master

        var written = ReadMasterNames(writtenPath);
        if (!written.Any(m => string.Equals(m, fo4, StringComparison.OrdinalIgnoreCase)))
        {
            mod.MasterReferences.Clear();
            mod.MasterReferences.Add(new Mutagen.Bethesda.Plugins.Records.MasterReference
            { Master = ModKey.FromNameAndExtension(fo4) });
            foreach (var m in written)
                mod.MasterReferences.Add(new Mutagen.Bethesda.Plugins.Records.MasterReference
                { Master = ModKey.FromNameAndExtension(m) });

            var forced = new BinaryWriteParameters
            {
                MastersListContent = MastersListContentOption.NoCheck,
                ModKey = ModKeyOption.NoCheck,
                MastersListOrdering = loOrdering,
            };
            mod.WriteToBinary(writtenPath, forced);
            written = ReadMasterNames(writtenPath);
        }

        // Iterate derives the MAST table without mutating the editable model. Keep the
        // session copy synchronized with the exact header bytes that survived ordering and
        // the optional Fallout4.esm rewrite. Follow-up operations such as CreateSeqFile use
        // this file-local master count and must not observe a stale pre-save list.
        mod.MasterReferences.Clear();
        foreach (var master in written)
            mod.MasterReferences.Add(new Mutagen.Bethesda.Plugins.Records.MasterReference
            { Master = ModKey.FromNameAndExtension(master) });
    }

    /// <summary>
    /// Evict a plugin from the editor session and re-open it from its original on-disk path.
    /// Fixes stale in-memory state after a save (e.g. check_plugin reporting false-positive
    /// undeclared-master errors because it sees pre-save session state).
    /// </summary>
    public static string ReloadPlugin(string plugin, object? env)
    {
        var (name, _) = NormalizePlugin(plugin);
        if (!_sourcePath.TryGetValue(name, out var path))
            return $"'{name}' is not open for editing. Use open_plugin first.";
        _mutable.Remove(name);
        _sourcePath.Remove(name);
        MutagenLoader.EditableMods.TryRemove(name, out _);
        // ReleaseLooseMod, not a bare TryRemove: if this plugin was loaded read-only before it was
        // opened for editing, the entry being dropped is a memory-mapped overlay holding the file.
        MutagenLoader.ReleaseLooseMod(name);
        return OpenPlugin(path, env);
    }

    /// <summary>
    /// Load a plugin binary, drop every COBJ condition whose param1/param2/reference FormKey
    /// belongs to one of the stripped masters, then write it back via Mutagen so that the master
    /// list is recomputed from scratch and all remaining FormID high-bytes are correct.
    /// This is the Mutagen-native fix for the ITO master-index corruption.
    /// </summary>
    public static string StripMastersClean(string sourcePath, string[] mastersToStrip, string outputPath, bool dryRun, object? env)
    {
        if (!File.Exists(sourcePath))
            return $"Source file not found: '{sourcePath}'.";
        if (mastersToStrip.Length == 0)
            return "Provide at least one master name to strip.";

        var stripSet = new HashSet<string>(mastersToStrip, StringComparer.OrdinalIgnoreCase);
        IFallout4Mod mod;
        try { mod = Fallout4Mod.CreateFromBinary(ModPath.FromPath(sourcePath), Fallout4Release.Fallout4); }
        catch (Exception ex) { return $"Failed to load '{sourcePath}': {ex.Message}"; }

        int dropped = 0, touched = 0;
        foreach (var cobj in mod.ConstructibleObjects)
        {
            var toRemove = cobj.Conditions.Where(c => ConditionRefersToStrippedMaster(c, stripSet)).ToList();
            if (toRemove.Count == 0) continue;
            foreach (var c in toRemove) cobj.Conditions.Remove(c);
            dropped += toRemove.Count;
            touched++;
        }

        // Iterate rebuilds the master list from the records' FormLinks, so a genuinely unused master
        // does drop on write. Remove the header entries explicitly anyway: it makes the intent
        // explicit and lets us report honestly. Iterate still runs afterwards, so a master something
        // really references is re-added and the call degrades to a no-op instead of writing dangling
        // references. The real trap is the reverse of what it looks like -- when a "stale" master
        // refuses to die, it is because something still references it (check the link report below),
        // NOT because the write failed.
        var presentTargets = mod.ModHeader.MasterReferences
            .Where(m => stripSet.Contains(m.Master.FileName.String))
            .ToList();
        foreach (var m in presentTargets)
            mod.ModHeader.MasterReferences.Remove(m);

        var targetNames = string.Join(", ", presentTargets.Select(m => m.Master.FileName.String));

        // Ask Mutagen itself which records still link to the target masters. Mutagen is the authority:
        // it is what recomputes the master list on write, and it sees FormLinks in places a hand-rolled
        // binary scan misses (MGEF/SPEL data structs, perk entry-point data, etc).
        var linkers = FindRecordsLinkingTo(mod, stripSet);
        var linkReport = linkers.Count == 0
            ? "No record links to the named master(s)."
            : $"{linkers.Count} record(s) still link to them: " +
              string.Join("; ", linkers.Take(15)) + (linkers.Count > 15 ? ", ..." : "");

        var summary = $"Loaded '{Path.GetFileName(sourcePath)}': " +
                      $"{dropped} condition(s) across {touched} record(s) reference the stripped master(s). " +
                      (presentTargets.Count == 0
                          ? "None of the named masters are declared in this plugin's header."
                          : $"Header master(s) to remove: {targetNames}.") +
                      " " + linkReport;
        if (dryRun)
            return $"[DRY RUN] {summary} Pass dry_run=false to write.";

        if (presentTargets.Count == 0 && dropped == 0)
            return $"{summary} Nothing to do; file left untouched.";

        if (string.IsNullOrWhiteSpace(outputPath))
            outputPath = sourcePath;
        try { Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!); } catch { }

        // Order MAST by the real load order, same as SavePlugin. Without this a multi-master result
        // can list a dependent ESM before its dependency (observed: DLCCoast.esm before Fallout4.esm),
        // which hangs the game on load with no crash log.
        var prms = new BinaryWriteParameters
        {
            MastersListContent = MastersListContentOption.Iterate,
            ModKey = ModKeyOption.NoCheck,
            MastersListOrdering = TryGetLoadOrderOrdering(env, out var stripOrderingUnavailable),
        };
        var tmp = outputPath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try { mod.WriteToBinary(tmp, prms); }
        catch (Exception ex) { try { File.Delete(tmp); } catch { } return $"Write failed: {ex.Message}"; }

        // Report what the written file ACTUALLY contains, not what was asked for. A master that
        // survives here is one Iterate re-added because a record still references it.
        var written = ReadMasterNames(tmp);
        var survivors = written.Where(w => stripSet.Contains(w)).ToList();

        try
        {
            if (File.Exists(outputPath)) File.Replace(tmp, outputPath, null);
            else File.Move(tmp, outputPath);
        }
        catch (Exception ex) { return $"Swap failed: {ex.Message} (tmp preserved at '{tmp}')"; }

        var removedCount = presentTargets.Count - survivors.Count;
        var result = $"Saved to '{outputPath}'. Removed {removedCount} of {presentTargets.Count} header master(s); " +
                     $"dropped {dropped} condition(s) across {touched} record(s). " +
                     $"Masters now: [{string.Join(", ", written)}].";
        if (survivors.Count > 0)
            result += $" STILL PRESENT (records reference them, so removal would dangle): {string.Join(", ", survivors)}.";
        if (stripOrderingUnavailable != null && written.Count > 1)
            result += $" WARNING: master order was NOT set from the load order ({stripOrderingUnavailable}); " +
                      "verify the MAST order before loading the game.";
        return result;
    }

    /// <summary>
    /// Report the plugin's declared masters in header order: index, on-disk size (when the master
    /// can be located), and whether anything in THIS plugin actually references it. "Used" comes
    /// from the same FormLink scan SavePlugin's Iterate uses to decide master-list membership, so a
    /// master reported unused here is exactly the kind Iterate silently drops on the next save --
    /// this is the direct-visibility tool that was missing (the only prior view of the master list
    /// was ReadMasterNames, which reads raw bytes off disk and has no "used" signal at all).
    /// </summary>
    public static string ListMasters(string plugin, object? env)
    {
        var mod = EnsureOpen(plugin, env, out var msg); if (mod == null) return msg;
        var (name, _) = NormalizePlugin(plugin);

        var masters = mod.MasterReferences.Select(m => m.Master.FileName.String).ToList();
        if (masters.Count == 0) return $"'{name}' declares no masters.";

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rec in mod.EnumerateMajorRecords())
            foreach (var link in rec.EnumerateFormLinks())
            {
                if (link.FormKey.IsNull) continue;
                used.Add(link.FormKey.ModKey.FileName.String);
            }

        var lines = new List<string>(masters.Count);
        for (int i = 0; i < masters.Count; i++)
        {
            var m = masters[i];
            var path = FindPluginPath(m, env);
            string sizeStr = "size unknown (not found on disk / not in the loaded environment)";
            if (path != null && File.Exists(path))
                try { sizeStr = $"{new FileInfo(path).Length:N0} bytes"; } catch { }
            var usedStr = used.Contains(m) ? "used" : "UNUSED -- save_plugin's Iterate would drop this master";
            lines.Add($"{i}: {m}  ({sizeStr}, {usedStr})");
        }
        return $"Masters of '{name}' ({masters.Count}):\n" + string.Join("\n", lines);
    }

    /// <summary>JSON variant of <see cref="ListMasters"/> for the GUI Masters panel: structured
    /// per-master rows (index/name/size/used) PLUS the plugin's current ESL/Small flag, so the panel
    /// can render its "Light plugin" checkbox from the same call instead of a second round trip.</summary>
    public static string ListMastersJson(string plugin, object? env)
    {
        var mod = EnsureOpen(plugin, env, out var msg);
        if (mod == null) return JsonConvert.SerializeObject(new { error = msg });
        var (name, _) = NormalizePlugin(plugin);

        var masters = mod.MasterReferences.Select(m => m.Master.FileName.String).ToList();

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rec in mod.EnumerateMajorRecords())
            foreach (var link in rec.EnumerateFormLinks())
            {
                if (link.FormKey.IsNull) continue;
                used.Add(link.FormKey.ModKey.FileName.String);
            }

        var rows = new List<object>(masters.Count);
        for (int i = 0; i < masters.Count; i++)
        {
            var m = masters[i];
            var path = FindPluginPath(m, env);
            long? size = null;
            if (path != null && File.Exists(path))
                try { size = new FileInfo(path).Length; } catch { }
            rows.Add(new { index = i, name = m, size, used = used.Contains(m) });
        }

        var light = mod.ModHeader.Flags.HasFlag(Fallout4ModHeader.HeaderFlag.Small);
        return JsonConvert.SerializeObject(new { pluginName = name, masters = rows, light });
    }

    /// <summary>
    /// Set a plugin's declared master order to an EXACT permutation of its current masters and write
    /// it immediately, bypassing save_plugin's automatic load-order-derived ordering. That bypass is
    /// the point, not an oversight: Mutagen's ModHeaderWriteLogic.SortMasters runs unconditionally at
    /// write time, even under MastersListContentOption.NoCheck, so a caller-specified order only
    /// survives the write when MastersListOrdering is ALSO explicitly NoCheck -- otherwise it gets
    /// silently re-sorted back to the load-order (or alphabetical-masters-first) order, which is
    /// exactly right for a normal save but makes reordering here impossible any other way. This is
    /// a manual-repair tool: use it to pin a verified-correct order when the automatic derivation
    /// isn't available or isn't trusted, not as a routine save path. A later save_plugin call
    /// re-derives the order from the live load order again and overwrites this one.
    /// Rejects any 'order' that is not exactly the plugin's current master set (same names, same
    /// count, no duplicates) -- this only reorders, it never adds or drops a master.
    /// </summary>
    public static string ReorderMasters(string plugin, string[] order, object? env)
    {
        var mod = EnsureOpen(plugin, env, out var msg); if (mod == null) return msg;
        var (name, explicitPath) = NormalizePlugin(plugin);
        if (ProtectedPlugins.IsProtected(name)) return ToolError.Fail(ProtectedPlugins.RefusalMessage(name));

        var current = mod.MasterReferences.Select(m => m.Master.FileName.String).ToList();
        if (current.Count == 0) return $"'{name}' has no masters to reorder.";
        if (current.Count == 1) return $"'{name}' has only one master ({current[0]}); nothing to reorder.";

        if (order == null || order.Length == 0)
            return ToolError.Fail($"Provide 'order' as the full permutation of the current masters: [{string.Join(", ", current)}].");

        var dup = order.GroupBy(o => o, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (dup != null) return ToolError.Fail($"reorder_masters: 'order' lists '{dup.Key}' more than once.");

        var currentSet = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
        var orderSet = new HashSet<string>(order, StringComparer.OrdinalIgnoreCase);
        if (order.Length != current.Count || !currentSet.SetEquals(orderSet))
        {
            var missing = current.Where(c => !orderSet.Contains(c)).ToList();
            var extra = order.Where(o => !currentSet.Contains(o)).ToList();
            return ToolError.Fail(
                $"reorder_masters: 'order' must be EXACTLY the plugin's current {current.Count} master(s) " +
                $"[{string.Join(", ", current)}] -- got {order.Length}: [{string.Join(", ", order)}]." +
                (missing.Count > 0 ? $" Missing: [{string.Join(", ", missing)}]." : "") +
                (extra.Count > 0 ? $" Not a declared master: [{string.Join(", ", extra)}]." : ""));
        }

        mod.MasterReferences.Clear();
        foreach (var m in order)
            mod.MasterReferences.Add(new Mutagen.Bethesda.Plugins.Records.MasterReference { Master = m });

        var path = explicitPath ?? (_sourcePath.TryGetValue(name, out var sp) ? sp : Path.Combine(NewPatchDir, name));
        var pathProblem = ProtectedPlugins.ValidateSavePath(path);
        if (pathProblem != null) return ToolError.Fail(pathProblem);

        var prms = new BinaryWriteParameters
        {
            MastersListContent = MastersListContentOption.NoCheck,
            ModKey = ModKeyOption.NoCheck,
            MastersListOrdering = new MastersListOrderingEnumOption { Option = MastersListOrderingOption.NoCheck },
        };
        try { Directory.CreateDirectory(Path.GetDirectoryName(path)!); } catch { }
        var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try { mod.WriteToBinary(tmp, prms); }
        catch (Exception ex) { try { File.Delete(tmp); } catch { } return ToolError.Fail($"reorder_masters write failed: {ex.Message}"); }

        try
        {
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
        catch (Exception)
        {
            var sideways = path + ".new";
            try { if (File.Exists(sideways)) File.Delete(sideways); File.Move(tmp, sideways); }
            catch { sideways = tmp; }
            return $"Reordered masters in memory and wrote '{sideways}', but could NOT overwrite '{path}' " +
                   "(locked by this editor's own 'Open MO2' overlay). Close the editor and rename the file, " +
                   "or save under a new plugin name.";
        }

        NotifyChanged(name);
        var written = ReadMasterNames(path);
        return $"Wrote {name} to '{path}' with masters in exactly this order: [{string.Join(", ", written)}]. " +
               "This bypassed save_plugin's automatic load-order-derived ordering -- only rely on it once " +
               "you've verified the permutation is dependency-correct (each master before anything that " +
               "depends on it). A later save_plugin call will re-derive and overwrite this order.";
    }

    /// <summary>
    /// Remove <paramref name="fk"/> from a top-level group and PROVE it is gone, by re-counting
    /// rather than trusting the call.
    ///
    /// The re-key paths add the replacement record before removing the original, so a removal that
    /// quietly does nothing leaves both -- two records sharing one FormKey, which is a documented
    /// save-breaking condition. These sites previously used
    /// <c>GetMethod("Remove", ...)?.Invoke(...)</c>, where the null-conditional swallowed an
    /// unresolved method and the caller still reported success.
    /// </summary>
    private static bool TryRemoveFromGroup(
        Mutagen.Bethesda.Plugins.Records.IGroup group, FormKey fk, out string error)
    {
        int CountMatching() => ((System.Collections.IEnumerable)group)
            .Cast<Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter>().Count(r => r.FormKey == fk);

        var groupName = group.GetType().Name;
        var before = CountMatching();
        if (before == 0) { error = $"{fk} is not in group '{groupName}'."; return false; }

        var removeM = group.GetType().GetMethod("Remove", new[] { typeof(FormKey) });
        if (removeM == null)
        {
            error = $"Mutagen group '{groupName}' exposes no Remove(FormKey), so {fk} cannot be removed.";
            return false;
        }

        try { removeM.Invoke(group, new object[] { fk }); }
        catch (Exception ex)
        {
            error = $"Removing {fk} from '{groupName}' threw: {ex.InnerException?.Message ?? ex.Message}";
            return false;
        }

        var after = CountMatching();
        if (after >= before)
        {
            error = $"Removing {fk} from '{groupName}' had no effect ({before} -> {after}).";
            return false;
        }

        error = "";
        return true;
    }

    /// <summary>
    /// Build a MAST-ordering option from the live load order. Returns null when no environment is
    /// available, in which case Mutagen falls back to its own ordering -- acceptable for a single-master
    /// plugin, but a multi-master one written that way can list a dependent ESM before its dependency
    /// and hang the game on load. Callers with an env should always pass it.
    /// </summary>
    private static AMastersListOrderingOption? TryGetLoadOrderOrdering(object? env, out string? unavailableReason)
    {
        if (env == null)
        {
            unavailableReason = "no game environment is loaded";
            return null;
        }
        try
        {
            var keys = new List<ModKey>();
            foreach (var l in ((System.Collections.IEnumerable)((dynamic)env).LoadOrder.ListedOrder).Cast<dynamic>())
                keys.Add((ModKey)l.ModKey);
            if (keys.Count == 0)
            {
                unavailableReason = "the loaded environment reports an empty load order";
                return null;
            }
            unavailableReason = null;
            return new MastersListOrderingByLoadOrder(keys);
        }
        catch (Exception ex)
        {
            // Was a bare `catch { return null; }`, which silently degraded to an unordered write --
            // the exact condition that hangs the game on load, reported as a clean save.
            DebugLog.Exception("SavePlugin master ordering", ex);
            unavailableReason = $"reading the load order failed ({ex.Message})";
            return null;
        }
    }

    /// <summary>
    /// Enumerate every record whose FormLinks point at one of the named masters. This is what
    /// decides whether a master can be stripped: if anything links to it, removing it from the
    /// header would leave a dangling reference (Mutagen re-adds it on write anyway).
    /// </summary>
    private static List<string> FindRecordsLinkingTo(IFallout4Mod mod, HashSet<string> stripSet)
    {
        var found = new List<string>();
        foreach (var rec in mod.EnumerateMajorRecords())
        {
            var seen = new HashSet<string>();
            foreach (var link in rec.EnumerateFormLinks())
            {
                if (link.FormKey.IsNull) continue;
                var name = link.FormKey.ModKey.FileName.String;
                if (!stripSet.Contains(name)) continue;
                seen.Add(link.FormKey.ToString());
            }
            if (seen.Count > 0)
                found.Add($"{rec.EditorID ?? rec.FormKey.ToString()} ({rec.GetType().Name}) -> {string.Join(",", seen)}");
        }
        return found;
    }

    /// <summary>Read the MAST entries straight out of a plugin's TES4 header, in order.</summary>
    /// <summary>
    /// Reads the MAST entries straight out of a plugin's TES4 header, in file order. Public because
    /// the raw header is the only trustworthy check of master order after a save -- check_plugin
    /// reports from the in-memory model, not from what was written.
    /// </summary>
    public static List<string> ReadMasterNames(string path)
    {
        var names = new List<string>();
        try
        {
            var d = File.ReadAllBytes(path);
            if (d.Length < 24 || d[0] != 'T' || d[1] != 'E' || d[2] != 'S' || d[3] != '4') return names;
            int size = BitConverter.ToInt32(d, 4);
            int pos = 24, end = 24 + size;
            while (pos + 6 <= end && pos + 6 <= d.Length)
            {
                var sig = System.Text.Encoding.ASCII.GetString(d, pos, 4);
                int len = BitConverter.ToUInt16(d, pos + 4);
                if (sig == "MAST")
                    names.Add(System.Text.Encoding.Latin1.GetString(d, pos + 6, len).TrimEnd('\0'));
                pos += 6 + len;
            }
        }
        catch { }
        return names;
    }

    private static bool ConditionRefersToStrippedMaster(Mutagen.Bethesda.Fallout4.IConditionGetter cond, HashSet<string> stripSet)
    {
        if (cond.Data is not Mutagen.Bethesda.Fallout4.IFunctionConditionDataGetter data) return false;
        if (!data.ParameterOneRecord.FormKey.IsNull && stripSet.Contains(data.ParameterOneRecord.FormKey.ModKey.FileName.String)) return true;
        if (!data.ParameterTwoRecord.FormKey.IsNull && stripSet.Contains(data.ParameterTwoRecord.FormKey.ModKey.FileName.String)) return true;
        if (!data.Reference.FormKey.IsNull && stripSet.Contains(data.Reference.FormKey.ModKey.FileName.String)) return true;
        return false;
    }

    /// <summary>
    /// Resolve a conflict by copying the chosen plugin's version of a record into a patch plugin
    /// as an override. The patch plugin (loading last) then wins the conflict. The patch is opened
    /// or created automatically and held in memory until SavePlugin is called.
    /// </summary>
    /// <summary>The sentinel a caller must recognise to offer "overwrite?". Kept as a prefix so the
    /// existing "starts with Copied" success contract in CopyAsOverrideMany is unaffected.</summary>
    public const string OverwritePrompt = "EXISTS:";

    /// <summary>
    /// Position of a plugin in the load order, or -1 when it is not in it (a patch about to be
    /// created is not, and will load last, which is why -1 is treated as "after everything").
    /// </summary>
    private static int LoadOrderIndexOf(object? env, string pluginName)
    {
        if (env == null || string.IsNullOrWhiteSpace(pluginName)) return -1;
        try
        {
            int i = 0;
            foreach (var l in ((System.Collections.IEnumerable)((dynamic)env).LoadOrder.ListedOrder).Cast<dynamic>())
            {
                if (string.Equals((string)l.ModKey.FileName.String, pluginName, StringComparison.OrdinalIgnoreCase))
                    return i;
                i++;
            }
        }
        catch { }
        return -1;
    }

    /// <summary>
    /// Declare <paramref name="master"/> on the mod if it is not already declared. Returns true when
    /// it was actually added. Masters are recomputed on save, but xEdit shows the dependency the
    /// moment the override exists, and so should the header the UI reads back.
    /// </summary>
    private static bool EnsureMaster(IFallout4Mod mod, string master, string selfName)
    {
        if (string.IsNullOrWhiteSpace(master)) return false;
        if (string.Equals(master, selfName, StringComparison.OrdinalIgnoreCase)) return false;  // never itself
        if (mod.ModHeader.MasterReferences.Any(m =>
                string.Equals(m.Master.FileName.String, master, StringComparison.OrdinalIgnoreCase)))
            return false;
        mod.ModHeader.MasterReferences.Add(
            new Mutagen.Bethesda.Plugins.Records.MasterReference { Master = master });
        return true;
    }

    /// <summary>
    /// Inspect a target before a multi-record operation. Existing targets fail closed: if the file
    /// cannot be opened or enumerated, the caller must stop before writing anything rather than
    /// treating an unknown state as "no collisions".
    /// </summary>
    private static bool TryFindExistingOverrides(
        object? env,
        string patchPlugin,
        IEnumerable<FormKey> candidates,
        out IReadOnlyList<(FormKey formKey, string editorId)> existing,
        out string error)
    {
        existing = Array.Empty<(FormKey, string)>();
        error = "";

        var wanted = candidates.Where(fk => !fk.IsNull).ToHashSet();
        if (wanted.Count == 0) return true;

        var (patchName, patchPath) = NormalizePlugin(patchPlugin);
        var patch = GetMutable(patchName);
        if (patch == null)
        {
            bool existsOnDisk = (patchPath != null && File.Exists(patchPath))
                || MutagenLoader.LooseModPaths.ContainsKey(patchName)
                || FindPluginPath(patchName, env) != null;
            if (!existsOnDisk) return true;

            var openResult = OpenPlugin(patchPlugin, env);
            patch = GetMutable(patchName);
            if (patch == null)
            {
                error = $"Could not inspect existing target '{patchName}' before copying: {openResult}";
                return false;
            }
        }

        try
        {
            existing = patch.EnumerateMajorRecords()
                .Where(r => wanted.Contains(r.FormKey))
                .Select(r => (r.FormKey, string.IsNullOrWhiteSpace(r.EditorID) ? r.FormKey.ToString() : r.EditorID!))
                .OrderBy(r => r.FormKey.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return true;
        }
        catch (Exception ex)
        {
            error = $"Could not inspect existing overrides in '{patchName}' before copying: {ex.Message}";
            return false;
        }
    }

    public static string ResolveConflict(object? env, string formKeyStr, string sourceName, string patchPlugin)
        => ResolveConflict(env, formKeyStr, sourceName, patchPlugin, overwrite: false);

    /// <summary>
    /// Copy a record into a patch as an override, with xEdit's rules around it:
    /// the target may not load before what it overrides, an existing override is not replaced
    /// without being asked, and the origin is declared as a master so the override actually resolves.
    /// </summary>
    public static string ResolveConflict(object? env, string formKeyStr, string winningPlugin, string patchPlugin,
        bool overwrite)
    {
        if (!FormKey.TryFactory(formKeyStr, out var fk)) return "Invalid FormKey.";
        if (string.IsNullOrWhiteSpace(patchPlugin)) return "Choose a patch plugin to write the resolution into.";

        var (sourceName, _) = NormalizePlugin(winningPlugin);
        var (patchName, patchPath) = NormalizePlugin(patchPlugin);
        if (string.Equals(sourceName, patchName, StringComparison.OrdinalIgnoreCase))
            return ToolError.Fail(
                $"Refused: source and target are both '{patchName}'. Replacing an override from the same " +
                "mutable plugin would remove the source record before it can be copied. Choose a separate patch plugin.");

        var src = MutagenLoader.GetRecordVersion(env, sourceName, fk);
        if (src == null) return $"Could not find {fk} in '{sourceName}'.";

        // The patch may be given as a bare name or a full path. Ensure it's open and mutable:
        // open it if it already exists on disk (so we extend it), else create it new.
        if (GetMutable(patchName) == null)
        {
            bool existsOnDisk = (patchPath != null && File.Exists(patchPath))
                || MutagenLoader.LooseModPaths.ContainsKey(patchName)
                || FindPluginPath(patchName, env) != null;
            if (existsOnDisk) OpenPlugin(patchPlugin, env);
            else CreatePlugin(patchPlugin);
        }
        var patch = GetMutable(patchName);
        if (patch == null) return $"Could not open or create patch plugin '{patchName}'.";

        if (src is not IMajorRecordGetter getter)
            return $"{fk} is not an overridable record.";

        // Guard: verify the BASE record in the originating master at this FormKey is the same type.
        // Using TryResolve (winning) is wrong: if the patch plugin is already in the load order
        // with its own COBJ override, TryResolve returns COBJ == COBJ and the check silently passes.
        // Instead compare against the specific master-plugin version (ground truth before overrides).
        var masterPlugin = fk.ModKey.FileName;
        var masterRec = MutagenLoader.GetRecordVersion(env, masterPlugin, fk);
        if (masterRec != null)
        {
            var srcSig = getter.Registration.GetterType;
            var masterSig = masterRec.Registration.GetterType;
            if (!masterSig.IsAssignableFrom(srcSig) && !srcSig.IsAssignableFrom(masterSig))
            {
                var masterEdid = string.IsNullOrEmpty(masterRec.EditorID) ? fk.ToString() : masterRec.EditorID;
                return $"Type collision refused: {fk} is {masterSig.Name} '{masterEdid}' in {masterPlugin} " +
                       $"but the supplied record is {srcSig.Name} from '{sourceName}'. " +
                       $"Creating this override would clobber the base record. " +
                       $"Check which mod owns this FormID in xEdit before proceeding.";
            }
        }

        // Load order: an override only wins if it loads AFTER what it overrides. Copying into a
        // plugin that loads earlier produces a record the game ignores, silently -- so refuse it
        // rather than write something that looks applied and is not. A patch that is not in the load
        // order yet (index -1) is about to be created and will load last, so it is always allowed.
        var recordMaster = fk.ModKey.FileName.String;
        int targetIdx = LoadOrderIndexOf(env, patchName);
        if (targetIdx >= 0)
        {
            int originIdx = LoadOrderIndexOf(env, sourceName);
            int masterIdx = LoadOrderIndexOf(env, recordMaster);
            // Must sit after BOTH the record's own master and the version being copied.
            int mustFollowIdx = Math.Max(originIdx, masterIdx);
            var mustFollow = originIdx >= masterIdx ? sourceName : recordMaster;
            if (mustFollowIdx >= 0 && targetIdx < mustFollowIdx)
            {
                return ToolError.Fail(
                    $"Refused: '{patchName}' is at load order {targetIdx:X2} but '{mustFollow}' is at " +
                    $"{mustFollowIdx:X2}. An override only takes effect when it loads AFTER the plugin it " +
                    $"overrides, so this copy would be ignored by the game. Move '{patchName}' later in the " +
                    $"load order, or copy into a plugin that already loads after '{mustFollow}'.");
            }
        }

        // Overwrite protection: Mutagen's GetOrAddAsOverride returns an existing override unchanged.
        // That is safe by default, but an explicit replacement needs a separate remove/copy path and
        // must never be mistaken for success without confirmation.
        bool alreadyPresent;
        try { alreadyPresent = patch.EnumerateMajorRecords().Any(r => r.FormKey == fk); }
        catch (Exception ex)
        {
            return ToolError.Fail(
                $"Could not inspect target '{patchName}' before copying; no changes were made: {ex.Message}");
        }
        if (alreadyPresent && !overwrite)
        {
            var existingEdid = string.IsNullOrEmpty(getter.EditorID) ? fk.ToString() : getter.EditorID;
            return $"{OverwritePrompt} '{patchName}' already contains an override for {existingEdid} ({fk}). " +
                   $"Overwrite the existing record with {sourceName}'s version?";
        }

        try
        {
            var copied = alreadyPresent
                ? ReplaceOverride(patch, getter)
                : AddOverride(patch, getter);
            if (!copied)
                return alreadyPresent
                    ? $"Could not replace the existing {getter.Registration.Name} override; the original was left unchanged."
                    : $"This record type ({getter.Registration.Name}) cannot be overridden yet.";

            // Master injection: the override references a record owned by another plugin, so that
            // plugin has to be declared or the FormID resolves to nothing. Save recomputes the list,
            // but the UI reads the header back immediately, and xEdit shows the dependency at once.
            var added = new List<string>();
            if (EnsureMaster(patch, recordMaster, patchName)) added.Add(recordMaster);
            if (EnsureMaster(patch, sourceName, patchName)) added.Add(sourceName);

            MutagenLoader.InvalidateModIndex(patchName);
            NotifyChanged(patchName);
            return $"Copied {sourceName}'s version of {fk} into {patchName} as an override"
                   + (alreadyPresent ? " (replacing the override already there)" : "")
                   + (added.Count > 0 ? $"; declared master(s): {string.Join(", ", added)}" : "")
                   + $". Ensure {patchName} loads after the conflicting plugins, then save it.";
        }
        catch (Exception ex)
        {
            return $"Resolve failed: {ex.Message}";
        }
    }

    // Mutagen 0.53.1 exposes GetOrAddAsOverride only on IGroup<T> (the group mixin), not on the
    // mod. Find the mod's typed group whose element matches the record and invoke the mixin via
    // reflection so this works for any record type without a per-signature switch.
    private static readonly MethodInfo? _groupOverrideMixin = typeof(GetOrAddAsOverrideMixIns)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .FirstOrDefault(m => m.Name == "GetOrAddAsOverride"
            && m.IsGenericMethodDefinition
            && m.GetGenericArguments().Length == 2
            && m.GetParameters().Length == 2);

    private static bool AddOverride(IFallout4Mod mod, IMajorRecordGetter getter) =>
        AddOverrideReturning(mod, getter) != null;

    // As AddOverride, but hands back the mutable override record so callers (e.g. batch_patch_records)
    // can apply edits in-place instead of re-finding it with a full-mod scan. GetOrAddAsOverride is
    // idempotent: a second call for the same FormKey returns the existing override.
    private static IFallout4MajorRecord? AddOverrideReturning(IFallout4Mod mod, IMajorRecordGetter getter)
    {
        if (_groupOverrideMixin == null) return null;
        // Match the mod's typed group by the record's GETTER type: the group's generic argument
        // is the CONCRETE record class (e.g. ConstructibleObject), which implements the getter
        // interface (IConstructibleObjectGetter). Registration.SetterType is the *interface*, so
        // comparing against it never matched the concrete group arg -- that was the bug.
        var getterType = getter.Registration.GetterType;

        foreach (var prop in mod.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var pt = prop.PropertyType;
            if (!pt.IsGenericType) continue;
            var args = pt.GetGenericArguments();
            if (args.Length != 1) continue;
            var groupMajor = args[0];                              // concrete record class
            if (!getterType.IsAssignableFrom(groupMajor)) continue;

            var group = prop.GetValue(mod);
            if (group is not Mutagen.Bethesda.Plugins.Records.IGroup) continue;

            try
            {
                var ovr = _groupOverrideMixin.MakeGenericMethod(groupMajor, getterType)
                    .Invoke(null, new[] { group, getter });
                return ovr as IFallout4MajorRecord;
            }
            catch { /* try another candidate group */ }
        }
        return null;
    }

    private static bool ReplaceOverride(IFallout4Mod mod, IMajorRecordGetter getter) =>
        ReplaceOverrideReturning(mod, getter) != null;

    /// <summary>
    /// Replace an existing override with a fresh override-mask copy of <paramref name="getter"/>.
    /// Mutagen's GetOrAddAsOverride intentionally returns an existing record unchanged, so calling
    /// it with overwrite=true used to report "replacing" while preserving the old fields. Remove
    /// only after retaining the old mutable record, and restore it if the deep copy or insertion
    /// fails, so an attempted overwrite cannot turn into data loss.
    /// </summary>
    private static IFallout4MajorRecord? ReplaceOverrideReturning(IFallout4Mod mod, IMajorRecordGetter getter)
    {
        if (_groupOverrideMixin == null) return null;
        var getterType = getter.Registration.GetterType;

        foreach (var prop in mod.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var pt = prop.PropertyType;
            if (!pt.IsGenericType) continue;
            var args = pt.GetGenericArguments();
            if (args.Length != 1) continue;
            var groupMajor = args[0];
            if (!getterType.IsAssignableFrom(groupMajor)) continue;
            if (prop.GetValue(mod) is not Mutagen.Bethesda.Plugins.Records.IGroup group) continue;
            var existing = group.Records.FirstOrDefault(record => record.FormKey == getter.FormKey);
            if (existing == null) return AddOverrideReturning(mod, getter);

            if (!TryRemoveFromGroup(group, getter.FormKey, out _)) return null;
            try
            {
                var replacement = _groupOverrideMixin.MakeGenericMethod(groupMajor, getterType)
                    .Invoke(null, new object?[] { group, getter }) as IFallout4MajorRecord;
                if (replacement != null) return replacement;

                group.SetUntyped(existing);
                return null;
            }
            catch
            {
                try { group.SetUntyped(existing); } catch { }
                throw;
            }
        }
        return null;
    }

    /// <summary>Plugins currently open for editing (candidates for a conflict patch target).</summary>
    public static IReadOnlyList<string> EditablePlugins() => _mutable.Keys.ToList();

    // ---- copy as override / delete / list editing / COBJ structs -----------------------

    /// <summary>
    /// Copy a record (identified by FormKey or EditorID) from the chosen source plugin into a patch
    /// plugin as an override -- the xEdit "Copy as Override Into" operation. The patch is opened or
    /// created automatically. After this, edit the override with set_field/set_components/etc.
    /// </summary>
    public static string CopyAsOverride(object? env, string sourcePlugin, string id, string patchPlugin,
        bool overwrite = false)
    {
        if (!ResolveFk(env, id, out var fk))
            return $"'{id}' is not a FormKey and no loaded record has that EditorID.";
        return ResolveConflict(env, fk.ToString(), sourcePlugin, patchPlugin, overwrite);
    }

    /// <summary>
    /// Copy several records into a patch as overrides in one call (the Conflicts-panel batch action).
    /// itemsJson is a JSON array of { "formKey": "...", "source": "..." }. Returns a JSON summary
    /// { total, ok, failed, failures: [ { formKey, reason } ] } so callers get authoritative counts
    /// instead of parsing human-readable result strings.
    /// </summary>
    public static string CopyAsOverrideMany(object? env, string itemsJson, string patchPlugin,
        bool overwrite = false)
    {
        List<(string formKey, string source)> items;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(itemsJson) ? "[]" : itemsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                throw new JsonException("Expected a JSON array.");

            items = doc.RootElement.EnumerateArray()
                .Select(el => (
                    formKey: el.TryGetProperty("formKey", out var fkEl) ? fkEl.GetString() ?? "" : "",
                    source: el.TryGetProperty("source", out var sEl) ? sEl.GetString() ?? "" : ""))
                .ToList();
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                total = 0,
                ok = 0,
                failed = 0,
                requiresOverwrite = false,
                existing = Array.Empty<object>(),
                failures = new[] { new { formKey = "", reason = $"Could not parse items JSON: {ex.Message}" } },
            });
        }

        // Preflight the whole batch before the first write. Duplicate destinations are ambiguous even
        // when overwrite was approved: processing them in order would copy the first item and then
        // silently replace it with the second source version. Reject the entire request before a patch
        // is created so malformed or duplicate UI selections cannot become partial writes.
        var parsedKeys = items
            .Select(i => FormKey.TryFactory(i.formKey, out var fk) ? fk : default)
            .Where(fk => !fk.IsNull)
            .ToArray();
        var duplicateKeys = parsedKeys
            .GroupBy(fk => fk)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(fk => fk.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (duplicateKeys.Length > 0)
        {
            return JsonSerializer.Serialize(new
            {
                total = items.Count,
                ok = 0,
                failed = items.Count,
                requiresOverwrite = false,
                existing = Array.Empty<object>(),
                failures = duplicateKeys.Select(fk => new
                {
                    formKey = fk.ToString(),
                    reason = "The batch contains this FormKey more than once; nothing was copied.",
                }).ToArray(),
            });
        }

        // Existing-target collisions still use one explicit confirmation, and cancellation leaves
        // every selected record untouched.
        if (!overwrite)
        {
            if (!TryFindExistingOverrides(env, patchPlugin, parsedKeys, out var existing, out var inspectError))
            {
                return JsonSerializer.Serialize(new
                {
                    total = items.Count,
                    ok = 0,
                    failed = items.Count,
                    requiresOverwrite = false,
                    existing = Array.Empty<object>(),
                    failures = new[] { new { formKey = "", reason = inspectError } },
                });
            }
            if (existing.Count > 0)
            {
                return JsonSerializer.Serialize(new
                {
                    total = items.Count,
                    ok = 0,
                    failed = 0,
                    requiresOverwrite = true,
                    existing = existing.Select(e => new
                    {
                        formKey = e.formKey.ToString(),
                        editorId = e.editorId,
                    }).ToArray(),
                    failures = Array.Empty<object>(),
                });
            }
        }

        int ok = 0;
        var failures = new List<(string formKey, string reason)>();
        foreach (var item in items)
        {
            var msg = ResolveConflict(env, item.formKey, item.source, patchPlugin, overwrite);
            if (msg.StartsWith("Copied", StringComparison.OrdinalIgnoreCase)) ok++;
            else failures.Add((item.formKey, msg));
        }

        return JsonSerializer.Serialize(new
        {
            total = items.Count,
            ok,
            failed = items.Count - ok,
            requiresOverwrite = false,
            existing = Array.Empty<object>(),
            failures = failures.Select(f => new { formKey = f.formKey, reason = f.reason }).ToArray(),
        });
    }

    /// <summary>Remove a record entirely from a plugin (xEdit "Remove"). Use for deleting a stub
    /// record the editor created incompletely, or dropping an unwanted override from a patch.</summary>
    public static string DeleteRecord(string plugin, string recordId, object? env)
    {
        var mod = EnsureOpen(plugin, env, out var openMsg); if (mod == null) return openMsg;
        var rec = FindMutableRecord(mod, recordId); if (rec == null) return ToolError.Fail($"Record '{recordId}' not found in {plugin}.");
        var fk = rec.FormKey;

        foreach (var groupProp in mod.GetType().GetProperties()
                     .Where(p => typeof(Mutagen.Bethesda.Plugins.Records.IGroup).IsAssignableFrom(p.PropertyType)))
        {
            if (groupProp.GetValue(mod) is not Mutagen.Bethesda.Plugins.Records.IGroup group) continue;
            var contains = ((System.Collections.IEnumerable)group)
                .Cast<Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter>().Any(r => r.FormKey == fk);
            if (!contains) continue;
            if (!TryRemoveFromGroup(group, fk, out var removeErr))
                return ToolError.Fail($"Could not remove {rec.EditorID} [{fk}] from {plugin}: {removeErr}");
            MutagenLoader.InvalidateModIndex(plugin); NotifyChanged(plugin);
            return $"Removed {rec.EditorID} [{fk}] from {plugin}. Call save_plugin to persist.";
        }
        return ToolError.Fail($"Could not locate the group holding {fk} in {plugin}.");
    }

    /// <summary>Remove a FormLink entry (by FormKey or EditorID) from a list field -- e.g. drop an
    /// item from a FormList's 'Items' or a keyword from 'Categories'/'Keywords'.</summary>
    public static string RemoveListItem(string plugin, string recordId, string field, string value, object? env)
    {
        var mod = EnsureOpen(plugin, env, out var openMsg); if (mod == null) return openMsg;
        var rec = FindMutableRecord(mod, recordId); if (rec == null) return ToolError.Fail($"Record '{recordId}' not found in {plugin}.");

        if (!RemoveListItemFromRecord(rec, field, value, env, out var msg)) return msg;
        MutagenLoader.InvalidateModIndex(plugin); NotifyChanged(plugin);
        return $"{msg} on {recordId}. save_plugin to persist.";
    }

    // Record-level core of RemoveListItem: no index invalidation / change notification (caller batches).
    private static bool RemoveListItemFromRecord(object rec, string field, string value, object? env, out string msg)
    {
        var prop = rec.GetType().GetProperty(field, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (prop?.GetValue(rec) is not System.Collections.IList list) { msg = $"Field '{field}' is not a list on {rec.GetType().Name}."; return false; }
        if (!ResolveFk(env, value, out var fk)) { msg = $"'{value}' is not a FormKey and no loaded record has that EditorID."; return false; }

        int removed = 0;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var item = list[i];
            var fkProp = item?.GetType().GetProperty("FormKey");
            if (fkProp?.GetValue(item) is FormKey ifk && ifk == fk) { list.RemoveAt(i); removed++; }
        }
        if (removed == 0) { msg = $"No entry matching {value} found in {field} ({list.Count} item(s))."; return false; }
        msg = $"Removed {removed} entr(y/ies) matching {value} from {field} (now {list.Count})";
        return true;
    }

    /// <summary>Replace a COBJ's component list. componentsJson is an array of
    /// {"component":"FormKeyOrEditorId","count":N}. Components are the materials the recipe consumes.</summary>
    public static string SetComponents(string plugin, string recordId, string componentsJson, object? env)
    {
        var mod = EnsureOpen(plugin, env, out var openMsg); if (mod == null) return openMsg;
        var rec = FindMutableRecord(mod, recordId); if (rec == null) return ToolError.Fail($"Record '{recordId}' not found in {plugin}.");
        if (rec is not ConstructibleObject && rec is not MiscItem)
            return $"set_components applies to COBJ (recipe inputs) and MISC (scrap components); {recordId} is {rec.GetType().Name}.";

        // COBJ and MISC use the same {component (a CMPO FormLink), count} shape - parse once.
        var parsed = new List<(Mutagen.Bethesda.Plugins.FormKey fk, uint count)>();
        var failures = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(componentsJson) ? "[]" : componentsJson);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var comp = el.TryGetProperty("component", out var c) ? c.GetString() ?? "" : "";
                uint count = el.TryGetProperty("count", out var ct) && ct.TryGetUInt32(out var n) ? n : 1u;
                if (!ResolveFk(env, comp, out var fk)) { failures.Add($"{comp}: unresolved"); continue; }
                parsed.Add((fk, count));
            }
        }
        catch (Exception ex) { return $"Could not parse components JSON: {ex.Message}. Expected [{{\"component\":\"01FAA5:Fallout4.esm\",\"count\":1}}]."; }

        int written;
        if (rec is ConstructibleObject cobj)
        {
            var list = new Noggog.ExtendedList<ConstructibleObjectComponent>();
            foreach (var (fk, count) in parsed) { var it = new ConstructibleObjectComponent { Count = count }; it.Component.SetTo(fk); list.Add(it); }
            cobj.Components = list; written = list.Count;
        }
        else
        {
            var misc = (MiscItem)rec;
            var list = new Noggog.ExtendedList<MiscItemComponent>();
            foreach (var (fk, count) in parsed) { var it = new MiscItemComponent { Count = count }; it.Component.SetTo(fk); list.Add(it); }
            misc.Components = list; written = list.Count;
        }

        MutagenLoader.InvalidateModIndex(plugin); NotifyChanged(plugin);
        var msg = $"Set {written} component(s) on {recordId} in {plugin}.";
        if (failures.Count > 0) msg += $" Skipped {failures.Count}: {string.Join("; ", failures)}.";
        return msg + " save_plugin to persist.";
    }

    /// <summary>Replace a COBJ's condition list. conditionsJson is an array of
    /// {"function","param1","param2","operator","value","compareGlobal","runOn","reference"}.
    /// function = e.g. GetGlobalValue/GetBaseValue/GetItemCount/HasKeyword/GetStageDone;
    /// param1/param2 = a FormKey/EditorID (record param) or an integer; operator = == != &gt; &gt;= &lt; &lt;=;
    /// value = the float compared against (omit when compareGlobal is set);
    /// compareGlobal = optional global FormKey to compare against instead of a constant;
    /// runOn = Subject/Target/Reference/...; reference = the ref FormKey when runOn=Reference (e.g. PlayerRef).</summary>
    public static string SetConditions(string plugin, string recordId, string conditionsJson, object? env)
    {
        var mod = EnsureOpen(plugin, env, out var openMsg); if (mod == null) return openMsg;
        var rec = FindMutableRecord(mod, recordId); if (rec == null) return ToolError.Fail($"Record '{recordId}' not found in {plugin}.");

        var condProp = rec.GetType().GetProperty("Conditions");
        if (condProp?.GetValue(rec) is not System.Collections.IList condList)
            return $"{rec.GetType().Name} has no editable Conditions list.";

        var built = new List<Condition>();
        var failures = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(conditionsJson) ? "[]" : conditionsJson);
            int idx = 0;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                idx++;
                var fnName = el.TryGetProperty("function", out var f) ? f.GetString() ?? "" : "";
                if (!Enum.TryParse<Condition.Function>(fnName, ignoreCase: true, out var fn))
                { failures.Add($"#{idx}: unknown function '{fnName}'"); continue; }

                var data = new FunctionConditionData { Function = fn };
                if (el.TryGetProperty("runOn", out var ro) && ro.GetString() is { } roStr &&
                    Enum.TryParse<Condition.RunOnType>(roStr, ignoreCase: true, out var roType))
                    data.RunOnType = roType;
                if (el.TryGetProperty("reference", out var rf) && rf.GetString() is { } rfStr &&
                    ResolveFk(env, rfStr, out var rfFk))
                    data.Reference.SetTo(rfFk);
                ApplyParam(el, "param1", env, fk => data.ParameterOneRecord.SetTo(fk), n => data.ParameterOneNumber = n);
                ApplyParam(el, "param2", env, fk => data.ParameterTwoRecord.SetTo(fk), n => data.ParameterTwoNumber = n);

                var op = ParseOperator(el.TryGetProperty("operator", out var o) ? o.GetString() : null);

                Condition.Flag condFlags = default;
                if (el.TryGetProperty("flags", out var flagEl) && flagEl.GetString() is { Length: > 0 } flagStr)
                {
                    foreach (var part in flagStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        if (Enum.TryParse<Condition.Flag>(part, ignoreCase: true, out var fl)) condFlags |= fl;
                }

                if (el.TryGetProperty("compareGlobal", out var cg) && cg.GetString() is { } cgStr && ResolveFk(env, cgStr, out var cgFk))
                {
                    var cond = new ConditionGlobal { CompareOperator = op, Data = data };
                    cond.ComparisonValue.SetTo(cgFk);
                    if (condFlags != default) cond.Flags = condFlags;
                    built.Add(cond);
                }
                else
                {
                    float val = el.TryGetProperty("value", out var v) && v.TryGetSingle(out var fv) ? fv : 1f;
                    var cond = new ConditionFloat { CompareOperator = op, ComparisonValue = val, Data = data };
                    if (condFlags != default) cond.Flags = condFlags;
                    built.Add(cond);
                }
            }
        }
        catch (Exception ex) { return $"Could not parse conditions JSON: {ex.Message}."; }

        condList.Clear();
        foreach (var c in built) condList.Add(c);
        MutagenLoader.InvalidateModIndex(plugin); NotifyChanged(plugin);
        var msg = $"Set {built.Count} condition(s) on {recordId} in {plugin}.";
        if (failures.Count > 0) msg += $" Skipped {failures.Count}: {string.Join("; ", failures)}.";
        return msg + " save_plugin to persist.";
    }

    // A condition parameter is either a record (FormKey/EditorID) or an integer.
    private static void ApplyParam(JsonElement el, string key, object? env, Action<FormKey> setRecord, Action<int> setNumber)
    {
        if (!el.TryGetProperty(key, out var p)) return;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n)) { setNumber(n); return; }
        if (p.ValueKind == JsonValueKind.String && p.GetString() is { Length: > 0 } s)
        {
            if (int.TryParse(s, out var ni)) setNumber(ni);
            else if (ResolveFk(env, s, out var fk)) setRecord(fk);
        }
    }

    private static CompareOperator ParseOperator(string? op) => (op ?? "==").Trim() switch
    {
        "==" or "=" or "EqualTo" => CompareOperator.EqualTo,
        "!=" or "<>" or "NotEqualTo" => CompareOperator.NotEqualTo,
        ">" or "GreaterThan" => CompareOperator.GreaterThan,
        ">=" or "GreaterThanOrEqualTo" => CompareOperator.GreaterThanOrEqualTo,
        "<" or "LessThan" => CompareOperator.LessThan,
        "<=" or "LessThanOrEqualTo" => CompareOperator.LessThanOrEqualTo,
        _ => CompareOperator.EqualTo,
    };

    // Resolve a FormKey ('001234:Plugin.esp') or an EditorID to a FormKey via the load-order link
    // cache (global) with a fallback to the per-mod index.
    private static bool ResolveFk(object? env, string value, out FormKey fk)
    {
        if (FormKey.TryFactory(value, out fk)) return true;
        var cache = MutagenLoader.LinkCache;
        if (cache != null && cache.TryResolve<IMajorRecordGetter>(value, out var rec)) { fk = rec.FormKey; return true; }
        fk = MutagenLoader.ResolveEditorIdToFormKey(env, value);
        return !fk.IsNull;
    }

    // ---- batch revert (undo a plugin's bad overrides across the whole load order) -------

    /// <summary>
    /// Revert records that <paramref name="badPlugin"/> currently WINS (it's the last override) back
    /// to the version loaded just before it, by forwarding that prior version into a patch plugin.
    /// This undoes a mod that mass-injected the same bad edit (e.g. a corrupted recipe template) across
    /// many records. Defaults to a DRY RUN that lists what it would do; pass apply=true to write them.
    /// Optionally filter to one signature (e.g. "COBJ") and/or to records whose winning version uses a
    /// specific component FormKey (e.g. the hubflower), to target exactly the corrupted set.
    /// </summary>
    public static string RevertOverridesFrom(object? env, string badPlugin, string patchPlugin,
        string? sig, string? containsComponent, bool apply, int limit)
    {
        if (string.IsNullOrWhiteSpace(badPlugin)) return "Provide the plugin whose overrides to revert.";
        FormKey? compFk = null;
        if (!string.IsNullOrWhiteSpace(containsComponent))
        {
            if (!ResolveFk(env, containsComponent, out var cfk))
                return $"'{containsComponent}' is not a FormKey/EditorID of a loaded record.";
            compFk = cfk;
        }

        var overrides = MutagenLoader.EnumerateOverrides(env, badPlugin, sig);
        if (overrides.Count == 0)
            return $"'{badPlugin}' carries no override records{(sig != null ? $" of type {sig}" : "")} (or it isn't loaded).";

        IFallout4Mod? patch = null;
        var existingPatchWarning = "";
        if (apply)
        {
            if (string.IsNullOrWhiteSpace(patchPlugin)) return "Provide a patch plugin to write the reverts into.";
            // Explicit refusal: without it OpenPlugin's guard would just leave patch null below and
            // report the misleading "Could not open or create".
            if (ProtectedPlugins.IsProtected(NormalizePlugin(patchPlugin).name))
                return ToolError.Fail(ProtectedPlugins.RefusalMessage(NormalizePlugin(patchPlugin).name));

            // When patch_plugin already exists on disk this injects overrides into that real,
            // installed mod and saves over it -- surfaced in the result so it is not mistaken for
            // "wrote a new patch".
            bool patchExisted = GetMutable(patchPlugin) != null ||
                                MutagenLoader.LooseModPaths.ContainsKey(patchPlugin) ||
                                FindPluginPath(patchPlugin, env) != null;
            if (GetMutable(patchPlugin) == null)
            {
                if (patchExisted) OpenPlugin(patchPlugin, env);
                else CreatePlugin(patchPlugin);
            }
            patch = GetMutable(patchPlugin);
            if (patch == null) return ToolError.Fail($"Could not open or create patch plugin '{patchPlugin}'.");
            existingPatchWarning = patchExisted
                ? $" NOTE: '{patchPlugin}' already existed and was modified in place -- the reverts were added to it."
                : "";
        }

        var preview = new List<string>();
        int reverted = 0, skipped = 0, noPrior = 0;
        foreach (var (fk, eid, rsig) in overrides)
        {
            var ctxs = MutagenLoader.GetRecordContexts(env, fk);
            if (ctxs.Count < 2) { noPrior++; continue; }
            // Only act where badPlugin is what the game currently uses (the winner).
            if (!string.Equals(ctxs[^1].plugin, badPlugin, StringComparison.OrdinalIgnoreCase)) { skipped++; continue; }
            if (compFk != null && !MutagenLoader.CobjUsesComponent(ctxs[^1].rec, compFk.Value)) { skipped++; continue; }

            var prior = ctxs[^2];   // the version badPlugin overrode
            if (apply)
            {
                if (prior.rec is IMajorRecordGetter g && AddOverride(patch!, g)) reverted++;
                else skipped++;
            }
            else
            {
                reverted++;
                if (preview.Count < limit)
                    preview.Add($"{eid} [{fk}] ({rsig}) -> restore {prior.plugin}'s version");
            }
        }

        if (apply)
        {
            MutagenLoader.InvalidateModIndex(patchPlugin); NotifyChanged(patchPlugin);
            var save = SavePlugin(patchPlugin, null, env);
            return $"Reverted {reverted} record(s) that {badPlugin} was overriding, into {patchPlugin}. " +
                   $"{(skipped > 0 ? skipped + " skipped (not the winner / filtered out). " : "")}{save} " +
                   $"Load {patchPlugin} AFTER {badPlugin} so the reverts win.{existingPatchWarning}";
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"DRY RUN -- {reverted} record(s) where {badPlugin} is the winner would be reverted to the prior version" +
                      $"{(compFk != null ? $" (filtered to ones using {containsComponent})" : "")}.");
        if (skipped > 0) sb.AppendLine($"({skipped} of {badPlugin}'s overrides skipped -- a later plugin already wins, or filtered out.)");
        foreach (var line in preview) sb.AppendLine("  " + line);
        if (reverted > preview.Count) sb.AppendLine($"  …and {reverted - preview.Count} more.");
        sb.AppendLine($"Run revert_overrides again with apply=true to write these into '{patchPlugin}'.");
        return sb.ToString();
    }

    // ---- batch patch --------------------------------------------------------

    /// <summary>
    /// Apply a list of field operations (set/add/remove) to ALL records of a given type in a
    /// source plugin, optionally filtered by a field value. Copies each matching record as an
    /// override into patchPlugin, then applies the operations. Defaults to dry-run mode.
    /// </summary>
    public static string BatchPatchRecords(
        object? env, string patchPlugin, string sourcePlugin, string sig,
        string operationsJson, string? filterField, string? filterValue,
        bool dryRun, int limit)
    {
        if (string.IsNullOrWhiteSpace(sig))         return "Provide record type, e.g. 'ConstructibleObject'.";
        if (string.IsNullOrWhiteSpace(sourcePlugin)) return "Provide source_plugin.";
        if (string.IsNullOrWhiteSpace(patchPlugin))  return "Provide patch_plugin.";

        // Parse operations array: [{op:"set"|"add"|"remove", field:"...", value:"..."}]
        List<(string op, string field, string val)> ops;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(operationsJson) ? "[]" : operationsJson);
            ops = doc.RootElement.EnumerateArray().Select(el => (
                op:    el.TryGetProperty("op",    out var o) ? o.GetString() ?? "set" : "set",
                field: el.TryGetProperty("field", out var f) ? f.GetString() ?? ""   : "",
                val:   el.TryGetProperty("value", out var v) ? v.GetString() ?? ""   : ""
            )).Where(x => x.field.Length > 0).ToList();
        }
        catch (Exception ex) { return $"Invalid operations JSON: {ex.Message}"; }

        if (ops.Count == 0 && !dryRun)
            return "Provide at least one operation in 'operations' (or use dry_run=true to preview).";

        // Collect and filter candidates
        var allRecs = MutagenLoader.GetRecordsForBatch(env, sourcePlugin, sig);
        if (allRecs.Count == 0)
            return $"No records of type '{sig}' found in '{sourcePlugin}'. Use list_record_types to confirm.";

        var matched = new List<Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter>();
        foreach (var rec in allRecs)
        {
            if (!BatchMatchesFilter(rec, filterField, filterValue)) continue;
            matched.Add(rec);
            if (matched.Count >= limit) break;
        }

        if (matched.Count == 0)
            return $"No {sig} records in '{sourcePlugin}' match the filter " +
                   $"({filterField}={filterValue}). Adjust the filter or check field names.";

        // Dry run: report what would change
        if (dryRun)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"DRY RUN -- {matched.Count} of {allRecs.Count} {sig} records in '{sourcePlugin}' match" +
                          (string.IsNullOrWhiteSpace(filterField) ? " (no filter)" : $" ({filterField}={filterValue})") + ":");
            foreach (var rec in matched.Take(25))
                sb.AppendLine($"  {rec.EditorID ?? "(no-eid)"} [{rec.FormKey}]");
            if (matched.Count > 25) sb.AppendLine($"  ...and {matched.Count - 25} more.");
            sb.AppendLine($"\nOperations to apply to each:");
            foreach (var (op, field, val) in ops)
                sb.AppendLine($"  {op} {field} = \"{val}\"");
            sb.AppendLine($"\nRun again with dry_run=false to write {matched.Count} records into '{patchPlugin}'.");
            return sb.ToString();
        }

        // Ensure patch plugin is open/created
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
        if (patch == null) return $"Could not open or create patch plugin '{patchName}'.";

        int done = 0, failed = 0, opsApplied = 0;
        var errors = new List<string>();

        // Apply ops to the override IN-PLACE. The previous version round-tripped through
        // SetField/AddListItem/RemoveListItem, each of which re-scanned the whole (growing) patch
        // mod and fired an index-invalidation + UI notification per op -- O(M²) plus a notification
        // storm for thousands of records. Here we keep the override AddOverride hands back and edit
        // it directly, invalidating + notifying ONCE at the end.
        foreach (var rec in matched)
        {
            try
            {
                var ovr = AddOverrideReturning(patch, rec);
                if (ovr == null)
                {
                    failed++;
                    if (errors.Count < 20) errors.Add($"{rec.EditorID} [{rec.FormKey}]: cannot override type '{sig}'");
                    continue;
                }
                foreach (var (op, field, val) in ops)
                {
                    bool ok = op switch
                    {
                        "add"    => AddListItemToRecord(ovr, field, val, env, out var ma) ? true : Fail(ref errors, rec, ma),
                        "remove" => RemoveListItemFromRecord(ovr, field, val, env, out var mr) ? true : Fail(ref errors, rec, mr),
                        _        => TrySet(ovr, field, val, env, out var ms) ? true : Fail(ref errors, rec, ms),
                    };
                    if (ok) opsApplied++;
                }
                done++;
            }
            catch (Exception ex)
            {
                failed++;
                if (errors.Count < 20) errors.Add($"{rec.FormKey}: {ex.Message}");
            }
        }

        MutagenLoader.InvalidateModIndex(patchName);
        NotifyChanged(patchName);

        var msg = new System.Text.StringBuilder();

        // If operations were requested but not one applied, the patch holds only ITM overrides
        // (almost always a wrong field name or value format). Don't persist that noise.
        if (ops.Count > 0 && opsApplied == 0)
        {
            msg.AppendLine($"ABORTED -- overrode {done} record(s) but NOT ONE operation succeeded " +
                           "(likely a wrong field name or value format). Nothing was saved.");
            if (errors.Count > 0)
            {
                msg.AppendLine("Errors:");
                foreach (var e in errors.Take(5)) msg.AppendLine("  " + e);
            }
            msg.Append("Check exact field names with list_records_summary, fix the operation, and re-run.");
            return msg.ToString();
        }

        msg.AppendLine($"Batch patched {done} of {matched.Count} records into '{patchName}' " +
                       $"({opsApplied} field op(s) applied)." + (failed > 0 ? $" {failed} failed." : ""));
        if (errors.Count > 0)
        {
            msg.AppendLine("First errors (check field names and value formats):");
            foreach (var e in errors.Take(5)) msg.AppendLine("  " + e);
        }
        msg.Append(SavePlugin(patchName, null, env));
        return msg.ToString();
    }

    // Record an op failure (capped) and return false so it threads through the bool switch above.
    private static bool Fail(ref List<string> errors, Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter rec, string m)
    {
        if (errors.Count < 20) errors.Add($"{rec.EditorID ?? rec.FormKey.ToString()}: {m}");
        return false;
    }

    // ---- support for the in-app C# script runner (PatchScriptHost / run_script) -------------

    /// <summary>Forward a getter into the named patch plugin as a mutable override and hand the
    /// override back. The patch is opened (if it exists on disk) or created on first use. Used by
    /// PatchScriptHost so scripts can do per-record edits without a per-record open/find round-trip.</summary>
    public static IFallout4MajorRecord? OverrideForScript(string patchPlugin, object? env, IMajorRecordGetter getter)
    {
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
        return patch == null ? null : AddOverrideReturning(patch, getter);
    }

    /// <summary>Invalidate + notify + write the script's patch plugin to disk (apply mode).</summary>
    public static string SaveScriptPatch(string patchPlugin, object? env)
    {
        var (name, _) = NormalizePlugin(patchPlugin);
        MutagenLoader.InvalidateModIndex(name);
        NotifyChanged(name);
        return SavePlugin(name, null, env);
    }

    /// <summary>Drop a patch plugin from the in-memory editable set without saving (dry-run cleanup),
    /// so a preview run leaves no half-built overrides behind.</summary>
    public static void DiscardScriptPatch(string patchPlugin)
    {
        var (name, _) = NormalizePlugin(patchPlugin);
        _mutable.Remove(name);
        _sourcePath.Remove(name);
        MutagenLoader.EditableMods.TryRemove(name, out _);
        // ReleaseLooseMod, not a bare TryRemove: if this plugin was loaded read-only before it was
        // opened for editing, the entry being dropped is a memory-mapped overlay holding the file.
        MutagenLoader.ReleaseLooseMod(name);
        NotifyChanged(name);
    }

    /// <summary>Public wrapper over TrySet so scripts can set scalar/nested fields on an override.</summary>
    public static bool TrySetField(object rec, string field, string value, object? env, out string msg) =>
        TrySet(rec, field, value, env, out msg);

    /// <summary>Build a single FO4 Condition from plain typed args (the non-JSON path used by the
    /// script host). Shares the same function/operator/param/flag semantics as set_conditions.</summary>
    public static Condition? BuildConditionTyped(
        object? env, string function, string? param1, string? param2,
        string? op, float value, string? runOn, string? reference,
        string? compareGlobal, string? flags, out string err)
    {
        err = "";
        if (!Enum.TryParse<Condition.Function>(function, ignoreCase: true, out var fn))
        { err = $"unknown function '{function}'"; return null; }

        var data = new FunctionConditionData { Function = fn };
        if (!string.IsNullOrWhiteSpace(runOn) && Enum.TryParse<Condition.RunOnType>(runOn, true, out var roType))
            data.RunOnType = roType;
        if (!string.IsNullOrWhiteSpace(reference) && ResolveFk(env, reference!, out var rfFk))
            data.Reference.SetTo(rfFk);
        ApplyParamStr(param1, env, fk => data.ParameterOneRecord.SetTo(fk), n => data.ParameterOneNumber = n);
        ApplyParamStr(param2, env, fk => data.ParameterTwoRecord.SetTo(fk), n => data.ParameterTwoNumber = n);

        var cop = ParseOperator(op);
        Condition.Flag condFlags = default;
        if (!string.IsNullOrWhiteSpace(flags))
            foreach (var part in flags!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (Enum.TryParse<Condition.Flag>(part, true, out var fl)) condFlags |= fl;

        if (!string.IsNullOrWhiteSpace(compareGlobal) && ResolveFk(env, compareGlobal!, out var cgFk))
        {
            var cond = new ConditionGlobal { CompareOperator = cop, Data = data };
            cond.ComparisonValue.SetTo(cgFk);
            if (condFlags != default) cond.Flags = condFlags;
            return cond;
        }
        else
        {
            var cond = new ConditionFloat { CompareOperator = cop, ComparisonValue = value, Data = data };
            if (condFlags != default) cond.Flags = condFlags;
            return cond;
        }
    }

    // A string condition parameter is either an integer or a record (FormKey/EditorID).
    private static void ApplyParamStr(string? s, object? env, Action<FormKey> setRecord, Action<int> setNumber)
    {
        if (string.IsNullOrWhiteSpace(s)) return;
        if (int.TryParse(s, out var ni)) setNumber(ni);
        else if (ResolveFk(env, s!, out var fk)) setRecord(fk);
    }

    // Check if a record matches a simple field filter. filterValue is a case-insensitive
    // substring check against the string representation of the field value (FormLinks are
    // formatted as "EditorID [FormID:Plugin]" so matching EditorID or FormKey both work).
    private static bool BatchMatchesFilter(
        Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter rec, string? filterField, string? filterValue)
    {
        if (string.IsNullOrWhiteSpace(filterField)) return true;

        var prop = rec.GetType().GetProperty(filterField, BindingFlags.Public | BindingFlags.Instance);
        if (prop == null) return false;

        object? val; try { val = prop.GetValue(rec); } catch { return false; }
        if (val == null) return string.IsNullOrWhiteSpace(filterValue);

        var valStr = val is Mutagen.Bethesda.Plugins.IFormLinkIdentifier fli
            ? MutagenLoader.FormatFormLink(fli)
            : val.ToString() ?? "";

        return string.IsNullOrWhiteSpace(filterValue) ||
               valStr.Contains(filterValue, StringComparison.OrdinalIgnoreCase);
    }

    // ---- helpers --------------------------------------------------------

    private const string SupportedTypes =
        "BOOK/HOLOTAPE, TERM, WEAP, ARMO, ARMA, ANIO, IDLE, MISC, COBJ, KYWD, AMMO, ALCH, ACTI, CONT, " +
        "FLST, MGEF, PERK, NPC_, QUST, MESG, GLOB (GLOBFLOAT/GLOBINT/GLOBSHORT/GLOBBOOL), AVIF, " +
        "LVLI, LVLN, SPEL, ENCH, FURN, IMAD, LIGH, STAT.";

    // True if the plugin must keep FormIDs in the ESL light range (0x800-0xFFF).
    private static bool IsEsl(IFallout4Mod mod)
    {
        if (mod.ModKey.FileName.String.EndsWith(".esl", StringComparison.OrdinalIgnoreCase)) return true;
        try
        {
            var flags = mod.ModHeader.Flags.ToString();
            return flags.Contains("Light", StringComparison.OrdinalIgnoreCase) ||
                   flags.Contains("Small", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    // Lowest free object FormID >= 0x800, capped at 0xFFF for ESL plugins.
    private static FormKey? NextFreeFormKey(IFallout4Mod mod)
    {
        uint end = IsEsl(mod) ? 0xFFFu : 0xFFFFFFu;
        var used = new HashSet<uint>(mod.EnumerateMajorRecords().Select(r => r.FormKey.ID));
        for (uint id = 0x800; id <= end; id++)
            if (!used.Contains(id)) return new FormKey(mod.ModKey, id);
        return null;   // ESL range exhausted
    }

    // Construct a record of the given type with an explicit (ESL-safe) FormKey and add it.
    // We allocate the FormKey ourselves rather than via AddNew(), which otherwise inherits
    // the plugin's header NextObjectID and can hand out IDs outside the ESL range.
    private static IFallout4MajorRecord? AddNewBySig(IFallout4Mod mod, string sig, string editorId, FormKey fk)
    {
        var r = Fallout4Release.Fallout4;
        switch (sig.ToUpperInvariant())
        {
            case "BOOK": case "HOLOTAPE": { var x = new Book(fk, r) { EditorID = editorId };               mod.Books.Add(x);                return x; }
            case "TERM":                  { var x = new Terminal(fk, r) { EditorID = editorId };           mod.Terminals.Add(x);            return x; }
            case "WEAP":                  { var x = new Weapon(fk, r) { EditorID = editorId };             mod.Weapons.Add(x);              return x; }
            case "ARMO":                  { var x = new Armor(fk, r) { EditorID = editorId };              mod.Armors.Add(x);               return x; }
            // Armor addon (ARMA), animated object (ANIO), idle animation (IDLE): the building blocks
            // for animated equippables. Mutagen handles their binary serialization, so creating them
            // here removes any need for a downstream tool (e.g. FO4AnimForge) to reimplement it.
            case "ARMA":                  { var x = new ArmorAddon(fk, r) { EditorID = editorId };         mod.ArmorAddons.Add(x);          return x; }
            case "ANIO":                  { var x = new AnimatedObject(fk, r) { EditorID = editorId };     mod.AnimatedObjects.Add(x);      return x; }
            case "IDLE":                  { var x = new IdleAnimation(fk, r) { EditorID = editorId };      mod.IdleAnimations.Add(x);       return x; }
            case "MISC":                  { var x = new MiscItem(fk, r) { EditorID = editorId };           mod.MiscItems.Add(x);            return x; }
            case "COBJ":                  { var x = new ConstructibleObject(fk, r) { EditorID = editorId };mod.ConstructibleObjects.Add(x); return x; }
            case "KYWD":                  { var x = new Keyword(fk, r) { EditorID = editorId };            mod.Keywords.Add(x);             return x; }
            case "AMMO":                  { var x = new Ammunition(fk, r) { EditorID = editorId };         mod.Ammunitions.Add(x);          return x; }
            case "ALCH":                  { var x = new Ingestible(fk, r) { EditorID = editorId };         mod.Ingestibles.Add(x);          return x; }
            case "ACTI":                  { var x = new Mutagen.Bethesda.Fallout4.Activator(fk, r) { EditorID = editorId }; mod.Activators.Add(x);    return x; }
            case "CONT":                  { var x = new Container(fk, r) { EditorID = editorId };          mod.Containers.Add(x);           return x; }
            case "FLST":                  { var x = new FormList(fk, r) { EditorID = editorId };           mod.FormLists.Add(x);            return x; }
            case "MGEF":                  { var x = new MagicEffect(fk, r) { EditorID = editorId };        mod.MagicEffects.Add(x);         return x; }
            case "PERK":                  { var x = new Perk(fk, r) { EditorID = editorId };               mod.Perks.Add(x);                return x; }
            case "NPC_":                  { var x = new Npc(fk, r) { EditorID = editorId };                mod.Npcs.Add(x);                 return x; }
            case "QUST":                  { var x = new Quest(fk, r) { EditorID = editorId };              mod.Quests.Add(x);               return x; }
            case "MESG":                  { var x = new Message(fk, r) { EditorID = editorId };            mod.Messages.Add(x);             return x; }
            // GlobalVariable: bare "GLOB" defaults to a float global (the bridge's getGlobal reads a
            // float and GetGlobalValue conditions are float-compared). Explicit subtypes let callers
            // pick int/short/bool. Set the value afterward via set_field on the 'Data' field.
            case "GLOB": case "GLOBFLOAT": { var x = new GlobalFloat(fk, r) { EditorID = editorId, Data = 0 };   mod.Globals.Add(x);          return x; }
            case "GLOBINT":               { var x = new GlobalInt(fk, r) { EditorID = editorId, Data = 0 };     mod.Globals.Add(x);          return x; }
            case "GLOBSHORT":             { var x = new GlobalShort(fk, r) { EditorID = editorId, Data = 0 };   mod.Globals.Add(x);          return x; }
            case "GLOBBOOL":              { var x = new GlobalBool(fk, r) { EditorID = editorId, Data = false };mod.Globals.Add(x);          return x; }
            // ActorValueInfo: custom actor value usable with Papyrus GetValue/ModValue and
            // GetBaseValue/GetValue conditions.
            case "AVIF":                  { var x = new ActorValueInformation(fk, r) { EditorID = editorId }; mod.ActorValueInformation.Add(x); return x; }
            // Leveled lists: shells only. Add weighted entries with add_leveled_entry (entries are
            // structs, not FormLinks, so add_list_item cannot build them).
            case "LVLI":                  { var x = new LeveledItem(fk, r) { EditorID = editorId };          mod.LeveledItems.Add(x);         return x; }
            case "LVLN":                  { var x = new LeveledNpc(fk, r) { EditorID = editorId };           mod.LeveledNpcs.Add(x);          return x; }
            // Spell / enchantment / object-effect: shells. Add effects with set_magic_effects.
            case "SPEL":                  { var x = new Spell(fk, r) { EditorID = editorId };               mod.Spells.Add(x);               return x; }
            case "ENCH":                  { var x = new ObjectEffect(fk, r) { EditorID = editorId };        mod.ObjectEffects.Add(x);        return x; }
            // Furniture (tents/benches with sit/sleep markers) and image-space adapter (visor tints).
            case "FURN":                  { var x = new Furniture(fk, r) { EditorID = editorId };           mod.Furniture.Add(x);            return x; }
            case "IMAD":                  { var x = new ImageSpaceAdapter(fk, r) { EditorID = editorId };   mod.ImageSpaceAdapters.Add(x);   return x; }
            // Light: the record a campfire/lantern needs to actually emit light. Without this the only
            // options were borrowing a vanilla LIGH (dragging in whatever master owns it) or a Spriggit
            // roundtrip. Shell only -- set Radius/Color/Flags/FadeValue etc. with set_field afterwards.
            case "LIGH":                  { var x = new Light(fk, r) { EditorID = editorId };               mod.Lights.Add(x);               return x; }
            // Static: plain world meshes (lit/unlit prop variants, markers) that get spawned with
            // PlaceAtMe. Same gap as LIGH -- previously you had to borrow a vanilla STAT.
            case "STAT":                  { var x = new Static(fk, r) { EditorID = editorId };              mod.Statics.Add(x);              return x; }
            default:                      return null;
        }
    }

    private static IFallout4MajorRecord? FindMutableRecord(IFallout4Mod mod, string id)
    {
        var all = mod.EnumerateMajorRecords().Cast<IFallout4MajorRecordGetter>().ToList();
        IFallout4MajorRecordGetter? hit = null;
        if (FormKey.TryFactory(id, out var fk))
            hit = all.FirstOrDefault(r => r.FormKey == fk);
        hit ??= all.FirstOrDefault(r => string.Equals(r.EditorID, id, StringComparison.OrdinalIgnoreCase));
        return hit as IFallout4MajorRecord;
    }

    private static bool TrySet(object rec, string field, string value, object? env, out string msg)
    {
        // Convenience: "Model" / "nif" on a record sets the mesh path (Model.File).
        if (string.Equals(field, "Model", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(field, "nif", StringComparison.OrdinalIgnoreCase))
            field = "Model.File";

        // Navigate a dotted/indexed path -- properties ("Model.File") AND list indices
        // ("Effects[0].Data.RunImmediately", "Components[0].Count") -- creating missing
        // intermediate objects where possible, then set the final leaf.
        var segments = SplitFieldPath(field);
        if (segments.Count == 0) { msg = "Empty field path."; return false; }

        // Every hop is recorded so a value-type (struct) hop can be written BACK afterwards. Reflection
        // hands back a BOXED COPY of a struct, so mutating it silently changes nothing: that is why
        // set_field on ObjectBounds.First.X ("ObjectBounds" -> "First" is a P3Int16 struct) reported
        // success while the record kept 0,0,0. Reference types are mutated in place and need no
        // write-back; the loop at the end stops as soon as it reaches one.
        var hops = new List<(object Owner, PropertyInfo? Prop, System.Collections.IList? List, int Idx)>();

        object cur = rec;
        for (int i = 0; i < segments.Count - 1; i++)
        {
            var seg = segments[i];
            if (TryIndex(seg, out var idx))
            {
                if (cur is not System.Collections.IList list)
                { msg = $"'{(i > 0 ? segments[i - 1] : "value")}' is not an indexable list."; return false; }
                if (idx < 0 || idx >= list.Count)
                { msg = $"index {idx} is out of range (the list has {list.Count} item(s))."; return false; }
                hops.Add((list, null, list, idx));
                cur = list[idx]!;
            }
            else
            {
                var p = cur.GetType().GetProperty(seg, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (p == null) { msg = $"No field '{seg}' on {cur.GetType().Name}."; return false; }
                var next = p.CanRead ? p.GetValue(cur) : null;
                if (next == null)
                {
                    var pt = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
                    try { next = System.Activator.CreateInstance(pt); } catch { next = null; }
                    if (next == null || !p.CanWrite) { msg = $"Can't initialise '{seg}'."; return false; }
                    p.SetValue(cur, next);
                }
                hops.Add((cur, p, null, -1));
                cur = next;
            }
        }

        var last = segments[^1];
        if (TryIndex(last, out _))
        {
            msg = $"Address a field INSIDE the list element, e.g. '{field}.<Field>' -- setting a whole element by index isn't supported.";
            return false;
        }
        if (!SetLeaf(cur, last, value, env, out msg)) return false;

        // Propagate struct edits back up. Stops at the first reference type, which was mutated in place.
        for (int i = hops.Count - 1; i >= 0 && cur.GetType().IsValueType; i--)
        {
            var (owner, prop, list, idx) = hops[i];
            if (prop != null)
            {
                if (!prop.CanWrite)
                { msg = $"'{segments[i]}' is a read-only struct and can't be written back."; return false; }
                prop.SetValue(owner, cur);
            }
            else if (list != null)
            {
                list[idx] = cur;
            }
            cur = owner;
        }
        return true;
    }

    // "[3]" -> 3. Returns false for property names.
    private static bool TryIndex(string seg, out int idx)
    {
        idx = -1;
        return seg.Length >= 3 && seg[0] == '[' && seg[^1] == ']' && int.TryParse(seg[1..^1], out idx);
    }

    // "Effects[0].Data.RunImmediately" -> ["Effects","[0]","Data","RunImmediately"].
    private static List<string> SplitFieldPath(string field)
    {
        var segs = new List<string>();
        foreach (var part in field.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            int b = part.IndexOf('[');
            if (b < 0) { segs.Add(part); continue; }
            if (b > 0) segs.Add(part[..b]);            // the property name before the first '['
            int j = b;
            while (j < part.Length && part[j] == '[')   // one or more [n] indexers
            {
                int close = part.IndexOf(']', j);
                if (close < 0) break;
                segs.Add(part[j..(close + 1)]);
                j = close + 1;
            }
        }
        return segs;
    }

    private static bool SetLeaf(object rec, string field, string value, object? env, out string msg)
    {
        msg = "";
        var prop = rec.GetType().GetProperty(field,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (prop == null) { msg = $"No field '{field}' on {rec.GetType().Name}."; return false; }

        // FormLink? Settable via the link's SetTo(FormKey) even when the property is get-only.
        var current = prop.CanRead ? prop.GetValue(rec) : null;
        if (current != null)
        {
            var setTo = current.GetType().GetMethods()
                .FirstOrDefault(m => m.Name == "SetTo" && m.GetParameters().Length == 1 &&
                    (m.GetParameters()[0].ParameterType == typeof(FormKey) ||
                     m.GetParameters()[0].ParameterType == typeof(FormKey?)));
            if (setTo != null)
            {
                FormKey fk;
                if (!FormKey.TryFactory(value, out fk))
                {
                    fk = MutagenLoader.ResolveEditorIdToFormKey(env, value);
                    if (fk.IsNull)
                    {
                        msg = $"'{value}' is not a FormKey and no loaded record has that EditorID. " +
                              $"Use a FormKey like '001234:Plugin.esp'.";
                        return false;
                    }
                }
                try { setTo.Invoke(current, new object[] { fk }); return true; }
                catch (Exception ex) { msg = $"Could not set link {field}: {ex.Message}"; return false; }
            }
        }

        if (!prop.CanWrite) { msg = $"Field '{field}' is read-only."; return false; }

        var t = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
        try
        {
            object converted;
            if (t == typeof(string)) converted = value;
            else if (t == typeof(TranslatedString))
                converted = typeof(TranslatedString).GetMethod("op_Implicit", new[] { typeof(string) })!
                    .Invoke(null, new object[] { value })!;
            else if (t.IsEnum) converted = Enum.Parse(t, value, ignoreCase: true);
            // Values arrive as invariant-formatted strings (from JSON / the React editor), so parse
            // them with InvariantCulture -- the current culture would break "1.5" on comma-decimal locales.
            else if (t == typeof(bool)) converted = bool.Parse(value);
            else if (t == typeof(float)) converted = float.Parse(value, Inv);
            else if (t == typeof(double)) converted = double.Parse(value, Inv);
            else if (t == typeof(int)) converted = int.Parse(value, Inv);
            else if (t == typeof(uint)) converted = uint.Parse(value, Inv);
            else if (t == typeof(short)) converted = short.Parse(value, Inv);
            else if (t == typeof(ushort)) converted = ushort.Parse(value, Inv);
            else if (t == typeof(byte)) converted = byte.Parse(value, Inv);
            else if (t == typeof(long)) converted = long.Parse(value, Inv);
            // Color: light/effect tints. Accepts "#RRGGBB" / "RRGGBB" / "R,G,B" (and an optional alpha
            // as "#AARRGGBB" or a 4th component). Without this every LIGH/EFSH colour had to be done
            // through a Spriggit roundtrip.
            else if (t == typeof(System.Drawing.Color)) converted = ParseColor(value);
            else { msg = $"Field '{field}' has type {t.Name}, which set_field can't set yet (scalar/text only)."; return false; }

            prop.SetValue(rec, converted);
            return true;
        }
        catch (Exception ex)
        {
            msg = $"Could not set {field}: {ex.Message}";
            return false;
        }
    }

    // "#F4A964" / "F4A964" / "#FFF4A964" / "244,169,100" -> Color. Throws on anything else so the
    // caller reports a real error instead of silently writing black.
    private static System.Drawing.Color ParseColor(string value)
    {
        var s = value.Trim();
        if (s.Contains(','))
        {
            var parts = s.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is not (3 or 4)) throw new FormatException($"'{value}' is not R,G,B[,A].");
            var c = parts.Select(p => byte.Parse(p.Trim(), Inv)).ToArray();
            return parts.Length == 3
                ? System.Drawing.Color.FromArgb(255, c[0], c[1], c[2])
                : System.Drawing.Color.FromArgb(c[3], c[0], c[1], c[2]);
        }
        if (s.StartsWith('#')) s = s[1..];
        if (s.Length is not (6 or 8)) throw new FormatException($"'{value}' is not #RRGGBB or #AARRGGBB.");
        var v = uint.Parse(s, System.Globalization.NumberStyles.HexNumber, Inv);
        return s.Length == 6
            ? System.Drawing.Color.FromArgb(255, (byte)(v >> 16), (byte)(v >> 8), (byte)v)
            : System.Drawing.Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }
}
