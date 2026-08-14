
export interface Backend {

  GetConflicts(): Promise<string>;
  GetConflictMatrix(formKey: string): Promise<string>;
  GetRecordTree(plugin: string, id: string): Promise<string>;
  GetReferencedBy(formKey: string): Promise<string>;
  GetProblems(formKey: string): Promise<string>;
  SearchRecords(query: string, typeFilter: string): Promise<string>;
  GetEditablePlugins(): Promise<string>;

  OpenPlugin(plugin: string): Promise<string>;
  CreatePlugin(name: string): Promise<string>;
  SetField(plugin: string, record: string, field: string, value: string): Promise<string>;
  SetComponents(plugin: string, record: string, componentsJson: string): Promise<string>;
  SetConditions(plugin: string, record: string, conditionsJson: string): Promise<string>;
  AddListItem(plugin: string, record: string, field: string, value: string): Promise<string>;
  RemoveListItem(plugin: string, record: string, field: string, value: string): Promise<string>;
  DeleteRecord(plugin: string, id: string): Promise<string>;

  CopyAsOverride(sourcePlugin: string, id: string, patchPlugin: string, overwrite: boolean): Promise<string>;
  CopyAsOverrideMany(itemsJson: string, patchPlugin: string, overwrite: boolean): Promise<string>;
  RevertOverrides(badPlugin: string, patchPlugin: string, signature: string,
                  containsComponent: string, apply: boolean, limit: number): Promise<string>;
  CompactToEsl(plugin: string): Promise<string>;
  CheckEslEligibility(plugin: string): Promise<string>;
  CleanPlugin(plugin: string): Promise<string>;
  RenumberFormId(plugin: string, record: string, newId: string): Promise<string>;

  DescribeElement(plugin: string, record: string, path: string): Promise<string>;
  AddElement(plugin: string, record: string, path: string, template: string): Promise<string>;
  RemoveElement(plugin: string, record: string, path: string): Promise<string>;
  MoveElement(plugin: string, record: string, path: string, delta: number): Promise<string>;
  ClearElement(plugin: string, record: string, path: string): Promise<string>;
  GetConditions(plugin: string, record: string): Promise<string>;
  GetConditionsAt(plugin: string, record: string, path: string): Promise<string>;
  SetConditionsAt(plugin: string, record: string, path: string, conditionsJson: string): Promise<string>;
  GetConditionFunctions(): Promise<string>;
  GetConditionRunOnTypes(): Promise<string>;
  GetConditionFunctionParams(): Promise<string>;
  ResolveFormKeyLabels(formKeysCsv: string): Promise<string>;
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

  GetActivePlugins(): Promise<string>;
  GetRecordTypeIndex(): Promise<string>;
  GetPluginRecordTypes(plugin: string): Promise<string>;
  GetRecordsOfType(signature: string, filter: string,
                   limit: number, offset: number): Promise<string>;
  GetRecordsGrid(plugin: string, type: string, limit: number, offset: number): Promise<string>;
  GetContainmentPath(formKey: string): Promise<string>;
  GetRecordDetails(formKey: string): Promise<string>;
  GetRecordPluginMatrix(formKey: string): Promise<string>;
  GetDependencies(formKey: string): Promise<string>;
  GetHistory(formKey: string): Promise<string>;
  GetLoadOrderSummary(): Promise<string>;

  ResolveConflict(formKey: string, winner: string, patch: string): Promise<string>;
  SavePatch(patch: string): Promise<string>;
  SavePlugin(plugin: string, path: string): Promise<string>;
}

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

export interface AppInterop {
  BrowseForMo2Folder(): Promise<string>;
  OpenMo2Profile(instancePath: string): Promise<void>;
  LoadEnvironment(): Promise<void>;
  ScanConflicts(): Promise<string>;
  ScanBrokenRefs(): Promise<string>;
  RefreshTree(): Promise<string>;
  GetPlugins(): Promise<string>;
  GetChildren(path: string): Promise<string>;
  OpenRecord(path: string): Promise<string>;
}

export interface ConflictFieldRow {
  Field: string;
  DisplayLabel: string;
  Level: number;
  Values: string[];
  Differs: boolean;
  Statuses: string[];
  Severity: string;
  Kind: 'Value' | 'Flag' | 'FormID';
  Group: string;
  GroupLabel: string;
  IsSummary: boolean;
  HasChildren: boolean;
  EditKind: 'Text' | 'Bool' | 'Enum' | 'Ref';
  EnumOptions: string[] | null;
  RefType: string | null;
  RefTypes: string | null;
}

export interface RefByEntry { Plugin: string; FormKey: string; EditorID: string; Type: string; }
export interface RecordProblem { Severity: string; Description: string; }
export interface SearchHit { FormKey: string; EditorID: string; Type: string; Plugin: string; }

