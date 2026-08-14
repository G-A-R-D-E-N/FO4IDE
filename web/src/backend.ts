// Typed access to the C# host objects exposed to the WebView2 page via AddHostObjectToScript.
// Every call is async: the WebView2 host-object proxy returns a Promise for each method.

/** The "backend" bridge (FO4RecordEditor.Services.BackendInterop) -- conflicts, records, edits, saves. */
export interface Backend {
  // ---- read ----
  GetConflicts(): Promise<string>;                          // JSON: ConflictEntry[]
  GetConflictMatrix(formKey: string): Promise<string>;      // JSON: ConflictMatrix
  GetRecordTree(plugin: string, id: string): Promise<string>; // JSON: FieldNode[]
  GetReferencedBy(formKey: string): Promise<string>;        // JSON: RefByEntry[]
  GetProblems(formKey: string): Promise<string>;            // JSON: RecordProblem[]
  SearchRecords(query: string, typeFilter: string): Promise<string>;  // JSON: SearchHit[]
  GetEditablePlugins(): Promise<string>;                    // JSON: string[]

  // ---- edit (in-memory until a save) ----
  OpenPlugin(plugin: string): Promise<string>;
  CreatePlugin(name: string): Promise<string>;
  SetField(plugin: string, record: string, field: string, value: string): Promise<string>;
  SetComponents(plugin: string, record: string, componentsJson: string): Promise<string>;
  SetConditions(plugin: string, record: string, conditionsJson: string): Promise<string>;
  AddListItem(plugin: string, record: string, field: string, value: string): Promise<string>;
  RemoveListItem(plugin: string, record: string, field: string, value: string): Promise<string>;
  DeleteRecord(plugin: string, id: string): Promise<string>;
  // Returns a message starting 'EXISTS:' when the target already overrides this record and
  // overwrite was false, so the caller can ask before replacing it.
  CopyAsOverride(sourcePlugin: string, id: string, patchPlugin: string, overwrite: boolean): Promise<string>;
  CopyAsOverrideMany(itemsJson: string, patchPlugin: string, overwrite: boolean): Promise<string>;
  RevertOverrides(badPlugin: string, patchPlugin: string, signature: string,
                  containsComponent: string, apply: boolean, limit: number): Promise<string>;
  CompactToEsl(plugin: string): Promise<string>;
  CheckEslEligibility(plugin: string): Promise<string>;
  CleanPlugin(plugin: string): Promise<string>;
  RenumberFormId(plugin: string, record: string, newId: string): Promise<string>;

  // ---- xEdit parity ----
  DescribeElement(plugin: string, record: string, path: string): Promise<string>;  // JSON: ElementActions
  AddElement(plugin: string, record: string, path: string, template: string): Promise<string>;
  RemoveElement(plugin: string, record: string, path: string): Promise<string>;
  MoveElement(plugin: string, record: string, path: string, delta: number): Promise<string>;
  ClearElement(plugin: string, record: string, path: string): Promise<string>;
  GetConditions(plugin: string, record: string): Promise<string>;   // JSON: ConditionDto[]
  GetConditionsAt(plugin: string, record: string, path: string): Promise<string>;
  SetConditionsAt(plugin: string, record: string, path: string, conditionsJson: string): Promise<string>;
  GetConditionFunctions(): Promise<string>;                         // JSON: string[]
  GetConditionRunOnTypes(): Promise<string>;                        // JSON: string[]
  GetConditionFunctionParams(): Promise<string>;                    // JSON: fn -> ParamSlot[]
  ResolveFormKeyLabels(formKeysCsv: string): Promise<string>;       // JSON: key -> "EditorID [key]"
  CopyAsNewRecord(sourcePlugin: string, id: string, targetPlugin: string,
                  newEditorId: string): Promise<string>;
  RemoveIdenticalToMaster(plugin: string, apply: boolean): Promise<string>;
  CreateMergedPatch(plugins: string, patchPlugin: string, apply: boolean): Promise<string>;
  AddMasters(plugin: string, mastersCsv: string): Promise<string>;
  RenumberPluginFormIds(plugin: string, start: string, apply: boolean): Promise<string>;
  CreateSeqFile(plugin: string, outputDir: string): Promise<string>;
  CheckCircularLeveledLists(plugin: string): Promise<string>;
  DeepCopyAsOverride(sourcePlugin: string, id: string, patchPlugin: string, apply: boolean, overwrite: boolean): Promise<string>;
  ChangeReferencingRecords(from: string, to: string, patchPlugin: string, apply: boolean): Promise<string>;

