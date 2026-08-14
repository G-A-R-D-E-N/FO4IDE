# Conflict Engine: xEdit Research + Plan to Beat It

Status drafted 2026-06-23. Source research: deep read of `TES5Edit-dev-4.1.6` (Delphi) + audit of our
current pipeline. This is the design reference for rebuilding the conflict viewport so it is clearer
than xEdit and semantically more correct. Pairs with [UI_REDESIGN_TASKS.md](UI_REDESIGN_TASKS.md)
(the live backlog) and [HISTORY.md](HISTORY.md) (shipped work).

The user's three complaints drive this:
1. Conflicts are hard to see (flat dark, text-color-only status).
2. Many fields are plain click-to-edit text instead of dropdowns / searchable pickers.
3. The conflict checker must beat xEdit.

---

## How xEdit actually does it (the parts that matter)

xEdit tracks TWO independent statuses (source: `Core/wbInterface.pas`, `xEdit/xeMainForm.pas`):

- **ConflictAll** = whole-record rollup across the load order. Enum (in order):
  `caUnknown, caOnlyOne, caNoConflict, caConflictBenign, caOverride, caConflict, caConflictCritical`.
  Colors: none, none, lime, green-yellow, yellow, red, fuchsia.
- **ConflictThis** = per-element status of one plugin's version. Enum:
  `ctUnknown, ctIgnored, ctNotDefined, ctIdenticalToMaster, ctOnlyOne, ctHiddenByModGroup, ctMaster,
  ctConflictBenign, ctOverride, ctIdenticalToMasterWinsConflict, ctConflictWins, ctConflictLoses`.
  Colors: master=purple, ITM=dark gray, override=green, win=orange, lose=red, notdefined=med gray.

**The secret weapon: DisplaySortKey, not string compare.** Equality for conflict detection uses
`GetDisplaySortKey(aExtended)` (`wbImplementation.pas`), a SEMANTIC comparison key, not the display
text. Consequences:
- FormIDs compare by resolved EditorID, so a FormID shift from a different master order is NOT a conflict.
- Sorted arrays (keywords) compare order-independently when the schema says so.
- Floats compare by value, not by formatting ("1.0" vs "1.000000" are equal).

**Conflict priority (severity) comes from the schema.** `TwbConflictPriority`:
`cpIgnore, cpBenignIfAdded, cpBenign, cpOverride, cpTranslate, cpNormal, cpNormalIgnoreEmpty,
cpCritical, cpFormID`. A differing field is benign/critical based on its priority. Special rule:
FormID fields are cpCritical EXCEPT in GMST/DFOB where they are cpBenign. `cpBenignIfAdded` is benign
only when the field was added (absent in master), not when modified.

**Per-element algorithm (`ConflictLevelForNodeDatas`, xeMainForm.pas ~2332):**
1. Drop ignored/dont-show elements; if <=1 real version -> caOnlyOne.
2. Collect unique DisplaySortKey values (dedup, case-insensitive).
3. Per version: master if first; ITM if equals first; win if equals last (winner); lose otherwise.
4. Roll up: 0/1 unique -> caNoConflict; 2 unique -> caOverride (or caConflict); 3+ -> caConflict.
   Then clamp by priority: cpBenign -> caConflictBenign, cpOverride -> caOverride, cpCritical + 2
   differing non-empty -> caConflictCritical.
5. Optional-all-zero fields collapse back to caNoConflict.

**Hide-no-conflict** hides rows below ctOverride (or below caConflictBenign when comparing siblings),
so the user sees only real differences.

---

## Where we fall short today (audit)

Pipeline: `ConflictScanner.Scan()` -> `ConflictEntry[]` ; click a record ->
`MutagenLoader.BuildConflictMatrix()` walks each version into one node tree ->
`FlattenConflictRows()` -> `ConflictMatrix{Rows[]}` -> `RecordView.tsx` renders with a JS
`cellStatus()` heuristic colored by TEXT only.

Gaps:
- **No severity.** Rows carry only `Differs: bool`. No benign/override/conflict/critical. Record level
  is binary "conflicted or not". (`ConflictScanner.cs` whole-record `Equals()`; `FlattenConflictRows`
  `MutagenLoader.cs`.)
- **Naive string equality.** `present.Distinct()` on display strings: float formatting, reordered
  FormLink lists, and struct order produce FALSE conflicts; semantic identity is missed.
  (`MutagenLoader.cs` FlattenConflictRows differs; `ConflictScanner.RecordsEqual`.)
- **Per-cell status is a frontend guess.** `cellStatus()` in `RecordView.tsx` re-derives master/win/
  lose from strings on the client; it has no benign/ITM/critical and no semantic equality.
