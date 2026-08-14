# Modern xEdit UI Redesign - Task Plan

A re-skin of the React record editor (`web/src/`) to
match the "Modern xEdit - Record Viewer" concept mockups.

> **Implemented.** All six phases and all seven backend contracts are implemented and on
> `main`. This document is kept as the record of what was specified and decided, not as a backlog.
>
> What shipped, by phase:
>
> | Phase | State |
> |---|---|
> | 1 App shell | Done. TopBar/StatusBar existed; multi-record tabs and the sidebar splitter finished it. |
> | 2 Left navigator | Done. `FileBrowser`, `RecordNavigator`, `NavigatorFilters` on a new Records tab, now the default. |
> | 3 Workspace header | Done. `WorkspaceHeader` with compare pills, breadcrumb and Actions menu. |
> | 4 Conflicts view | Done, **Variant B (cards)** as primary per the locked decision, with the matrix as a persisted toggle. |
> | 5 Detail rail | Done. Details, SVG donut, quick actions, contained-in, plugins-affecting table. |
> | 6 Theming and polish | Tokens already existed and are used throughout; empty/loading states are in each new panel. |
>
> **Two things deliberately not built**, both flagged rather than quietly dropped:
> - **Raw View** (task 3.3): a hex/subrecord dump needs a backend contract that is not among the
>   seven this plan specified. Inventing one was outside the approved scope.
> - **Marketing Home banner** (task 6.2): the plan itself said build only if a landing screen is
>   wanted, otherwise drop. The Home tab already has a usable card, so it was dropped.
>
> One decision made during execution: `Kind` (Value/Flag/FormID) and `Group` are computed
> server-side on `ConflictFieldRow`, not inferred in the frontend. The plan allowed either. Doing it
> in the backend means the sub-tab counts and the donut come from the same rows the grid renders and
> cannot drift from what is on screen.
>
> Current architecture is in [ARCHITECTURE.md](ARCHITECTURE.md).

The two concept images are two variants of the SAME screen. They agree on the overall shell and
differ only in how the Conflicts view is laid out:

- **Variant A (matrix):** one row per field with columns `Field | Fallout4.esm | MyMod.esp | Other Plugins (N) | Final Value`, plus a status icon (check / warning) per row.
- **Variant B (cards):** each conflict group is a card showing base-vs-override side by side, with an `Overridden By (N)` column listing the plugin chips that also touch it.

Recommendation: build the shared shell first (Phases 1-3), then implement the Conflicts layout.

**DECISIONS:**
- **Scope:** full end-to-end redesign approved (all 6 phases + the backend contracts).
- **Default Conflicts layout: Variant B (cards)** - base-vs-override cards with an `Overridden By (N)`
  plugin-chip column. Phase 4 builds Variant B as the primary view; Variant A (matrix) is an
  optional secondary toggle if wanted later.
- **Execution model:** one phase at a time, each as its own plan + subagent execution + two-stage
  review, staying in the loop between phases. Progress is tracked in `bd` (`f4se_og-4wp`).

---

## What already exists (reuse, do not rebuild)

- `MainShell.tsx` - activity bar, plugin tree, search, Conflicts panel, status bar, MCP feed.
- `RecordView.tsx` - the conflict matrix (columns = plugins, rows = fields), status filter,
  context actions, Referenced-By / Problems drawer, record picker.
- `backend.ts` bridge with `GetConflictMatrix`, `GetConflicts`, `GetReferencedBy`, `GetProblems`,
  `SearchRecords`, plus the edit/save methods.
- `ConflictMatrix` data shape: `{ FormKey, EditorID, Type, Winner, Plugins[], Rows[] }` where each
  row is `{ Field, DisplayLabel, Level, Values[], Differs, IsSummary, HasChildren, EditKind, EnumOptions, RefType, RefTypes }`.

The redesign is mostly **presentation** on top of this data, plus a set of **new backend data
contracts** (called out per task as `[BACKEND]`). Tasks marked `[FE]` are frontend-only.