  // ---- navigator / workspace / detail rail ----
  GetActivePlugins(): Promise<string>;                       // JSON: ActivePlugin[]
  GetRecordTypeIndex(): Promise<string>;                     // JSON: RecordTypeEntry[]
  GetPluginRecordTypes(plugin: string): Promise<string>;     // JSON: string[] "Type (count)", one plugin only
  GetRecordsOfType(signature: string, filter: string,
                   limit: number, offset: number): Promise<string>;   // JSON: SearchHit[]
  GetRecordsGrid(plugin: string, type: string, limit: number, offset: number): Promise<string>; // JSON: { columns, rows, total, offset }
  GetContainmentPath(formKey: string): Promise<string>;      // JSON: BreadcrumbNode[]
  GetRecordDetails(formKey: string): Promise<string>;        // JSON: RecordDetails | null
  GetRecordPluginMatrix(formKey: string): Promise<string>;   // JSON: PluginMatrixRow[]
  GetDependencies(formKey: string): Promise<string>;         // JSON: Dependency[]
  GetHistory(formKey: string): Promise<string>;              // JSON: HistoryEntry[]
  GetLoadOrderSummary(): Promise<string>;                    // JSON: LoadOrderSummary

  // ---- save ----
  ResolveConflict(formKey: string, winner: string, patch: string): Promise<string>;
  SavePatch(patch: string): Promise<string>;
  SavePlugin(plugin: string, path: string): Promise<string>;
}

/** A plugin's kind comes from its header flags, not its extension: an .esp with the Master flag
 *  really is a master, and the ESL/light flag is stored as "Small". */
export type PluginKind = 'master' | 'plugin' | 'light';

export interface ActivePlugin {
  Name: string; Kind: PluginKind; LoadOrder: number; Editable: boolean; Size: number;
}
export interface RecordTypeEntry { Type: string; FriendlyName: string; Count: number; }
export interface BreadcrumbNode { Kind: string; Label: string; FormKey: string; }
export interface RecordDetails {
  FormKey: string; FormId: string; EditorId: string; Signature: string; ClassName: string;
  BaseForm: string; BaseFormKey: string; File: string; Winner: string; OverrideCount: number;
}
export interface PluginMatrixRow {
  Plugin: string; LoadOrder: number; Kind: PluginKind; Changes: number; Conflicts: number;
  IsOverride: boolean; IsWinner: boolean; LastModified: string;
}
export interface Dependency {
  Kind: 'link' | 'missing'; FormKey: string; EditorId: string; Type: string; Plugin: string;
}
export interface HistoryEntry {
  Plugin: string; LoadOrder: number; Action: string; ChangedFields: number; LastModified: string;
}
export interface LoadOrderSummary {
  TotalRecords: number; PluginCount: number; Plugins: string[];
}

/** The "appInterop" bridge (FO4RecordEditor.Services.AppInterop) -- explorer tree + env loading. */
export interface AppInterop {
  BrowseForMo2Folder(): Promise<string>;   // native folder picker; "" if cancelled/invalid
  OpenMo2Profile(instancePath: string): Promise<void>;
  LoadEnvironment(): Promise<void>;
  ScanConflicts(): Promise<string>;          // full load-order conflict scan (cached); returns a summary
  ScanBrokenRefs(): Promise<string>;         // full modlist broken-ref scan; returns full text report
  RefreshTree(): Promise<string>;            // rebuild the tree from current state (incl. AI edits); JSON plugin nodes
  GetPlugins(): Promise<string>;            // JSON: plugin nodes
  GetChildren(path: string): Promise<string>;
  OpenRecord(path: string): Promise<string>;   // resolves the node's FormKey ("" if unresolved)
}