- **Field-type classification incomplete.** Walker (`MutagenLoader.cs` WalkObject) handles Bool, Enum,
  FormLink(Ref), String, byte-blob, Color, Condition/Component. MISSING: flag enums (`[Flags]`) render
  as a single dropdown instead of a multi-select; some struct/value fields fall through to plain Text.
  This is the "click to edit instead of dropdown/searchable" complaint. (FormLink Ref picker already
  works well via `RecordPicker.tsx`.)
- **Visual: text-color-only on flat dark.** `RecordView.css` `.rv-c-*` set text color only; conflicts
  do not pop. xEdit uses bold backgrounds + the status palette.

---

## The plan: a conflict engine that beats xEdit

Beat it on BOTH correctness (semantic status + severity) and clarity (modern visuals, proper editors,
AI explain/resolve). Build in this order; each step is shippable.

### Step C1 - Backend conflict-status engine (the core) [TDD]
Compute status SERVER-SIDE with xEdit semantics and emit it in the matrix, replacing the JS heuristic.
- Add to `ConflictFieldRow`: `Statuses: string[]` (one per plugin column: `notdefined | master |
  identical | override | win | lose | only`) and `Severity: string` (`none | benign | override |
  conflict | critical`).
- Add to `ConflictMatrix`: `Level: string` (record ConflictAll: `onlyone | noconflict | benign |
  override | conflict | critical`).
- Compute per-row from a SEMANTIC equality key (not the display string):
  - Floats: parse + epsilon compare.
  - FormLink single: compare by resolved target (already formatted) - treat equal targets as equal.
  - FormLink lists / sorted sets: compare as sets (order-independent).
  - Else: case-sensitive string of the normalized value.
- Per-cell ConflictThis mapping mirrors xEdit (master / identical-to-master / win / lose / override /
  notdefined). Record `Level` rolls up the worst row severity.
- Severity heuristic (approximate xEdit priority without the schema): `none` if not differing;
  `override` if exactly one effective value beyond master / added field; `conflict` if 2+ effective
  values disagree; `benign` for known-cosmetic categories (set-equal sorted lists, all-zero optional);
  `critical` for broken/null FormLinks where a target is required. Leave a clear extension point to
  refine per record type.
- Files: `Models/ConflictMatrix.cs`, `Services/MutagenLoader.cs` (FlattenConflictRows + a new
  `SemanticEquality`/`ClassifyRow` helper), `Services/ConflictScanner.cs` (use the same equality so the
  conflict LIST and the matrix agree). Tests in `FO4RecordEditor.Tests` (float-format-not-a-conflict,
  set-equal-list-not-a-conflict, master/win/lose statuses, record Level rollup).

### Step C2 - Field-type classification completeness (the "dropdowns" fix) [backend + FE]
- Detect `[Flags]` enums -> new `EditKind = "Flags"` + `EnumOptions`; frontend renders a checkbox/
  multi-select that ORs the bits. (`MutagenLoader.cs` enum branch, `RecordNode.FieldEditKind`,
  `backend.ts`, `RecordView`/card editor.)
- Audit the walker so every scalar lands on a real editor: enums -> dropdown, FormLink -> picker,
  bool -> toggle, numeric -> number input, string -> text. Nothing editable should be a bare text box
  when a constrained editor exists. Confirm nested struct/value leaves get classified, not dumped as
  Text.
- Keep the existing searchable `RecordPicker` for all Ref fields (single and list entries).

### Step C3 - Visuals: make conflicts pop (Variant B cards) [FE]
- Drive cell coloring from the backend `Statuses[]`/`Severity` (delete the JS `cellStatus` guess).
- Apply real status colors as BACKGROUNDS (not just text) using the token palette
  (`--status-base/changed/warning/ok/info`, `--conflict-*`): identical dimmed, master purple-ish,
  winner highlighted, loser red, benign muted, critical strong.
- Status icons per cell (check / warning / overridden) and a per-record Level badge.
- Build the Variant B conflict CARDS (base-vs-override side by side + `OverriddenBy` chips per group,
  components already built) grouped by subrecord, with a severity filter and "hide identical".

### Step C4 - Beat-xEdit differentiators [FE + AI]
- One-click resolve per field/group: "use this plugin's value", "take winner", "copy to patch".
- AI: "explain this conflict" and "is this benign or will it break?" using the existing chat bridge,
  grounded in the record + severities.
- Severity-sorted conflict list (critical first), and a load-order "Plugins Affecting This Record"
  table (Phase 4.5 of the redesign) showing changes/conflicts/overrides per plugin.

---

## Why this beats xEdit
- Same semantic correctness (DisplaySortKey-style equality, benign vs critical) WITHOUT xEdit's noisy
  default grid: modern cards, real background colors, severity filtering, proper editors everywhere.
- AI explain/resolve on top of the real severity data - something xEdit structurally cannot do.
- Server-computed status (one source of truth) instead of a client heuristic, so the list, the cards,
  and any export always agree.

Build order: C1 (engine) -> C2 (editors) -> C3 (visual cards) -> C4 (differentiators). C1 is the
foundation everything else reads from.