---

## Phase 1 - App shell and chrome

Goal: the outer frame (tab bar, global search, top-right tools, three-pane layout) matching the mockup.

### Task 1.1 - Top tab bar `[FE]`
- Add a tab strip above the workspace: `Home` tab + one closeable `Record Viewer` tab per open record.
- Files: new `web/src/Shell/TabBar.tsx` + css; mount in `App.tsx` / `MainShell.tsx`.
- Each open record becomes a tab (today only one record is shown at a time). Hold open records in
  shell state as an array; clicking a tab swaps the active record. Closing a tab removes it.

### Task 1.2 - Global command/search bar (Ctrl+K) `[FE]`
- Center-top search input "Search records, fields, or formIDs..." with a `Ctrl K` chip.
- Wire to the existing `backend.SearchRecords(query, '')`; show a results dropdown; Enter opens the
  hit via the existing `openByFormKey`.
- Files: new `web/src/Shell/CommandBar.tsx`; register a `Ctrl+K` keydown handler.

### Task 1.3 - Top-right tool cluster `[FE]`
- Icon buttons: settings (opens existing `SettingsModal`), layout toggle (show/hide right rail and
  chat), help, chat (toggles `ChatPanel`), user avatar (static "JD" placeholder).
- Files: `web/src/Shell/TopBar.tsx`.

### Task 1.4 - Three-pane layout grid `[FE]`
- Restructure `MainShell` into: left navigator (Phase 2), center workspace (Phase 3-4),
  right detail rail (Phase 5). Use CSS grid with resizable splitters; persist sizes to settings.
- Keep `ChatPanel` as a toggleable overlay/4th column rather than always-on.

### Task 1.5 - Status bar parity `[FE]`
- Bottom bar: `Ready | Records: N | Visible: N | Selected: N | Load Order: ...`.
- `Records`/`Visible` come from the loaded environment; `Selected` from the active record;
  `Load Order` from the plugin list. `[BACKEND]` add a small `GetLoadOrderSummary()` returning
  total record count + ordered plugin names (or compute from existing `GetPlugins`).

---

## Phase 2 - Left navigator (File Browser + Record Navigator + Filters)

The mockup splits the left pane into three stacked sections.

### Task 2.1 - File Browser section `[FE]`
- "Active Files (N)" list with a colored chip per plugin (assign each plugin a stable color),
  a per-file filter input, and an `Add File` button (reuses Open MO2 / Load Env / file picker).
- Files: `web/src/Navigator/FileBrowser.tsx`.
- `[BACKEND]` add `GetActivePlugins()` returning `{ name, color?, kind (master/plugin/light), loadOrder }`
  OR derive color client-side from the plugin name hash.

### Task 2.2 - Record Navigator tree by record type `[FE + BACKEND]`
- Tree grouped by record type (Armor Addon, Armor, Book, Constructible Object, ...), expandable to
  the records of that type, with a name/formID filter.
- Today's tree is plugin -> groups -> records. The mockup is type-first across the load order.
- `[BACKEND]` add `GetRecordTypeIndex()` returning the list of record types present with counts,
  and reuse `list_records`/`SearchRecords` to lazily load a type's records.
- Files: `web/src/Navigator/RecordNavigator.tsx`.

### Task 2.3 - Filters section `[FE]`
- `Record Type` dropdown (with count, e.g. "Placeable Object (6,123)"), `FormID` text input,
  `Reset Filters` button. Drives the navigator + center list.
- Files: `web/src/Navigator/NavigatorFilters.tsx`.

---

## Phase 3 - Center workspace header

### Task 3.1 - Plugin compare selector `[FE]`
- Pills at the top of the center pane: `[dot] Fallout4.esm  vs  [dot] MyMod.esp  + Add Plugin`,
  plus `Compare`, `Filters`, and a green `Actions` dropdown on the right.
- The `vs` selector chooses which two columns anchor the compare; `Actions` exposes the existing
  context-menu operations (Copy as Override, Change FormID, Compact to ESL, Clean UDR, Delete).
