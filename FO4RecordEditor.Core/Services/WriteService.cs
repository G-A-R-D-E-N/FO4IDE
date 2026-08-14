using System.IO;
using System.Reflection;
using System.Text.Json;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Strings;

using JsonConvert = Newtonsoft.Json.JsonConvert;

namespace FO4RecordEditor.Services;

public static partial class WriteService
{
    private static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

    private static readonly Dictionary<string, IFallout4Mod> _mutable = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> _sourcePath = new(StringComparer.OrdinalIgnoreCase);

    public static string? OutputFolderOverride { get; set; }

    public static string DefaultOutputDir =>
        !string.IsNullOrWhiteSpace(OutputFolderOverride)
            ? OutputFolderOverride!
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output");

    public static string? Mo2OverwriteFolder { get; set; }

    public static string NewPatchDir =>
        !string.IsNullOrWhiteSpace(Mo2OverwriteFolder) ? Mo2OverwriteFolder! : DefaultOutputDir;

    public static IFallout4Mod? GetMutable(string name) =>
        _mutable.TryGetValue(name, out var m) ? m : null;

    internal static bool TryGetSourcePath(string name, out string path) =>
        _sourcePath.TryGetValue(name, out path!);

    private static void Register(string name, IFallout4Mod mod)
    {
        _mutable[name] = mod;
        MutagenLoader.EditableMods[name] = mod;

        MutagenLoader.ReplaceLooseMod(name, mod);
    }

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

        if (ProtectedPlugins.IsProtected(name)) return ToolError.Fail(ProtectedPlugins.RefusalMessage(name));
        if (_mutable.ContainsKey(name)) return $"'{name}' is already open for editing.";

        string? filePath = explicitPath ?? FindPluginPath(name, env);
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return $"Could not locate '{name}' on disk. Pass a full path (e.g. 'D:\\...\\{name}'), load the game environment, or open the file via File > Open.";