/** xEdit-style conflict matrix from Backend.GetConflictMatrix (matches Models/ConflictMatrix). */
export interface ConflictFieldRow {
  Field: string;
  DisplayLabel: string;
  Level: number;
  Values: string[];      // one per plugin column, parallel to ConflictMatrix.Plugins ("" = unset)
  Differs: boolean;
  Statuses: string[];    // per-plugin status parallel to Values: notdefined|master|identical|win|override|lose|only
  Severity: string;      // row severity: none|override|conflict|critical
  Kind: 'Value' | 'Flag' | 'FormID';   // classified server-side, drives the sub-tabs and donut
  Group: string;         // first path segment: the subrecord this row groups under ("" = top level)
  GroupLabel: string;    // friendly form of Group, used as the group header
  IsSummary: boolean;    // a condition/component entry: collapse its sub-fields by default
  HasChildren: boolean;  // deeper rows belong to this one (show an expand chevron)
  EditKind: 'Text' | 'Bool' | 'Enum' | 'Ref';   // how a value cell edits ('Ref' = record picker)
  EnumOptions: string[] | null;          // dropdown options when EditKind === 'Enum'
  RefType: string | null;                // short display label when EditKind === 'Ref'
  RefTypes: string | null;               // csv of concrete target record types (picker filter)
}

export interface RefByEntry { Plugin: string; FormKey: string; EditorID: string; Type: string; }
export interface RecordProblem { Severity: string; Description: string; }
export interface SearchHit { FormKey: string; EditorID: string; Type: string; Plugin: string; }
/** Which xEdit element actions are legal at a grid row, from DescribeElement. */
export interface ElementActions {
  canAdd: boolean;
  templates: string[];     // types the list accepts; >1 means Add is a submenu, as in xEdit
  elementType: string;
  canRemove: boolean;
  canMoveUp: boolean;
  canMoveDown: boolean;
  canClear: boolean;
  count: number;
  error?: string;
}

/** One row of a record's Conditions list, in the shape SetConditions round-trips. */
export interface ConditionDto {
  function: string;
  operator: string;
  value?: number;
  compareGlobal?: string;
  param1?: string | number;
  param2?: string | number;
  runOn?: string;
  reference?: string;
  flags?: string;
}
export interface ConflictMatrix {
  FormKey: string;
  EditorID: string;
  Type: string;
  Winner: string;        // winning plugin (last in load order)
  Plugins: string[];     // columns, in load order
  Rows: ConflictFieldRow[];
  Level: string;         // record rollup: onlyone|noconflict|override|conflict|critical
}

/** One conflicting record across the load order (matches Models/ConflictEntry). */
export interface ConflictEntry {
  FormKey: string;
  EditorID: string;
  Type: string;
  Plugins: string[];
  Winner: string;
  InvolvesMod: boolean;
}

/** Shape of each node from Backend.GetRecordTree (matches BackendInterop.NodeDto). */
export interface FieldNode {
  key: string;
  label: string;
  value: string;
  values: Record<string, string>;             // value per plugin column
  editKind: "Text" | "Bool" | "Enum";
  enumOptions: string[] | null;               // dropdown options when editKind === "Enum"
  isSummary: boolean;
  hasChildren: boolean;
  children: FieldNode[];
}

/** The "chat" bridge (FO4RecordEditor.Services.ChatInterop) -- the Claude AI assistant.
 *  Replies stream back as web messages, not return values: listen on chrome.webview 'message'
 *  for {Type:"AiToken",Text} / {Type:"AiToolStatus",Text} / {Type:"AiDone"[,Stopped]} / {Type:"AiError",Text}. */
export interface Chat {
  SendMessage(sessionId: string, text: string, imagesJson: string): Promise<void>;   // imagesJson: JSON array of data-URL/base64 images
  CancelMessage(sessionId: string): Promise<void>;
  ResetChat(): Promise<void>;
  IsAgentReady(): Promise<boolean>;
  // sessions (persisted under %AppData%\FO4RecordEditor\Chats)
  ListSessions(): Promise<string>;            // JSON: [{id,name,createdAt,count}]
  NewSession(): Promise<string>;              // JSON: {id,name,messages:[]}
  LoadSession(id: string): Promise<string>;   // JSON: {id,name,messages:[{isUser,text}]}
  RenameSession(id: string, name: string): Promise<string>;
  DeleteSession(id: string): Promise<string>; // returns updated ListSessions JSON
  ForkSession(id: string): Promise<string>;   // summarize -> new chat seeded with the summary; JSON session
  GetCommands(): Promise<string>;             // JSON: [{name,args,help}]
}

