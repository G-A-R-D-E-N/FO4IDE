# FO4RecordEditor — Build History

How the editor got to its current shape, and which roads were abandoned. Condensed 2026-07-16 from
`PROGRESS.md` and `geminiprogress.md`, two overlapping snapshots of the same work.

**This is history, not status.** For what the code is now, read [ARCHITECTURE.md](ARCHITECTURE.md).
For the approved-but-unbuilt UI redesign, read [UI_REDESIGN_TASKS.md](UI_REDESIGN_TASKS.md) — that one
is a **live backlog**, not history, and was deliberately left out of this file. For open work, `bd ready`.

---

## The arc

**2026-06-09 — AI Plugin IDE (M0–M6).** Turned a tree-based ESP browser into an IDE. Landed a pure-data
core with zero WPF dependency and xUnit coverage: `KnowledgeGraph` (inverted index by FormKey /
EditorID / Type, inbound+outbound refs, `AnalyzeImpact`, `GetNeighborhood`), `ErrorScanner`,
`DiffEngine`, `LogService`, `UndoStack`, `CommandRegistry`. Plus an AI layer — `IAIProvider`,
Anthropic (streaming SSE) and Ollama providers, `AIContextBuilder` (grounds prompts in graph data),
`ChatService`. All of this is still live and has since grown well past the plan (`AnthropicAgent`,
`ClaudeCodeProvider`, `GeminiProvider`, `PluginMcpServer`, `StdioMcpServer`, `PluginToolExecutor`).

Two fixes from this era are worth remembering because they were silent: `IsWinningOverride` was
**inverted**, and `ErrorScanner` was quadratic at 50k records until an O(1) `_outbound` index was
added to the graph.

**M6 shipped a full WPF shell — and was then thrown away.** See "Abandoned" below.

**2026-08-07/08 -- the Papyrus compiler (issue #78).** The tool could read, understand and decompile
Papyrus on a machine with no Creation Kit, but not produce a script: `compile_papyrus` shelled out to
Bethesda's `PapyrusCompiler.exe`, the same shape of dependency the `Archive2.exe` one had been. Phase
1 (PR #89) built the front end -- lexer, parser, script index -- and the panel's Analyze mode. Phase 2
(PR #98) built the semantic half and the back end: resolver, type checker, `.pex` writer, code
generator, and the user-flag table that removes the last file only the CK ships. `compile_papyrus`
gained an `engine` parameter; `auto` prefers an installed CK and falls back to the built-in compiler.
The whole subsystem is documented in [PAPYRUS.md](PAPYRUS.md), including the thing worth not
re-deriving: byte-identity with the CK is **not** achievable for a code generator, because the CK's
output is not a function of the source alone.

**2026-06-11 — Readability (renderer registry).** Killed raw reflection dumps
(`Model.Data[0..31]`, `MarkerColor.R/G/B/A`) with a generic `ElementRenderer` registry +
`FriendlyNames` map consulted by `MutagenLoader.WalkObject`. The invariants this established are
load-bearing and are recorded in [ARCHITECTURE.md](ARCHITECTURE.md#rendering).

**2026-06-11 → 06-22 — The React pivot.** The decisive change. The WPF frontend was ripped out and
replaced with Vite + React + TypeScript in a WebView2 host, on the reasoning that a genuinely
VSCode-like UI needed the web's design flexibility. The C# Mutagen engine, MO2 handling, plugin
loading, and record edits were all preserved untouched — only the frontend changed. Hard-won details
from that pivot:

- **Virtual Host Name Folder Mapping** (`https://app.local/` → `web/dist`) is what makes ES modules
  and assets load; Chromium's local-file CORS policy blocks the naive approach.
- **HashRouter**, not BrowserRouter, inside WebView2.
- COM Dispatch interop throws `DISP_E_BADPARAMCOUNT` unless generic delegates are decoupled from
  public constructors.
- The `.csproj` has MSBuild targets that build the React bundle and inject it into the C# release
  output on every Release build.

**2026-06-22 — Power-user editing UX (M8).** `RenumberFormId` + the `renumber_formid` tool, plus
`CompactToEsl` / `CleanPlugin` surfaced through the bridge. Conflict-status filter, clickable
Referenced-By rows, multi-select batch copy-as-override, and the `fo4:ask-ai` CustomEvent bridge that
lets any part of the UI push an impact prompt into the chat panel. Destructive plugin-wide ops
(Compact, Clean) are gated behind confirm guards.

**2026-06-22 — strip_masters_clean + condition flags.** `strip_masters_clean`, `reload_plugin`, and
condition `flags` support. The root cause it fixed is in [KNOWLEDGE.md](KNOWLEDGE.md) — it is the
reason binary master-stripping is banned in favor of a Mutagen load/save round-trip.

**2026-07-14 — Made distributable.** Everything external was hardcoded to `E:\F4SE OG`, so on anyone
else's machine the NIF, Papyrus and texture tools silently did nothing. Added the `ToolPaths`
resolver chain (env var → settings.json → bundled copy → local FO4 install → dev paths last),
`package.ps1`, GPL-3.0 licensing (not a choice: it links Mutagen and bundles nifly, both GPL-3.0).

---

## Abandoned

**The WPF shell (M6).** Fully built and reviewed — activity bar, side panel, center tabs, AI panel,
bottom tabs, command palette — then superseded by the React pivot weeks later. The panels
(`ExplorerPanel`, `RecordEditorView`, `ChatPanel`, `ErrorsPanel`, `ComparePanel`) are still in
`Views/` and are constructed by nothing. **They are dead code.** Only `CommandPalette` and
`SettingsDialog` survive.

**`RecordMatrixView` / `ShellWindow`** (specified 2026-06-11) were never built at all. The editable
conflict matrix concept did ship — as React `RecordView.tsx`.

Anyone reading the old plans will think the app is WPF. It is not.

---

## Never started

From the 2026-06-09 roadmap, explicitly out of scope then and untouched since. Listed only so nobody
re-derives them as new ideas:

- AI write-back (model emits a structured edit list → one-click apply via `UndoStack`).
- A dedicated Patch Generator tab.
- Leveled-list / keyword / FormList merge automation on top of `GetConflicts()`.
- A persistent index (LiteDB) for 200k+ record load orders.
- Missing model/texture scanners (resolve mesh/texture paths against Data + BA2).
- Circular-reference and invalid-script detectors.
- Favorites / Recently Viewed; layout persistence.

Small deferred polish, still true: `HttpClient` is one-per-instance rather than `static readonly`;
Anthropic raw error bodies are not truncated before hitting the chat; `OllamaProvider` does not check
`IsSuccessStatusCode`, so a missing model reports poorly.