export interface ElementActions {
  canAdd: boolean;
  templates: string[];
  elementType: string;
  canRemove: boolean;
  canMoveUp: boolean;
  canMoveDown: boolean;
  canClear: boolean;
  count: number;
  error?: string;
}

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
  Winner: string;
  Plugins: string[];
  Rows: ConflictFieldRow[];
  Level: string;
}

export interface ConflictEntry {
  FormKey: string;
  EditorID: string;
  Type: string;
  Plugins: string[];
  Winner: string;
  InvolvesMod: boolean;
}

export interface FieldNode {
  key: string;
  label: string;
  value: string;
  values: Record<string, string>;
  editKind: "Text" | "Bool" | "Enum";
  enumOptions: string[] | null;
  isSummary: boolean;
  hasChildren: boolean;
  children: FieldNode[];
}

export interface Chat {
  SendMessage(sessionId: string, text: string, imagesJson: string): Promise<void>;
  CancelMessage(sessionId: string): Promise<void>;
  ResetChat(): Promise<void>;
  IsAgentReady(): Promise<boolean>;

  ListSessions(): Promise<string>;
  NewSession(): Promise<string>;
  LoadSession(id: string): Promise<string>;
  RenameSession(id: string, name: string): Promise<string>;
  DeleteSession(id: string): Promise<string>;
  ForkSession(id: string): Promise<string>;
  GetCommands(): Promise<string>;
}

export interface ChatSessionMeta { id: string; name: string; createdAt: string; count: number; }
export interface ChatSessionMsg { isUser: boolean; text: string; }
export interface ChatSessionFull { id: string; name: string; messages: ChatSessionMsg[]; }
export interface SlashCommand { name: string; args: string; help: string; }

export interface PapyrusHost {
  BrowseForFile(title: string, filter: string): Promise<string>;
  BrowseForFolder(title: string): Promise<string>;

  Compile(source: string, output: string, imports: string, flags: string,
          all: boolean, optimize: boolean, release: boolean, compilerPath: string,
          engine: string): Promise<string>;

  Decompile(source: string, output: string, assembly: boolean, write: boolean): Promise<string>;

  OpenFolder(path: string): Promise<string>;

  StageDroppedFile(name: string, base64: string): Promise<string>;

  LookupFunction(script: string, functionName: string): Promise<string>;

  LookupScriptInfo(script: string): Promise<string>;

  Analyze(text: string, path: string): Promise<string>;

  SymbolAt(text: string, path: string, offset: number, imports: string): Promise<string>;

  ReadScript(path: string): Promise<string>;

  WriteScript(path: string, text: string): Promise<string>;
}

export interface PapyrusDiagnostic {
  code: string;
  severity: 'error' | 'warning';
  message: string;
  line: number;
  column: number;
  start: number;
  length: number;
}

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

  error?: string;
}

export interface PapyrusSymbolResult {
  resolved: boolean;
  name?: string;
  kind?: string;
  signature?: string;
  documentation?: string | null;
  container?: string | null;
  file?: string | null;

  sameFile?: boolean;
  line?: number;
  column?: number;
  start?: number;
  length?: number;
  error?: string;
}

export interface NifHost {
  BrowseForFile(title: string, filter: string): Promise<string>;
  BrowseForFolder(title: string): Promise<string>;
  BrowseForSave(title: string, filter: string): Promise<string>;

  Import(objPath: string, outNif: string, material: string, texDiffuse: string,
         texNormal: string, collision: boolean, fromBlender: boolean): Promise<string>;
  Inspect(nifPath: string): Promise<string>;
  Geo(nifPath: string): Promise<string>;

  GetTexture(nifPath: string, relTexPath: string, textureRoot: string): Promise<string>;
  Verify(nifPath: string): Promise<string>;
  Fix(nifPath: string, outNif: string): Promise<string>;
  Tree(nifPath: string): Promise<string>;

  ApplyEdits(nifPath: string, editsJson: string, outNif: string): Promise<string>;
  OpenFolder(path: string): Promise<string>;
  StageDroppedFile(name: string, base64: string): Promise<string>;
}

export interface MasterRow {
  index: number;
  name: string;
  size: number | null;
  used: boolean;
}
export interface MastersListResult {
  pluginName?: string;
  masters?: MasterRow[];
  light?: boolean;
  error?: string;
}

export interface MastersHost {
  GetPlugins(): Promise<string>;
  List(plugin: string): Promise<string>;

  Reorder(plugin: string, orderJson: string): Promise<string>;
  SetLight(plugin: string, light: boolean): Promise<string>;
  SavePlugin(plugin: string): Promise<string>;
}

export interface ArchiveEntry { path: string; size: number; }
export interface ArchiveListResult {
  archiveName?: string;
  totalCount?: number;
  shownCount?: number;
  truncated?: boolean;
  entries?: ArchiveEntry[];
  error?: string;
}

export type ArchiveFilterMode = 'simple' | 'wildcard' | 'regex';