export interface ChatSessionMeta { id: string; name: string; createdAt: string; count: number; }
export interface ChatSessionMsg { isUser: boolean; text: string; }
export interface ChatSessionFull { id: string; name: string; messages: ChatSessionMsg[]; }
export interface SlashCommand { name: string; args: string; help: string; }

/** The "papyrus" bridge (FO4RecordEditor.Services.PapyrusInterop) -- compile/decompile Papyrus. */
export interface PapyrusHost {
  BrowseForFile(title: string, filter: string): Promise<string>;   // native file picker; "" if cancelled
  BrowseForFolder(title: string): Promise<string>;                 // native folder picker; "" if cancelled
  // Compile .psc -> .pex. engine is 'auto' | 'builtin' | 'creationkit'; auto prefers an installed
  // Creation Kit and uses the built-in compiler when there is none. Returns the compiler's output.
  Compile(source: string, output: string, imports: string, flags: string,
          all: boolean, optimize: boolean, release: boolean, compilerPath: string,
          engine: string): Promise<string>;
  // Decompile .pex -> .psc (built-in). For a single file returns source text inline unless write=true.
  Decompile(source: string, output: string, assembly: boolean, write: boolean): Promise<string>;
  // Open a folder (or a file's folder) in Windows Explorer. Returns "" on success.
  OpenFolder(path: string): Promise<string>;
  // Write a dropped file's bytes to a temp path and return it (drag-and-drop helper). "ERR:" on fail.
  StageDroppedFile(name: string, base64: string): Promise<string>;
  // Look up a function's Syntax/Parameters/Return Value from the CK wiki mirror (see Settings ->
  // CK Wiki Path). script disambiguates when the function name alone is ambiguous.
  LookupFunction(script: string, functionName: string): Promise<string>;
  // Look up a script's Extends/Global Functions/Member Functions/Events from the CK wiki mirror.
  LookupScriptInfo(script: string): Promise<string>;

  // --- Source analysis (Analyze mode). No Creation Kit involved: these PARSE, they do not compile,
  // so a buffer with no diagnostics is not proof that it builds. They take the editor buffer rather
  // than a path, because "errors as you type" is about unsaved text.

  // Diagnostics + outline for a buffer, as JSON (PapyrusAnalyzeResult). `path` may be "".
  Analyze(text: string, path: string): Promise<string>;
  // The declaration of the symbol at a 0-based offset, as JSON (PapyrusSymbolResult).
  SymbolAt(text: string, path: string, offset: number, imports: string): Promise<string>;
  // Read a .psc into the editor. Returns its text, or a string starting with "ERR:".
  ReadScript(path: string): Promise<string>;
  // Write the buffer back to a .psc. Returns "" on success, or a string starting with "ERR:".
  WriteScript(path: string, text: string): Promise<string>;
}

/** One syntax problem, positioned in the buffer. Lines and columns are 1-based. */
export interface PapyrusDiagnostic {
  code: string;
  severity: 'error' | 'warning';
  message: string;
  line: number;
  column: number;
  start: number;
  length: number;
}

/** One declaration in the outline. `start`/`nameStart` are 0-based buffer offsets. */
export interface PapyrusSymbol {
  name: string;
  kind: string;
  signature: string;
  documentation: string | null;
  container: string | null;
  line: number;
  column: number;
  start: number;
  nameStart: number;
  nameLength: number;
  nameLine: number;
}

export interface PapyrusAnalyzeResult {
  script?: string;
  extends?: string | null;
  errorCount?: number;
  diagnostics?: PapyrusDiagnostic[];
  symbols?: PapyrusSymbol[];
  /** Set only if the parser itself failed, which is not supposed to be possible. */
  error?: string;
}

/**
 * Where a symbol is declared. `resolved: false` is a normal answer, not a failure -- phase 1 has no
 * type checker, so a member reached through an expression whose type is not written down cannot be
 * resolved, and it reports that rather than guessing.
 */
