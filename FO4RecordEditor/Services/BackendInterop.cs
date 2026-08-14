using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using FO4RecordEditor.Models;
using FO4RecordEditor.ViewModels;
using Newtonsoft.Json;

namespace FO4RecordEditor.Services;

/// <summary>
/// Bridges the React frontend (via WebView2 host objects) to the live backend. Reads the env from
/// the shell on each call so it always reflects the currently loaded modlist (not a stale snapshot).
/// Clean AutoDual class with NO explicit COM interface -- the interface + AutoDual together produce
/// two dispatch surfaces and trip "Invalid number of parameters" (0x8002000E) from JS.
/// </summary>
[ClassInterface(ClassInterfaceType.AutoDual)]
[ComVisible(true)]
public class BackendInterop
{
    private readonly ShellViewModel? _shell;
    private readonly IEnumerable<ConflictEntry>? _staticConflicts;
    private readonly object? _staticEnv;

    /// <summary>Live mode (main window): reads the env + scans conflicts from the shell on demand.</summary>
    public BackendInterop(ShellViewModel shell) => _shell = shell;

    /// <summary>Snapshot mode (the standalone conflict window): a pre-computed conflict list + env.</summary>
    public BackendInterop(IEnumerable<ConflictEntry> conflicts, object? env)
    {
        _staticConflicts = conflicts;
        _staticEnv = env;
    }

    private object? Env => _shell?.GameEnvironment ?? _staticEnv;

    public System.Threading.Tasks.Task<string> GetConflicts() =>
        DebugLog.GuardAsync(nameof(GetConflicts), async () =>
        {
            if (_staticConflicts != null) return JsonConvert.SerializeObject(_staticConflicts);
            if (Env == null) return "[]";
            // Scan the loaded load order for conflicts on demand. Host-object methods are invoked on the
            // UI thread, so awaiting (rather than .GetResult()) keeps the window responsive while a large
            // modlist scans on a background thread. WebView2 marshals the Task result back to the JS await.
            var conflicts = await ConflictScanner.ScanAsync(Env);
            return JsonConvert.SerializeObject(conflicts);
        });

    public string GetConflictMatrix(string formKey) =>
        DebugLog.Guard(nameof(GetConflictMatrix), () =>
            JsonConvert.SerializeObject(MutagenLoader.BuildConflictMatrix(Env, formKey)), formKey);

    /// <summary>Records that reference this FormKey (xEdit "Referenced By"). Scans the load order off
    /// the UI thread so a big modlist doesn't freeze the window.</summary>
    public System.Threading.Tasks.Task<string> GetReferencedBy(string formKey) =>
        DebugLog.GuardAsync(nameof(GetReferencedBy), async () =>
        {
            if (Env == null) return "[]";
            var list = await System.Threading.Tasks.Task.Run(() => MutagenLoader.GetReferencedBy(Env, formKey));
            return JsonConvert.SerializeObject(list);
        }, formKey);

    /// <summary>Per-record problems (deleted flag, dangling references) for the winning version.</summary>
    public string GetProblems(string formKey) =>
        DebugLog.Guard(nameof(GetProblems), () =>
            Env == null ? "[]" : JsonConvert.SerializeObject(MutagenLoader.GetRecordProblems(Env, formKey)), formKey);

    /// <summary>Record picker search across the load order (EditorID / FormID), optional type filter.</summary>
    public System.Threading.Tasks.Task<string> SearchRecords(string query, string typeFilter) =>
        DebugLog.GuardAsync(nameof(SearchRecords), async () =>
        {
            if (Env == null) return "[]";
            var hits = await System.Threading.Tasks.Task.Run(() =>
                MutagenLoader.SearchAllRecords(Env, query, string.IsNullOrWhiteSpace(typeFilter) ? null : typeFilter));
            return JsonConvert.SerializeObject(hits);
        }, $"{query} [{typeFilter}]");

    /// <summary>The field tree for one record, as JSON the React editor can render directly
    /// (friendly label, value, EditKind/EnumOptions for dropdowns/checkboxes, summary + children).</summary>
    public string GetRecordTree(string plugin, string id) =>
        DebugLog.Guard(nameof(GetRecordTree), () =>
            JsonConvert.SerializeObject(MutagenLoader.BuildPopulatedFields(id, Env, plugin).Select(NodeDto).ToArray()),
            $"{plugin} {id}");