export interface ArchiveCompareResult {
  archiveA?: string;
  archiveB?: string;
  added?: string[];
  removed?: string[];
  changed?: string[];
  identicalCount?: number;
  error?: string;
}

export interface ArchiveHost {
  BrowseForFile(title: string, filter: string): Promise<string>;
  BrowseForFolder(title: string): Promise<string>;
  BrowseForSave(title: string, filter: string): Promise<string>;
  List(archivePath: string, filter: string, limit: number, filterMode: ArchiveFilterMode): Promise<string>;
  ExtractFile(archivePath: string, innerPath: string, outPath: string): Promise<string>;

  ExtractSelected(archivePath: string, innerPathsJson: string, outDir: string): Promise<string>;
  ExtractAll(archivePath: string, outDir: string, filter: string, limit: number, filterMode: ArchiveFilterMode): Promise<string>;
  Compare(archivePathA: string, archivePathB: string): Promise<string>;

  Pack(sourcePathsJson: string, outputBa2: string, format: string, rootDir: string, compress: boolean): Promise<string>;
  OpenFolder(path: string): Promise<string>;
}

export interface MaterialField {
  name: string;
  section: 'material' | 'header';
  type: 'bool' | 'float' | 'int' | 'string' | 'color';
  value: string;
}
export interface MaterialInspectResult {
  fileName?: string;
  version?: number;
  fields?: MaterialField[];
  error?: string;
}

export interface MaterialHost {
  BrowseForFile(title: string, filter: string): Promise<string>;
  Inspect(path: string): Promise<string>;

  SetFields(path: string, fieldsJson: string, outPath: string): Promise<string>;
  OpenFolder(path: string): Promise<string>;
  StageDroppedFile(name: string, base64: string): Promise<string>;
}

export interface AudioHost {
  BrowseForFile(title: string, filter: string): Promise<string>;
  BrowseForFolder(title: string): Promise<string>;
  BrowseForSave(title: string, filter: string): Promise<string>;

  ConvertToXwm(source: string, output: string, bitrateBps: number): Promise<string>;

  ConvertFromXwm(source: string, output: string, targetExt: string): Promise<string>;

  MakeFuz(audioSource: string, lipPath: string, fuzOutput: string, noLip: boolean): Promise<string>;

  ExtractFuz(fuzPath: string, xwmOutput: string, lipOutput: string, alsoWav: boolean): Promise<string>;
  OpenFolder(path: string): Promise<string>;
  StageDroppedFile(name: string, base64: string): Promise<string>;
}

export interface SettingsHost {
  GetSettings(): Promise<string>;
  SaveSettings(json: string): Promise<string>;
  BrowseFolder(title: string, current: string): Promise<string>;
  BrowseFile(title: string, filter: string, current: string): Promise<string>;
  TestClaude(path: string): Promise<string>;
}

export interface CellPlacedReference {
  formKey: string;
  editorId?: string;
  recordType: string;
  baseType?: string;
  baseFormKey: string;
  baseEditorId?: string;
  modelPath: string | null;

  decalDiffuse?: string | null;
  decalWidth?: number | null;
  decalHeight?: number | null;

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

export interface CellGeoBatchResult {
  count: number;
  geometry: Record<string, unknown>;
}

export interface CellSearchHit {
  FormKey: string;
  EditorID: string;
  Type: string;
  Plugin: string;
  Name: string;
}

export interface CellHost {
  GetPlacedReferences(cellId: string): Promise<string>;

  GetPlacedReferencesAtGrid(worldspace: string, gridX: number, gridY: number): Promise<string>;
  SearchWorldspaces(query: string, limit: number): Promise<string>;

  GetGeometryBatch(relModelPathsJson: string): Promise<string>;
  GetPlugins(): Promise<string>;
  SearchCells(query: string, limit: number): Promise<string>;

  GetTexture(relModelPath: string, relTexPath: string): Promise<string>;

  GetGeometryBatchProgress(): Promise<string>;

  SetPlacedReferenceTransform(formKey: string, patchPlugin: string,
    x: number, y: number, z: number, rx: number, ry: number, rz: number): Promise<string>;
}

export interface GraphHost {

  GetCorePalette(): Promise<string>;

  SearchPalette(kind: string, query: string, scriptFilter: string, limit: number): Promise<string>;

  GetNodeSignature(nodeType: string): Promise<string>;

  ValidateGraph(documentJson: string): Promise<string>;

  CompileToSource(documentJson: string): Promise<string>;

  CompileToPex(documentJson: string, outputDirectory: string): Promise<string>;
  LoadGraph(path: string): Promise<string>;

  LoadScript(path: string): Promise<string>;
  SaveGraph(path: string, documentJson: string): Promise<string>;
  BrowseForGraph(save: boolean): Promise<string>;

  BrowseForScript(): Promise<string>;

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
        addEventListener(type: string, listener: (event: MessageEvent) => void): void;
        removeEventListener(type: string, listener: (event: MessageEvent) => void): void;
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