export interface PapyrusSymbolResult {
  resolved: boolean;
  name?: string;
  kind?: string;
  signature?: string;
  documentation?: string | null;
  container?: string | null;
  file?: string | null;
  /** False when the declaration is in another file, which the panel cannot jump to in place. */
  sameFile?: boolean;
  line?: number;
  column?: number;
  start?: number;
  length?: number;
  error?: string;
}

/** The "nif" bridge (FO4RecordEditor.Services.NifInterop) -- author/inspect/verify/repair FO4 NIFs. */
export interface NifHost {
  BrowseForFile(title: string, filter: string): Promise<string>;   // native file picker; "" if cancelled
  BrowseForFolder(title: string): Promise<string>;                 // native folder picker; "" if cancelled
  BrowseForSave(title: string, filter: string): Promise<string>;   // native save picker; "" if cancelled
  // Author a static FO4 NIF from an OBJ. Returns niftool's RESULT + verify output.
  Import(objPath: string, outNif: string, material: string, texDiffuse: string,
         texNormal: string, collision: boolean, fromBlender: boolean): Promise<string>;
  Inspect(nifPath: string): Promise<string>;   // JSON summary
  Geo(nifPath: string): Promise<string>;        // full geometry JSON (flat arrays) for the 3D viewer
  // Resolve a NIF texture (game-relative path) to a PNG data URL via texconv; "" if not found.
  // textureRoot: optional user-picked Data/Textures folder tried before auto-resolution.
  GetTexture(nifPath: string, relTexPath: string, textureRoot: string): Promise<string>;
  Verify(nifPath: string): Promise<string>;    // RESULT: + [ok]/[FAIL] checks
  Fix(nifPath: string, outNif: string): Promise<string>;  // repair + report actions
  Tree(nifPath: string): Promise<string>;      // curated editable property tree (JSON) for Edit mode
  // Apply a JSON array of field edits and save. outNif blank = save in place. Returns RESULT text.
  ApplyEdits(nifPath: string, editsJson: string, outNif: string): Promise<string>;
  OpenFolder(path: string): Promise<string>;   // reveal in Explorer; "" on success
  StageDroppedFile(name: string, base64: string): Promise<string>;  // drag-drop helper; "ERR:" on fail
}

/** One row in a MastersListResult (matches WriteService.ListMastersJson's anonymous shape). */
export interface MasterRow {
  index: number;
  name: string;
  size: number | null;   // null = not found on disk / not in the loaded environment
  used: boolean;          // false = save_plugin's Iterate would drop this master
}
export interface MastersListResult {
  pluginName?: string;
  masters?: MasterRow[];
  light?: boolean;        // current ESL/Small header flag
  error?: string;
}

/** The "masters" bridge (FO4RecordEditor.Services.MastersInterop) -- master table + ESL flag. */
export interface MastersHost {
  GetPlugins(): Promise<string>;                       // JSON: string[] of plugin names
  List(plugin: string): Promise<string>;                // JSON: MastersListResult
  // orderJson: JSON array of master names in the new order (must be an exact permutation). Writes immediately.
  Reorder(plugin: string, orderJson: string): Promise<string>;
  SetLight(plugin: string, light: boolean): Promise<string>;   // in memory until SavePlugin
  SavePlugin(plugin: string): Promise<string>;
}

/** One entry in an ArchiveListResult (matches ArchiveService.ListArchiveJson's anonymous shape). */
export interface ArchiveEntry { path: string; size: number; }
export interface ArchiveListResult {
  archiveName?: string;
  totalCount?: number;
  shownCount?: number;
  truncated?: boolean;
  entries?: ArchiveEntry[];
  error?: string;
}

/** Filter mode for ArchiveHost.List/ExtractAll's 'filter' string. 'simple' = plain substring
 * (the default, and the only mode the AI-facing MCP tools use). 'wildcard'/'regex' are GUI-only. */
export type ArchiveFilterMode = 'simple' | 'wildcard' | 'regex';

/** Result of ArchiveHost.Compare (matches ArchiveService.CompareArchivesJson's anonymous shape). */
export interface ArchiveCompareResult {
  archiveA?: string;
  archiveB?: string;
  added?: string[];
  removed?: string[];
  changed?: string[];
  identicalCount?: number;
  error?: string;
}