    // Map a RecordNode to a cycle-free DTO (the node's Parent back-reference would otherwise loop).
    private static object NodeDto(Models.RecordNode n) => new
    {
        key = n.Key,
        label = Rendering.FriendlyNames.Label(n.Key),
        value = n.Value,
        values = n.Values,
        editKind = n.EditKind.ToString(),
        enumOptions = n.EnumOptions,
        isSummary = n.IsSummary,
        hasChildren = n.Children.Count > 0,
        children = n.Children.Select(NodeDto).ToArray(),
    };

    // ---- navigator / workspace / detail rail ----
    // Anything that scans the whole load order runs on a background thread: these are called on
    // every navigator expand and record open, and the UI thread owns the window.

    /// <summary>Load order for the File Browser: name, kind (master/plugin/light), index, editable.</summary>
    public string GetActivePlugins() =>
        DebugLog.Guard(nameof(GetActivePlugins), () =>
            JsonConvert.SerializeObject(MutagenLoader.GetActivePlugins(Env)));

    /// <summary>Record types present across the load order, with counts, for the type-first tree.</summary>
    public System.Threading.Tasks.Task<string> GetRecordTypeIndex() =>
        DebugLog.GuardAsync(nameof(GetRecordTypeIndex), async () =>
        {
            if (Env == null) return "[]";
            var types = await System.Threading.Tasks.Task.Run(() => MutagenLoader.GetRecordTypeIndex(Env));
            return JsonConvert.SerializeObject(types);
        });

    /// <summary>Winning records of one type across the load order (the navigator's lazy expand).</summary>
    public System.Threading.Tasks.Task<string> GetRecordsOfType(string signature, string filter, int limit, int offset) =>
        DebugLog.GuardAsync(nameof(GetRecordsOfType), async () =>
        {
            if (Env == null) return "[]";
            var hits = await System.Threading.Tasks.Task.Run(() =>
                MutagenLoader.GetRecordsOfTypeAcrossLoadOrder(Env, signature, filter, limit <= 0 ? 500 : limit, offset));
            return JsonConvert.SerializeObject(hits);
        }, $"{signature} [{filter}] {offset}+{limit}");

    /// <summary>Structured grid data for the Spreadsheet panel (#51): columns + rows for every
    /// record of one type in a plugin, for the editable bulk-edit table.</summary>
    public string GetRecordsGrid(string plugin, string type, int limit, int offset) =>
        DebugLog.Guard(nameof(GetRecordsGrid),
            () => MutagenLoader.GetRecordsGridJson(Env, plugin, type, limit <= 0 ? 100 : limit, offset),
            $"{plugin} {type} {offset}+{limit}");

    /// <summary>Worldspace/cell breadcrumb for the workspace header.</summary>
    public string GetContainmentPath(string formKey) =>
        DebugLog.Guard(nameof(GetContainmentPath), () =>
            JsonConvert.SerializeObject(MutagenLoader.GetContainmentPath(Env, formKey)), formKey);

    /// <summary>Header fields for the detail rail (FormID, EditorID, base form, class, file).</summary>
    public string GetRecordDetails(string formKey) =>
        DebugLog.Guard(nameof(GetRecordDetails), () =>
            JsonConvert.SerializeObject(MutagenLoader.GetRecordDetails(Env, formKey)), formKey);

    /// <summary>"Plugins Affecting This Record": per-plugin changed/conflicting field counts.</summary>
    public System.Threading.Tasks.Task<string> GetRecordPluginMatrix(string formKey) =>
        DebugLog.GuardAsync(nameof(GetRecordPluginMatrix), async () =>
        {
            var rows = await System.Threading.Tasks.Task.Run(() => MutagenLoader.GetRecordPluginMatrix(Env, formKey));
            return JsonConvert.SerializeObject(rows);
        }, formKey);

    /// <summary>Outgoing FormLinks of the winning version; unresolved ones are reported, not dropped.</summary>
    public System.Threading.Tasks.Task<string> GetDependencies(string formKey) =>
        DebugLog.GuardAsync(nameof(GetDependencies), async () =>
        {
            var deps = await System.Threading.Tasks.Task.Run(() => MutagenLoader.GetDependencies(Env, formKey));
            return JsonConvert.SerializeObject(deps);
        }, formKey);