- Files: `web/src/Workspace/WorkspaceHeader.tsx`.

### Task 3.2 - Worldspace breadcrumb `[BACKEND]`
- Breadcrumb: `Worldspace > 0000003C <Commonwealth> > Block -1,0 > Sub-Block -1,1 > 0001F1A2 <Red Rocket Truck Stop>`.
- `[BACKEND]` add `GetContainmentPath(formKey)` returning the worldspace/cell/block/sub-block chain
  for placeable references (CELL/REFR worldspace hierarchy). Non-spatial records get a simpler path.

### Task 3.3 - Record tabs `[FE + BACKEND]`
- Tab row: `Record View | Field View | Raw View | References (N) | Conflicts (N) | History (N) | Dependencies (N)`.
- `Record View` = the editable field tree (exists). `Field View` = flat spreadsheet of fields.
  `Raw View` = hex/raw subrecord dump. `References (N)` = existing `GetReferencedBy` count.
  `Conflicts (N)` = existing conflict count. `History (N)` and `Dependencies (N)` are new.
- `[BACKEND]` add: `GetReferenceCount(formKey)` (or reuse refBy length), `GetDependencies(formKey)`
  (masters/records this record needs), and `GetHistory(formKey)` (per-plugin override timeline; can
  start as "one entry per plugin that overrides it" from the conflict data, no real VCS history).

---

## Phase 4 - Conflicts view (the centerpiece, Variant A)

### Task 4.1 - Conflict sub-tabs and grouping controls `[FE]`
- Pills: `All Conflicts N | Values N | Flags N | FormIDs N`, a `Group by: Field` dropdown, and
  `Collapse All`. The Value/Flag/FormID split is a classification of each differing row.
- `[BACKEND or FE]` classify each `ConflictFieldRow` as Value / Flag / FormID. Cheapest: infer on
  the frontend from `EditKind` + field name (Bool/flags -> Flag, Ref/FormID-looking -> FormID, else
  Value). Cleaner: add a `Kind` field to `ConflictFieldRow` server-side.

### Task 4.2 - Conflict summary stat row `[FE]`
- Header strip: `N Conflicts found in this record` + three counters `Modified Values`,
  `Modified Flags`, `Changed FormIDs`. Computed from the classification in 4.1.

### Task 4.3 - Grouped collapsible conflict sections `[FE + BACKEND]`
- Group differing rows under their parent subrecord (e.g. `DATA - Position/Rotation (4 conflicts)`,
  `DATA - Model (2 conflicts)`), each collapsible with a per-group conflict count.
- The `ConflictMatrix.Rows` already carry `Level` + parent structure; grouping can be derived from
  the row hierarchy. `[BACKEND]` optionally add a `Group` label per row (subrecord signature +
  friendly name) so the headers match xEdit subrecord names.

### Task 4.4 - Matrix rows with Final Value + status icons `[FE]`
- Per row: `Field | <plugin col> ... | Other Plugins (N) | Final Value | status`.
- Color coding: base value green, changed/losing values red, winner highlighted; a check icon when
  the final value is unambiguous, a warning icon when multiple plugins disagree.
- Collapse columns beyond the two anchored plugins into an `Other Plugins (N)` expander.
- Reuse the existing `cellStatus()` logic; add the `Final Value` column from `Winner`.

### Task 4.5 - "Plugins Affecting This Record" table `[BACKEND]`
- Bottom table: `Plugin | Load Order | Type | Changes | Conflicts | Overrides | Last Modified`.
- `[BACKEND]` add `GetRecordPluginMatrix(formKey)` returning per-plugin: load-order index,
  plugin kind, number of changed fields, number of conflicting fields, override flag, file mtime.

### Task 4.6 - Variant B card layout (optional) `[FE]`
- A toggle that renders each conflict group as a base-vs-override card with an `Overridden By (N)`
  chip column, per the second mockup. Pure presentation over the same data.