/** The "archive" bridge (FO4RecordEditor.Services.ArchiveInterop) -- list/extract BA2/BSA contents. */
export interface ArchiveHost {
  BrowseForFile(title: string, filter: string): Promise<string>;   // native file picker; "" if cancelled
  BrowseForFolder(title: string): Promise<string>;                 // native folder picker; "" if cancelled
  BrowseForSave(title: string, filter: string): Promise<string>;   // native save picker; "" if cancelled
  List(archivePath: string, filter: string, limit: number, filterMode: ArchiveFilterMode): Promise<string>;   // JSON: ArchiveListResult
  ExtractFile(archivePath: string, innerPath: string, outPath: string): Promise<string>;
  // innerPathsJson: JSON array of the panel's selected entry paths.
  ExtractSelected(archivePath: string, innerPathsJson: string, outDir: string): Promise<string>;
  ExtractAll(archivePath: string, outDir: string, filter: string, limit: number, filterMode: ArchiveFilterMode): Promise<string>;
  Compare(archivePathA: string, archivePathB: string): Promise<string>;   // JSON: ArchiveCompareResult
  // sourcePathsJson: JSON array of source folder paths. format: "General" | "DDS". rootDir: required.
  Pack(sourcePathsJson: string, outputBa2: string, format: string, rootDir: string, compress: boolean): Promise<string>;
  OpenFolder(path: string): Promise<string>;   // reveal in Explorer; "" on success
}

/** One field in a MaterialField list (matches MaterialService.CollectFields' anonymous shape). */
export interface MaterialField {
  name: string;
  section: 'material' | 'header';
  type: 'bool' | 'float' | 'int' | 'string' | 'color';
  value: string;   // 'true'/'false' for bool, a number as text for float/int, 'r, g, b' for color
}
export interface MaterialInspectResult {
  fileName?: string;
  version?: number;
  fields?: MaterialField[];
  error?: string;
}

/** The "material" bridge (FO4RecordEditor.Services.MaterialInterop) -- inspect/edit .bgsm shader fields. */
export interface MaterialHost {
  BrowseForFile(title: string, filter: string): Promise<string>;   // native file picker; "" if cancelled
  Inspect(path: string): Promise<string>;    // JSON: MaterialInspectResult
  // fieldsJson: {"FieldName":"newValue", ...}. outPath blank = overwrite in place.
  SetFields(path: string, fieldsJson: string, outPath: string): Promise<string>;
  OpenFolder(path: string): Promise<string>;   // reveal in Explorer; "" on success
  StageDroppedFile(name: string, base64: string): Promise<string>;  // drag-drop helper; "ERR:" on fail
}

/** The "audio" bridge (FO4RecordEditor.Services.AudioInterop) -- convert to/from xWMA, merge/split .fuz. */
export interface AudioHost {
  BrowseForFile(title: string, filter: string): Promise<string>;   // native file picker; "" if cancelled
  BrowseForFolder(title: string): Promise<string>;                 // native folder picker; "" if cancelled
  BrowseForSave(title: string, filter: string): Promise<string>;   // native save picker; "" if cancelled
  // Any ffmpeg-readable audio/video source -> .xwm. bitrateBps: one of xWMAEncode's supported
  // bitrates (20000/32000/48000/64000/96000/160000/192000), or 0 for its default (48000).
  ConvertToXwm(source: string, output: string, bitrateBps: number): Promise<string>;
  // .xwm -> WAV, and on to targetExt (mp3/flac/ogg/...) via ffmpeg if targetExt isn't "wav".
  ConvertFromXwm(source: string, output: string, targetExt: string): Promise<string>;
  // Pack an audio source (encoded to xwm first if needed) + optional .lip into a .fuz container.
  MakeFuz(audioSource: string, lipPath: string, fuzOutput: string, noLip: boolean): Promise<string>;
  // Split a .fuz into its xwm/lip parts, optionally also decoding the xwm to .wav.
  ExtractFuz(fuzPath: string, xwmOutput: string, lipOutput: string, alsoWav: boolean): Promise<string>;
  OpenFolder(path: string): Promise<string>;   // reveal in Explorer; "" on success
  StageDroppedFile(name: string, base64: string): Promise<string>;  // drag-drop helper; "ERR:" on fail
}