        try
        {
            var mod = Fallout4Mod.CreateFromBinary(ModPath.FromPath(filePath), Fallout4Release.Fallout4);
            Register(name, mod);
            _sourcePath[name] = filePath;
            MutagenLoader.LooseModPaths[name] = filePath;
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

        if (MutagenLoader.LooseModPaths.TryGetValue(plugin, out var loosePath))
            return loosePath;

        if (env != null)
        {

            try
            {
                dynamic dynEnv = env;
                IReadOnlyDictionary<string, string> pluginPaths = dynEnv.PluginPaths;
                if (pluginPaths.TryGetValue(plugin, out var mo2Path)) return mo2Path;
            }
            catch { }

            try
            {
                dynamic dynEnv = env;
                foreach (dynamic l in (System.Collections.IEnumerable)dynEnv.LoadOrder.ListedOrder)
                {
                    if (!string.Equals((string)l.ModKey.FileName.String, plugin, StringComparison.OrdinalIgnoreCase))
                        continue;

                    try { var p = (string?)l.Path?.Path; if (p != null) return p; } catch { }

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

        var (fileName, explicitPath) = NormalizePlugin(name);
        if (!fileName.EndsWith(".esp", StringComparison.OrdinalIgnoreCase) &&
            !fileName.EndsWith(".esm", StringComparison.OrdinalIgnoreCase) &&
            !fileName.EndsWith(".esl", StringComparison.OrdinalIgnoreCase))
            fileName += ".esp";

        var mod = new Fallout4Mod(ModKey.FromNameAndExtension(fileName), Fallout4Release.Fallout4);
        Register(fileName, mod);
        if (explicitPath != null) _sourcePath[fileName] = explicitPath;
        NotifyChanged(fileName);
        return $"Created new plugin '{fileName}'. Add records with create_record, then call save_plugin to write it.";
    }

    public static event Action<string>? PluginChanged;
    private static void NotifyChanged(string name) => PluginChanged?.Invoke(name);

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

    private static IFallout4Mod? EnsureOpen(string plugin, object? env, out string msg)
    {
        msg = "";
        var (name, _) = NormalizePlugin(plugin);
        var mod = GetMutable(name);
        if (mod != null) return mod;

        var openResult = OpenPlugin(plugin, env);
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

    private static bool AddListItemToRecord(object rec, string field, string value, object? env, out string msg)
    {
        var prop = rec.GetType().GetProperty(field,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (prop == null) { msg = $"No field '{field}' on {rec.GetType().Name}."; return false; }

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

    public static string CompactToEsl(string plugin, object? env)
    {
        var mod = EnsureOpen(plugin, env, out var msg); if (mod == null) return msg;

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

    public static string SetLightFlag(string plugin, bool light, object? env)
    {
        var mod = EnsureOpen(plugin, env, out var msg); if (mod == null) return msg;
        var (name, _) = NormalizePlugin(plugin);

        bool wasLight = mod.ModHeader.Flags.HasFlag(Fallout4ModHeader.HeaderFlag.Small);
        if (light == wasLight)
            return $"'{name}' already {(light ? "has" : "does not have")} the ESL (Small) flag set.";

        if (light)
        {

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
            vt.GetProperty("Version")?.SetValue(vmad, (short)6);
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

    private static string DescribeSave(string name, string path, string? orderingUnavailable)
    {
        var inOverwrite = !string.IsNullOrWhiteSpace(Mo2OverwriteFolder) &&
            path.StartsWith(Mo2OverwriteFolder!, StringComparison.OrdinalIgnoreCase);
        var baseMsg = inOverwrite
            ? $"Saved {name} to the MO2 overwrite folder ({path}). MO2 loads it automatically at top " +
              "priority -- just enable it in the plugin list if it isn't already checked, and place it " +
              "after the plugins it patches."
            : $"Saved {name} to {path}.";

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

        if (string.IsNullOrWhiteSpace(path))
            path = explicitPath ?? (_sourcePath.TryGetValue(name, out var sp) ? sp : Path.Combine(NewPatchDir, name));

        var pathProblem = ProtectedPlugins.ValidateSavePath(path);
        if (pathProblem != null) return ToolError.Fail(pathProblem);

        var loOrdering = TryGetLoadOrderOrdering(env, out var orderingUnavailable);

        var prms = new BinaryWriteParameters
        {

            MastersListContent = MastersListContentOption.Iterate,

            ModKey = ModKeyOption.NoCheck,
            MastersListOrdering = loOrdering,
        };
        try { Directory.CreateDirectory(Path.GetDirectoryName(path)!); } catch { }

        var writeParams = prms;

        var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try { mod.WriteToBinary(tmp, writeParams); }
        catch (Exception ex) { try { File.Delete(tmp); } catch { } return ToolError.Fail($"Save failed while writing: {ex.Message}"); }

        try { EnsureFallout4MasterWritten(mod, tmp, loOrdering); }
        catch (Exception ex) { try { File.Delete(tmp); } catch { } return ToolError.Fail($"Save failed while enforcing the Fallout4.esm master: {ex.Message}"); }

        if (Mo2ProfileLoader.TryReplaceLoadedPluginFile(env, name, tmp, path, out var swapError))
        {
            DuplicateFormIdScanner.Invalidate(path);
            NotifyChanged(name);
            return DescribeSave(name, path, orderingUnavailable);
        }
        _ = swapError;

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

    private static void EnsureFallout4MasterWritten(IFallout4Mod mod, string writtenPath, AMastersListOrderingOption? loOrdering)
    {
        const string fo4 = "Fallout4.esm";
        if (string.Equals(mod.ModKey.FileName.String, fo4, StringComparison.OrdinalIgnoreCase))
            return;

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

        mod.MasterReferences.Clear();
        foreach (var master in written)
            mod.MasterReferences.Add(new Mutagen.Bethesda.Plugins.Records.MasterReference
            { Master = ModKey.FromNameAndExtension(master) });
    }

    public static string ReloadPlugin(string plugin, object? env)
    {
        var (name, _) = NormalizePlugin(plugin);
        if (!_sourcePath.TryGetValue(name, out var path))
            return $"'{name}' is not open for editing. Use open_plugin first.";
        _mutable.Remove(name);
        _sourcePath.Remove(name);
        MutagenLoader.EditableMods.TryRemove(name, out _);

        MutagenLoader.ReleaseLooseMod(name);
        return OpenPlugin(path, env);
    }

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

        var presentTargets = mod.ModHeader.MasterReferences
            .Where(m => stripSet.Contains(m.Master.FileName.String))
            .ToList();
        foreach (var m in presentTargets)
            mod.ModHeader.MasterReferences.Remove(m);

        var targetNames = string.Join(", ", presentTargets.Select(m => m.Master.FileName.String));

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

        var prms = new BinaryWriteParameters
        {
            MastersListContent = MastersListContentOption.Iterate,
            ModKey = ModKeyOption.NoCheck,
            MastersListOrdering = TryGetLoadOrderOrdering(env, out var stripOrderingUnavailable),
        };
        var tmp = outputPath + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try { mod.WriteToBinary(tmp, prms); }
        catch (Exception ex) { try { File.Delete(tmp); } catch { } return $"Write failed: {ex.Message}"; }

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

            DebugLog.Exception("SavePlugin master ordering", ex);
            unavailableReason = $"reading the load order failed ({ex.Message})";
            return null;
        }
    }

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

    public const string OverwritePrompt = "EXISTS:";

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

    private static bool EnsureMaster(IFallout4Mod mod, string master, string selfName)
    {
        if (string.IsNullOrWhiteSpace(master)) return false;
        if (string.Equals(master, selfName, StringComparison.OrdinalIgnoreCase)) return false;
        if (mod.ModHeader.MasterReferences.Any(m =>
                string.Equals(m.Master.FileName.String, master, StringComparison.OrdinalIgnoreCase)))
            return false;
        mod.ModHeader.MasterReferences.Add(
            new Mutagen.Bethesda.Plugins.Records.MasterReference { Master = master });
        return true;
    }

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

        var recordMaster = fk.ModKey.FileName.String;
        int targetIdx = LoadOrderIndexOf(env, patchName);
        if (targetIdx >= 0)
        {
            int originIdx = LoadOrderIndexOf(env, sourceName);
            int masterIdx = LoadOrderIndexOf(env, recordMaster);

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

    private static readonly MethodInfo? _groupOverrideMixin = typeof(GetOrAddAsOverrideMixIns)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .FirstOrDefault(m => m.Name == "GetOrAddAsOverride"
            && m.IsGenericMethodDefinition
            && m.GetGenericArguments().Length == 2
            && m.GetParameters().Length == 2);

    private static bool AddOverride(IFallout4Mod mod, IMajorRecordGetter getter) =>
        AddOverrideReturning(mod, getter) != null;

    private static IFallout4MajorRecord? AddOverrideReturning(IFallout4Mod mod, IMajorRecordGetter getter)
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

            var group = prop.GetValue(mod);
            if (group is not Mutagen.Bethesda.Plugins.Records.IGroup) continue;

            try
            {
                var ovr = _groupOverrideMixin.MakeGenericMethod(groupMajor, getterType)
                    .Invoke(null, new[] { group, getter });
                return ovr as IFallout4MajorRecord;
            }
            catch {  }
        }
        return null;
    }

    private static bool ReplaceOverride(IFallout4Mod mod, IMajorRecordGetter getter) =>
        ReplaceOverrideReturning(mod, getter) != null;

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

    public static IReadOnlyList<string> EditablePlugins() => _mutable.Keys.ToList();

    public static string CopyAsOverride(object? env, string sourcePlugin, string id, string patchPlugin,
        bool overwrite = false)
    {
        if (!ResolveFk(env, id, out var fk))
            return $"'{id}' is not a FormKey and no loaded record has that EditorID.";
        return ResolveConflict(env, fk.ToString(), sourcePlugin, patchPlugin, overwrite);
    }

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

    public static string RemoveListItem(string plugin, string recordId, string field, string value, object? env)
    {
        var mod = EnsureOpen(plugin, env, out var openMsg); if (mod == null) return openMsg;
        var rec = FindMutableRecord(mod, recordId); if (rec == null) return ToolError.Fail($"Record '{recordId}' not found in {plugin}.");

        if (!RemoveListItemFromRecord(rec, field, value, env, out var msg)) return msg;
        MutagenLoader.InvalidateModIndex(plugin); NotifyChanged(plugin);
        return $"{msg} on {recordId}. save_plugin to persist.";
    }

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

    public static string SetComponents(string plugin, string recordId, string componentsJson, object? env)
    {
        var mod = EnsureOpen(plugin, env, out var openMsg); if (mod == null) return openMsg;
        var rec = FindMutableRecord(mod, recordId); if (rec == null) return ToolError.Fail($"Record '{recordId}' not found in {plugin}.");
        if (rec is not ConstructibleObject && rec is not MiscItem)
            return $"set_components applies to COBJ (recipe inputs) and MISC (scrap components); {recordId} is {rec.GetType().Name}.";

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

    private static bool ResolveFk(object? env, string value, out FormKey fk)
    {
        if (FormKey.TryFactory(value, out fk)) return true;
        var cache = MutagenLoader.LinkCache;
        if (cache != null && cache.TryResolve<IMajorRecordGetter>(value, out var rec)) { fk = rec.FormKey; return true; }
        fk = MutagenLoader.ResolveEditorIdToFormKey(env, value);
        return !fk.IsNull;
    }

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

            if (ProtectedPlugins.IsProtected(NormalizePlugin(patchPlugin).name))
                return ToolError.Fail(ProtectedPlugins.RefusalMessage(NormalizePlugin(patchPlugin).name));

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

            if (!string.Equals(ctxs[^1].plugin, badPlugin, StringComparison.OrdinalIgnoreCase)) { skipped++; continue; }
            if (compFk != null && !MutagenLoader.CobjUsesComponent(ctxs[^1].rec, compFk.Value)) { skipped++; continue; }

            var prior = ctxs[^2];
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

    public static string BatchPatchRecords(
        object? env, string patchPlugin, string sourcePlugin, string sig,
        string operationsJson, string? filterField, string? filterValue,
        bool dryRun, int limit)
    {
        if (string.IsNullOrWhiteSpace(sig))         return "Provide record type, e.g. 'ConstructibleObject'.";
        if (string.IsNullOrWhiteSpace(sourcePlugin)) return "Provide source_plugin.";
        if (string.IsNullOrWhiteSpace(patchPlugin))  return "Provide patch_plugin.";

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

    private static bool Fail(ref List<string> errors, Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter rec, string m)
    {
        if (errors.Count < 20) errors.Add($"{rec.EditorID ?? rec.FormKey.ToString()}: {m}");
        return false;
    }

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

    public static string SaveScriptPatch(string patchPlugin, object? env)
    {
        var (name, _) = NormalizePlugin(patchPlugin);
        MutagenLoader.InvalidateModIndex(name);
        NotifyChanged(name);
        return SavePlugin(name, null, env);
    }

    public static void DiscardScriptPatch(string patchPlugin)
    {
        var (name, _) = NormalizePlugin(patchPlugin);
        _mutable.Remove(name);
        _sourcePath.Remove(name);
        MutagenLoader.EditableMods.TryRemove(name, out _);

        MutagenLoader.ReleaseLooseMod(name);
        NotifyChanged(name);
    }

    public static bool TrySetField(object rec, string field, string value, object? env, out string msg) =>
        TrySet(rec, field, value, env, out msg);

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

    private static void ApplyParamStr(string? s, object? env, Action<FormKey> setRecord, Action<int> setNumber)
    {
        if (string.IsNullOrWhiteSpace(s)) return;
        if (int.TryParse(s, out var ni)) setNumber(ni);
        else if (ResolveFk(env, s!, out var fk)) setRecord(fk);
    }

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

    private const string SupportedTypes =
        "BOOK/HOLOTAPE, TERM, WEAP, ARMO, ARMA, ANIO, IDLE, MISC, COBJ, KYWD, AMMO, ALCH, ACTI, CONT, " +
        "FLST, MGEF, PERK, NPC_, QUST, MESG, GLOB (GLOBFLOAT/GLOBINT/GLOBSHORT/GLOBBOOL), AVIF, " +
        "LVLI, LVLN, SPEL, ENCH, FURN, IMAD, LIGH, STAT.";

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

    private static FormKey? NextFreeFormKey(IFallout4Mod mod)
    {
        uint end = IsEsl(mod) ? 0xFFFu : 0xFFFFFFu;
        var used = new HashSet<uint>(mod.EnumerateMajorRecords().Select(r => r.FormKey.ID));
        for (uint id = 0x800; id <= end; id++)
            if (!used.Contains(id)) return new FormKey(mod.ModKey, id);
        return null;
    }

    private static IFallout4MajorRecord? AddNewBySig(IFallout4Mod mod, string sig, string editorId, FormKey fk)
    {
        var r = Fallout4Release.Fallout4;
        switch (sig.ToUpperInvariant())
        {
            case "BOOK": case "HOLOTAPE": { var x = new Book(fk, r) { EditorID = editorId };               mod.Books.Add(x);                return x; }
            case "TERM":                  { var x = new Terminal(fk, r) { EditorID = editorId };           mod.Terminals.Add(x);            return x; }
            case "WEAP":                  { var x = new Weapon(fk, r) { EditorID = editorId };             mod.Weapons.Add(x);              return x; }
            case "ARMO":                  { var x = new Armor(fk, r) { EditorID = editorId };              mod.Armors.Add(x);               return x; }

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

            case "GLOB": case "GLOBFLOAT": { var x = new GlobalFloat(fk, r) { EditorID = editorId, Data = 0 };   mod.Globals.Add(x);          return x; }
            case "GLOBINT":               { var x = new GlobalInt(fk, r) { EditorID = editorId, Data = 0 };     mod.Globals.Add(x);          return x; }
            case "GLOBSHORT":             { var x = new GlobalShort(fk, r) { EditorID = editorId, Data = 0 };   mod.Globals.Add(x);          return x; }
            case "GLOBBOOL":              { var x = new GlobalBool(fk, r) { EditorID = editorId, Data = false };mod.Globals.Add(x);          return x; }

            case "AVIF":                  { var x = new ActorValueInformation(fk, r) { EditorID = editorId }; mod.ActorValueInformation.Add(x); return x; }

            case "LVLI":                  { var x = new LeveledItem(fk, r) { EditorID = editorId };          mod.LeveledItems.Add(x);         return x; }
            case "LVLN":                  { var x = new LeveledNpc(fk, r) { EditorID = editorId };           mod.LeveledNpcs.Add(x);          return x; }

            case "SPEL":                  { var x = new Spell(fk, r) { EditorID = editorId };               mod.Spells.Add(x);               return x; }
            case "ENCH":                  { var x = new ObjectEffect(fk, r) { EditorID = editorId };        mod.ObjectEffects.Add(x);        return x; }

            case "FURN":                  { var x = new Furniture(fk, r) { EditorID = editorId };           mod.Furniture.Add(x);            return x; }
            case "IMAD":                  { var x = new ImageSpaceAdapter(fk, r) { EditorID = editorId };   mod.ImageSpaceAdapters.Add(x);   return x; }

            case "LIGH":                  { var x = new Light(fk, r) { EditorID = editorId };               mod.Lights.Add(x);               return x; }

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

        if (string.Equals(field, "Model", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(field, "nif", StringComparison.OrdinalIgnoreCase))
            field = "Model.File";

        var segments = SplitFieldPath(field);
        if (segments.Count == 0) { msg = "Empty field path."; return false; }

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

    private static bool TryIndex(string seg, out int idx)
    {
        idx = -1;
        return seg.Length >= 3 && seg[0] == '[' && seg[^1] == ']' && int.TryParse(seg[1..^1], out idx);
    }

    private static List<string> SplitFieldPath(string field)
    {
        var segs = new List<string>();
        foreach (var part in field.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            int b = part.IndexOf('[');
            if (b < 0) { segs.Add(part); continue; }
            if (b > 0) segs.Add(part[..b]);
            int j = b;
            while (j < part.Length && part[j] == '[')
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

            else if (t == typeof(bool)) converted = bool.Parse(value);
            else if (t == typeof(float)) converted = float.Parse(value, Inv);
            else if (t == typeof(double)) converted = double.Parse(value, Inv);
            else if (t == typeof(int)) converted = int.Parse(value, Inv);
            else if (t == typeof(uint)) converted = uint.Parse(value, Inv);
            else if (t == typeof(short)) converted = short.Parse(value, Inv);
            else if (t == typeof(ushort)) converted = ushort.Parse(value, Inv);
            else if (t == typeof(byte)) converted = byte.Parse(value, Inv);
            else if (t == typeof(long)) converted = long.Parse(value, Inv);

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
