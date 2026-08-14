using System.Text.Json;

namespace FO4RecordEditor.Services;

/// <summary>
/// Executes the AI's tool calls against the loaded plugin data (via MutagenLoader's
/// cached per-mod index). The env is resolved lazily through a provider so it always
/// reflects the current Load Order / opened ESPs.
/// </summary>
public sealed class PluginToolExecutor
{
    private readonly Func<object?> _envProvider;
    private readonly Func<string?>? _mo2PathProvider;
    private readonly Func<string?>? _ckWikiPathProvider;

    public PluginToolExecutor(Func<object?> envProvider, Func<string?>? mo2PathProvider = null,
        Func<string?>? ckWikiPathProvider = null)
    {
        _envProvider = envProvider;
        _mo2PathProvider = mo2PathProvider;
        _ckWikiPathProvider = ckWikiPathProvider;
    }

    public sealed record ToolSpec(string Name, string Description, object Schema);

    // Single source of truth for the tools; projected into Anthropic (input_schema) and
    // MCP (inputSchema) formats so the API path and the Claude Code / MCP path stay in sync.
    private static readonly ToolSpec[] _specs =
    [
        new("list_plugins",
            "List every loaded plugin (ESP/ESM/ESL) the user has open. Call this first to discover what data is available.",
            new { type = "object", properties = new { }, required = Array.Empty<string>() }),
        new("list_record_types",
            "List the record-type signatures present in a given plugin, each with its record count, e.g. 'WEAP (42)'.",
            new
            {
                type = "object",
                properties = new { plugin = new { type = "string", description = "Plugin file name, e.g. 'MyMod.esp'." } },
                required = new[] { "plugin" }
            }),
        new("list_records",
            "List records of a specific type in a plugin (EditorID + FormKey). Use to browse a group. Paginated: " +
            "the header reports 'Showing X-Y of TOTAL' and the next-page offset; pass 'offset' to page through large groups.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    type = new { type = "string", description = "Record signature, e.g. 'WEAP'." },
                    limit = new { type = "integer", description = "Max records to return (default 100)." },
                    offset = new { type = "integer", description = "Skip this many records first (for paging; default 0)." }
                },
                required = new[] { "plugin", "type" }
            }),
        new("list_records_summary",
            "Get a compact field table for ALL records of a type in a plugin -- one row per record with " +
            "key fields (scalars, FormLinks, list counts) shown inline. Use this to survey an entire " +
            "record group AT ONCE instead of calling get_record in a loop. Essential for bulk conflict " +
            "analysis: e.g. all COBJ records with their CreatedObject + component count + workbench, " +
            "all WEAP with damage values, all NPC_ with race + level. Follow up with get_record ONLY " +
            "on specific records that need a complete field dump. 'type' is the Mutagen class name: " +
            "ConstructibleObject, Weapon, Armor, Npc, LeveledItem, FormList, etc.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    type = new { type = "string", description = "Record type (Mutagen class name), e.g. 'ConstructibleObject'." },
                    limit = new { type = "integer", description = "Max records to return (default 200, max 500)." },
                    offset = new { type = "integer", description = "Skip this many records first (for paging; default 0)." }
                },
                required = new[] { "plugin", "type" }
            }),
        new("search_records",
            "Search a SINGLE plugin for records whose EditorID or FormKey contains the query text. Returns EditorID, type and FormKey. (Use search_all to search the whole load order and match display Name too.)",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    query = new { type = "string" },
                    limit = new { type = "integer", description = "Max matches (default 50)." },
                    offset = new { type = "integer", description = "Skip this many matches first (for paging; default 0)." }
                },
                required = new[] { "plugin", "query" }
            }),
        new("search_all",
            "Search the ENTIRE load order for records whose EditorID, FormID, or display Name contains the query " +
            "(case-insensitive). Optional 'type' signature filter (e.g. 'KYWD' or 'WEAP,ARMO'). De-duplicated to the " +
            "winning version per FormKey. Returns EditorID (type) [FormKey] \"Name\" <plugin>. Use this when you don't " +
            "know which plugin defines something, or to match by in-game name.",
            new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string" },
                    type = new { type = "string", description = "Optional signature filter, comma-separated (e.g. 'WEAP,ARMO')." },
                    limit = new { type = "integer", description = "Max matches (default 100)." }
                },
                required = new[] { "query" }
            }),
        new("resolve_editorid",
            "Resolve an EditorID (or a FormKey) to its 'XXXXXX:Plugin.esp' FormKey across the whole load order. " +
            "Use before set_field/set_conditions/script-property calls when you have an EditorID but need the FormKey, " +
            "or to confirm a record exists. Returns the FormKey string, or a not-found message.",
            new
            {
                type = "object",
                properties = new { id = new { type = "string", description = "EditorID or FormKey to resolve." } },
                required = new[] { "id" }
            }),
        new("get_scripts",
            "Read back the Papyrus scripts (VMAD) attached to a record and their property values. Use to VERIFY " +
            "attach_script / set_script_property results (e.g. confirm a quest's bridge properties or a holotape's " +
            "linked terminal). Accepts a FormKey or EditorID; reads the winning version.",
            new
            {
                type = "object",
                properties = new { id = new { type = "string", description = "FormKey or EditorID of the record." } },
                required = new[] { "id" }
            }),
        new("diff_records",
            "Field-level diff of two records (each resolved to its winning version) -- lines only in A are marked '-', " +
            "lines only in B '+'. Useful to verify a copy_as_override matches its source, or to compare two similar " +
            "records. Accepts FormKeys or EditorIDs.",
            new
            {
                type = "object",
                properties = new
                {
                    a = new { type = "string", description = "FormKey or EditorID of record A." },
                    b = new { type = "string", description = "FormKey or EditorID of record B." }
                },
                required = new[] { "a", "b" }
            }),
        new("get_record",
            "Get the full field dump of a single record by FormKey ('001234:Plugin.esp') or EditorID, within a plugin.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    id = new { type = "string", description = "FormKey or EditorID of the record." }
                },
                required = new[] { "plugin", "id" }
            }),
        new("get_conflicts",
            "Show EVERY plugin that overrides a record in load order, which one WINS, and a " +
            "PER-FIELD diff table showing each plugin's value for every field that differs. " +
            "Severity tags: [OVERRIDE] = 2 distinct values; [CONFLICT] = 3+ distinct; [CRITICAL] = " +
            "winner value is empty/null while another plugin had a real value. " +
            "The LAST plugin is ALWAYS the winner -- it is what the game actually uses. " +
            "Call this BEFORE deciding what to patch. Accepts FormKey ('054C84:Fallout4.esm') or EditorID.",
            new
            {
                type = "object",
                properties = new { id = new { type = "string", description = "FormKey or EditorID of the record." } },
                required = new[] { "id" }
            }),
        new("get_winning_record",
            "Get the EFFECTIVE (winning) version of a record -- the field values the game actually uses " +
            "after all overrides are applied -- plus which plugin won. Use this instead of get_record when " +
            "you care about the real in-game result. Accepts a FormKey or an EditorID.",
            new
            {
                type = "object",
                properties = new { id = new { type = "string", description = "FormKey or EditorID of the record." } },
                required = new[] { "id" }
            }),
        new("search_robco_configs",
            "Search RobCo Patcher config files (F4SE\\Plugins\\RobCo_Patcher\\**\\*.ini) for text. These " +
            "INI files patch records at RUNTIME (scrap recipes, leveled lists, weapons, components, etc.) " +
            "and are NOT in any plugin -- so if a behaviour can't be explained by the plugin records, a " +
            "RobCo patch is the usual cause. Search by FormID ('054C83'), EditorID, or a component/keyword " +
            "name. Returns the mod + matching config lines.",
            new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "Text to find, e.g. a FormID, EditorID, or component name." },
                    limit = new { type = "integer", description = "Max matching lines (default 60)." }
                },
                required = new[] { "query" }
            }),

        new("scan_conflicts",
            "Scan the ENTIRE load order for conflicting records (the same scan as the app's conflict view) " +
            "to survey what conflicts exist before planning fixes. Returns each conflicting record with the " +
            "plugins that touch it and the WINNER. By default only conflicts that involve a mod (non-vanilla) " +
            "are returned. Use this to get the big picture; then get_conflicts / get_winning_record on a " +
            "specific record for detail. Can be slow on a large modlist.",
            new
            {
                type = "object",
                properties = new
                {
                    mod_only = new { type = "boolean", description = "Only conflicts involving a non-vanilla plugin (default true)." },
                    type = new { type = "string", description = "Optional record-type filter, e.g. 'ConstructibleObject' or 'Weapon'." },
                    limit = new { type = "integer", description = "Max records to return (default 200)." },
                    hide_grouped = new { type = "boolean", description = "Default true: hide conflicts where every touching plugin belongs to one declared ModGroup (list_mod_groups / create_mod_group) -- those are the intended kind, not the accidental kind." }
                },
                required = Array.Empty<string>()
            }),
        new("create_mod_group",
            "Declare a ModGroup: a named set of two or more plugins that are meant to be used together, so " +
            "conflicts BETWEEN them stop being reported as problems by scan_conflicts (xEdit ModGroups). Use " +
            "for a framework plus its official patches, or a compatibility-patch bundle for one mod suite.",
            new
            {
                type = "object",
                properties = new
                {
                    name = new { type = "string" },
                    plugins = new { type = "array", items = new { type = "string" }, description = "Two or more plugin filenames." }
                },
                required = new[] { "name", "plugins" }
            }),
        new("update_mod_group",
            "Rename a ModGroup and/or replace its plugin list.",
            new
            {
                type = "object",
                properties = new
                {
                    name = new { type = "string", description = "The group to update." },
                    new_name = new { type = "string", description = "Optional: rename the group." },
                    plugins = new { type = "array", items = new { type = "string" }, description = "Optional: replace the plugin list entirely." }
                },
                required = new[] { "name" }
            }),
        new("delete_mod_group",
            "Remove a ModGroup. The plugins themselves are untouched; only the suppression it granted goes away.",
            new
            {
                type = "object",
                properties = new { name = new { type = "string" } },
                required = new[] { "name" }
            }),
        new("list_mod_groups",
            "List every declared ModGroup and its plugins.",
            new { type = "object", properties = new { }, required = Array.Empty<string>() }),
        new("resolve_asset",
            "Which loose file or BA2 actually serves a game-relative asset path (xEdit's ResourceExists/" +
            "ResourceContainerList). Answers 'does Meshes\\Weapons\\x.nif exist anywhere in this load " +
            "order, and where' WITHOUT you having to already know which archive to look in. Searches " +
            "every mod folder in MO2 priority order plus the game Data folder, loose files before that " +
            "root's archives. Use this to VERIFY a MODL/texture path instead of guessing one.",
            new
            {
                type = "object",
                properties = new
                {
                    path = new { type = "string", description = @"Game-relative path, e.g. 'Meshes\Clutter\Rock01.nif' or 'Textures\Sky\Clouds_d.dds'." },
                    extract = new { type = "boolean", description = "Also materialize the winning copy as a real file on disk (extracting it from its BA2 if needed) and report the path -- so you can hand it to nif_inspect/bgsm_inspect. Default false." },
                    limit = new { type = "integer", description = "Max providers to list (default 25)." }
                },
                required = new[] { "path" }
            }),
        new("get_referenced_by",
            "List records across the load order that REFERENCE the given record (xEdit 'Referenced By') -- i.e. " +
            "what would break if you changed or removed it. Accepts a FormKey or EditorID. Use for impact " +
            "analysis before editing or deleting a record.",
            new
            {
                type = "object",
                properties = new
                {
                    id = new { type = "string", description = "FormKey or EditorID of the record." },
                    limit = new { type = "integer", description = "Max referencing records (default 200)." }
                },
                required = new[] { "id" }
            }),
        new("get_problems",
            "Report problems with a single record's WINNING version: whether it is flagged DELETED, and any " +
            "FormLink references that don't resolve in the load order (dangling, crash-risk). Accepts a " +
            "FormKey or EditorID. Use to validate a record before/after editing.",
            new
            {
                type = "object",
                properties = new { id = new { type = "string", description = "FormKey or EditorID of the record." } },
                required = new[] { "id" }
            }),
        new("scan_broken_refs",
            "Scan a single plugin for FormLinks that point to records no longer present in the load order " +
            "(crash-risk dangling references). Skips vanilla DLC master refs that Mutagen cannot index. " +
            "Results are grouped by master plugin so you can see which mod version mismatch is the cause.",
            new
            {
                type = "object",
                properties = new { plugin = new { type = "string", description = "Plugin file name to scan, e.g. 'FallenWorldCrafting_Compat.esp'." } },
                required = new[] { "plugin" }
            }),
        new("scan_all_plugins",
            "Scan every non-vanilla plugin in the full load order for dangling FormLinks (crash risks). " +
            "Returns a per-plugin summary grouped by missing master so you can identify which mods have " +
            "broken references and what target plugin caused the mismatch. Slower than scan_broken_refs " +
            "but covers the entire modlist in one call.",
            new
            {
                type = "object",
                properties = new { },
                required = Array.Empty<string>()
            }),

        // ---- write tools (author plugins) ----
        new("open_plugin",
            "Open an existing plugin for editing so records can be added or modified. The 'plugin' arg " +
            "accepts a bare file name ('MyMod.esp') OR a full path ('D:\\\\...\\\\MyMod.esp'). Required before " +
            "create_record/set_field on a plugin loaded from the game environment rather than created with create_plugin.",
            new
            {
                type = "object",
                properties = new { plugin = new { type = "string", description = "Plugin file name or full path, e.g. 'MyMod.esp' or 'D:\\\\mods\\\\X\\\\MyMod.esp'." } },
                required = new[] { "plugin" }
            }),
        new("create_plugin",
            "Create a brand-new, empty plugin (ESP) the AI can author. Changes stay in memory until save_plugin.",
            new
            {
                type = "object",
                properties = new { name = new { type = "string", description = "Plugin file name, e.g. 'MyMod.esp'." } },
                required = new[] { "name" }
            }),
        new("create_record",
            "Add a new record of a given type to a plugin. Existing loaded plugins are opened for editing automatically. Returns the new FormKey. " +
            "Supported types: BOOK/HOLOTAPE, TERM, WEAP, ARMO, ARMA, ANIO, IDLE, MISC, COBJ, KYWD, AMMO, ALCH, ACTI, CONT, FLST, MGEF, PERK, NPC_, QUST, MESG, GLOB, AVIF, LVLI, LVLN, SPEL, ENCH, FURN, IMAD, LIGH, STAT. " +
            "GLOB defaults to a float global; use GLOBINT/GLOBSHORT/GLOBBOOL for other global value types. Set a global's value via set_field on 'Data'. " +
            "LVLI/LVLN/SPEL/ENCH are shells: add leveled entries with add_leveled_entry and spell/enchant effects with set_magic_effects (their lists are structs, not FormLinks). " +
            "Build perk effects with set_perk_effects and quest internals with set_quest_aliases/set_quest_stages/set_quest_objectives.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    type = new { type = "string", description = "Record signature, e.g. 'BOOK' for a holotape." },
                    editorId = new { type = "string" }
                },
                required = new[] { "plugin", "type", "editorId" }
            }),
        new("create_cell",
            "Create an interior CELL in a plugin. create_record cannot make cells: they live in a Block/SubBlock " +
            "tree under the mod's Cells group rather than a flat top-level group. Interior cells are self-contained " +
            "(no worldspace, no vanilla cell override), so they are the safe place to park references a plugin owns " +
            "outright -- e.g. map markers a script relocates at runtime. Add references with create_placed_object.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    editorId = new { type = "string" },
                    name = new { type = "string", description = "Optional in-game cell name." }
                },
                required = new[] { "plugin", "editorId" }
            }),
        new("create_placed_object",
            "Create a placed reference (REFR) inside a cell the plugin owns. create_record cannot make these: a " +
            "placed object is not a top-level record, it lives in a Cell's Persistent/Temporary list. The cell must " +
            "be in THIS plugin -- make one with create_cell, or copy_as_override an existing cell in first. " +
            "For a MAP MARKER: set base to the vanilla MapMarker static (000010:Fallout4.esm) and pass mapMarkerName " +
            "-- without map marker data the reference is just an invisible static and no marker appears.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    cell = new { type = "string", description = "EditorID or FormKey of a cell in THIS plugin." },
                    baseObject = new { type = "string", description = "FormKey or EditorID of the base object, e.g. '000010:Fallout4.esm' for a map marker." },
                    editorId = new { type = "string", description = "Optional EditorID for the reference." },
                    x = new { type = "number" },
                    y = new { type = "number" },
                    z = new { type = "number" },
                    rotZ = new { type = "number", description = "Z rotation in radians." },
                    persistent = new { type = "boolean", description = "Default true. Persistent refs go in the cell's Persistent list and can be reached by script from anywhere." },
                    initiallyDisabled = new { type = "boolean", description = "Default false." },
                    mapMarkerName = new { type = "string", description = "Set to attach map-marker data (turns the ref into a real map marker)." },
                    mapMarkerType = new { type = "string", description = "Marker icon, e.g. 'Graveyard', 'Settlement', 'Cave'. Default Cave." },
                    mapMarkerVisible = new { type = "boolean", description = "Default false -- visible on the map from the start." }
                },
                required = new[] { "plugin", "cell", "baseObject" }
            }),
        new("disable_previs",
            "xEdit's 'Fallout4 - Disable PreVis' script: for an EXTERIOR cell that has precombine data, copies it " +
            "as an override, sets the NoPreVis record flag, and clears its previs/precombine data (the typed " +
            "equivalents of xEdit's VISI/RVIS/PCMB/XCRI subrecords) so the engine stops using stale previs after a " +
            "cell edit. Precombine/previs conflicts (edits silently hidden, or a cell breaking outright) are one " +
            "of the most common real FO4 modding failures; use this on any cell you've placed/removed/moved a " +
            "static in. Refuses interior cells and cells with no precombine data to begin with. Dry-run by default.",
            new
            {
                type = "object",
                properties = new
                {
                    cell = new { type = "string", description = "EditorID or FormKey of the exterior cell." },
                    patch_plugin = new { type = "string" },
                    apply = new { type = "boolean", description = "Default false (preview only)." }
                },
                required = new[] { "cell", "patch_plugin" }
            }),
        new("set_field",
            "Set a field on a record. Supports scalar/text fields (Name, Value, Weight, Description), booleans " +
            "(pass 'true'/'false' to check/UNCHECK a flag), enums (pass the value name), FormLink references " +
            "(a FormKey like '001234:Plugin.esp' or an EditorID), the mesh path ('Model'/'nif' -> Model.File), " +
            "and NESTED/INDEXED paths: dotted ('Model.File') and array indices ('Effects[0].RunImmediately', " +
            "'Components[0].Count', 'Conditions[1].Data.RunOnType'). Existing plugins are opened automatically.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    record = new { type = "string", description = "FormKey or EditorID of the record." },
                    field = new { type = "string", description = "Property name, e.g. 'Name'." },
                    value = new { type = "string" }
                },
                required = new[] { "plugin", "record", "field", "value" }
            }),
        new("add_list_item",
            "Append an item to a list field (e.g. add a keyword to a record's 'Keywords', or a FormLink to a " +
            "FormList's 'Items'). Pass a FormKey ('001234:Plugin.esp') or an EditorID of a loaded record.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    record = new { type = "string", description = "FormKey or EditorID of the record." },
                    field = new { type = "string", description = "List field, e.g. 'Keywords' or 'Items'." },
                    value = new { type = "string", description = "FormKey or EditorID to add." }
                },
                required = new[] { "plugin", "record", "field", "value" }
            }),
        new("attach_script",
            "Attach a Papyrus script to a record (creates the script adapter if needed). Then configure it with " +
            "set_script_property. This is how a holotape links to its terminal program in FO4.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    record = new { type = "string", description = "FormKey or EditorID of the record." },
                    script = new { type = "string", description = "Script name, e.g. 'DefaultHolotapeProgram'." }
                },
                required = new[] { "plugin", "record", "script" }
            }),
        new("set_script_property",
            "Set a property on a record's attached script. 'type' is 'object' (a FormLink -- pass a FormKey or EditorID, " +
            "e.g. the terminal), 'int', 'float', 'bool', or 'string'. Omit type to infer it.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    record = new { type = "string" },
                    script = new { type = "string" },
                    name = new { type = "string", description = "Property name on the script." },
                    value = new { type = "string" },
                    type = new { type = "string", description = "object | int | float | bool | string (optional)." }
                },
                required = new[] { "plugin", "record", "script", "name", "value" }
            }),
        new("check_plugin",
            "Run an error check over a plugin (xEdit-style): lists deleted records and references to masters " +
            "the plugin doesn't declare. Read-only.",
            new
            {
                type = "object",
                properties = new { plugin = new { type = "string" } },
                required = new[] { "plugin" }
            }),
        new("compact_to_esl",
            "Renumber a plugin's records into the ESL range (0x800-0xFFF) and fix all references, so a too-big " +
            "plugin can become a light master. In memory until you save_plugin (save with a .esl path).",
            new
            {
                type = "object",
                properties = new { plugin = new { type = "string" } },
                required = new[] { "plugin" }
            }),
        new("check_esl_eligibility",
            "Read-only precheck for compact_to_esl: reports whether a plugin's native records would fit the " +
            "ESL/Small range (0x800-0xFFF) and flags anything that would make compact_to_esl fail (too many " +
            "records, or out-of-range records living in nested groups like cells/placed objects that can't be " +
            "re-keyed) -- all WITHOUT mutating the plugin. Use this before compact_to_esl to rule out its two " +
            "failure modes ahead of the destructive renumber, especially across a folder of candidate plugins.",
            new
            {
                type = "object",
                properties = new { plugin = new { type = "string" } },
                required = new[] { "plugin" }
            }),
        new("renumber_formid",
            "Change one record's FormID within its plugin (xEdit 'Change FormID') and repoint every " +
            "reference to it in the SAME plugin. References in OTHER plugins are NOT rewritten. Call " +
            "get_referenced_by first and patch external referrers if any. In memory until save_plugin. " +
            "To split a DUPLICATE FormKey (two records sharing one id, which makes the plugin unsavable), " +
            "pass the EditorID of the WRONG twin as 'record' and set repoint_references=false so the " +
            "shared references stay on the id kept by the correct twin.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    record = new { type = "string", description = "EditorID or current FormKey of the record. Use the EditorID to disambiguate a duplicate FormKey." },
                    new_id = new { type = "string", description = "New 6-digit hex object id, e.g. '000F99'." },
                    repoint_references = new { type = "boolean", description = "Default true: repoint in-plugin references onto the new id. Set FALSE when splitting a duplicate FormKey so existing references stay on the retained twin." }
                },
                required = new[] { "plugin", "record", "new_id" }
            }),
        new("clean_plugin",
            "Clean a plugin xEdit-style: undelete deleted records and mark them initially disabled (UDR), " +
            "which removes hard deletes that cause crashes. Call save_plugin afterwards.",
            new
            {
                type = "object",
                properties = new { plugin = new { type = "string" } },
                required = new[] { "plugin" }
            }),
        new("backup_plugin",
            "Copy the plugin's original on-disk file to a timestamped .bak file beside it BEFORE making any edits. " +
            "ALWAYS call this before the first write to any existing plugin in a session. Safe to call multiple times -- " +
            "each call produces a distinct backup (e.g. MyMod.esp.20250623_142031.bak).",
            new
            {
                type = "object",
                properties = new { plugin = new { type = "string", description = "Plugin file name, e.g. 'MyMod.esp'." } },
                required = new[] { "plugin" }
            }),
        new("save_plugin",
            "Write a plugin to disk. By default OVERWRITES the file it was opened from (or the full path it was " +
            "created with); newly created plugins go to the Output folder. Pass 'path' to choose a location. " +
            "NOTE: a plugin currently loaded via 'Open MO2' is memory-mapped and LOCKED by this editor, so it " +
            "can't be overwritten in place -- save_plugin then writes a '<name>.new' file beside it and says so. " +
            "If that happens, DO NOT tell the user to close MO2 (it's the editor's own lock); instead save under a " +
            "NEW plugin name not in the load order, or tell them to close the editor and rename the .new file.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    path = new { type = "string", description = "Optional full output path; defaults to <app>/Output/<plugin>." }
                },
                required = new[] { "plugin" }
            }),
        new("copy_as_override",
            "Copy a record from one plugin into a patch plugin as an OVERRIDE (xEdit 'Copy as Override Into'). " +
            "The patch is opened/created automatically; afterwards edit the override with set_field, set_components, " +
            "set_conditions, add_list_item, or remove_list_item. Make the patch load LAST so it wins. " +
            "REFUSES if the patch loads BEFORE the plugin being overridden (the game would ignore it), and refuses " +
            "with 'EXISTS:' if the patch already overrides this record unless overwrite=true. The origin plugin is " +
            "declared as a master automatically.",
            new
            {
                type = "object",
                properties = new
                {
                    source_plugin = new { type = "string", description = "Plugin to copy the record FROM (the version you want)." },
                    id = new { type = "string", description = "FormKey ('00008E:MAIM.esp') or EditorID of the record." },
                    patch_plugin = new { type = "string", description = "Patch plugin to copy the override INTO, e.g. 'FallenWorld_CraftingFixes.esp'." },
                    overwrite = new { type = "boolean", description = "Replace an override the patch already has for this record. Without it the call REFUSES and reports what is already there, so an existing edit is never silently discarded." }
                },
                required = new[] { "source_plugin", "id", "patch_plugin" }
            }),
        new("copy_as_new_record",
            "Duplicate a record into a plugin under a BRAND NEW FormID (xEdit 'Copy as new record into'), rather " +
            "than overriding the original the way copy_as_override does. This is how you make a variant of an " +
            "existing item. The target plugin is opened/created automatically.",
            new
            {
                type = "object",
                properties = new
                {
                    source_plugin = new { type = "string", description = "Plugin to copy the record FROM. Omit to take the winning version." },
                    id = new { type = "string", description = "FormKey ('00008E:MAIM.esp') or EditorID of the record to duplicate." },
                    target_plugin = new { type = "string", description = "Plugin the new record is created in." },
                    new_editor_id = new { type = "string", description = "EditorID for the copy. Defaults to the source EditorID with 'DUP' appended." }
                },
                required = new[] { "id", "target_plugin" }
            }),
        new("remove_identical_to_master",
            "Remove Identical-to-Master (ITM) records from a plugin -- the other half of the standard cleaning " +
            "pass that clean_plugin's UDR handling does not cover. An ITM is an override whose every field equals " +
            "the version it overrides, so it costs a conflict row and changes nothing. DRY RUN by default: it " +
            "lists what it would remove. Pass apply=true to actually remove, then save_plugin.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    apply = new { type = "boolean", description = "Default false (dry run). True actually removes the records." }
                },
                required = new[] { "plugin" }
            }),
        new("create_merged_patch",
            "Build a patch plugin carrying the WINNING version of every record two or more of the selected plugins " +
            "touch (xEdit 'Create Merged Patch'). DRY RUN by default, reporting how many records each plugin wins. " +
            "Pass apply=true to write them, then make the patch load last and save_plugin.",
            new
            {
                type = "object",
                properties = new
                {
                    plugins = new { type = "string", description = "Comma-separated plugin filenames to merge. Omit for the whole load order." },
                    patch_plugin = new { type = "string", description = "Patch plugin the merged records are written into." },
                    apply = new { type = "boolean", description = "Default false (dry run)." }
                },
                required = new[] { "patch_plugin" }
            }),
        new("get_conditions",
            "Read a record's Conditions list back in exactly the JSON shape set_conditions accepts. set_conditions " +
            "REPLACES the whole list, so call this first when adding or changing one condition, edit the array, " +
            "and hand it back.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string", description = "Plugin whose version to read. Omit for the winning version." },
                    record = new { type = "string", description = "FormKey or EditorID of the record." },
                    path = new { type = "string", description = "Optional nested list, e.g. 'Effects[0].Conditions'. Omit for the record's own Conditions." }
                },
                required = new[] { "record" }
            }),
        new("deep_copy_as_override",
            "Copy a record into a patch as an override, ALONG WITH every record it references that is owned by " +
            "the SAME source plugin, so the copy is self-contained instead of dangling. Only follows links owned " +
            "by the source plugin (a link into Fallout4.esm or another already-loaded master is left alone -- " +
            "that record already exists everywhere). Refuses CELL and WRLD: xEdit's deep copy for those means " +
            "something this tool cannot reproduce (their placed-reference/cell-block tree), so copy those with " +
            "copy_as_override plus create_placed_object instead. DRY RUN by default. Existing target " +
            "overrides are never replaced unless overwrite=true is supplied explicitly.",
            new
            {
                type = "object",
                properties = new
                {
                    source_plugin = new { type = "string", description = "Plugin to copy FROM. Omit to take the winning version." },
                    id = new { type = "string", description = "FormKey or EditorID of the root record." },
                    patch_plugin = new { type = "string" },
                    apply = new { type = "boolean", description = "Default false (dry run)." },
                    overwrite = new { type = "boolean", description = "Default false. Set true only after reviewing the EXISTS refusal and approving replacement of every existing override in the deep-copy set." }
                },
                required = new[] { "id", "patch_plugin" }
            }),
        new("change_referencing_records",
            "Point every record that references 'from' at 'to' instead (xEdit 'Change Referencing Records'), by " +
            "copying each referencing record into the patch as an override and rewriting its FormLinks. Use this " +
            "to retire a duplicate record without leaving dangling links: repoint everything at the record " +
            "you're keeping, then delete_record the duplicate. DRY RUN by default.",
            new
            {
                type = "object",
                properties = new
                {
                    from = new { type = "string", description = "FormKey or EditorID of the record being retired." },
                    to = new { type = "string", description = "FormKey or EditorID of the record to point at instead." },
                    patch_plugin = new { type = "string" },
                    apply = new { type = "boolean", description = "Default false (dry run)." }
                },
                required = new[] { "from", "to", "patch_plugin" }
            }),
        new("element_add",
            "xEdit's element 'Add': insert a NEW default-constructed entry into a list at a path, then edit its " +
            "fields with set_field. Unlike add_list_item (FormLinks only), this works for STRUCT lists -- " +
            "conditions, effects, leveled entries, components. Call element_describe first to learn which types " +
            "the list accepts when it holds an abstract type (e.g. a Conditions list takes ConditionFloat or " +
            "ConditionGlobal).",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    record = new { type = "string", description = "FormKey or EditorID." },
                    path = new { type = "string", description = "The list ('Conditions') to append to, or an entry ('Conditions[1]') to insert before." },
                    template = new { type = "string", description = "Which type to create, when the list accepts more than one. Defaults to the first." }
                },
                required = new[] { "plugin", "record", "path" }
            }),
        new("element_remove",
            "xEdit's element 'Remove': drop one entry from a list. The path must name the ENTRY, e.g. " +
            "'Conditions[1]', not the list.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    record = new { type = "string" },
                    path = new { type = "string", description = "e.g. 'Effects[0].Conditions[2]'." }
                },
                required = new[] { "plugin", "record", "path" }
            }),
        new("element_move",
            "xEdit's element 'Move up'/'Move down': reorder an entry within its list. Order is meaningful for " +
            "conditions, leveled entries and effects, so this is a real edit.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    record = new { type = "string" },
                    path = new { type = "string", description = "The entry to move, e.g. 'Conditions[2]'." },
                    delta = new { type = "integer", description = "-1 to move up, 1 to move down." }
                },
                required = new[] { "plugin", "record", "path" }
            }),
        new("element_clear",
            "xEdit's element 'Clear': empty a list without removing the field itself.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    record = new { type = "string" },
                    path = new { type = "string", description = "The list to empty, e.g. 'Conditions'." }
                },
                required = new[] { "plugin", "record", "path" }
            }),
        new("element_describe",
            "Which element actions are legal at a path, and which types the list accepts. Returns " +
            "{canAdd, templates, elementType, canRemove, canMoveUp, canMoveDown, canClear, count}. Read-only.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string", description = "Omit for the winning version." },
                    record = new { type = "string" },
                    path = new { type = "string" }
                },
                required = new[] { "record", "path" }
            }),
        new("set_conditions_at",
            "Replace the Condition list at a nested path (xEdit's per-effect conditions). set_conditions only " +
            "reaches a record's OWN Conditions; a magic effect keeps its own at 'Effects[0].Conditions' and a " +
            "perk two deep at 'Effects[0].Conditions[0].Conditions', where the outer list is the run-on tab " +
            "wrapper. Read the current list with get_conditions first (it takes the same path).",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    record = new { type = "string", description = "FormKey or EditorID of the record." },
                    path = new { type = "string", description = "e.g. 'Effects[0].Conditions' or 'Effects[0].Conditions[0].Conditions'." },
                    conditions = new { type = "string", description = "JSON array in set_conditions' schema. Replaces the whole list." }
                },
                required = new[] { "plugin", "record", "path", "conditions" }
            }),
        new("add_masters",
            "Declare one or more plugins as masters of this plugin (xEdit 'Add Masters...'). Needed BEFORE " +
            "authoring a reference into a file the plugin does not already master -- the reference has nowhere " +
            "valid to point otherwise. save_plugin re-derives the master list from actual references, so write " +
            "the reference before saving or the added master will not survive.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    masters = new { type = "array", items = new { type = "string" }, description = "Plugin filenames to declare, e.g. ['DLCCoast.esm']. Each must be loaded." }
                },
                required = new[] { "plugin", "masters" }
            }),
        new("renumber_plugin_formids",
            "Renumber EVERY record in a plugin consecutively from a base object id, repointing all in-plugin " +
            "references (xEdit 'Renumber FormIDs from...'). Use to resolve a FormID collision with another mod. " +
            "renumber_formid does one record. DRY RUN by default. References from OTHER plugins into this one " +
            "will break -- they still point at the old ids.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    start = new { type = "string", description = "Hex object id to start from, e.g. '000800' or '001000'." },
                    apply = new { type = "boolean", description = "Default false (dry run)." }
                },
                required = new[] { "plugin", "start" }
            }),
        new("create_seq_file",
            "Write the .seq file a plugin's start-game-enabled quests need (xEdit 'Create SEQ File'). Without it " +
            "those quests silently never start on an existing save, so this is a required build step for quest " +
            "mods. Skips quests whose previous version already starts at game start.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    output_dir = new { type = "string", description = "Folder to write into. Defaults to <Output>/Seq. Deploy as Data/Seq/<plugin>.seq." }
                },
                required = new[] { "plugin" }
            }),
        new("check_circular_leveled_lists",
            "Find leveled lists that contain themselves, directly or through another list (xEdit 'Check for " +
            "Circular Leveled Lists'). The engine hangs or crashes resolving one. Read-only; walks the winning " +
            "version of every LVLI/LVLN across the load order.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string", description = "Optional: only report cycles mentioning this plugin." },
                    limit = new { type = "integer", description = "Max cycles to report (default 200)." }
                },
                required = Array.Empty<string>()
            }),
        new("delete_record",
            "Remove a record entirely from a plugin (xEdit 'Remove') -- e.g. delete an incomplete stub record, " +
            "or drop an unwanted override from a patch.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    id = new { type = "string", description = "FormKey or EditorID of the record to remove." }
                },
                required = new[] { "plugin", "id" }
            }),
        new("remove_list_item",
            "Remove a FormLink entry (by FormKey or EditorID) from a list field -- e.g. drop an item from a " +
            "FormList's 'Items', or a keyword from 'Categories'/'Keywords'.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    record = new { type = "string", description = "FormKey or EditorID of the record." },
                    field = new { type = "string", description = "List field, e.g. 'Items'." },
                    value = new { type = "string", description = "FormKey or EditorID to remove." }
                },
                required = new[] { "plugin", "record", "field", "value" }
            }),
        new("set_components",
            "Replace a record's component list. Works on COBJ (crafting-recipe INPUT materials) AND MISC " +
            "(the scrap COMPONENTS a junk/part item breaks down into at a workbench). Pass a JSON array of " +
            "{\"component\":\"FormKeyOrEditorId\",\"count\":N} where component is a CMPO (e.g. c_Steel). Example: " +
            "[{\"component\":\"c_Steel\",\"count\":3},{\"component\":\"c_Circuitry\",\"count\":1}].",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    record = new { type = "string", description = "FormKey or EditorID of the COBJ or MISC." },
                    components = new { type = "string", description = "JSON array of {component, count}." }
                },
                required = new[] { "plugin", "record", "components" }
            }),
        new("set_conditions",
            "Replace a COBJ's condition list (when a recipe is available). Pass a JSON array of condition objects: " +
            "{\"function\":\"GetGlobalValue\",\"param1\":\"000126:MAIM.esp\",\"operator\":\"==\",\"value\":0,\"runOn\":\"Subject\"}. " +
            "function = GetGlobalValue/GetBaseValue/GetItemCount/HasKeyword/GetStageDone/...; param1/param2 = a FormKey/EditorID " +
            "(record arg, e.g. the global or actor-value form) or an integer; operator = == != > >= < <=; value = the constant " +
            "compared against; compareGlobal = optional global FormKey to compare against instead of a constant; runOn = " +
            "Subject/Target/Reference; reference = the ref FormKey when runOn=Reference (e.g. '000014:Fallout4.esm' for PlayerRef); " +
            "flags = optional comma-separated condition flags, e.g. 'UseOr' to combine this condition with OR instead of AND.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    record = new { type = "string", description = "FormKey or EditorID of the COBJ." },
                    conditions = new { type = "string", description = "JSON array of condition objects." }
                },
                required = new[] { "plugin", "record", "conditions" }
            }),
        new("add_leveled_entry",
            "Append a weighted entry to a LeveledItem (LVLI) or LeveledNpc (LVLN). Leveled-list entries are " +
            "structs (reference + level + count + chance-none), so add_list_item CANNOT build them -- use this. " +
            "level = min player/zone level for this entry; count = how many; chance_none = % chance the entry " +
            "rolls nothing (0 = always). Call once per entry.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    record = new { type = "string", description = "FormKey or EditorID of the LVLI/LVLN." },
                    reference = new { type = "string", description = "FormKey or EditorID of the item/npc/leveled-list to add." },
                    level = new { type = "integer", description = "Minimum level for this entry (default 1)." },
                    count = new { type = "integer", description = "Count for this entry (default 1)." },
                    chance_none = new { type = "number", description = "Percent chance (0-100) this entry yields nothing (default 0)." }
                },
                required = new[] { "plugin", "record", "reference" }
            }),
        new("set_perk_effects",
            "Replace a PERK's effects list (perk entry-point effects are structs that no other tool can build). " +
            "Pass a JSON array; each object has \"kind\": \"ability\" {ability:\"<SPEL/perk-ability>\"}, " +
            "\"modifyValue\" {entryPoint, modification:\"Set|Add|Multiply\", value}, or \"activateChoice\" " +
            "{buttonLabel, spell?:\"<SPEL>\", entryPoint?=Activate}. Optional per-effect: rank, priority, and " +
            "conditions (same objects as set_conditions, each may add tabIndex). entryPoint = an " +
            "APerkEntryPointEffect.EntryType name (e.g. Activate, ModAttackDamage, CalculateMyCriticalHitChance).",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    record = new { type = "string", description = "FormKey or EditorID of the PERK." },
                    effects = new { type = "string", description = "JSON array of perk-effect objects." }
                },
                required = new[] { "plugin", "record", "effects" }
            }),
        new("set_magic_effects",
            "Replace a Spell (SPEL) or Object Effect/enchantment (ENCH) Effects list -- the MGEF chain with " +
            "magnitude/area/duration. Pass a JSON array: {\"effect\":\"<MGEF FormKey/EditorID>\",\"magnitude\":N," +
            "\"area\":N,\"duration\":N,\"conditions\":[...]}. Conditions use the same objects as set_conditions.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    record = new { type = "string", description = "FormKey or EditorID of the SPEL/ENCH." },
                    effects = new { type = "string", description = "JSON array of {effect, magnitude, area, duration, conditions}." }
                },
                required = new[] { "plugin", "record", "effects" }
            }),
        new("set_quest_aliases",
            "Replace a QUEST's reference aliases (structs add_list_item can't build). JSON array: " +
            "{\"id\":0,\"name\":\"PlayerAlias\",\"forcedReference\":\"000014:Fallout4.esm\"} for a forced ref " +
            "(player = 000014:Fallout4.esm), or \"uniqueActor\":\"<NPC>\" for a unique actor. Optional flags " +
            "(comma-separated AQuestAlias.Flag names). A persistent quest with a forced player alias is the " +
            "standard host for the PrismaUI bridge and for OnPlayerLoadGame events.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    record = new { type = "string", description = "FormKey or EditorID of the QUST." },
                    aliases = new { type = "string", description = "JSON array of alias objects." }
                },
                required = new[] { "plugin", "record", "aliases" }
            }),
        new("set_quest_stages",
            "Replace a QUEST's stages. JSON array: {\"index\":10,\"logEntry\":\"Journal text...\"," +
            "\"flags\":\"RunOnStart\",\"complete\":false}. index = stage number; logEntry = optional journal " +
            "text shown at this stage; complete=true marks the log entry as completing the quest.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    record = new { type = "string", description = "FormKey or EditorID of the QUST." },
                    stages = new { type = "string", description = "JSON array of stage objects." }
                },
                required = new[] { "plugin", "record", "stages" }
            }),
        new("set_quest_objectives",
            "Replace a QUEST's objectives (the on-screen quest goals). JSON array: " +
            "{\"index\":10,\"displayText\":\"Hunt the beast\",\"flags\":\"ORObjective\"}.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    record = new { type = "string", description = "FormKey or EditorID of the QUST." },
                    objectives = new { type = "string", description = "JSON array of objective objects." }
                },
                required = new[] { "plugin", "record", "objectives" }
            }),
        new("set_furniture_markers",
            "Replace a FURN's marker parameters -- the entry markers that tell the engine WHERE to put the " +
            "actor using it. MarkerParameters is a STRUCT list, and create_record FURN makes a shell with NONE; " +
            "a furniture with zero markers cannot be entered at all (a bed reports 'someone else is using it', " +
            "a workbench reads as unusable). Vanilla equivalents all carry at least one {enabled, entryTypes:255} " +
            "marker. JSON array: [{\"enabled\":true,\"entryTypes\":255,\"offsetX\":0,\"offsetY\":0,\"offsetZ\":0," +
            "\"rotationZ\":0}], or [{}] for one default marker at the origin.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    record = new { type = "string", description = "FormKey or EditorID of the FURN." },
                    markers = new { type = "string", description = "JSON array of marker objects; [{}] for a single default." }
                },
                required = new[] { "plugin", "record", "markers" }
            }),
        new("set_message_buttons",
            "Replace a MESG record's menu buttons and flag it as a MessageBox (without that flag it renders " +
            "as a corner notification and Show() returns nothing). MenuButtons is a STRUCT list, so " +
            "add_list_item cannot author it. JSON array of strings [\"Use\",\"Move\",\"Pack up\",\"Cancel\"] or " +
            "objects [{\"text\":\"Use\"}]. Papyrus Message.Show() returns the zero-based index in this order.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    record = new { type = "string", description = "FormKey or EditorID of the MESG." },
                    buttons = new { type = "string", description = "JSON array of button texts (or {text} objects), in order." }
                },
                required = new[] { "plugin", "record", "buttons" }
            }),
        new("compile_papyrus",
            "Compile Papyrus source .psc -> .pex. TWO ENGINES, chosen by 'engine': 'builtin' is this tool's own " +
            "lexer/parser/resolver/type-checker/codegen/.pex-writer and needs NO PapyrusCompiler.exe (it does " +
            "still need the vanilla base script SOURCES on the import path -- those are just .psc text, not the " +
            "Windows-only compiler); 'creationkit' shells out to PapyrusCompiler.exe; 'auto' (the DEFAULT) uses " +
            "the CK when one is installed and the built-in engine otherwise. 'source' auto-detects " +
            "a single .psc file or a folder (set all=true for a folder). NAMESPACES are handled automatically and " +
            "namespaced output is written into namespace subfolders (IHO\\Foo.pex). F4SE + vanilla base scripts " +
            "are on the import path automatically; add extra extender roots via 'imports' (semicolon-separated). " +
            "'output' defaults to the source folder. release=-r, which strips DebugOnly/BetaOnly calls in BOTH " +
            "engines. Passing a .pas (assembly) is rejected with guidance (compile .psc, not .pas). Returns a " +
            "'RESULT: X succeeded, Y failed' summary. THE BUILT-IN ENGINE REFUSES rather than guessing when a " +
            "callee is not on the roots -- its arity is unknowable once optional parameters exist -- so a failure " +
            "naming PAP0050/PAP0051 usually means a missing 'imports' root, not bad source.",
            new
            {
                type = "object",
                properties = new
                {
                    source = new { type = "string", description = "A .psc file path, or a folder of scripts (with all=true)." },
                    output = new { type = "string", description = "Output dir for .pex (default: the source folder)." },
                    imports = new { type = "string", description = "Extra import roots, semicolon-separated (base scripts added automatically)." },
                    flags = new { type = "string", description = "Flags file (default Institute_Papyrus_Flags.flg; the built-in engine has the table built in and does not need it)." },
                    all = new { type = "boolean", description = "Compile every .psc in the source folder." },
                    optimize = new { type = "boolean", description = "Optimize (-op). Creation Kit engine only." },
                    release = new { type = "boolean", description = "Release build: strips DebugOnly/BetaOnly calls (-r)." },
                    engine = new { type = "string", description = "'auto' (default), 'builtin' (no CK needed), or 'creationkit'." },
                    debug_info = new { type = "boolean", description = "Emit line numbers and property groups (default true). Built-in engine only." },
                    compiler_path = new { type = "string", description = "Override path to PapyrusCompiler.exe." }
                },
                required = new[] { "source" }
            }),
        new("decompile_papyrus",
            "Decompile compiled Papyrus .pex -> source .psc with the built-in FO4 decompiler (no external tool). " +
            "'source' auto-detects: a single .pex, or a folder/whole-mod (recurses ALL subfolders for .pex). " +
            "For a single .pex it returns the source inline; it SAVES when write=true OR an 'output' folder is given " +
            "(folder mode always saves, default output = source folder). Namespaced scripts (IHO:Foo) are written " +
            "into namespace subfolders (IHO\\Foo.psc) so they recompile as-is. Declarations are reconstructed " +
            "exactly; bodies are best-effort (temps inlined, jumps -> If/Else/While) and validated at full parity " +
            "with original source. assembly=true emits a faithful .pas bytecode listing (inspect-only; NOT " +
            "compilable). Returns a 'RESULT: X/Y decompiled' summary + OUTPUT path.",
            new
            {
                type = "object",
                properties = new
                {
                    source = new { type = "string", description = "A .pex file, or a folder of .pex files." },
                    output = new { type = "string", description = "Output dir when writing (default: the source folder)." },
                    assembly = new { type = "boolean", description = "Emit faithful bytecode disassembly instead of reconstructed source." },
                    write = new { type = "boolean", description = "Write .psc/.pas files to disk instead of returning text inline." }
                },
                required = new[] { "source" }
            }),
        new("revert_overrides",
            "BATCH-fix a mod that injected the same bad edit across many records (e.g. a corrupted recipe " +
            "template). For every record where 'bad_plugin' is currently the WINNER, this restores the " +
            "version loaded just before it, forwarded into a patch plugin -- in ONE call, no per-record loop. " +
            "Defaults to a DRY RUN that lists what it would do; pass apply=true to write them. Optionally " +
            "filter by 'signature' (e.g. COBJ) and/or 'contains_component' (a FormKey/EditorID like the " +
            "hubflower) to target exactly the corrupted records.",
            new
            {
                type = "object",
                properties = new
                {
                    bad_plugin = new { type = "string", description = "The plugin whose overrides to undo, e.g. 'FallenWorldCrafting_Compat.esp'." },
                    patch_plugin = new { type = "string", description = "Patch to write the reverts into, e.g. 'FallenWorld_CraftingFixes.esp'." },
                    signature = new { type = "string", description = "Optional record type filter, e.g. 'COBJ'." },
                    contains_component = new { type = "string", description = "Optional: only revert COBJs whose winning version uses this component (FormKey/EditorID)." },
                    apply = new { type = "boolean", description = "false (default) = dry-run preview; true = write the reverts." },
                    limit = new { type = "integer", description = "Max records to list in the dry-run preview (default 50)." }
                },
                required = new[] { "bad_plugin", "patch_plugin" }
            }),
        new("batch_patch_records",
            "Apply field operations to ALL records of a type in a plugin IN ONE CALL -- the bulk write " +
            "equivalent of list_records_summary for reading. Instead of N copy_as_override + N set_field " +
            "calls, this handles 3000+ records at once. Defaults to dry_run=true (safe preview). " +
            "Pass dry_run=false to write. 'operations' is a JSON array of {op, field, value}: " +
            "op = 'set' (default), 'add' (list append), 'remove' (list remove). " +
            "Optional filter_field + filter_value narrow to only records whose field contains that value " +
            "(FormLink fields match on EditorID or FormKey string). " +
            "Always dry-run first to confirm the record count and filter, then pass dry_run=false. " +
            "Example: fix all 3000 COBJ workbench keywords in one call with filter_field='WorkbenchKeyword' " +
            "filter_value='OldWorkbench' and op set WorkbenchKeyword to the new FormKey.",
            new
            {
                type = "object",
                properties = new
                {
                    source_plugin = new { type = "string", description = "Plugin whose records to patch." },
                    patch_plugin  = new { type = "string", description = "Patch plugin to write overrides into." },
                    type          = new { type = "string", description = "Record type (Mutagen class name), e.g. 'ConstructibleObject'." },
                    operations    = new { type = "string", description = "JSON array of operations: [{\"op\":\"set\",\"field\":\"WorkbenchKeyword\",\"value\":\"001234:Fallout4.esm\"}]." },
                    filter_field  = new { type = "string", description = "Optional: only patch records where this field contains filter_value." },
                    filter_value  = new { type = "string", description = "Filter value (substring match; FormLinks matched by EditorID or FormKey)." },
                    dry_run       = new { type = "boolean", description = "true (default) = preview only; false = write the patch." },
                    limit         = new { type = "integer", description = "Max records to process (default 5000)." }
                },
                required = new[] { "source_plugin", "patch_plugin", "type", "operations" }
            }),
        new("run_script",
            "Run a C# script you author to make PER-RECORD edits across many records in ONE call -- for " +
            "changes batch_patch_records can't express because each record needs different values " +
            "(e.g. per-recipe component swaps, conditional condition edits). The script gets a 'host' " +
            "object; the tool compiles + runs it against the loaded records and writes overrides into " +
            "patch_plugin. ALWAYS dry_run=true first (runs against a throwaway patch, reports counts + " +
            "your Log lines, writes nothing), then dry_run=false to apply.\n" +
            "host API:\n" +
            "  Discovery (getters): host.Cobjs(\"Mod.esp\"); host.Records(\"Weapon\",\"Mod.esp\").\n" +
            "  Override: var c = host.Cobj(g); (COBJ) or host.Override(g) (any) -> mutable override in the patch.\n" +
            "  Components: host.HasComponent(g,\"Steel\"); host.AddComponent(c,\"Steel\",5); " +
            "host.RemoveComponent(c,\"Aluminum\"); host.SetCount(c,\"Screw\",3); " +
            "host.Swap(c,\"HubFlower\",\"Carrot\") (keeps count); host.ClearComponents(c).\n" +
            "  Conditions: host.AddCondition(c,\"HasPerk\",param1:\"Gun Nut\",op:\">=\",value:1); " +
            "host.RemoveConditions(c,\"HasPerk\",param1:\"Gun Nut\"); host.ClearConditions(c).\n" +
            "  Fields: host.Set(c,\"Priority\",\"5\"). Util: host.Resolve(\"EditorID\"); host.Log(msg).\n" +
            "Components/conditions take a FormKey ('01FAA5:Fallout4.esm') or EditorID. Loop over discovery, " +
            "branch on each record's own data, override only the ones you change. Example: " +
            "foreach (var g in host.Cobjs(\"Mod.esp\")) { if (host.HasComponent(g,\"HubFlower\")) " +
            "{ var c = host.Cobj(g); host.Swap(c,\"HubFlower\",\"Carrot\"); } } host.Log($\"done {host.Applied}\");",
            new
            {
                type = "object",
                properties = new
                {
                    script       = new { type = "string", description = "C# script body using the 'host' API. No class/Main wrapper; top-level statements." },
                    patch_plugin = new { type = "string", description = "Patch plugin to write overrides into, e.g. 'FW_CraftingFixes.esp'." },
                    dry_run      = new { type = "boolean", description = "true (default) = preview only (throwaway patch, nothing saved); false = apply + save." }
                },
                required = new[] { "script", "patch_plugin" }
            }),
        new("reload_plugin",
            "Evict a plugin from the editor session and re-open it fresh from disk. Fixes stale " +
            "in-memory state that causes check_plugin to report false-positive 'undeclared master' errors " +
            "after save_plugin writes new master references. Use this when check_plugin complains about a " +
            "master that the binary file already declares correctly.",
            new
            {
                type = "object",
                properties = new { plugin = new { type = "string", description = "Plugin file name, e.g. 'FWC_NonFWC_Schematics.esp'." } },
                required = new[] { "plugin" }
            }),
        new("strip_masters_clean",
            "Load a plugin binary, drop every COBJ condition whose param/reference FormKey belongs to " +
            "one of the named masters, then save via Mutagen so that the master list and all FormID " +
            "high-bytes are recomputed correctly. Use to fix master-index corruption caused by binary " +
            "header surgery (e.g. the ITO strip). source_path may be a .bak file. Defaults to a dry run; " +
            "pass dry_run=false to write. output_path defaults to source_path (overwrites in place).",
            new
            {
                type = "object",
                properties = new
                {
                    source_path = new { type = "string", description = "Full path to the binary plugin to load, e.g. 'E:\\\\...\\\\FWC_NonFWC_Schematics.esp.bak'." },
                    masters = new { type = "string", description = "JSON array of master file names to strip, e.g. '[\"ITO.esp\",\"ITOBeta.esp\"]'." },
                    output_path = new { type = "string", description = "Full path to write the result (optional; defaults to source_path)." },
                    dry_run = new { type = "boolean", description = "true (default) = preview only; false = write the file." }
                },
                required = new[] { "source_path", "masters" }
            }),
        new("list_masters",
            "List a plugin's declared masters in header order: index, on-disk size (when found), and " +
            "whether anything IN THIS PLUGIN actually references it. A master reported UNUSED is exactly " +
            "the kind save_plugin's automatic ordering silently drops on write. Use this to inspect a " +
            "plugin's master table directly instead of guessing from check_plugin's error text.",
            new
            {
                type = "object",
                properties = new { plugin = new { type = "string" } },
                required = new[] { "plugin" }
            }),
        new("reorder_masters",
            "Set a plugin's master order to an EXACT permutation of its CURRENT masters and write it " +
            "immediately, bypassing save_plugin's automatic load-order-derived ordering. Call list_masters " +
            "first to see the current set. This is a manual-repair tool for when the automatic ordering " +
            "isn't available or produced a wrong result -- a plain save_plugin call afterwards re-derives " +
            "the order from the live load order and overwrites whatever you set here. 'order' must contain " +
            "every current master exactly once (same names, no additions, no removals) or the call is refused.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    order = new
                    {
                        type = "array",
                        items = new { type = "string" },
                        description = "The plugin's current masters, reordered. Must be an exact permutation -- see list_masters."
                    }
                },
                required = new[] { "plugin", "order" }
            }),
        new("set_light_flag",
            "Set or clear a plugin's header ESL ('light master' / Small) flag directly -- the same bit " +
            "xEdit's 'ESL flag' checkbox sets. compact_to_esl only renumbers FormIDs into the 0x800-0xFFF " +
            "range and never touches this bit, so a compacted plugin still needs this call (or a .esl file " +
            "extension at save time) to actually behave as light. In memory until save_plugin.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    light = new { type = "boolean", description = "true = set the ESL flag, false = clear it. Default true." }
                },
                required = new[] { "plugin" }
            }),
        new("set_localized_flag",
            "Set or clear a plugin's header Localized flag -- the bit that makes Mutagen automatically " +
            "read/write this plugin's translated text (FULL/DESC/etc) through Strings\\<plugin>_<lang>." +
            "STRINGS/.DLSTRINGS/.ILSTRINGS instead of storing it inline. Reading and writing through those " +
            "files already works transparently once this flag is set (get_record/set_field/save_plugin need " +
            "no special handling) -- this is just the one missing piece, flipping the bit itself. Setting it " +
            "with no Strings\\ folder next to the plugin yet means every translated field reads back empty " +
            "until save_plugin (or an added Strings\\ folder) produces one.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    localized = new { type = "boolean", description = "true = set the Localized flag, false = clear it. Default true." }
                },
                required = new[] { "plugin" }
            }),
        new("nif_import",
            "Author a game-ready Fallout 4 static NIF from a Blender-exported OBJ mesh (no NifSkope, no " +
            "Creation Kit). Builds a BSTriShape with a BSLightingShaderProperty, texture slots, computed " +
            "normals+tangents, BSXFlags, and (optionally) a box collision derived from the mesh bounds. " +
            "Auto-runs verify after writing. Point a record's mesh at the result with set_field " +
            "(field 'Model' / 'nif') on a STAT/WEAP/MISC/ARMO. Returns a 'RESULT:' line.",
            new
            {
                type = "object",
                properties = new
                {
                    obj_path = new { type = "string", description = "Full path to the input .obj (Blender export)." },
                    out_nif = new { type = "string", description = "Full path to write the .nif." },
                    material = new { type = "string", description = "BGSM material path stored on the shader, e.g. 'Materials\\\\mymod\\\\thing.bgsm' (optional)." },
                    tex_diffuse = new { type = "string", description = "Diffuse texture path, slot 0, e.g. 'Textures\\\\mymod\\\\thing_d.dds' (optional)." },
                    tex_normal = new { type = "string", description = "Normal texture path, slot 1 (optional)." },
                    collision = new { type = "boolean", description = "Add a box collision sized to the mesh bounds (OL_STATIC). Default false." },
                    from_blender = new { type = "boolean", description = "Convert Blender Y-up axes to NIF Z-up. Set true if the OBJ was exported Y-up. Default false." }
                },
                required = new[] { "obj_path", "out_nif" }
            }),
        new("nif_inspect",
            "Inspect a Fallout 4 NIF and return a JSON summary: FO4 header check, per-shape " +
            "{name, verts, tris, skinned, shader type, texture slots, tangents}, plus whether it has " +
            "BSXFlags and collision. The programmatic replacement for opening a NIF in NifSkope.",
            new
            {
                type = "object",
                properties = new { nif_path = new { type = "string", description = "Full path to the .nif file." } },
                required = new[] { "nif_path" }
            }),
        new("nif_verify",
            "Verify a Fallout 4 NIF loads and is structurally game-ready: FO4 header, every shape has " +
            "verts/tris, a shader, a diffuse texture, tangents, and the file has BSXFlags. Returns a " +
            "'RESULT: N checks, M failed' line followed by [ok]/[FAIL] per check.",
            new
            {
                type = "object",
                properties = new { nif_path = new { type = "string", description = "Full path to the .nif file." } },
                required = new[] { "nif_path" }
            }),
        new("nif_fix",
            "Repair common NIF breakages without NifSkope: recompute missing tangents, add a missing " +
            "BSXFlags, trim texture paths, and fix shader/BSX flags. Writes a corrected copy and reports " +
            "the actions taken. Run nif_verify afterwards to confirm.",
            new
            {
                type = "object",
                properties = new
                {
                    nif_path = new { type = "string", description = "Full path to the input .nif file." },
                    out_nif = new { type = "string", description = "Full path to write the fixed .nif (may equal nif_path to overwrite)." }
                },
                required = new[] { "nif_path", "out_nif" }
            }),
        new("archive_list",
            "List the entries inside a Fallout 4 BA2 (or BSA) archive: each file's in-archive path and " +
            "uncompressed size. Reads the real archive via Mutagen's own reader, no Archive2.exe needed. " +
            "Use 'filter' (a path substring) to narrow a large archive before extracting.",
            new
            {
                type = "object",
                properties = new
                {
                    archive_path = new { type = "string", description = "Full path to the .ba2/.bsa file." },
                    filter = new { type = "string", description = "Optional path substring filter, e.g. 'Meshes\\\\mymod\\\\'." },
                    limit = new { type = "integer", description = "Max entries to list (default 500)." }
                },
                required = new[] { "archive_path" }
            }),
        new("archive_extract",
            "Extract ONE file out of a BA2/BSA archive to disk. Call archive_list first to get the exact " +
            "in-archive path.",
            new
            {
                type = "object",
                properties = new
                {
                    archive_path = new { type = "string", description = "Full path to the .ba2/.bsa file." },
                    inner_path = new { type = "string", description = "The file's path INSIDE the archive, as shown by archive_list." },
                    out_path = new { type = "string", description = "Full path to write the extracted file to." }
                },
                required = new[] { "archive_path", "inner_path", "out_path" }
            }),
        new("archive_extract_all",
            "Extract ALL (or filter-matched) files from a BA2/BSA archive into a folder, preserving the " +
            "archive's internal folder structure. Refuses rather than silently truncating if the match count " +
            "exceeds 'limit' -- narrow with 'filter' first for a big archive.",
            new
            {
                type = "object",
                properties = new
                {
                    archive_path = new { type = "string", description = "Full path to the .ba2/.bsa file." },
                    out_dir = new { type = "string", description = "Folder to extract into (created if missing)." },
                    filter = new { type = "string", description = "Optional path substring filter, e.g. 'Scripts\\\\Source\\\\'." },
                    limit = new { type = "integer", description = "Max entries to extract before refusing (default 2000)." }
                },
                required = new[] { "archive_path", "out_dir" }
            }),
        new("papyrus_function_lookup",
            "Look up a Papyrus function's signature (Syntax/Parameters/Return Value) from an offline " +
            "Creation Kit Wiki HTML mirror, instead of reading whole wiki pages yourself. Works out of the " +
            "box against the mirror bundled with the app; pass '--ck-wiki <folder>' at launch (or set " +
            "CkWikiPath in Settings) only to point at a different or newer mirror. " +
            "Pass 'script' when the function name alone is ambiguous across multiple script types.",
            new
            {
                type = "object",
                properties = new
                {
                    function = new { type = "string", description = "Function name, e.g. 'GetBaseObject'." },
                    script = new { type = "string", description = "Owning script, e.g. 'ActiveMagicEffect' (optional but disambiguates)." }
                },
                required = new[] { "function" }
            }),
        new("papyrus_script_info",
            "Get a Papyrus script's overview from the offline Creation Kit Wiki mirror: what it Extends, " +
            "its Global Functions, Member Functions, and Events (name + one-line description each). Works " +
            "out of the box against the bundled mirror; '--ck-wiki <folder>' at launch (or CkWikiPath in " +
            "Settings) only overrides which mirror is used. Use this to see a script's full " +
            "function list before calling papyrus_function_lookup on a specific one.",
            new
            {
                type = "object",
                properties = new { script = new { type = "string", description = "Script name, e.g. 'ObjectReference' or 'Actor'." } },
                required = new[] { "script" }
            }),
        new("papyrus_check",
            "Check Papyrus SOURCE (.psc) with NO Creation Kit installed -- the built-in front end, not " +
            "PapyrusCompiler.exe. 'source' auto-detects a single .psc or a folder (recursed). Reports " +
            "'file(line,col): error CODE: message' per problem. Three passes: syntax, then (semantic=true, the " +
            "DEFAULT) name resolution and type checking. NAME AND TYPE REPORTING SWITCHES OFF for a file whose " +
            "parent, import or a named type is not on the roots -- otherwise every inherited member reads as " +
            "undefined -- and those files are counted separately, so 'clean' never means 'could not tell'. " +
            "Pass 'imports' (semicolon-separated roots) to widen what it can see. This still stops short of " +
            "codegen: use compile_papyrus to actually produce a .pex.",
            new
            {
                type = "object",
                properties = new
                {
                    source = new { type = "string", description = "A .psc file path, or a folder of scripts (recursed)." },
                    semantic = new { type = "boolean", description = "Resolve names and check types too (default true)." },
                    imports = new { type = "string", description = "Extra source roots, semicolon-separated (base scripts added automatically)." }
                },
                required = new[] { "source" }
            }),
        new("graph_validate",
            "Check a node graph (.fograph) WITHOUT compiling it. Reports every problem against the NODE and PIN " +
            "that caused it, not a line in generated text, so a failure names the thing you would click on. " +
            "Codes are GRA####, mirroring the PAP#### the Papyrus front end uses. Same checks graph_compile " +
            "runs first, so a graph that validates clean here will not fail structurally there.",
            new
            {
                type = "object",
                properties = new
                {
                    graph = new { type = "string", description = "Path to a .fograph document." },
                    imports = new { type = "string", description = "Extra script roots, semicolon-separated." }
                },
                required = new[] { "graph" }
            }),
        new("graph_compile",
            "Compile a node graph to Papyrus. The graph becomes readable .psc source, which the built-in " +
            "compiler turns into a .pex -- so the output is source you can read, diff and hand-edit, not an " +
            "opaque binary. Set source_only to stop after generating the .psc. With 'output' it writes both " +
            "beside each other; without it the source is returned inline. A failure reports the offending NODE " +
            "and shows the generated source, since you never wrote that text and cannot inspect it otherwise.",
            new
            {
                type = "object",
                properties = new
                {
                    graph = new { type = "string", description = "Path to a .fograph document." },
                    output = new { type = "string", description = "Folder to write the .psc and .pex into." },
                    imports = new { type = "string", description = "Extra script roots, semicolon-separated." },
                    source_only = new { type = "boolean", description = "Stop after generating .psc (default false)." }
                },
                required = new[] { "graph" }
            }),
        new("graph_palette_search",
            "Find node types a graph may contain. The palette is generated live from whatever scripts are on " +
            "the import roots, so it covers the base game, F4SE and any extender the roots include -- it is " +
            "not a fixed list. Returns definition ids in the form graph documents reference them by " +
            "(call:Script.Fn, global:Script.Fn, event:Script.Ev, prop.get:Script.Prop, plus built-ins such as " +
            "branch and op.add). Use graph_node_info for one type's pins.",
            new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "Text to match against a member or script name." },
                    imports = new { type = "string", description = "Extra script roots, semicolon-separated." },
                    limit = new { type = "integer", description = "Maximum entries to return (default 30)." }
                },
                required = new[] { "query" }
            }),
        new("graph_node_info",
            "Describe one node type: its pins, their direction, whether each carries control flow or a value, " +
            "the type each value pin takes, and which are optional with what default. This is what you need to " +
            "author wires into a graph document by hand.",
            new
            {
                type = "object",
                properties = new
                {
                    node_type = new { type = "string", description = "A definition id, as graph_palette_search returns." },
                    imports = new { type = "string", description = "Extra script roots, semicolon-separated." }
                },
                required = new[] { "node_type" }
            }),
        new("papyrus_outline",
            "List everything a .psc declares -- script header, imports, structs and their members, custom " +
            "events, groups, properties, variables, functions, events and states -- each with its signature " +
            "and source position. Use this instead of reading a long script end to end when you only need to " +
            "know what it exposes. Works on a file with syntax errors too (it reports what could still be " +
            "read), and needs no Creation Kit.",
            new
            {
                type = "object",
                properties = new
                {
                    source = new { type = "string", description = "Path to the .psc file." }
                },
                required = new[] { "source" }
            }),
        new("papyrus_definition",
            "Go to definition: given a position in a .psc, report where the symbol under it is declared, with " +
            "its signature and doc comment. Resolves locals, parameters, this script's members, its Extends " +
            "chain, imported scripts, and script/type names, searching the file's own folder plus any " +
            "'imports' roots plus the detected base scripts. It has NO type checker, so a member reached " +
            "through an expression whose type is not written down (GetOwner().Foo) returns 'not resolved' " +
            "rather than a guess.",
            new
            {
                type = "object",
                properties = new
                {
                    source = new { type = "string", description = "Path to the .psc file the position is in." },
                    line = new { type = "integer", description = "1-based line number." },
                    column = new { type = "integer", description = "1-based column number." },
                    imports = new { type = "string", description = "Extra source roots to search, semicolon-separated." }
                },
                required = new[] { "source", "line", "column" }
            }),
        new("bgsm_inspect",
            "Read a FO4 material file -- .bgsm (lighting) OR .bgem (effect) -- and dump every shader field " +
            "(Smoothness, SpecularColor, texture paths, PBR/porosity, emissive, translucency, terrain, " +
            "falloff, glass, ...). The format is detected from the file's own magic, not its extension. " +
            "Fields absent from the output aren't stored by this file's version, not an error.",
            new
            {
                type = "object",
                properties = new { path = new { type = "string", description = "Full path to the .bgsm or .bgem file." } },
                required = new[] { "path" }
            }),
        new("bgsm_set_field",
            "Set ONE field on a FO4 material file (.bgsm or .bgem) and write it back -- direct structured " +
            "editing, no material-editor GUI needed. Call bgsm_inspect first to see the exact field names and " +
            "current values for this file, since they differ by format and version. Color fields " +
            "(SpecularColor, EmittanceColor, HairTintColor, TranslucencySubsurfaceColor, BaseColor, " +
            "GlassFresnelColor) take 3 comma-separated numbers, e.g. '1.0, 0.5, 0.2'.",
            new
            {
                type = "object",
                properties = new
                {
                    path = new { type = "string", description = "Full path to the .bgsm or .bgem file." },
                    field = new { type = "string", description = "Exact field name from bgsm_inspect, e.g. 'Smoothness' (bgsm) or 'FalloffStartAngle' (bgem)." },
                    value = new { type = "string", description = "New value: a number, 'true'/'false', a string, or 'r, g, b' for a color field." },
                    out_path = new { type = "string", description = "Optional output path; defaults to overwriting 'path' in place." }
                },
                required = new[] { "path", "field", "value" }
            }),
        new("catalog_mod_folder",
            "Summarize an unfamiliar mod folder's contents by category (meshes/textures/materials/sounds/" +
            "scripts/plugins/archives/voice/...) with per-category counts and a few top-level subfolders. " +
            "Use this to get oriented in a mod before deciding what to inspect next, instead of walking the " +
            "folder tree yourself.",
            new
            {
                type = "object",
                properties = new { mod_path = new { type = "string", description = "Full path to the mod's folder." } },
                required = new[] { "mod_path" }
            }),
        new("audit_asset_usage",
            "xEdit's 'List used meshes' / 'Output used assets filenames': walks every one of a plugin's own " +
            "(native) records for string fields shaped like an asset path (.nif/.dds/.wav/.xwm/.fuz/.bgsm/" +
            ".bgem/.hkx/.swf/.pex/.seq), then diffs that set against what the plugin's own on-disk folder " +
            "actually ships (loose files + any BA2 beside it). Reports ORPHANED files (shipped, nothing in " +
            "this plugin references them -- trim candidates for a repack) and DANGLING references " +
            "(referenced, not found in this plugin's own folder -- may just be a base-game/shared asset, " +
            "not necessarily broken; full load-order resolution needs the not-yet-built resolve_asset tool).",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string" },
                    record_limit = new { type = "integer", description = "Cap on native records walked. Default 3000." }
                },
                required = new[] { "plugin" }
            }),
        new("audio_convert_to_xwm",
            "Convert an audio or video file (or a WHOLE FOLDER of them, recursed, converted in parallel " +
            "with the original folder structure preserved) to Fallout 4's xWMA (.xwm) format. Any " +
            "ffmpeg-readable input works (wav/mp3/flac/ogg/m4a/wma/mp4/...); non-PCM-WAV sources are " +
            "decoded through ffmpeg first, then encoded with Microsoft's xWMAEncode. Works out of the box " +
            "against the bundled ffmpeg/xWMAEncode; no setup needed. Use this before archive_pack when " +
            "repacking a mod's extracted BA2 with re-encoded audio.",
            new
            {
                type = "object",
                properties = new
                {
                    source = new { type = "string", description = "Full path to the source audio/video file, OR a folder to convert every audio/video file under (recursively)." },
                    output = new { type = "string", description = "Output path. For a single-file source: the .xwm path (optional, defaults to the source path with a .xwm extension). For a folder source: the output folder to mirror the structure into (optional, defaults to converting in place, alongside each original file)." },
                    bitrate_bps = new { type = "integer", description = "One of xWMAEncode's supported bitrates: 20000, 32000, 48000, 64000, 96000, 160000, 192000. Omit for its default (48000)." }
                },
                required = new[] { "source" }
            }),
        new("audio_convert_from_xwm",
            "Decode a Fallout 4 .xwm file back to WAV, and optionally on to another format (mp3/flac/ogg/" +
            "m4a/wma/...) via ffmpeg. Works out of the box against the bundled xWMAEncode/ffmpeg.",
            new
            {
                type = "object",
                properties = new
                {
                    source = new { type = "string", description = "Full path to the source .xwm file." },
                    output = new { type = "string", description = "Output path. Optional -- defaults to the source path with the target extension." },
                    target_ext = new { type = "string", description = "Output format: wav, mp3, flac, ogg, m4a, wma, ... Defaults to 'wav' (no ffmpeg needed for that case)." }
                },
                required = new[] { "source" }
            }),
        new("audio_make_fuz",
            "Pack an audio source (encoded to xwm first if it isn't already) and an optional .lip file " +
            "into a .fuz voice container -- the format FO4 voice lines actually ship as. If lip_path is " +
            "omitted, a same-named .lip next to the audio source is used automatically if present.",
            new
            {
                type = "object",
                properties = new
                {
                    audio_source = new { type = "string", description = "Full path to the source audio file (any format audio_convert_to_xwm accepts, or an existing .xwm)." },
                    lip_path = new { type = "string", description = "Optional .lip file path. Auto-detected next to audio_source if omitted." },
                    fuz_output = new { type = "string", description = "Output .fuz path." },
                    no_lip = new { type = "boolean", description = "Force a lip-less fuz even if a matching .lip file is found." }
                },
                required = new[] { "audio_source", "fuz_output" }
            }),
        new("audio_extract_fuz",
            "Split a .fuz voice file back into its .xwm and .lip parts, optionally also decoding the xwm " +
            "to .wav in the same step.",
            new
            {
                type = "object",
                properties = new
                {
                    fuz_path = new { type = "string", description = "Full path to the source .fuz file." },
                    xwm_output = new { type = "string", description = "Optional output .xwm path. Defaults to the fuz path with a .xwm extension." },
                    lip_output = new { type = "string", description = "Optional output .lip path. Defaults to the fuz path with a .lip extension." },
                    also_wav = new { type = "boolean", description = "Also decode the extracted xwm to a .wav alongside it." }
                },
                required = new[] { "fuz_path" }
            }),
        new("archive_pack",
            "Pack one or more loose folders into a new BA2 archive -- the counterpart to " +
            "archive_extract_all. Written in process: the Creation Kit is NOT needed. Typical flow to " +
            "re-encode a mod's sounds: archive_extract_all the mod's BA2, audio_convert_to_xwm on the " +
            "extracted Sound folder, then archive_pack that same folder tree back into a new BA2. " +
            "'DDS' (texture) archives are written in process too: each texture's own header supplies " +
            "the height/width/mip count/DXGI format, and the payload is split by mip range the way " +
            "vanilla does. A DDS archive holds .dds files only -- anything else in the source folder " +
            "is an error, not a silent skip.",
            new
            {
                type = "object",
                properties = new
                {
                    source_paths = new { type = "array", items = new { type = "string" }, description = "One or more source folders to pack (e.g. a single 'Sound' folder, or ['meshes','materials'] together)." },
                    output_ba2 = new { type = "string", description = "Full path for the new .ba2 file." },
                    format = new { type = "string", description = "'General' (sounds/meshes/scripts/anything non-texture) or 'DDS' (texture-only archives -- do not mix)." },
                    root_dir = new { type = "string", description = "REQUIRED. The folder each source's in-archive path is computed relative to -- e.g. root='...\\Data', source='...\\Data\\Sound\\...' produces the in-archive entry 'Sound\\...'. Get this wrong and the game won't find the packed files." },
                    compress = new { type = "boolean", description = "Default true. Set false to store every file raw (some mods ship sound BA2s uncompressed)." },
                    use_archive2 = new { type = "boolean", description = "Default false. Force the old Creation Kit Archive2.exe path instead of the in-process writer. Only needed to reproduce Archive2's exact output." }
                },
                required = new[] { "source_paths", "output_ba2", "root_dir" }
            }),
        new("precombine_plan",
            "Which placed references in an INTERIOR cell are eligible to be baked into a combined mesh, " +
            "grouped by the model they use -- phase 1 of CK-free precombine generation, and the answer " +
            "to 'filter for precombined statics'. READ-ONLY: nothing is written. Reports the eligible " +
            "groups AND why every other reference was skipped, since on a real cell the useful question " +
            "is usually why so few qualified. A reference qualifies only if it is not deleted or " +
            "initially disabled, has no script, enable parent, teleport destination, activate parent or " +
            "linked reference, belongs to this plugin rather than being an override, and its base is a " +
            "Static with a model, no script and no material swap.",
            new
            {
                type = "object",
                properties = new
                {
                    plugin = new { type = "string", description = "The plugin whose own references are being planned." },
                    cell_id = new { type = "string", description = "CELL FormKey or EditorID. Interiors only." },
                    min_instances = new { type = "integer", description = "Ignore model groups with fewer than this many references (default 2 -- combining one object saves nothing)." },
                    group_limit = new { type = "integer", description = "Max model groups to list, largest first (default 40). The response reports how many were omitted." },
                    include_instances = new { type = "boolean", description = "Default false. Include each group's per-reference transforms, capped at 25 per group. A real interior has hundreds, which overruns the result budget -- ask for these only when you need a specific group's placements." }
                },
                required = new[] { "plugin", "cell_id" }
            }),
        new("cell_get_placed_references",
            "List a CELL's placed references (position/rotation/scale + base object model path), " +
            "unioned across the whole load order -- not just the winning record, since a plugin can " +
            "'win' a CELL with an empty reference list while every other plugin's own placed objects " +
            "still load in-game. Powers the Cell Viewer panel; useful for an AI agent to inspect what's " +
            "actually placed in a cell without opening the UI. For an EXTERIOR cell you don't already " +
            "have a FormKey/EditorID for, pass 'worldspace' + 'grid_x' + 'grid_y' instead of 'cell_id' -- " +
            "resolves the cell at that grid position first (exterior cells are nested in the worldspace's " +
            "own Block/SubBlock tree, not reachable by FormKey guesswork).",
            new
            {
                type = "object",
                properties = new
                {
                    cell_id = new { type = "string", description = "A FormKey ('001234:Fallout4.esm') or an EditorID. Omit if using worldspace+grid_x+grid_y instead." },
                    worldspace = new { type = "string", description = "FormKey or EditorID of the worldspace, e.g. 'Commonwealth'. Use with grid_x/grid_y instead of cell_id." },
                    grid_x = new { type = "integer", description = "Exterior cell grid X coordinate." },
                    grid_y = new { type = "integer", description = "Exterior cell grid Y coordinate." }
                },
                required = Array.Empty<string>()
            }),
        new("cell_search",
            "Type-ahead search for CELL records by EditorID/Name/FormKey substring, de-duplicated to " +
            "the winning version. Lighter-weight than search_all with type=CELL -- this only ever " +
            "touches CELL records instead of building a full per-signature index of every record type " +
            "in every loaded plugin, which matters on a real modlist (measured: search_all type=CELL " +
            "cost ~2.3 GB and 7s on a 650-plugin load order; this tool avoids that entirely). Use this " +
            "for cell lookups instead of search_all.",
            new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "Substring to match against EditorID, in-game Name, or FormKey. Empty returns the first `limit` cells encountered." },
                    limit = new { type = "integer", description = "Max matches (default 25)." }
                },
                required = new[] { "query" }
            }),
        new("cleanup_placed_references",
            "xEdit's 'Remove duplicate references' / 'Remove excess references' (#60), for a single cell (by " +
            "cell_id, or worldspace+grid_x+grid_y for an exterior cell -- see cell_get_placed_references). " +
            "Scoped to PlacedObject (REFR) specifically, the large majority of real duplicate-clutter cases. " +
            "mode 'dedupe': two refs sharing the same base record (or base model if by_model=true), rounded " +
            "position/rotation, and scale are duplicates; keeps whichever carries 'special' data (EditorID, " +
            "script, enable parent, door teleport, linked ref, patrol) over one without, or the first-seen if " +
            "neither/both are special. mode 'excess': caps TEMPORARY refs at max_count, removing the excess " +
            "by list order (NOT random the way xEdit's own script does it -- deliberate, for reproducibility). " +
            "Dry-run by default; writes removals into patch_plugin as an override when apply=true.",
            new
            {
                type = "object",
                properties = new
                {
                    cell_id = new { type = "string", description = "FormKey or EditorID of the cell. Omit if using worldspace+grid_x+grid_y." },
                    worldspace = new { type = "string", description = "Worldspace FormKey or EditorID, for an exterior cell instead of cell_id." },
                    grid_x = new { type = "integer" },
                    grid_y = new { type = "integer" },
                    mode = new { type = "string", description = "'dedupe' or 'excess'." },
                    max_count = new { type = "integer", description = "For mode 'excess': the cap on temporary refs. Default 50." },
                    by_model = new { type = "boolean", description = "For mode 'dedupe': match by base model path instead of base record. Default false." },
                    patch_plugin = new { type = "string" },
                    apply = new { type = "boolean", description = "Default false (preview only)." }
                },
                required = new[] { "mode", "patch_plugin" }
            }),
    ];

    /// <summary>Anthropic Messages API tool format (input_schema).</summary>
    public static object[] ToolDefinitions() =>
        _specs.Select(t => (object)new { name = t.Name, description = t.Description, input_schema = t.Schema }).ToArray();

    /// <summary>
    /// Tool definitions with a prompt-cache breakpoint on the LAST tool. Tools render before the
    /// system prompt and never change, so this caches the whole tool prefix across every request --
    /// the cached tokens then cost ~0.1x. cache_control is null (dropped by WhenWritingNull) on the
    /// others.
    /// </summary>
    public static object[] ToolDefinitionsCached() =>
        _specs.Select((t, i) => (object)new
        {
            name = t.Name,
            description = t.Description,
            input_schema = t.Schema,
            cache_control = i == _specs.Length - 1 ? new { type = "ephemeral" } : null,
        }).ToArray();

    /// <summary>MCP tool format (inputSchema) for the Claude Code MCP server.</summary>
    public static object[] McpToolDefinitions() =>
        _specs.Select(t => (object)new { name = t.Name, description = t.Description, inputSchema = t.Schema }).ToArray();

    /// <summary>Gemini function-declaration format. Gemini's schema wants UPPERCASE OpenAPI types
    /// (OBJECT/STRING/INTEGER/BOOLEAN/…), so we transform our lowercase JSON-schema types.</summary>
    public static object[] GeminiToolDefinitions() =>
        _specs.Select(t => (object)new
        {
            name = t.Name,
            description = t.Description,
            parameters = ToGeminiSchema(t.Schema),
        }).ToArray();

    // Built with System.Text.Json nodes so GeminiProvider (which serializes with System.Text.Json) emits it correctly.
    private static System.Text.Json.Nodes.JsonNode ToGeminiSchema(object schema)
    {
        var node = System.Text.Json.JsonSerializer.SerializeToNode(schema)!;
        UppercaseTypes(node);
        return node;
    }

    private static void UppercaseTypes(System.Text.Json.Nodes.JsonNode? node)
    {
        if (node is System.Text.Json.Nodes.JsonObject o)
        {
            if (o["type"] is System.Text.Json.Nodes.JsonValue tv && tv.TryGetValue<string>(out var ts))
                o["type"] = ts.ToUpperInvariant();
            foreach (var kv in o) UppercaseTypes(kv.Value);
        }
        else if (node is System.Text.Json.Nodes.JsonArray a)
        {
            foreach (var item in a) UppercaseTypes(item);
        }
    }

    // Hard ceiling on any single tool result. A full record dump or a big conflict matrix can be
    // tens of KB; returning that to the model is slow and trips Claude Code's "output too large".
    private const int MaxResultChars = 8000;

    /// <summary>Raised on the calling thread after every tool call, whether read or write.
    /// The GUI subscribes to drive its live PIE-mode feed and auto-refresh the open record.</summary>
    public record McpToolEvent(string Tool, string Plugin, string Record, string Field, string Summary, bool IsWrite);
    public static event Action<McpToolEvent>? ToolCompleted;

    /// <summary>
    /// Runs a tool and reports whether it failed, per the <see cref="ToolError"/> contract, so a
    /// transport can set JSON-RPC <c>isError</c> honestly. Framing a failed edit as success lets an
    /// agent proceed to save_plugin and report a change that was never applied.
    /// </summary>
    public ToolResult ExecuteWithStatus(string toolName, string inputJson)
    {
        try { return ToolError.Unwrap(ExecuteMarked(toolName, inputJson)); }
        catch (Exception ex) { return ToolResult.Fail("Tool error: " + ex.Message); }
    }

    /// <summary>
    /// Plain-string tool call for the GUI. Text and throwing behaviour are unchanged; the internal
    /// error marker is stripped. Use <see cref="ExecuteWithStatus"/> to detect failure.
    /// </summary>
    public string Execute(string toolName, string inputJson) =>
        ToolError.Unwrap(ExecuteMarked(toolName, inputJson)).Text;

    private string ExecuteMarked(string toolName, string inputJson)
    {
        DebugLog.Write("INFO", "AI-Tool", $"call {toolName}",
            string.IsNullOrWhiteSpace(inputJson) ? null : inputJson);
        string raw;
        try
        {
            raw = ExecuteInner(toolName, inputJson);
        }
        catch (Exception ex)
        {
            DebugLog.Exception($"AI-Tool {toolName}", ex);
            throw;   // the agent surfaces "Tool error: ..."; the full stack is now in the debug log
        }
        DebugLog.Write("DEBUG", "AI-Tool", $"done {toolName} -> {raw.Length} chars");
        try { ToolCompleted?.Invoke(ExtractEvent(toolName, inputJson)); } catch { }
        if (raw.Length <= MaxResultChars) return raw;
        return raw[..MaxResultChars] +
               $"\n\n…[truncated {raw.Length - MaxResultChars:N0} more chars. Narrow the request -- e.g. ask for a " +
               "specific field, use a higher 'limit' sparingly, or use revert_overrides for bulk fixes instead of reading each record.]";
    }

    private static McpToolEvent ExtractEvent(string toolName, string inputJson)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(inputJson) ? "{}" : inputJson);
        var root = doc.RootElement;
        string Str(string k) => root.TryGetProperty(k, out var v) ? v.GetString() ?? "" : "";

        var plugin = Str("plugin");
        var record = Str("record") is { Length: > 0 } r ? r : Str("id");
        var field  = Str("field");

        bool isWrite = toolName is "set_field" or "set_conditions" or "set_components" or
            "add_list_item" or "remove_list_item" or "open_plugin" or "save_plugin" or "backup_plugin" or
            "create_record" or "create_cell" or "create_placed_object" or "disable_previs" or "cleanup_placed_references" or
            "delete_record" or "copy_as_override" or "copy_as_new_record" or "clean_plugin" or
            "remove_identical_to_master" or "create_merged_patch" or "add_masters" or "set_conditions_at" or
            "element_add" or "element_remove" or "element_move" or "element_clear" or
            "deep_copy_as_override" or "change_referencing_records" or
            "renumber_plugin_formids" or "create_seq_file" or
            "compact_to_esl" or "renumber_formid" or "reload_plugin" or "strip_masters_clean" or
            "attach_script" or "set_script_property" or "revert_overrides" or "batch_patch_records" or
            "run_script" or "reorder_masters" or "set_light_flag" or "set_localized_flag" or "archive_extract" or "archive_extract_all" or
            "bgsm_set_field" or "audio_convert_to_xwm" or "audio_convert_from_xwm" or "audio_make_fuz" or "audio_extract_fuz" or
            "archive_pack";

        var val = Str("value");
        if (val.Length > 40) val = val[..37] + "...";

        bool Applying() => root.TryGetProperty("apply", out var av) && av.ValueKind == JsonValueKind.True;

        string summary = toolName switch
        {
            "open_plugin"         => $"open {plugin}",
            "backup_plugin"       => $"backup {plugin}",
            "save_plugin"         => $"save {plugin}",
            "set_field"           => $"{ShortId(record)}.{field} = {val}",
            "set_conditions"      => $"{ShortId(record)}: conditions rebuilt",
            "set_components"      => $"{ShortId(record)}: components set",
            "add_list_item"       => $"{ShortId(record)}.{field} += {Str("value")}",
            "remove_list_item"    => $"{ShortId(record)}.{field} -= {Str("value")}",
            "create_record"       => $"create {Str("type")} '{Str("editorId")}' in {plugin}",
            "create_cell"         => $"create CELL '{Str("editorId")}' in {plugin}",
            "create_placed_object" => $"place {ShortId(Str("baseObject"))} in cell '{Str("cell")}' of {plugin}",
            "delete_record"       => $"delete {ShortId(record)} from {plugin}",
            "copy_as_override"    => $"{ShortId(Str("id"))} -> {Str("patch_plugin")}",
            "copy_as_new_record"  => $"{ShortId(Str("id"))} -> new record in {Str("target_plugin")}",
            "remove_identical_to_master" => $"{(Applying() ? "remove" : "list")} ITMs in {plugin}",
            "create_merged_patch" => $"{(Applying() ? "build" : "preview")} merged patch {Str("patch_plugin")}",
            "get_conditions"      => $"read conditions of {ShortId(record)}",
            "set_conditions_at"   => $"{ShortId(record)}: conditions rebuilt at {Str("path")}",
            "deep_copy_as_override" => $"{(Applying() ? "deep copy" : "preview deep copy")} {ShortId(Str("id"))} -> {Str("patch_plugin")}",
            "change_referencing_records" => $"{(Applying() ? "repoint" : "preview repoint")} {ShortId(Str("from"))} -> {ShortId(Str("to"))}",
            "element_add"         => $"{ShortId(record)}: add to {Str("path")}",
            "element_remove"      => $"{ShortId(record)}: remove {Str("path")}",
            "element_move"        => $"{ShortId(record)}: move {Str("path")}",
            "element_clear"       => $"{ShortId(record)}: clear {Str("path")}",
            "element_describe"    => $"{ShortId(record)}: actions at {Str("path")}",
            "add_masters"         => $"add master(s) to {plugin}",
            "renumber_plugin_formids" => $"{(Applying() ? "renumber" : "preview renumber of")} every record in {plugin}",
            "create_seq_file"     => $"write {plugin}.seq",
            "check_circular_leveled_lists" => "check for circular leveled lists",
            "reload_plugin"       => $"reload {plugin}",
            "strip_masters_clean" => $"strip masters: {System.IO.Path.GetFileName(Str("source_path"))}",
            "list_masters"        => $"list masters of {plugin}",
            "reorder_masters"     => $"reorder masters of {plugin}",
            "set_light_flag"      => $"set_light_flag({plugin}, {Str("light")})",
            "set_localized_flag"  => $"set_localized_flag({plugin}, {Str("localized")})",
            "archive_list"        => $"list {System.IO.Path.GetFileName(Str("archive_path"))}",
            "archive_extract"     => $"extract {Str("inner_path")} <- {System.IO.Path.GetFileName(Str("archive_path"))}",
            "archive_extract_all" => $"extract all <- {System.IO.Path.GetFileName(Str("archive_path"))}",
            "papyrus_function_lookup" => $"papyrus lookup {Str("function")}",
            "papyrus_script_info" => $"papyrus script {Str("script")}",
            "graph_validate"      => $"validate {System.IO.Path.GetFileName(Str("graph"))}",
            "graph_compile"       => $"compile {System.IO.Path.GetFileName(Str("graph"))}",
            "graph_palette_search" => $"palette '{Str("query")}'",
            "graph_node_info"     => $"node {Str("node_type")}",
            "papyrus_check"       => $"check {System.IO.Path.GetFileName(Str("source"))}",
            "papyrus_outline"     => $"outline {System.IO.Path.GetFileName(Str("source"))}",
            "papyrus_definition"  => $"definition {System.IO.Path.GetFileName(Str("source"))}",
            "bgsm_inspect"        => $"inspect {System.IO.Path.GetFileName(Str("path"))}",
            "bgsm_set_field"      => $"{System.IO.Path.GetFileName(Str("path"))}.{Str("field")} = {Str("value")}",
            "catalog_mod_folder"  => $"catalog {System.IO.Path.GetFileName(Str("mod_path"))}",
            "audio_convert_to_xwm"   => $"{System.IO.Path.GetFileName(Str("source"))} -> xwm",
            "audio_convert_from_xwm" => $"{System.IO.Path.GetFileName(Str("source"))} -> {(Str("target_ext") is { Length: > 0 } te ? te : "wav")}",
            "audio_make_fuz"      => $"{System.IO.Path.GetFileName(Str("audio_source"))} -> {System.IO.Path.GetFileName(Str("fuz_output"))}",
            "audio_extract_fuz"   => $"extract {System.IO.Path.GetFileName(Str("fuz_path"))}",
            "archive_pack"        => $"pack -> {System.IO.Path.GetFileName(Str("output_ba2"))}",
            "check_plugin"        => $"check {plugin}",
            "clean_plugin"        => $"clean UDRs in {plugin}",
            "compact_to_esl"      => $"compact {plugin} to ESL",
            "renumber_formid"     => $"renumber a record in {plugin}",
            "get_record"          => $"read {ShortId(Str("id"))} in {plugin}",
            "list_records"         => $"list {Str("type")} in {plugin}",
            "list_records_summary" => $"summary {Str("type")} in {plugin}",
            "search_records"      => $"search '{Str("query")}' in {plugin}",
            "get_conflicts"       => $"conflicts for {ShortId(Str("id"))}",
            "get_winning_record"  => $"winning record {ShortId(Str("id"))}",
            "revert_overrides"    => $"revert {Str("bad_plugin")} -> {Str("patch_plugin")}",
            "batch_patch_records" => $"batch patch {Str("type")} in {Str("source_plugin")} -> {Str("patch_plugin")}",
            "run_script"          => $"run script -> {Str("patch_plugin")}",
            "scan_conflicts"      => "scan all conflicts",
            "create_mod_group"    => $"create ModGroup '{Str("name")}'",
            "update_mod_group"    => $"update ModGroup '{Str("name")}'",
            "delete_mod_group"    => $"delete ModGroup '{Str("name")}'",
            "list_mod_groups"     => "list ModGroups",
            "resolve_asset"       => $"resolve asset {Str("path")}",
            "scan_broken_refs"    => $"scan broken refs in {plugin}",
            "scan_all_plugins"    => "scan entire modlist for broken refs",
            "cell_get_placed_references" => $"placed refs in {ShortId(Str("cell_id"))}",
            "cell_search"                => $"cell search '{Str("query")}'",
            "precombine_plan"            => $"precombine plan for {ShortId(Str("cell_id"))}",
            _                     => toolName.Replace('_', ' '),
        };

        return new McpToolEvent(toolName, plugin, record, field, summary, isWrite);
    }

    private static string ShortId(string id) => id.Length <= 32 ? id : id[..29] + "...";

    private string ExecuteInner(string toolName, string inputJson)
    {
        var env = _envProvider();
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(inputJson) ? "{}" : inputJson);
        var root = doc.RootElement;

        string Str(string k) => root.TryGetProperty(k, out var v) ? v.GetString() ?? "" : "";
        int Int(string k, int def) => root.TryGetProperty(k, out var v) && v.TryGetInt32(out var i) ? i : def;
        bool Bool(string k) => root.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True;
        // Distinguishes "absent" from "" -- an absent mapMarkerName must NOT attach marker data.
        string? StrOrNull(string k) => root.TryGetProperty(k, out var v) ? v.GetString() : null;
        // Bool() can't express a default of true (absent reads as false).
        bool BoolOr(string k, bool def) => root.TryGetProperty(k, out var v)
            ? v.ValueKind == JsonValueKind.True
            : def;
        float Flt(string k, float def) => root.TryGetProperty(k, out var v) && v.TryGetSingle(out var f) ? f : def;

        switch (toolName)
        {
            case "list_plugins":
            {
                var plugins = MutagenLoader.QueryLoadedPlugins(env);
                return plugins.Count == 0 ? "No plugins loaded." : string.Join("\n", plugins);
            }
            case "list_record_types":
            {
                var types = MutagenLoader.QueryRecordTypes(env, Str("plugin"));
                return types.Count == 0 ? "No record types (plugin not loaded or empty)." : string.Join("\n", types);
            }
            case "list_records":
            {
                int limit = Int("limit", 100), offset = Int("offset", 0);
                var recs = MutagenLoader.QueryRecordsOfType(env, Str("plugin"), Str("type"), limit, offset);
                if (recs.Count == 0) return offset > 0 ? $"No records at offset {offset}." : "No records of that type.";
                int total = MutagenLoader.CountRecordsOfType(env, Str("plugin"), Str("type"));
                var body = string.Join("\n", recs.Select(r => $"{r.editorId} [{r.formKey}]"));
                int end = offset + recs.Count;
                var hdr = $"Showing {offset + 1}-{end} of {total}." + (end < total ? $" Next page: offset={end}." : "");
                return hdr + "\n" + body;
            }
            case "list_records_summary":
                return MutagenLoader.QueryRecordSummaries(env, Str("plugin"), Str("type"), Math.Min(Int("limit", 200), 500), Int("offset", 0));
            case "search_records":
            {
                var hits = MutagenLoader.QuerySearch(env, Str("plugin"), Str("query"), Int("limit", 50), Int("offset", 0));
                return hits.Count == 0 ? "No matches." : string.Join("\n", hits.Select(h => $"{h.editorId} ({h.type}) [{h.formKey}]"));
            }
            case "search_all":
            {
                var hits = MutagenLoader.SearchAllRecords(env, Str("query"),
                    root.TryGetProperty("type", out var saT) ? saT.GetString() : null, Int("limit", 100));
                return hits.Count == 0 ? "No matches across the load order."
                    : string.Join("\n", hits.Select(h => $"{h.EditorID} ({h.Type}) [{h.FormKey}] {(string.IsNullOrEmpty(h.Name) ? "" : "\"" + h.Name + "\" ")}<{h.Plugin}>"));
            }
            case "resolve_editorid":
            {
                var fk = ResolveToFk(env, Str("id"));
                return fk.IsNull ? $"Could not resolve '{Str("id")}' to a FormKey in the load order." : fk.ToString();
            }
            case "get_scripts":
                return GetScripts(env, Str("id"));
            case "diff_records":
                return DiffRecords(env, Str("a"), Str("b"));
            case "get_record":
                return MutagenLoader.QueryRecordFields(env, Str("plugin"), Str("id"));
            case "get_conflicts":
                return GetConflicts(env, Str("id"));
            case "get_winning_record":
                return GetWinningRecord(env, Str("id"));
            case "search_robco_configs":
                return SearchRobcoConfigs(_mo2PathProvider?.Invoke(), Str("query"), Int("limit", 60));
            case "scan_conflicts":
                return ScanConflicts(env, !root.TryGetProperty("mod_only", out var mo) || mo.ValueKind != JsonValueKind.False,
                    root.TryGetProperty("type", out var tf) ? tf.GetString() : null, Int("limit", 200),
                    !root.TryGetProperty("hide_grouped", out var hg) || hg.ValueKind != JsonValueKind.False);
            case "create_mod_group":
            {
                var plugins = root.TryGetProperty("plugins", out var pgEl) && pgEl.ValueKind == JsonValueKind.Array
                    ? pgEl.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0)
                    : Enumerable.Empty<string>();
                return ModGroupsService.Create(Str("name"), plugins);
            }
            case "update_mod_group":
            {
                IEnumerable<string>? plugins = null;
                if (root.TryGetProperty("plugins", out var upEl) && upEl.ValueKind == JsonValueKind.Array)
                    plugins = upEl.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0);
                return ModGroupsService.Update(Str("name"), plugins,
                    Str("new_name") is { Length: > 0 } nn ? nn : null);
            }
            case "delete_mod_group":
                return ModGroupsService.Delete(Str("name"));
            case "list_mod_groups":
                return ModGroupsService.Groups.Count == 0
                    ? "No ModGroups declared."
                    : string.Join("\n", ModGroupsService.Groups.Select(g => $"{g.Name}: [{string.Join(", ", g.Plugins)}]"));
            case "resolve_asset":
                return AssetResolver.ResolveText(Str("path"), Bool("extract"), Int("limit", 25));
            case "get_referenced_by":
                return GetReferencedByText(env, Str("id"), Int("limit", 200));
            case "get_problems":
                return GetProblemsText(env, Str("id"));
            case "scan_broken_refs":
                return MutagenLoader.ScanBrokenRefs(env, Str("plugin"));
            case "scan_all_plugins":
                return MutagenLoader.ScanAllPluginsForBrokenRefs(env);
            case "precombine_plan":
                return PrecombineService.BuildPlanJson(env, Str("plugin"), Str("cell_id"), Int("min_instances", 2),
                    includeInstances: Bool("include_instances"), groupLimit: Int("group_limit", 40));
            case "cell_get_placed_references":
            {
                int? gx = root.TryGetProperty("grid_x", out var gxEl) && gxEl.TryGetInt32(out var gxv) ? gxv : null;
                int? gy = root.TryGetProperty("grid_y", out var gyEl) && gyEl.TryGetInt32(out var gyv) ? gyv : null;
                return CellService.GetPlacedReferencesJson(Str("cell_id"), env, StrOrNull("worldspace"), gx, gy);
            }
            case "cell_search":
            {
                var cellHits = MutagenLoader.SearchCellRecords(env, Str("query"), Int("limit", 25));
                return cellHits.Count == 0 ? "No matching cells."
                    : string.Join("\n", cellHits.Select(h => $"{h.EditorID} [{h.FormKey}] {(string.IsNullOrEmpty(h.Name) ? "" : "\"" + h.Name + "\" ")}<{h.Plugin}>"));
            }
            case "cleanup_placed_references":
            {
                int? cgx = root.TryGetProperty("grid_x", out var cgxEl) && cgxEl.TryGetInt32(out var cgxv) ? cgxv : null;
                int? cgy = root.TryGetProperty("grid_y", out var cgyEl) && cgyEl.TryGetInt32(out var cgyv) ? cgyv : null;
                return CellService.CleanupPlacedReferencesJson(env, Str("cell_id"), StrOrNull("worldspace"), cgx, cgy,
                    Str("mode"), Int("max_count", 50), Bool("by_model"), Str("patch_plugin"), Bool("apply"));
            }

            // ---- write tools ----
            case "open_plugin":
                return WriteService.OpenPlugin(Str("plugin"), env);
            case "create_plugin":
                return WriteService.CreatePlugin(Str("name"));
            case "create_record":
                return WriteService.CreateRecord(Str("plugin"), Str("type"), Str("editorId"), env);
            case "create_cell":
                return WriteService.CreateCell(Str("plugin"), Str("editorId"), StrOrNull("name"), env);
            case "create_placed_object":
                return WriteService.CreatePlacedObject(
                    Str("plugin"), Str("cell"), Str("baseObject"), StrOrNull("editorId"),
                    Flt("x", 0f), Flt("y", 0f), Flt("z", 0f), Flt("rotZ", 0f),
                    BoolOr("persistent", true), BoolOr("initiallyDisabled", false),
                    StrOrNull("mapMarkerName"), StrOrNull("mapMarkerType"), BoolOr("mapMarkerVisible", false),
                    env);
            case "disable_previs":
                return WriteService.DisablePrevis(env, Str("cell"), Str("patch_plugin"), Bool("apply"));
            case "set_field":
                return WriteService.SetField(Str("plugin"), Str("record"), Str("field"), Str("value"), env);
            case "add_list_item":
                return WriteService.AddListItem(Str("plugin"), Str("record"), Str("field"), Str("value"), env);
            case "attach_script":
                return WriteService.AttachScript(Str("plugin"), Str("record"), Str("script"), env);
            case "set_script_property":
                return WriteService.SetScriptProperty(Str("plugin"), Str("record"), Str("script"), Str("name"), Str("value"),
                    root.TryGetProperty("type", out var stv) ? stv.GetString() : null, env);
            case "check_plugin":
                return MutagenLoader.CheckPlugin(env, Str("plugin"));
            case "compact_to_esl":
                return WriteService.CompactToEsl(Str("plugin"), env);
            case "check_esl_eligibility":
                return WriteService.CheckEslEligibility(Str("plugin"), env);
            case "renumber_formid":
                return WriteService.RenumberFormId(Str("plugin"), Str("record"), Str("new_id"), env,
                    repointRefs: !(root.TryGetProperty("repoint_references", out var rr) && rr.ValueKind == JsonValueKind.False));
            case "clean_plugin":
                return WriteService.CleanPlugin(Str("plugin"), env);
            case "backup_plugin":
                return WriteService.BackupPlugin(Str("plugin"));
            case "save_plugin":
                return WriteService.SavePlugin(Str("plugin"), root.TryGetProperty("path", out var pv) ? pv.GetString() : null, env);
            case "copy_as_override":
                return WriteService.CopyAsOverride(env, Str("source_plugin"), Str("id"), Str("patch_plugin"),
                    Bool("overwrite"));
            case "copy_as_new_record":
                return WriteService.CopyAsNewRecord(env, Str("source_plugin"), Str("id"), Str("target_plugin"),
                    Str("new_editor_id") is { Length: > 0 } nedid ? nedid : null);
            case "remove_identical_to_master":
                return WriteService.RemoveIdenticalToMaster(env, Str("plugin"), Bool("apply"));
            case "create_merged_patch":
                return WriteService.CreateMergedPatch(env, Str("plugins"), Str("patch_plugin"), Bool("apply"));
            case "get_conditions":
                return Str("path") is { Length: > 0 } condPath
                    ? WriteService.GetConditionsAtPath(env, Str("plugin"), Str("record"), condPath)
                    : WriteService.GetConditionsJson(env, Str("plugin"), Str("record"));
            case "deep_copy_as_override":
                return WriteService.DeepCopyAsOverride(env, Str("source_plugin"), Str("id"), Str("patch_plugin"),
                    Bool("apply"), Bool("overwrite"));
            case "change_referencing_records":
                return WriteService.ChangeReferencingRecords(env, Str("from"), Str("to"), Str("patch_plugin"), Bool("apply"));
            case "element_add":
                return ElementService.AddElement(Str("plugin"), Str("record"), Str("path"),
                    Str("template") is { Length: > 0 } tpl ? tpl : null, env);
            case "element_remove":
                return ElementService.RemoveElement(Str("plugin"), Str("record"), Str("path"), env);
            case "element_move":
                return ElementService.MoveElement(Str("plugin"), Str("record"), Str("path"), Int("delta", 1), env);
            case "element_clear":
                return ElementService.ClearElement(Str("plugin"), Str("record"), Str("path"), env);
            case "element_describe":
                return ElementService.DescribeElement(env, Str("plugin"), Str("record"), Str("path"));
            case "set_conditions_at":
                return WriteService.SetConditionsAtPath(Str("plugin"), Str("record"), Str("path"), Str("conditions"), env);
            case "add_masters":
            {
                var masters = root.TryGetProperty("masters", out var mEl) && mEl.ValueKind == JsonValueKind.Array
                    ? mEl.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToArray()
                    : Array.Empty<string>();
                return WriteService.AddMasters(Str("plugin"), masters, env);
            }
            case "renumber_plugin_formids":
                return WriteService.RenumberPluginFormIds(Str("plugin"), Str("start"), Bool("apply"), env);
            case "create_seq_file":
                return WriteService.CreateSeqFile(env, Str("plugin"),
                    Str("output_dir") is { Length: > 0 } seqDir ? seqDir : null);
            case "check_circular_leveled_lists":
                return WriteService.CheckCircularLeveledLists(env, Str("plugin"), Int("limit", 200));
            case "delete_record":
                return WriteService.DeleteRecord(Str("plugin"), Str("id"), env);
            case "remove_list_item":
                return WriteService.RemoveListItem(Str("plugin"), Str("record"), Str("field"), Str("value"), env);
            case "set_components":
                return WriteService.SetComponents(Str("plugin"), Str("record"), Str("components"), env);
            case "set_conditions":
                return WriteService.SetConditions(Str("plugin"), Str("record"), Str("conditions"), env);
            case "add_leveled_entry":
                return WriteService.AddLeveledEntry(Str("plugin"), Str("record"), Str("reference"),
                    Int("level", 1), Int("count", 1),
                    root.TryGetProperty("chance_none", out var cn) && cn.TryGetDouble(out var cnv) ? cnv : 0.0, env);
            case "set_perk_effects":
                return WriteService.SetPerkEffects(Str("plugin"), Str("record"), Str("effects"), env);
            case "set_magic_effects":
                return WriteService.SetMagicEffects(Str("plugin"), Str("record"), Str("effects"), env);
            case "set_quest_aliases":
                return WriteService.SetQuestAliases(Str("plugin"), Str("record"), Str("aliases"), env);
            case "set_quest_stages":
                return WriteService.SetQuestStages(Str("plugin"), Str("record"), Str("stages"), env);
            case "set_quest_objectives":
                return WriteService.SetQuestObjectives(Str("plugin"), Str("record"), Str("objectives"), env);
            case "set_message_buttons":
                return WriteService.SetMessageButtons(Str("plugin"), Str("record"), Str("buttons"), env);
            case "set_furniture_markers":
                return WriteService.SetFurnitureMarkers(Str("plugin"), Str("record"), Str("markers"), env);
            case "revert_overrides":
                return WriteService.RevertOverridesFrom(env, Str("bad_plugin"), Str("patch_plugin"),
                    root.TryGetProperty("signature", out var sg) ? sg.GetString() : null,
                    root.TryGetProperty("contains_component", out var cc) ? cc.GetString() : null,
                    Bool("apply"), Int("limit", 50));
            case "batch_patch_records":
                return WriteService.BatchPatchRecords(
                    env, Str("patch_plugin"), Str("source_plugin"), Str("type"),
                    Str("operations"),
                    root.TryGetProperty("filter_field",  out var ff) ? ff.GetString() : null,
                    root.TryGetProperty("filter_value",  out var fv) ? fv.GetString() : null,
                    !root.TryGetProperty("dry_run", out var dr2) || dr2.ValueKind != JsonValueKind.False,
                    Int("limit", 5000));
            case "run_script":
                return PatchScriptRunner.Run(Str("script"), env, Str("patch_plugin"),
                    !root.TryGetProperty("dry_run", out var drs) || drs.ValueKind != JsonValueKind.False);
            case "reload_plugin":
                return WriteService.ReloadPlugin(Str("plugin"), env);
            case "decompile_papyrus":
                return PapyrusService.Decompile(Str("source"),
                    root.TryGetProperty("output", out var dpo) ? dpo.GetString() : null,
                    Bool("assembly"), Bool("write"));
            case "compile_papyrus":
                return PapyrusService.Compile(Str("source"),
                    root.TryGetProperty("output", out var poo) ? poo.GetString() : null,
                    root.TryGetProperty("imports", out var pii) ? pii.GetString() : null,
                    root.TryGetProperty("flags", out var pff) ? pff.GetString() : null,
                    Bool("all"), Bool("optimize"), Bool("release"),
                    root.TryGetProperty("compiler_path", out var pcc) ? pcc.GetString() : null,
                    StrOrNull("engine"),
                    !root.TryGetProperty("debug_info", out var pdi) || pdi.ValueKind != JsonValueKind.False);
            case "strip_masters_clean":
            {
                var mastersRaw = Str("masters");
                string[] masters;
                try
                {
                    using var mastersDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(mastersRaw) ? "[]" : mastersRaw);
                    masters = mastersDoc.RootElement.EnumerateArray()
                        .Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToArray();
                }
                catch { masters = Array.Empty<string>(); }
                bool isDryRun = !root.TryGetProperty("dry_run", out var dr) || dr.ValueKind != JsonValueKind.False;
                return WriteService.StripMastersClean(Str("source_path"), masters, Str("output_path"), isDryRun, env);
            }
            case "list_masters":
                return WriteService.ListMasters(Str("plugin"), env);
            case "reorder_masters":
            {
                var order = root.TryGetProperty("order", out var ordEl) && ordEl.ValueKind == JsonValueKind.Array
                    ? ordEl.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToArray()
                    : Array.Empty<string>();
                return WriteService.ReorderMasters(Str("plugin"), order, env);
            }
            case "set_light_flag":
                return WriteService.SetLightFlag(Str("plugin"), BoolOr("light", true), env);
            case "set_localized_flag":
                return WriteService.SetLocalizedFlag(Str("plugin"), BoolOr("localized", true), env);

            case "nif_import":
                return NifService.Import(Str("obj_path"), Str("out_nif"), Str("material"),
                    Str("tex_diffuse"), Str("tex_normal"), Bool("collision"), Bool("from_blender"));
            case "nif_inspect":
                return NifService.Inspect(Str("nif_path"));
            case "nif_verify":
                return NifService.Verify(Str("nif_path"));
            case "nif_fix":
                return NifService.Fix(Str("nif_path"), Str("out_nif"));

            case "archive_list":
                return ArchiveService.ListArchive(Str("archive_path"), StrOrNull("filter"), Int("limit", 500));
            case "archive_extract":
                return ArchiveService.ExtractFile(Str("archive_path"), Str("inner_path"), Str("out_path"));
            case "archive_extract_all":
                return ArchiveService.ExtractAll(Str("archive_path"), Str("out_dir"), StrOrNull("filter"), Int("limit", 2000));
            case "papyrus_function_lookup":
                return PapyrusWikiService.LookupFunction(_ckWikiPathProvider?.Invoke() ?? "", Str("script"), Str("function"));
            case "papyrus_script_info":
                return PapyrusWikiService.LookupScriptInfo(_ckWikiPathProvider?.Invoke() ?? "", Str("script"));
            case "papyrus_check":
                return Services.Papyrus.PapyrusAnalysisService.Check(
                    Str("source"),
                    all: true,
                    semantic: !root.TryGetProperty("semantic", out var pcs) || pcs.ValueKind != JsonValueKind.False,
                    imports: StrOrNull("imports"));
            case "graph_validate":
                return Services.Graph.GraphToolService.Validate(Str("graph"), StrOrNull("imports"));
            case "graph_compile":
                return Services.Graph.GraphToolService.Compile(
                    Str("graph"), StrOrNull("output"), StrOrNull("imports"),
                    sourceOnly: root.TryGetProperty("source_only", out var gso) && gso.ValueKind == JsonValueKind.True);
            case "graph_palette_search":
                return Services.Graph.GraphToolService.SearchPalette(
                    Str("query"), StrOrNull("imports"), Int("limit", 30));
            case "graph_node_info":
                return Services.Graph.GraphToolService.DescribeNode(Str("node_type"), StrOrNull("imports"));

            case "papyrus_outline":
                return Services.Papyrus.PapyrusAnalysisService.Outline(Str("source"));
            case "papyrus_definition":
                return Services.Papyrus.PapyrusAnalysisService.Definition(
                    Str("source"), Int("line", 0), Int("column", 0), StrOrNull("imports"));

            case "bgsm_inspect":
                return MaterialService.Inspect(Str("path"));
            case "bgsm_set_field":
                return MaterialService.SetField(Str("path"), Str("field"), Str("value"), StrOrNull("out_path"));
            case "catalog_mod_folder":
                return ModInspectService.CatalogFolder(Str("mod_path"));
            case "audit_asset_usage":
                return WriteService.AuditAssetUsage(Str("plugin"), env, Int("record_limit", 3000));

            case "audio_convert_to_xwm":
            {
                var bitrate = root.TryGetProperty("bitrate_bps", out var brEl) && brEl.TryGetInt32(out var br) ? (int?)br : null;
                return AudioService.ConvertToXwm(Str("source"), StrOrNull("output") ?? "", bitrate);
            }
            case "audio_convert_from_xwm":
                return AudioService.ConvertFromXwm(Str("source"), StrOrNull("output") ?? "", StrOrNull("target_ext") ?? "");
            case "audio_make_fuz":
                return AudioService.MakeFuz(Str("audio_source"), StrOrNull("lip_path") ?? "", Str("fuz_output"), Bool("no_lip"));
            case "audio_extract_fuz":
                return AudioService.ExtractFuz(Str("fuz_path"), StrOrNull("xwm_output") ?? "", StrOrNull("lip_output") ?? "", Bool("also_wav"));

            case "archive_pack":
            {
                var sourcePaths = root.TryGetProperty("source_paths", out var spEl) && spEl.ValueKind == JsonValueKind.Array
                    ? spEl.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToArray()
                    : Array.Empty<string>();
                return ArchiveService.Pack(sourcePaths, Str("output_ba2"), StrOrNull("format") ?? "General", Str("root_dir"),
                    BoolOr("compress", true), Bool("use_archive2"));
            }

            default:
                return ToolError.Fail($"Unknown tool: {toolName}");
        }
    }

    // Resolve a FormKey ('001234:Plugin.esp') or an EditorID to a FormKey, using the load order's
    // link cache (resolves both, globally) with a fallback to the per-mod index.
    private static Mutagen.Bethesda.Plugins.FormKey ResolveToFk(object? env, string id) =>
        MutagenLoader.ResolveId(env, id);

    private static string GetConflicts(object? env, string id)
    {
        var fk = ResolveToFk(env, id);
        if (fk.IsNull)
            return $"Could not resolve '{id}'. Pass a FormKey like '054C84:Fallout4.esm' or an exact EditorID.";

        var matrix = MutagenLoader.BuildConflictMatrix(env, fk.ToString());
        if (matrix == null || matrix.Plugins.Count == 0) return $"No record found for {fk}.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{matrix.EditorID} [{matrix.FormKey}] {matrix.Type}  |  record-level: {matrix.Level.ToUpperInvariant()}");

        if (matrix.Plugins.Count == 1)
        {
            sb.AppendLine($"Defined only in {matrix.Plugins[0]} -- no override conflict.");
            return sb.ToString();
        }

        sb.AppendLine($"Override chain ({matrix.Plugins.Count} plugins, load order -- LAST WINS):");
        for (int i = 0; i < matrix.Plugins.Count; i++)
            sb.AppendLine($"  {i + 1}. {matrix.Plugins[i]}"
                + (i == matrix.Plugins.Count - 1 ? "  <-- WINNER (what the game uses)" : ""));

        var diffRows = matrix.Rows.Where(r => r.Differs).ToList();
        sb.AppendLine($"\n{diffRows.Count} field(s) differ:");

        if (diffRows.Count > 0)
        {
            // Budget-bound the table so the executor's 8 KB hard cap never blind-truncates it
            // mid-row (which loses the footer and wastes the cut tokens). We stop emitting rows
            // when approaching the budget and report an honest omission count instead.
            const int FW = 34, VW = 20, Budget = 6500;
            var head = "  " + "Field".PadRight(FW);
            foreach (var p in matrix.Plugins) head += " | " + Trunc(p, VW).PadRight(VW);
            sb.AppendLine(head.TrimEnd());
            sb.AppendLine("  " + new string('-', Math.Min(FW + matrix.Plugins.Count * (VW + 3), 180)));

            int shown = 0;
            foreach (var row in diffRows)
            {
                if (sb.Length > Budget)
                {
                    sb.AppendLine($"  ... {diffRows.Count - shown} more differing field(s) omitted to save tokens. " +
                                  "Use get_winning_record for effective values, or get_record(plugin,id) for one plugin's full dump.");
                    break;
                }
                var sev = $"[{row.Severity.ToUpperInvariant()}]";
                var line = "  " + (sev + " " + row.Field).PadRight(FW);
                for (int ci = 0; ci < matrix.Plugins.Count; ci++)
                    line += " | " + Trunc(ci < row.Values.Count ? row.Values[ci] : "", VW).PadRight(VW);
                sb.AppendLine(line.TrimEnd());   // drop trailing column padding -- pure wasted tokens
                shown++;
            }
        }

        sb.AppendLine("\nUse get_winning_record(id) for the complete effective field dump.");
        return sb.ToString();
    }

    private static string Trunc(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..(max - 3)] + "...";

    private static string ScanConflicts(object? env, bool modOnly, string? typeFilter, int limit, bool hideGrouped)
    {
        if (env == null) return "No environment loaded. Use 'Load Env' or 'Open MO2' first.";

        var all = ConflictScanner.ScanCached(env);
        var matched = all.Where(c =>
            (!modOnly || c.InvolvesMod) &&
            (string.IsNullOrWhiteSpace(typeFilter) || string.Equals(c.Type, typeFilter, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var suppressed = matched.Count(c => c.Suppressed);
        var filtered = hideGrouped ? matched.Where(c => !c.Suppressed).ToList() : matched;
        var groupNote = suppressed == 0 ? ""
            : hideGrouped ? $" ({suppressed} hidden by ModGroups; pass hide_grouped=false to see them)"
                          : $" ({suppressed} of them declared intentional by a ModGroup, shown anyway)";

        if (filtered.Count == 0)
            return "No conflicts found" + (modOnly ? " involving mods" : "") +
                   (string.IsNullOrWhiteSpace(typeFilter) ? "" : $" of type {typeFilter}") + "." + groupNote;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{filtered.Count} conflicting record(s){(modOnly ? " involving mods" : "")}" +
                      $"{(string.IsNullOrWhiteSpace(typeFilter) ? "" : $" of type {typeFilter}")}{groupNote}; showing {Math.Min(limit, filtered.Count)}:");
        foreach (var c in filtered.Take(limit))
            sb.AppendLine($"  {c.EditorID} [{c.FormKey}] ({c.Type}) -- {c.Plugins.Count} plugins, WINNER={c.Winner}" +
                          (c.Suppressed ? " [MODGROUP]" : ""));
        if (filtered.Count > limit)
            sb.AppendLine($"...and {filtered.Count - limit} more. Filter by 'type' or raise 'limit' to see them.");
        return sb.ToString();
    }

    private static string GetReferencedByText(object? env, string id, int limit)
    {
        var fk = ResolveToFk(env, id);
        if (fk.IsNull) return $"Could not resolve '{id}'. Pass a FormKey or an exact EditorID.";

        var refs = MutagenLoader.GetReferencedBy(env, fk.ToString(), limit);
        if (refs.Count == 0) return $"Nothing references {fk}.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{refs.Count} record(s) reference {fk}:");
        foreach (var r in refs)
            sb.AppendLine($"  {(string.IsNullOrEmpty(r.EditorID) ? r.FormKey : r.EditorID)} [{r.FormKey}] ({r.Type}) in {r.Plugin}");
        return sb.ToString();
    }

    private static string GetProblemsText(object? env, string id)
    {
        var fk = ResolveToFk(env, id);
        if (fk.IsNull) return $"Could not resolve '{id}'. Pass a FormKey or an exact EditorID.";

        var problems = MutagenLoader.GetRecordProblems(env, fk.ToString());
        if (problems.Count == 0) return $"No problems found for {fk}.";
        return $"{problems.Count} problem(s) for {fk}:\n" +
               string.Join("\n", problems.Select(p => $"  [{p.Severity}] {p.Description}"));
    }

    private static string GetWinningRecord(object? env, string id)
    {
        var fk = ResolveToFk(env, id);
        if (fk.IsNull)
            return $"Could not resolve '{id}'. Pass a FormKey or an exact EditorID.";

        var contexts = MutagenLoader.GetRecordContexts(env, fk);
        if (contexts.Count == 0) return $"No record found for {fk}.";

        var winner = contexts[^1].plugin;   // load order, last = winner
        var dump = MutagenLoader.QueryRecordFields(env, winner, fk.ToString());
        return $"Winning plugin: {winner}  (of {contexts.Count} version(s) in the load order)\n{dump}";
    }

    // Read back the Papyrus scripts (VMAD) attached to a record and their property values, so the
    // AI can verify attach_script / set_script_property results. Reflection-based (works for every
    // record family that exposes a VirtualMachineAdapter).
    private static string GetScripts(object? env, string id)
    {
        var fk = ResolveToFk(env, id);
        if (fk.IsNull) return $"Could not resolve '{id}'.";
        var cache = MutagenLoader.LinkCache;
        if (cache == null || !cache.TryResolve<Mutagen.Bethesda.Plugins.Records.IMajorRecordGetter>(fk, out var rec))
            return $"No record found for {fk}.";

        var vmad = rec.GetType().GetProperty("VirtualMachineAdapter")?.GetValue(rec);
        if (vmad == null) return $"{rec.EditorID ?? fk.ToString()} [{fk}] has no attached scripts.";
        if (vmad.GetType().GetProperty("Scripts")?.GetValue(vmad) is not System.Collections.IEnumerable scripts)
            return $"{rec.EditorID ?? fk.ToString()} [{fk}] has a script adapter but no scripts.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Scripts on {rec.EditorID ?? fk.ToString()} [{fk}]:");
        int n = 0;
        foreach (var s in scripts)
        {
            n++;
            var name = s.GetType().GetProperty("Name")?.GetValue(s)?.ToString() ?? "(unnamed)";
            sb.AppendLine($"  - {name}");
            if (s.GetType().GetProperty("Properties")?.GetValue(s) is System.Collections.IEnumerable props)
                foreach (var p in props)
                {
                    var pname = p.GetType().GetProperty("Name")?.GetValue(p)?.ToString() ?? "?";
                    sb.AppendLine($"      {pname} = {DescribeScriptProp(p)}");
                }
        }
        if (n == 0) sb.AppendLine("  (adapter present but no scripts)");
        return sb.ToString();
    }

    private static string DescribeScriptProp(object p)
    {
        var t = p.GetType();
        if (t.GetProperty("Object")?.GetValue(p) is { } obj)
        {
            var inner = obj.GetType().GetProperty("Object")?.GetValue(obj) ?? obj;
            var fkv = inner.GetType().GetProperty("FormKey")?.GetValue(inner);
            return $"<{fkv}> (object)";
        }
        if (t.GetProperty("Data") is { } dataProp)
        {
            var v = dataProp.GetValue(p);
            if (v is System.Collections.IEnumerable en && v is not string)
            {
                int c = 0; foreach (var _ in en) c++;
                return $"[{c} items] ({t.Name})";
            }
            return $"{v} ({t.Name})";
        }
        return $"({t.Name})";
    }

    // Field-level diff of two records (each resolved to its winning version), using the same field
    // dump as get_record. Self-contained text diff -- no dependency on the GUI's RecordNode tree.
    private static string DiffRecords(object? env, string aId, string bId)
    {
        var fa = ResolveToFk(env, aId); if (fa.IsNull) return $"Could not resolve A '{aId}'.";
        var fb = ResolveToFk(env, bId); if (fb.IsNull) return $"Could not resolve B '{bId}'.";
        var ca = MutagenLoader.GetRecordContexts(env, fa); if (ca.Count == 0) return $"No record for A {fa}.";
        var cb = MutagenLoader.GetRecordContexts(env, fb); if (cb.Count == 0) return $"No record for B {fb}.";

        var da = MutagenLoader.QueryRecordFields(env, ca[^1].plugin, fa.ToString()).Replace("\r", "").Split('\n');
        var db = MutagenLoader.QueryRecordFields(env, cb[^1].plugin, fb.ToString()).Replace("\r", "").Split('\n');
        var setA = new HashSet<string>(da.Select(x => x.TrimEnd()));
        var setB = new HashSet<string>(db.Select(x => x.TrimEnd()));

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"diff  A={fa} ({ca[^1].plugin})  B={fb} ({cb[^1].plugin})   (- only in A, + only in B)");
        int diffs = 0;
        foreach (var line in da) { var l = line.TrimEnd(); if (l.Trim().Length > 0 && !setB.Contains(l)) { sb.AppendLine("  - " + l.Trim()); diffs++; } }
        foreach (var line in db) { var l = line.TrimEnd(); if (l.Trim().Length > 0 && !setA.Contains(l)) { sb.AppendLine("  + " + l.Trim()); diffs++; } }
        if (diffs == 0) sb.AppendLine("  (identical field dumps)");
        return sb.ToString();
    }

    private static string SearchRobcoConfigs(string? instancePath, string query, int limit)
    {
        if (string.IsNullOrWhiteSpace(instancePath) || !System.IO.Directory.Exists(instancePath))
            return "MO2 instance unknown -- load the modlist with 'Open MO2' first.";
        if (string.IsNullOrWhiteSpace(query)) return "Provide a query (a FormID, EditorID, or name).";

        // Collect each mod's (and overwrite's) RobCo_Patcher root, then scan its .ini files.
        var roots = new List<string>();
        var modsDir = System.IO.Path.Combine(instancePath, "mods");
        if (System.IO.Directory.Exists(modsDir))
            foreach (var mod in System.IO.Directory.EnumerateDirectories(modsDir))
            {
                var rc = System.IO.Path.Combine(mod, "F4SE", "Plugins", "RobCo_Patcher");
                if (System.IO.Directory.Exists(rc)) roots.Add(rc);
            }
        var ow = System.IO.Path.Combine(instancePath, "overwrite", "F4SE", "Plugins", "RobCo_Patcher");
        if (System.IO.Directory.Exists(ow)) roots.Add(ow);

        var hits = new List<string>();
        foreach (var root in roots)
        {
            // RobCo Patcher reads disabled/renamed configs too (.ini.bak, .ini.DISABLED, .ini.hidden),
            // so match "*.ini*" not just "*.ini" -- a renamed config still applies in-game.
            foreach (var ini in System.IO.Directory.EnumerateFiles(root, "*.ini*", System.IO.SearchOption.AllDirectories))
            {
                string[] lines;
                try { lines = System.IO.File.ReadAllLines(ini); } catch { continue; }
                var rel = ini.Length > modsDir.Length && ini.StartsWith(modsDir, StringComparison.OrdinalIgnoreCase)
                    ? ini[(modsDir.Length + 1)..] : System.IO.Path.GetFileName(ini);
                foreach (var line in lines)
                {
                    var l = line.Trim();
                    if (l.Length == 0 || l.StartsWith(';') || l.StartsWith('#')) continue;
                    if (l.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        hits.Add($"{rel}\n    {l}");
                        if (hits.Count >= limit) goto done;
                    }
                }
            }
        }
    done:
        if (roots.Count == 0) return "No RobCo Patcher config folders found under this MO2 instance.";
        return hits.Count == 0
            ? $"No RobCo Patcher config lines match '{query}'. (Searched {roots.Count} mod patcher folder(s).)"
            : $"RobCo Patcher matches for '{query}' ({hits.Count}):\n" + string.Join("\n", hits);
    }
}