---

## Phase 5 - Right detail rail

### Task 5.1 - Record Details panel `[BACKEND]`
- `RECORD DETAILS`: record class + signature badge (e.g. "Placeable Object" / `STAT`), `FormID`,
  `Editor ID`, `Base Form`, `Form Type`, `File`.
- `[BACKEND]` add `GetRecordDetails(formKey)` returning these fields (Base Form = the base object a
  REFR points at; Form Type = the record signature + friendly class name).

### Task 5.2 - Conflict Summary donut `[FE]`
- Donut chart "N Total Conflicts" with a category legend (Values / Flags / FormIDs / Other) using
  the Phase 4.1 classification. Use a lightweight SVG donut (no chart lib needed).

### Task 5.3 - Quick Actions panel `[FE]`
- `Copy FormID`, `Jump to Base Form` (Enter), `Jump to in Explorer` (Ctrl+E), `Add to Filter`,
  `Add to Favorites`. Wire to clipboard, `openByFormKey`, navigator scroll, the filter input, and a
  new favorites store (`[FE]` localStorage / settings).

### Task 5.4 - Contained In panel `[BACKEND]`
- Mirrors the breadcrumb chain (Worldspace > Cell > Block > Sub-Block). Reuses
  `GetContainmentPath` from Task 3.2.

---

## Phase 6 - Theming and polish `[FE]`

### Task 6.1 - Design tokens
- Extract the mockup palette into CSS variables (dark base, green accent for primary/`xEdit`,
  red for losing/changed values, amber for warnings, per-plugin chip colors). Apply across panels.

### Task 6.2 - Marketing Home tab (optional)
- The top feature banner ("ESP/ESM Optimized", "Powerful Navigation", ...) belongs on the `Home`
  tab, not the editor. Build it only if a landing screen is wanted; otherwise drop it.

### Task 6.3 - Empty/loading/error states
- Skeleton loaders for the matrix and panels; consistent empty states; toast/status surfacing for
  backend messages (replaces the current inline `setStatus` strings where appropriate).

---

## New backend bridge contracts to add (summary)

These are the `[BACKEND]` items, grouped for a single backend pass (extend `BackendInterop` +
`backend.ts`, back them with `MutagenLoader`/`ConflictScanner`):

1. `GetActivePlugins()` -> `[{ name, kind, loadOrder, color? }]`
2. `GetRecordTypeIndex()` -> `[{ type, friendlyName, count }]`
3. `GetContainmentPath(formKey)` -> ordered breadcrumb nodes (worldspace/cell/block/sub-block)
4. `GetRecordDetails(formKey)` -> `{ formId, editorId, baseForm, formType, className, file }`
5. `GetRecordPluginMatrix(formKey)` -> per-plugin `{ loadOrder, kind, changes, conflicts, override, lastModified }`
6. `GetDependencies(formKey)` and `GetHistory(formKey)` (history can start as the override list)
7. Optional: add `Kind` (Value/Flag/FormID) + `Group` (subrecord) to `ConflictFieldRow` so 4.1/4.3
   do not have to infer on the frontend.

---

## Suggested execution order

1. Phase 1 (shell) + Phase 6.1 (tokens) - establishes the frame and look.
2. Phase 2 (navigator) + Phase 3 (workspace header).
3. Phase 4 (Conflicts view, Variant A) - the highest-value visual change; mostly reuses
   `ConflictMatrix`. Do 4.5 and any `[BACKEND]` items in one backend pass.
4. Phase 5 (right rail).
5. Phase 4.6 (Variant B), Phase 6.2-6.3 (polish), as optional follow-ups.

Each phase is independently shippable: the shell still works with the existing `RecordView` until
the new Conflicts view replaces it. Build behind the existing components, swap when a phase is done.

Recommendation: when ready to execute, turn the chosen phase into a full bite-sized implementation
plan (one `docs/superpowers/plans/YYYY-MM-DD-<phase>.md`) before touching code.