    /// <summary>Override timeline: one entry per plugin carrying the record, in load order.</summary>
    public System.Threading.Tasks.Task<string> GetHistory(string formKey) =>
        DebugLog.GuardAsync(nameof(GetHistory), async () =>
        {
            var entries = await System.Threading.Tasks.Task.Run(() => MutagenLoader.GetHistory(Env, formKey));
            return JsonConvert.SerializeObject(entries);
        }, formKey);

    /// <summary>Status-bar totals. The record count is the real total, not the plugin count.</summary>
    public System.Threading.Tasks.Task<string> GetLoadOrderSummary() =>
        DebugLog.GuardAsync(nameof(GetLoadOrderSummary), async () =>
        {
            var summary = await System.Threading.Tasks.Task.Run(() => MutagenLoader.GetLoadOrderSummary(Env));
            return JsonConvert.SerializeObject(summary);
        });

    // ---- edit ---- (every result is logged so we can see what the UI changed and why a save failed)
    public string OpenPlugin(string plugin) =>
        DebugLog.Guard(nameof(OpenPlugin), () => WriteService.OpenPlugin(plugin, Env), plugin);
    public string CreatePlugin(string name) =>
        DebugLog.Guard(nameof(CreatePlugin), () => WriteService.CreatePlugin(name), name);
    public string SetField(string plugin, string record, string field, string value) =>
        DebugLog.Guard(nameof(SetField), () => WriteService.SetField(plugin, record, field, value, Env),
            $"{plugin} {record} {field}={value}");
    public string SetComponents(string plugin, string record, string componentsJson) =>
        DebugLog.Guard(nameof(SetComponents), () => WriteService.SetComponents(plugin, record, componentsJson, Env),
            $"{plugin} {record}");
    public string SetConditions(string plugin, string record, string conditionsJson) =>
        DebugLog.Guard(nameof(SetConditions), () => WriteService.SetConditions(plugin, record, conditionsJson, Env),
            $"{plugin} {record}");
    public string AddListItem(string plugin, string record, string field, string value) =>
        DebugLog.Guard(nameof(AddListItem), () => WriteService.AddListItem(plugin, record, field, value, Env),
            $"{plugin} {record} {field}+={value}");
    public string RemoveListItem(string plugin, string record, string field, string value) =>
        DebugLog.Guard(nameof(RemoveListItem), () => WriteService.RemoveListItem(plugin, record, field, value, Env),
            $"{plugin} {record} {field}-={value}");
    public string DeleteRecord(string plugin, string id) =>
        DebugLog.Guard(nameof(DeleteRecord), () => WriteService.DeleteRecord(plugin, id, Env), $"{plugin} {id}");
    /// <summary>Copy as override. Returns a message starting with WriteService.OverwritePrompt when
    /// the target already has this record and <paramref name="overwrite"/> was not set, so the UI can
    /// ask before replacing it.</summary>
    public string CopyAsOverride(string sourcePlugin, string id, string patchPlugin, bool overwrite) =>
        DebugLog.Guard(nameof(CopyAsOverride), () => WriteService.CopyAsOverride(Env, sourcePlugin, id, patchPlugin, overwrite),
            $"{id} {sourcePlugin}->{patchPlugin}");
    public string CopyAsOverrideMany(string itemsJson, string patchPlugin, bool overwrite) =>
        DebugLog.Guard(nameof(CopyAsOverrideMany),
            () => WriteService.CopyAsOverrideMany(Env, itemsJson, patchPlugin, overwrite), patchPlugin);
    public string RevertOverrides(string badPlugin, string patchPlugin, string signature, string containsComponent, bool apply, int limit) =>
        DebugLog.Guard(nameof(RevertOverrides), () => WriteService.RevertOverridesFrom(Env, badPlugin, patchPlugin,
            string.IsNullOrWhiteSpace(signature) ? null : signature,
            string.IsNullOrWhiteSpace(containsComponent) ? null : containsComponent,
            apply, limit <= 0 ? 50 : limit), $"{badPlugin}->{patchPlugin} apply={apply}");
    public string CompactToEsl(string plugin) =>
        DebugLog.Guard(nameof(CompactToEsl), () => WriteService.CompactToEsl(plugin, Env), plugin);
    public string CheckEslEligibility(string plugin) =>
        DebugLog.Guard(nameof(CheckEslEligibility), () => WriteService.CheckEslEligibility(plugin, Env), plugin);
    public string CleanPlugin(string plugin) =>
        DebugLog.Guard(nameof(CleanPlugin), () => WriteService.CleanPlugin(plugin, Env), plugin);
    public string RenumberFormId(string plugin, string record, string newId) =>
        DebugLog.Guard(nameof(RenumberFormId), () => WriteService.RenumberFormId(plugin, record, newId, Env),
            $"{plugin} {record}->{newId}");

