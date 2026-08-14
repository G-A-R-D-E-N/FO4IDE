# FO4IDE: Architecture & Contributor Facts

How the editor is put together: the components, the data flow, and the invariants the code
depends on. Contributor-facing; user documentation lives in the [README](README.md) and
[MCP_SETUP](../MCP_SETUP.md).

---

## Repository facts (read before touching the code)

- **The app repo is self-contained.** `FO4RecordEditor/` is its own git root; commits there do
  not touch any surrounding workspace repo.
- **The app is a WebView2 shell, not WPF.** `MainWindow.xaml` is a WPF-UI `FluentWindow` hosting a
  single `WebView2`. The window title is `FO4IDE`. The real UI is React under `web/src/`
  (`MainShell.tsx`, `RecordView.tsx`, `ChatPanel.tsx`).
- **The IPC contract.** React calls
  `window.chrome.webview.hostObjects.backend.<Method>()` → `Services/BackendInterop.cs`. Every
  `BackendInterop` method wraps its body in `DebugLog.Guard(name, () => ...)` (sync) or
  `DebugLog.GuardAsync` (async) and **returns a `string`**.
- **Tests run serially, deliberately.** `[assembly: CollectionBehavior(DisableTestParallelization=true)]`
 , `WriteService`/`MutagenLoader` use process-global registries.
- **The test project must share the `net9.0-windows` TFM** (+ `UseWPF`) or the project reference fails
  to resolve.
- **React is not unit-tested.** UI changes are manual-verify only.
- **Do not run `publish.bat` / `package.ps1` while the editor or an MCP server is open**: the exe and
  its DLLs are locked. See the MSB3021 note in [KNOWLEDGE.md](https://github.com/G-A-R-D-E-N/FO4IDE/blob/main/docs/internal/KNOWLEDGE.md).

### Dead code you will trip over

~15 WPF `Views/*.xaml` panels (`ExplorerPanel`, `ChatPanel`, `ErrorsPanel`, `ComparePanel`,
`RecordEditorView`) are **constructed by nothing**, orphaned when the UI moved to React. Only
`CommandPalette` and `SettingsDialog` are still referenced from `MainWindow.xaml.cs`. Likewise
`RecordMatrixView` and `ShellWindow` were specified but never built; the editable
matrix shipped as React `RecordView.tsx` instead. Do not treat any of these as live architecture.

---

## Design decisions worth keeping

**The knowledge graph is the differentiator over xEdit.**
> Index every FormKey, EditorID, and reference once, then answer "what breaks if I delete this?" in
> O(1) instead of rescanning.

**Generic over every record type, always.** xEdit reads all records via its element-def system; we
read them all via Mutagen's typed records plus a reflection walker. Readability fixes must therefore
be *generic with per-type enhancements*, never hardcoded to a handful of record types.

**AI-augmented parity.** Manual buttons do the xEdit operation; the agent can do the same thing via
MCP tools *and explain the impact*. That pairing is the product, not the buttons alone.

**Incremental shell-first was chosen over a big-bang rewrite** (unverifiable in slices) and over a
theme-only veneer (delivers neither inline editing nor the chrome). The app stays runnable and
visually testable after every phase.

**Rejected: drag-and-drop copy-as-override.** Redundant with the context-menu "Copy as Override into"
and the batch flow, and HTML5 DnD in WebView2 is not worth the complexity.

**Standing non-goals.** No change to environment/MO2 loading (`Open ESP` / `Load Env` / `Open MO2`
behave as they do today). Worldspace/cell navigation redesign is out of scope.

---

## Invariants

### Adding an MCP tool touches FOUR sites

Miss one and the tool half-exists, the classic symptom is a tool that lists but never dispatches.
In `Services/Ai/PluginToolExecutor.cs`:

1. a `ToolSpec[] _specs` entry,
2. a `switch (toolName)` dispatch case in `ExecuteInner`,
3. membership in the `IsWrite` set (if it mutates),
4. a case in the summary `switch`.

Then `BackendInterop.cs` + `web/src/backend.ts` if the GUI should expose it too.

### Rendering

- **Keep raw keys on the nodes; only the *displayed* label is friendly.** Edits and paths resolve
  against the raw keys, `FriendlyNames.LabelPath` relabels only the final path segment when it is a
  plain property name, and leaves dotted paths and indices structurally intact.
- **Byte-blob collapse is checked on the collection *before* `WalkObject` expands it**, not per
  element. This ordering is load-bearing.
- **A renderer returning `true` means "show this text and do not recurse."** Unknown types fall back
  to the generic tree, so nothing is ever hidden by accident; a renderer that *throws* also falls back
  rather than blanking the row.
- **The FormLink/Condition formatters are injected once at startup** (`App.xaml.cs` →
  `ElementRenderer.Init(...)`) because they need the link cache, and inverting that would be a
  circular dependency.

### Records and conflicts

- FormKey wire format is `001234:Fallout4.esm`: regex `^[0-9A-Fa-f]{6}:.+\.(esp|esm|esl)$`.
- When two plugins define the same FormKey, **the last indexed wins** (load order). Losing overrides
  are tracked so the Compare/Errors tabs can surface them.
- Conflict status is not binary. The model mirrors xEdit's: no-conflict, benign override, critical
  conflict, identical-to-master.
- A `KnownMasters` allowlist (Fallout4.esm + the 7 DLC esms) exists to stop false-positive broken refs.
- **Renumbering does not rewrite references in *other* plugins**: that needs a patch. The tool
  reports how many exist so the caller or the AI can act on it.
- The Mutagen re-key pattern is `mod.RemapLinks(Dictionary<FormKey,FormKey>)` +
  `DuplicateInAsNewUntypedRecord(IMajorRecord, FormKey)` + `Remove(FormKey)`. `CompactToEsl` has the
  exact reflection pattern over `IGroup` properties.
- Inline edits that fail validation surface an inline message and leave the patch unchanged.

### Anthropic provider

System messages are hoisted to the top-level `system` field, the API accepts only `user`/`assistant`
in `messages`.

---

## Palette

**Source of truth is `web/src/index.css`**, CSS custom properties (`--conflict-green`,
`--conflict-yellow`, `--conflict-orange`, `--conflict-purple`, `--conflict-red`, `--conflict-empty`,
`--text-identical`), with a `[data-theme="light"]` block overriding them. Do not copy the hex values
into docs; every doc that did has already drifted.

What is durable is the *intent*: VSCode Dark+ as the base, and **xEdit's exact semantic color
meanings preserved**, identical/override/winner/master/loser, because that mapping is what modders
already have in their heads. Retune the values freely; do not remap the meanings.

The WPF `Views/ConflictStatusToBrush.cs` and `ConflictCellBrush.cs` still exist but are part of the
orphaned WPF layer above, they do not color the shipping UI.

---

## Inner loop

```powershell
cd FO4RecordEditor
dotnet test FO4RecordEditor.Tests\FO4RecordEditor.Tests.csproj --filter FullyQualifiedName~<Name>
dotnet test                                   # full suite
cd web && npm run build                       # the csproj copies web\dist on Release build
dotnet build FO4RecordEditor\FO4RecordEditor.csproj -c Release
```

Then the user runs **`/mcp reconnect`**, a rebuilt exe does not reach a running server. If the build
fails MSB3021 "being used by another process", that is a **file lock, not a code error**: stop every
`FO4RecordEditor.exe --mcp` instance, or prove the code with `dotnet build -t:Compile`.