/** The "settings" bridge (FO4RecordEditor.Services.SettingsInterop) -- the app settings panel. */
export interface SettingsHost {
  GetSettings(): Promise<string>;            // JSON of editable settings
  SaveSettings(json: string): Promise<string>;  // persists + rebuilds AI provider; returns status
  BrowseFolder(title: string, current: string): Promise<string>;  // native folder picker; "" if cancelled
  BrowseFile(title: string, filter: string, current: string): Promise<string>;  // native file picker; "" if cancelled
  TestClaude(path: string): Promise<string>; // verifies the Claude Code CLI
}

/** One placed reference from CellService.GetPlacedReferencesJson. modelPath is null for reference
 * types/base objects with no Model subrecord (actors, traps, etc.) -- the viewer draws a marker. */
export interface CellPlacedReference {
  formKey: string;
  editorId?: string;
  recordType: string;
  baseType?: string;      // base object's record type (Static/Light/Furniture/...) -- the useful layer grouping
  baseFormKey: string;
  baseEditorId?: string;
  modelPath: string | null;
  // Ground decal: the base is a TextureSet (TXST), which has no Model at all -- the engine projects
  // its Diffuse texture directly, no dedicated mesh. modelPath stays null for these; decalDiffuse +
  // decalWidth/decalHeight (world units, from the TXST's ObjectBounds) are set instead.
  decalDiffuse?: string | null;
  decalWidth?: number | null;
  decalHeight?: number | null;
  // Static Collection (SCOL) fallback: the SCOL's own precombined modelPath often doesn't exist as a
  // real file (a lot of mods ship the record without ever baking precombines -- confirmed on a real
  // case, see CellService.cs's doc comment). scolParts lists each member static's own model path and
  // every local placement (position/rotation/scale relative to THIS reference's own transform) so the
  // viewer can render the exploded member statics instead of a "mesh unavailable" marker.
  scolParts?: {
    modelPath: string;
    placements: { x: number; y: number; z: number; rx: number; ry: number; rz: number; scale: number }[];
  }[] | null;
  position: { x: number; y: number; z: number };
  rotation: { x: number; y: number; z: number };
  scale: number;
}
export interface CellReferencesResult {
  cellFormKey?: string;
  cellEditorId?: string;
  cellName?: string;
  interior?: boolean;
  referenceCount?: number;
  withModelCount?: number;
  references?: CellPlacedReference[];
  error?: string;
}
/** GeoBatch's per-model result: either niftool's raw geo JSON (NifGeo shape, see NifViewport.tsx)
 * or { error }, e.g. "Not found" or a Next-Gen-archive decompression failure. */
export interface CellGeoBatchResult {
  count: number;
  geometry: Record<string, unknown>;
}

/** The "cell" bridge (FO4RecordEditor.Services.CellInterop) -- the Cell Viewer panel. */
/** One match from CellHost.SearchCells (MutagenLoader.SearchHit, CELL-filtered). Newtonsoft
 * serializes the C# record's properties as-is (PascalCase), same as the existing SearchHit above. */
export interface CellSearchHit {
  FormKey: string;
  EditorID: string;
  Type: string;
  Plugin: string;
  Name: string;
}

export interface CellHost {
  GetPlacedReferences(cellId: string): Promise<string>;          // JSON: CellReferencesResult
  // Exterior cells are addressed by worldspace + grid coordinate rather than by FormKey/EditorID.
  GetPlacedReferencesAtGrid(worldspace: string, gridX: number, gridY: number): Promise<string>;
  SearchWorldspaces(query: string, limit: number): Promise<string>;  // JSON: CellSearchHit[] (WRLD)
  // relModelPathsJson: JSON array of unique modelPath strings from GetPlacedReferences.
  GetGeometryBatch(relModelPathsJson: string): Promise<string>;  // JSON: CellGeoBatchResult
  GetPlugins(): Promise<string>;                                  // JSON: string[] -- empty = nothing loaded
  SearchCells(query: string, limit: number): Promise<string>;    // JSON: CellSearchHit[]
  // Same TextureService pipeline NifPanel's "View" mode uses; "" if unresolved/unconvertible.
  GetTexture(relModelPath: string, relTexPath: string): Promise<string>;  // "data:image/png;base64,..." or ""
  // Poll while a GetGeometryBatch call is in flight for a real N/total readout.
  GetGeometryBatchProgress(): Promise<string>;                    // JSON: { done: number; total: number }
  // Gizmo drag-end save: writes the moved/rotated reference into an override in patchPlugin (created
  // if new) and leaves it open in memory -- still needs backend.SavePlugin(patchPlugin, "") to persist
  // to disk. x/y/z/rx/ry/rz are in the SAME REFR-space units GetPlacedReferences reports (not the
  // viewport's recentered world-space -- undo the worldCenter offset before calling this).
  SetPlacedReferenceTransform(formKey: string, patchPlugin: string,
    x: number, y: number, z: number, rx: number, ry: number, rz: number): Promise<string>;
}