    // ---- xEdit parity ----
    public string GetConditions(string plugin, string record) =>
        DebugLog.Guard(nameof(GetConditions), () => WriteService.GetConditionsJson(Env, plugin, record),
            $"{plugin} {record}");
    public string GetConditionsAt(string plugin, string record, string path) =>
        DebugLog.Guard(nameof(GetConditionsAt), () => WriteService.GetConditionsAtPath(Env, plugin, record, path),
            $"{plugin} {record} {path}");
    public string SetConditionsAt(string plugin, string record, string path, string conditionsJson) =>
        DebugLog.Guard(nameof(SetConditionsAt), () => WriteService.SetConditionsAtPath(plugin, record, path, conditionsJson, Env),
            $"{plugin} {record} {path}");
    public string GetConditionFunctions() =>
        DebugLog.Guard(nameof(GetConditionFunctions), WriteService.ConditionFunctionNames);
    public string GetConditionFunctionParams() =>
        DebugLog.Guard(nameof(GetConditionFunctionParams), ConditionFunctions.AsJson);
    /// <summary>"EditorID [FormKey]" for each FormKey passed in, so the editor can show names
    /// instead of raw ids. Unresolvable keys come back as themselves.</summary>
    public string ResolveFormKeyLabels(string formKeysCsv) =>
        DebugLog.Guard(nameof(ResolveFormKeyLabels), () =>
            Newtonsoft.Json.JsonConvert.SerializeObject(
                (formKeysCsv ?? "").Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries)
                    .Distinct()
                    .ToDictionary(k => k, k => MutagenLoader.DescribeFormKey(Env, k))), formKeysCsv);
    public string GetConditionRunOnTypes() =>
        DebugLog.Guard(nameof(GetConditionRunOnTypes), WriteService.ConditionRunOnNames);
    public string CopyAsNewRecord(string sourcePlugin, string id, string targetPlugin, string newEditorId) =>
        DebugLog.Guard(nameof(CopyAsNewRecord),
            () => WriteService.CopyAsNewRecord(Env, sourcePlugin, id, targetPlugin,
                string.IsNullOrWhiteSpace(newEditorId) ? null : newEditorId),
            $"{id} {sourcePlugin}->{targetPlugin}");
    public string RemoveIdenticalToMaster(string plugin, bool apply) =>
        DebugLog.Guard(nameof(RemoveIdenticalToMaster), () => WriteService.RemoveIdenticalToMaster(Env, plugin, apply),
            $"{plugin} apply={apply}");
    public string DeepCopyAsOverride(string sourcePlugin, string id, string patchPlugin, bool apply, bool overwrite) =>
        DebugLog.Guard(nameof(DeepCopyAsOverride),
            () => WriteService.DeepCopyAsOverride(Env, sourcePlugin, id, patchPlugin, apply, overwrite),
            $"{id} {sourcePlugin}->{patchPlugin} apply={apply} overwrite={overwrite}");
    public string ChangeReferencingRecords(string from, string to, string patchPlugin, bool apply) =>
        DebugLog.Guard(nameof(ChangeReferencingRecords),
            () => WriteService.ChangeReferencingRecords(Env, from, to, patchPlugin, apply),
            $"{from}->{to} patch={patchPlugin} apply={apply}");

    // ---- xEdit element menu (Add / Remove / Move / Clear on a list row) ----
    public string DescribeElement(string plugin, string record, string path) =>
        DebugLog.Guard(nameof(DescribeElement), () => ElementService.DescribeElement(Env, plugin, record, path),
            $"{plugin} {record} {path}");
    public string AddElement(string plugin, string record, string path, string template) =>
        DebugLog.Guard(nameof(AddElement), () => ElementService.AddElement(plugin, record, path,
            string.IsNullOrWhiteSpace(template) ? null : template, Env), $"{plugin} {record} {path} <{template}>");
    public string RemoveElement(string plugin, string record, string path) =>
        DebugLog.Guard(nameof(RemoveElement), () => ElementService.RemoveElement(plugin, record, path, Env),
            $"{plugin} {record} {path}");
    public string MoveElement(string plugin, string record, string path, int delta) =>
        DebugLog.Guard(nameof(MoveElement), () => ElementService.MoveElement(plugin, record, path, delta, Env),
            $"{plugin} {record} {path} {delta:+#;-#;0}");
    public string ClearElement(string plugin, string record, string path) =>
        DebugLog.Guard(nameof(ClearElement), () => ElementService.ClearElement(plugin, record, path, Env),
            $"{plugin} {record} {path}");

    public string AddMasters(string plugin, string mastersCsv) =>
        DebugLog.Guard(nameof(AddMasters), () => WriteService.AddMasters(plugin,
            (mastersCsv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), Env),
            $"{plugin} += {mastersCsv}");
    public string RenumberPluginFormIds(string plugin, string start, bool apply) =>
        DebugLog.Guard(nameof(RenumberPluginFormIds), () => WriteService.RenumberPluginFormIds(plugin, start, apply, Env),
            $"{plugin} from {start} apply={apply}");
    public string CreateSeqFile(string plugin, string outputDir) =>
        DebugLog.Guard(nameof(CreateSeqFile), () => WriteService.CreateSeqFile(Env, plugin,
            string.IsNullOrWhiteSpace(outputDir) ? null : outputDir), plugin);
    public string CheckCircularLeveledLists(string plugin) =>
        DebugLog.Guard(nameof(CheckCircularLeveledLists), () => WriteService.CheckCircularLeveledLists(Env, plugin), plugin);
    public string CreateMergedPatch(string plugins, string patchPlugin, bool apply) =>
        DebugLog.Guard(nameof(CreateMergedPatch), () => WriteService.CreateMergedPatch(Env, plugins, patchPlugin, apply),
            $"{plugins}->{patchPlugin} apply={apply}");

    // ---- save ----
    public string ResolveConflict(string formKey, string winner, string patch) =>
        DebugLog.Guard(nameof(ResolveConflict), () => WriteService.ResolveConflict(Env, formKey, winner, patch),
            $"{formKey} winner={winner} patch={patch}");
    public string SavePatch(string patch) =>
        DebugLog.Guard(nameof(SavePatch), () => WriteService.SavePlugin(patch, null, Env), patch);
    public string SavePlugin(string plugin, string path) =>
        DebugLog.Guard(nameof(SavePlugin), () =>
            WriteService.SavePlugin(plugin, string.IsNullOrWhiteSpace(path) ? null : path, Env), $"{plugin} {path}");

    // Return JSON (not string[]): a raw array comes back over the WebView2 bridge as a remote proxy,
    // which the JS caller can't reliably spread/iterate. Every other method returns JSON for this reason.
    /// <summary>
    /// The record types present in ONE plugin, with that plugin's own counts ("Keyword (33)").
    /// The spreadsheet's type list used to come from the load-order-wide index, so it offered 149
    /// types with global counts and most choices returned an empty grid for the chosen plugin.
    /// </summary>
    public System.Threading.Tasks.Task<string> GetPluginRecordTypes(string plugin) =>
        DebugLog.GuardAsync(nameof(GetPluginRecordTypes), async () =>
        {
            if (Env == null) return "[]";
            var types = await System.Threading.Tasks.Task.Run(() => MutagenLoader.QueryRecordTypes(Env, plugin));
            return JsonConvert.SerializeObject(types);
        }, plugin);

    public string GetEditablePlugins() =>
        DebugLog.Guard(nameof(GetEditablePlugins), () => JsonConvert.SerializeObject(WriteService.EditablePlugins().ToArray()));
}