/** The "graph" bridge (FO4RecordEditor.Services.GraphInterop) -- the Blueprint panel.
 *  Every payload is a JSON string: the marshaller carries only string|boolean|number in and
 *  string|void out, so a whole graph document travels as text in both directions. Casing is
 *  camelCase on both sides, set once in GraphDocumentJson. */
export interface GraphHost {
  /** Built-in node types plus the script list. One call per app run, not per panel open. */
  GetCorePalette(): Promise<string>;
  /** At most `limit` entries plus the true total, so a capped list can say what it is hiding. */
  SearchPalette(kind: string, query: string, scriptFilter: string, limit: number): Promise<string>;
  /** The full pin list for one node type. Search results omit pins deliberately. */
  GetNodeSignature(nodeType: string): Promise<string>;
  /** Diagnostics only. Cheap enough to run on a debounce while editing. */
  ValidateGraph(documentJson: string): Promise<string>;
  /** Generated .psc plus diagnostics, without compiling. */
  CompileToSource(documentJson: string): Promise<string>;
  /** Generated .psc through to .pex, written into outputDirectory when it compiles. */
  CompileToPex(documentJson: string, outputDirectory: string): Promise<string>;
  LoadGraph(path: string): Promise<string>;
  /**
   * A .psc or .pex read into a graph. Unlike LoadGraph this answers with an envelope, because a
   * script the lifter cannot express is refused by name and line and those refusals belong in the
   * problems list rather than in a status string.
   */
  LoadScript(path: string): Promise<string>;
  SaveGraph(path: string, documentJson: string): Promise<string>;
  BrowseForGraph(save: boolean): Promise<string>;
  /** The Open dialog, offering .fograph, .psc and .pex. */
  BrowseForScript(): Promise<string>;
  /** Drops the cached palette after the import roots or the scripts on them change. */
  Refresh(): Promise<void>;
}

declare global {
  interface Window {
    chrome?: {
      webview?: {
        hostObjects?: {
          backend?: Backend;
          appInterop?: AppInterop;
          chat?: Chat;
          settings?: SettingsHost;
          papyrus?: PapyrusHost;
          nif?: NifHost;
          material?: MaterialHost;
          masters?: MastersHost;
          archive?: ArchiveHost;
          audio?: AudioHost;
          cell?: CellHost;
          graph?: GraphHost;
        };
      };
    };
  }
}

export const getBackend = (): Backend | undefined =>
  window.chrome?.webview?.hostObjects?.backend;

export const getAppInterop = (): AppInterop | undefined =>
  window.chrome?.webview?.hostObjects?.appInterop;

export const getPapyrus = (): PapyrusHost | undefined =>
  window.chrome?.webview?.hostObjects?.papyrus;

export const getNif = (): NifHost | undefined =>
  window.chrome?.webview?.hostObjects?.nif;

export const getMaterial = (): MaterialHost | undefined =>
  window.chrome?.webview?.hostObjects?.material;

export const getMasters = (): MastersHost | undefined =>
  window.chrome?.webview?.hostObjects?.masters;

export const getArchive = (): ArchiveHost | undefined =>
  window.chrome?.webview?.hostObjects?.archive;

export const getAudio = (): AudioHost | undefined =>
  window.chrome?.webview?.hostObjects?.audio;

export const getCell = (): CellHost | undefined =>
  window.chrome?.webview?.hostObjects?.cell;

export const getGraph = (): GraphHost | undefined =>
  window.chrome?.webview?.hostObjects?.graph;
