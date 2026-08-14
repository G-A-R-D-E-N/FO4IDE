# FO4RecordEditor xEdit Parity Audit -- 2026-08

Draft-only capability/field-gap audit of FO4RecordEditor against xEdit/FO4Edit for eight priority record
types (NPC_, WEAP, ARMO, RACE, CELL, REFR, QUST, PROJ), plus a whole-plugin record-signature diff (Phase A)
and a FormID/ESL-compaction behavioral check (Phase B). Method for every section: (1) read the record's
authoritative binary layout in `TES5Edit-dev-4.1.6/Core/wbDefinitionsFO4.pas` (and, for Phase B,
`wbImplementation.pas`/`wbInterface.pas`), (2) cross-check the matching Mutagen C# model under
`Mutagen/Mutagen.Bethesda.Fallout4/Records/Major Records/`, (3) cross-check whether FO4RecordEditor's
write/MCP-exposure layer (`FO4RecordEditor.Core/Services/WriteService*.cs`,
`FO4RecordEditor/Services/Ai/PluginToolExecutor.cs`) actually lets an agent read and write every field
Mutagen models. Only genuine, source-verified gaps are drafted as issues below; low-confidence or
already-fixed items are called out explicitly in each section's "Notes / uncertain items" instead.

All 17 issues below were filed to GitHub on 2026-08-08 after direct user confirmation (issues #122-138 on
`PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool`). This document remains the source-of-truth writeup each
issue was drafted from.

## Summary table

| Record type / Phase | Subrecords / fields checked | Gaps found | Confidence |
|---|---|---|---|
| NPC_ (Non-Player Character) | 52 | 0 | High |
| WEAP (Weapon) | 38 | 1 | High |
| ARMO (Armor / Armor Addon) | 30 | 2 | High |
| RACE | ~90 | 3 | High (tool-surface findings); Medium (BodyData cardinality real-world impact) |
| CELL | 30 | 1 | High |
| REFR (Placed Object Reference) | 60+ | 2 | High |
| QUST (Quest) | 70+ | 3 | High |
| PROJ (Projectile) | 34 | 1 | High |
| Phase A -- record signature diff | 147 xEdit signatures vs. 144 Mutagen-modeled | 2 | High |
| Phase B -- FormID / ESL handling | 2 hand-rolled FormID-encoding call sites audited | 2 | High |
| **Total** | | **17** | |

Highest-value finding: **Phase A found two genuinely unmodeled top-level FO4 record types in Mutagen --
`EYES` (Eyes) and `LVSP` (Leveled Spell)** -- see that section for full detail. Every other record type in
xEdit's active FO4 definition set (147 signatures, after excluding 10 commented-out dead Skyrim-era stubs
and 1 cross-game internal-only bookkeeping signature) has a real Mutagen C# model.

---

## Phase A: Record Signature Diff (xEdit vs Mutagen)

**Method:**

1. **xEdit side** -- extracted every top-level record definition from
   `TES5Edit-dev-4.1.6/Core/wbDefinitionsFO4.pas` by grepping for the three call patterns xEdit
   actually uses to register a top-level (GRUP-child) record type:
   - `wbRecord(SIG, 'Name', ...)` -- the general case (147 raw matches).
   - `wbRefRecord(SIG, 'Name', ...)` -- used directly for `REFR` and `ACHR`.
   - A local helper `procedure ReferenceRecord(aSignature; aName)` (defined inline at line 3593)
     that itself calls `wbRefRecord` -- used for the other 8 placed-reference types
     (`PARW, PBAR, PBEA, PCON, PFLA, PGRE, PHZD, PMIS`).

   Raw union = 157 signatures. Every raw match was manually inspected against surrounding source,
   because a plain grep can't tell a live definition from a Pascal `{ ... }` comment block. This
   caught **10 dead signatures** (`LSPR, MICN, RGDL, SCPT, SCRL, SHOU, SKIL, TLOD, TOFT, WOOP`) that
   are textually present but wrapped in `{wbRecord(...); }` comments -- i.e. xEdit's own source
   disables them (Skyrim/legacy leftovers, each just an EDID-only stub). Excluding those gives
   **147 genuinely active xEdit-defined top-level FO4 record signatures.**

2. **Mutagen side** -- for every top-level record type Mutagen actually models for FO4, the class's
   `TriggeringRecordType` field resolves through `Fallout4.Internals.RecordTypes.XXXX`, and
   `RecordTypes_Generated.cs` proves `XXXX` is literally the ASCII signature (e.g.
   `RecordTypes.ACTI = new(0x49544341)` = "ACTI"). Built the authoritative "this is a real top-level
   record in Mutagen's Fallout4Mod" list from `Fallout4Mod_Generated.cs`'s own group wiring
   (`Fallout4Group<T>` / `Fallout4ListGroup<T>` properties -- 126 classes), then resolved each class
   name to its signature via its own `<Class>_Generated.cs`. Four more top-level types that aren't
   exposed as their own `Fallout4Mod` group property (because they nest under a parent record) were
   checked and added individually: `CELL` (`Cell.cs`, reached via `Fallout4ListGroup<CellBlock>`),
   `DIAL`/`INFO` (`DialogTopic.cs`/`DialogResponses.cs`, nested under `Quest`), `DLBR`
   (`DialogBranch.cs`), plus `TES4` (`Fallout4ModHeader.cs`, the file header itself) and the 10
   placed/reference types (`ACHR, REFR, PARW, PBAR, PBEA, PCON, PFLA, PGRE, PHZD, PMIS`, all present
   as dedicated `Placed*.cs` classes). `DUAL` (`DualCastData.cs`) was also confirmed to have a full
   generated class with `GrupRecordType` wired -- it just isn't exposed as its own
   `Fallout4Group<DualCastData>` property on `Fallout4Mod`, which is a *reachability* gap, not a
   *zero-model* gap, so it's counted as modeled for Phase A purposes.
   Total = **144 Mutagen-modeled top-level FO4 signatures.**

3. **Diff** -- `comm -23` of the two sorted signature sets, then individually re-verified every
   leftover entry by grepping the entire vendored `Mutagen/` tree (not just the FO4 project) for
   the raw signature string, to rule out it being modeled under a differently-named class.

**xEdit signature count:** 147 (genuinely active; 157 raw text matches minus 10 commented-out dead stubs)
**Mutagen-modeled signature count:** 144

**Genuinely unmodeled in Mutagen:** `EYES`, `LVSP`

(A third diff entry, `PLYR`, is a false positive -- see "False positives ruled out" below.)

### [MISSING RECORD TYPE]: Mutagen has no model for EYES (Eyes)

**Issue Summary:**
FO4's `Eyes` record type (form type 51, `wbFormTypeEnum` entry `51, 'Eyes'`) is a real, fully-defined,
active top-level record in xEdit's FO4 definitions -- not a stub, not commented out. Mutagen has
**zero** trace of it anywhere in the vendored tree: no `Eyes.cs`/`Eyes_Generated.cs` file, no
`RecordTypes.EYES` reference inside the `Mutagen.Bethesda.Fallout4` project (the identifier only
exists as a raw byte-pattern constant shared across all games in
`Mutagen.Bethesda.Core/Plugins/Records/RecordTypes_Generated.cs`, which is not a functional model --
no Bethesda-game module in this vendored checkout has an `Eyes` class at all). This means
`fo4recordeditor` cannot read, display, create, or edit an `EYES` record under any circumstance --
`create_record`, `list_records`, `search_all type=EYES`, etc. all silently see nothing, because there
is no C# type to deserialize the record into.

**Source Reference (xEdit):**
* File: `wbDefinitionsFO4.pas`
* Line / Definition: lines 7107-7124
  ```pascal
  wbRecord(EYES, 'Eyes',
    wbFlags(wbFlagsList([
      {0x00000004}  2, 'Non-Playable'
    ])), [
    wbEDID,
    wbFULLReq,
    wbString(ICON, 'Texture', 0, cpNormal, True),
    wbInteger(DATA, 'Flags', itU8, wbFlags([
      {0x01}'Playable', {0x02}'Not Male', {0x04}'Not Female',
      {0x08}'Unknown 4', {0x10}'Unknown 5', {0x20}'Unknown 6',
      {0x40}'Unknown 7', {0x80}'Unknown 8'
    ]), cpNormal, True).IncludeFlag(dfCollapsed, wbCollapseFlags)
  ]);
  ```
* Also registered in the form-type enum at line 5290: `51, 'Eyes',`.
* Small, simple record: `EDID`, `FULL` (required), `ICON` (texture path string), `DATA` (an 8-bit
  playable/gender flags byte), plus the record header's own Flags field (bit 2 = Non-Playable).

**Current Behavior (`fo4recordeditor`):**
* `list_record_types` / `search_all type=EYES` / `create_record` on signature `EYES` have no backing
  C# type to target -- the tool cannot represent an `Eyes` record in memory at all.
* Confirmed via direct grep of the entire vendored `Mutagen/` tree: no file named `Eyes*.cs` in any
  game namespace (Fallout4 or otherwise) in this checkout.

**Expected Behavior (xEdit Parity):**
* A dedicated `Eyes` major record class exists under
  `Mutagen/Mutagen.Bethesda.Fallout4/Records/Major Records/`, wired into `Fallout4Mod` as its own
  `Fallout4Group<Eyes>` (analogous to the existing `HeadParts`/`Races` groups it sits next to in
  Creation Kit's UI), and `EYES` records round-trip through `list_record_types`, `create_record`,
  `set_field`, `search_records`, etc. like every other FO4 record type.

**Technical Specification & Required Changes:**
1. Add `RecordTypes.EYES` to `Mutagen.Bethesda.Fallout4/Records/RecordTypes_Generated.cs` (or confirm
   the shared `Mutagen.Bethesda.Core` constant can be reused directly).
2. Author `Eyes.cs` + accompanying `.xml` schema under
   `Mutagen/Mutagen.Bethesda.Fallout4/Records/Major Records/`, modeled on the existing simple
   record pattern (e.g. `HeadPart.cs`/`MaterialType.cs`) with fields: `EditorID`, `Name` (FULL,
   required), `Icon` (texture path string), and an 8-bit `Flags` byte (Playable / Not Male /
   Not Female / 5 unknown bits) plus the standard major-record header Flags bit for Non-Playable.
   Run the project's normal Loqui code-gen pass to produce `Eyes_Generated.cs`.
3. Wire `Fallout4Group<Eyes> Eyes` into `Fallout4Mod`/`Fallout4Mod_Generated.cs` next to `HeadParts`
   and `Races` (same generation step as #2 if code-gen is driven from the `.xml`).
4. Add `create_record` SIGNATURES support for `EYES` in
   `FO4RecordEditor/Services/Ai/PluginToolExecutor.cs` and update `KNOWLEDGE.md`'s signature list.
5. Verify round-trip against real game data once located (Fallout 4's vanilla ESM/DLC set should be
   checked for actual `EYES` records to confirm the field layout before shipping -- this audit did
   not attempt binary-level verification of vanilla data, only the xEdit-vs-Mutagen source diff).

**Labels:** `record-definitions`, `xedit-parity`, `enhancement`

---

### [MISSING RECORD TYPE]: Mutagen has no model for LVSP (Leveled Spell)

**Issue Summary:**
`LVSP` ("Leveled Spell") is a real, fully-defined, active top-level record in xEdit's FO4
definitions -- structurally the third member of FO4's leveled-list family alongside `LVLI`
(Leveled Item) and `LVLN` (Leveled Npc), both of which Mutagen *does* model
(`LeveledItem.cs`/`LeveledNpc.cs`). `LVSP` is explicitly listed in xEdit's own
`sigBaseObjects` constant array right next to `LVLI`/`LVLN`, confirming it's a genuine
placeable/referenceable base-object type, not a leftover. Mutagen has **zero** trace of it: no
`LeveledSpell.cs` file anywhere in the vendored tree (any game), and `RecordTypes.LVSP` only exists
as a raw shared byte-constant in `Mutagen.Bethesda.Core`, never as an actual FO4 (or any other game)
record class in this checkout.

**Source Reference (xEdit):**
* File: `wbDefinitionsFO4.pas`
* `sigBaseObjects` constant array, lines 77-86, includes `'LVLI', 'LVLN', 'LVSP'`.
* Full record definition, lines 10376-10393:
  ```pascal
  wbRecord(LVSP, 'Leveled Spell', [
    wbEDID,
    wbOBND(True),
    wbLVLD,
    wbInteger(LVLM, 'Max Count', itU8), { Always 00 }
    wbInteger(LVLF, 'Flags', itU8, wbFlags([
      {0x01} 'Calculate from all levels <= player''s level',
      {0x02} 'Calculate for each item in count',
      {0x04} 'Use All'
    ]), cpNormal, True).IncludeFlag(dfCollapsed, wbCollapseFlags),
    wbLLCT,
    wbRArrayS('Leveled List Entries',
      wbRStructSK([0], 'Leveled List Entry', [
        wbLeveledListEntry('Spell', [LVSP, SPEL])
      ]).SetSummaryMemberMaxDepth(0, 1)
        .IncludeFlag(dfCollapsed, wbCollapseLeveledItems)
    ).SetCountPath(LLCT)
  ]);
  ```
* Layout is a near-exact structural match to `LVLI`/`LVLN` immediately above it in the same file
  (`EDID`, `OBND`, `LVLD` chance-none byte, `LVLM` max-count, `LVLF` flags, `LLCT` count, then a
  `LVLO`-style leveled-entry array whose `Item` FormLink is constrained to `[LVSP, SPEL]` instead of
  `sigBaseObjects` or `[LVLN, NPC_]`).

**Current Behavior (`fo4recordeditor`):**
* No `LeveledSpell` class exists anywhere in the vendored `Mutagen/` tree (confirmed by filename
  search and by `grep -rn "\bLVSP\b"`, which only turns up the shared cross-game byte-pattern
  constant in `Mutagen.Bethesda.Core`, never a functional record model).
* `add_leveled_entry` (the tool's dedicated struct-list authoring helper for `LVLI`/`LVLN`/`LVSP`
  per `KNOWLEDGE.md`) cannot actually target `LVSP` today since there is no record to attach entries
  to; `create_record`/`list_records`/`search_all type=LVSP` have nothing to construct.

**Expected Behavior (xEdit Parity):**
* A `LeveledSpell` major record class exists under `Records/Major Records/`, wired into
  `Fallout4Mod` as `Fallout4Group<LeveledSpell> LeveledSpells` (mirroring `LeveledItems`/
  `LeveledNpcs`), and `LVSP` records round-trip through the same tool surface `LVLI`/`LVLN`
  already support, including `add_leveled_entry`.

**Technical Specification & Required Changes:**
1. Add `RecordTypes.LVSP` to `Mutagen.Bethesda.Fallout4/Records/RecordTypes_Generated.cs`.
2. Author `LeveledSpell.cs` + `.xml` under
   `Mutagen/Mutagen.Bethesda.Fallout4/Records/Major Records/`, copying the shape of
   `LeveledNpc.cs`/`LeveledItem.cs` but simplified to match the smaller field set above (no
   `LVLG`/`ONAM`/filter-keyword-chance fields that `LVLI` has -- `LVSP`'s xEdit definition is the
   leanest of the three, essentially identical to a bare leveled-entry list scoped to `[LVSP, SPEL]`
   FormLinks). Run Loqui code-gen to produce `LeveledSpell_Generated.cs`.
3. Wire `Fallout4Group<LeveledSpell> LeveledSpells` into `Fallout4Mod`/`Fallout4Mod_Generated.cs`.
4. Add `LVSP` to `create_record`'s SIGNATURES list and to `add_leveled_entry`'s accepted record types
   in `FO4RecordEditor/Services/Ai/PluginToolExecutor.cs` / `WriteService.cs`; update `KNOWLEDGE.md`.
5. Confirm against real vanilla/DLC data whether any FO4 record actually ships `LVSP` entries in
   practice (not attempted in this audit -- this was a source-level xEdit-vs-Mutagen diff only); if
   FO4 genuinely never uses leveled spells in shipped content, this is still worth fixing for
   third-party mod compatibility (a modder could ship a plugin defining one) and for `check_plugin`/
   `scan_broken_refs` to not silently mis-parse any that do exist in the load order.

**Labels:** `record-definitions`, `xedit-parity`, `enhancement`

---

### False positives ruled out during Phase A

* **`PLYR` ("Player Reference")** -- present in xEdit's raw signature list (`wbRecord(PLYR, 'Player
  Reference', [...]).IncludeFlag(dfInternalEditOnly);` at line 12491) but flagged
  `dfInternalEditOnly`. Cross-checked: the *identical* stub definition (same fields, same flag,
  same comment-free/active status) exists verbatim in `wbDefinitionsTES4.pas`, `wbDefinitionsFO3.pas`,
  `wbDefinitionsFNV.pas`, and `wbDefinitionsFO76.pas` -- i.e. it's a cross-game xEdit-internal
  bookkeeping construct, not a per-game real record. It exists purely so xEdit's own FormID-reference
  validation machinery (the `sigReferences` array explicitly includes `'PLYR'`) recognizes the
  hardcoded runtime "Player" FormID as a legal reference target for fields like "Linked References,"
  without a real on-disk `PLYR` GRUP-child record ever existing in any plugin. `dfInternalEditOnly`
  is xEdit's own flag for "this definition exists for internal use, don't expose it as an editable
  record type" -- confirmed via ~30 other usages of that flag throughout `wbImplementation.pas`
  gating UI-visibility/editability, not file-format presence. Not a finding.

* **`LSPR`, `MICN`, `RGDL`, `SCPT`, `SCRL`, `SHOU`, `SKIL`, `TLOD`, `TOFT`, `WOOP`** -- all 10 are
  textually present in `wbDefinitionsFO4.pas` as `wbRecord(...)` calls, which is why a naive grep
  flags them, but every one of them is wrapped in a Pascal `{ ... }` comment block, e.g.:
  ```pascal
  {wbRecord(WOOP, 'Word of Power', [
    wbEDID
  ]);}
  ```
  These are disabled Skyrim/legacy-era stub reservations (each just `wbEDID` -- no real fields),
  never actually registered by `DefineFO4`. Not a finding.

* **`DUAL` ("Dual Cast Data")** -- genuinely active in xEdit (line 9793, uncommented) and initially
  looked missing because it isn't exposed as its own `Fallout4Group<DualCastData>` property on
  `Fallout4Mod`. But `DualCastData_Generated.cs` proves Mutagen *does* have a full major-record class
  for it -- it's just not reachable from the top-level mod-group walk, which is a *reachability/wiring*
  gap (same category as the previously-documented `Fallout4ListGroup<CellBlock>` not implementing
  `IGroup`), not a *zero C# model* gap. Excluded from Phase A by the task's own definition.

* **`LAND`, `NAVM`, `SCEN`, `CELL`, `DIAL`, `INFO`, `DLBR`, `TES4`, and all 10 placed/reference types
  (`ACHR, REFR, PARW, PBAR, PBEA, PCON, PFLA, PGRE, PHZD, PMIS`)** -- all individually confirmed
  present as dedicated Mutagen classes despite not appearing in the flat `Fallout4Group<T>` property
  scan of `Fallout4Mod_Generated.cs`, because they're reached via nested/special-cased paths. Not
  findings.

* **`MSET` ("Media Set")** -- explicitly called out in the audit brief as a record type to verify,
  but it does not exist in `wbDefinitionsFO4.pas` at all (zero occurrences, checked case-insensitively
  including the `wbFormTypeEnum` list). Fallout 4 apparently never had a Media Set record type in the
  first place -- both sides correctly have nothing, so there's no gap to report.

---

## Phase B: FormID / ESL Handling

**Checked:**
- `docs/internal/KNOWLEDGE.md` Rule 4 ("Plugin-file FormID encoding = MAST-table-index for ALL masters") and its stated rationale.
- xEdit's actual on-disk FormID encoding for light (ESL) masters: `TwbFileID`/`TwbFormID` in
  `TES5Edit-dev-4.1.6/Core/wbInterface.pas` (record layout, `CreateFull`/`CreateLight`/`CreateFromFormID`/`BaseFormID`),
  the master-index resolver `TwbFile.GetMasterIndexForFileID` in `Core/wbImplementation.pas`, the
  self-reference resolver `TwbFile.GetFileFileID`, and the "fixed FormID" resolver `TwbMainRecord.DoGetFixedFormID`.
- xEdit's "Create SEQ File" feature (`xEdit/xeMainForm.pas`, `mniNavCreateSEQFileClick`) to see which FormID
  representation (`MainRecord.FixedFormID`) it actually writes to the `.seq` file.
- `FO4RecordEditor.Core/Services/WriteService.cs`: `CompactToEsl`, `CheckEslEligibility`, `IsEsl`/`NextFreeObjectId`
  (ESL range check), and `FixActorValueConditionParams` (manual FormID encoder for CTDA `ParameterOneNumber`).
- `FO4RecordEditor.Core/Services/WriteService.XEdit2.cs`: `RenumberPluginFormIds` (own-record filter) and
  `CreateSeqFile` (manual FormID encoder for the `.seq` output).
- `FO4RecordEditor.Core/Services/WriteService.cs` `RenumberFormId` (single-record renumber) -- goes through
  Mutagen's `FormKey`/`RemapLinks`, not manual bit math.
- `FO4RecordEditor/Services/Ai/PluginToolExecutor.cs` tool definitions for `compact_to_esl`, `check_esl_eligibility`,
  `renumber_formid`, `renumber_plugin_formids`, `create_seq_file` (descriptions vs. actual behavior).
- Grepped the whole tool (`grep -rn "<< 24"` over all first-party `.cs`, excluding vendored Mutagen/DDS code) to
  confirm exactly which call sites hand-encode a FormID's high byte; found only the two listed as gaps below.
- Confirmed the ESL *object-ID-range* eligibility check (0x800-0xFFF) is correct against xEdit's own constants:
  `TwbFormID.IsHardcoded` (`_FormID < $800`) and `TwbFileID.MaxLightSlot` (`$FFF`) in `wbInterface.pas`.

**Gaps found:** 2 (same root cause -- KNOWLEDGE.md Rule 4's formula, taken from the AV-condition fix, was
generalized as "for ALL masters" and reused verbatim in a second, unrelated write path -- two distinct
call sites in shipped code apply it to plugins/masters that are ESL-flagged, where it is provably wrong).

**Confidence:** High -- verified byte-for-byte against xEdit's own `TwbFileID`/`TwbFormID` implementation, not
inference from documentation prose.

### [BUG]: FormID encoding for ESL-master references treats light masters as full masters (`FixActorValueConditionParams`)

**Issue Summary:**

`WriteService.FixActorValueConditionParams` manually re-encodes a CTDA condition's raw `ParameterOneNumber`
field (used when a condition's Parameter #1 is a custom ActorValueInformation record, which Mutagen's own
`FunctionConditionData` binary writer drops from `ParameterOneRecord`). It builds the encoding with

```csharp
enc[masters[i]] = ((uint)i << 24, 0x00FFFFFFu);   // WriteService.cs:2247
```

i.e. `FormID = (masterListPosition << 24) | objId` for **every** declared master, including ones flagged
ESL/light master. The surrounding comment states this is deliberate: "Plugin-file FormID encoding uses the
MAST table index for ALL masters, including ESL. The 0xFExxxxxx space is the engine's runtime remapping,
not the on-disk format." That claim mirrors `docs/internal/KNOWLEDGE.md` Rule 4 verbatim, and it is wrong:
xEdit itself parses/writes raw on-disk FormIDs with a literal `0xFE` high byte whenever the referenced
master is a light master -- this is not a runtime-only representation.

**Source Reference (xEdit):**
* File: `TES5Edit-dev-4.1.6/Core/wbImplementation.pas`
* `TwbFile.GetMasterIndexForFileID`, lines 4532-4566: when resolving/assigning the on-disk `FileID` for a
  reference, light masters get their own separate counter (`lLightIndex`) that increments **only** across
  masters whose `ModuleType = mtLight` in the plugin's declared master list -- completely independent of
  the ordinary position-in-MAST-list index (`lFullIndex`) used for full masters. A reference to the 3rd
  declared master that happens to be the 1st *light* master among them gets light-index 0, not master-list
  index 2.
* File: `TES5Edit-dev-4.1.6/Core/wbInterface.pas`
* `TwbFileID.BaseFormID`, lines 22831-22841: `if IsLightSlot then Result := (Cardinal(LightFullSlot) shl 24) or (Cardinal(_LightSlot) shl 12)` -- i.e. the encoded FormID literally starts with byte `LightFullSlot` (`$FE`, see `TwbFileID.LightFullSlot`, line 22922-22928) with the light-only index in bits 12-23, for masters resolved as light.
* `TwbFileID.CreateFromFormID`, lines 22843-22856: when **parsing** any raw on-disk FormID, a top byte of
  `$FE` is unconditionally decoded as a light-master reference (`_LightSlot := (aFormID shr 12) and $FFF`).
  There is no "this only applies at runtime" branch -- this runs on the bytes read directly from the file.

**Current Behavior (`fo4recordeditor`):**
* `FO4RecordEditor.Core/Services/WriteService.cs:2242-2247` builds one `enc` dictionary entry per declared
  master using `(uint)i << 24` where `i` is the master's ordinary position in the full `masters` list
  (built from the probe-written MAST order), with no branch for `MutagenLoader.MasterIsEsl[...]` even
  though that ESL-tracking dictionary is already populated and checked for `Count == 0` two lines earlier
  (`WriteService.cs:2203`) -- it is read but never consulted per-master.
* If a plugin has a CTDA condition whose ActorValue lives in a declared master that is itself ESL-flagged
  (e.g. a custom skill system shipped as `.esl`, exactly the `S7 System.esp`-style case this function
  exists to fix per KNOWLEDGE.md), the written `ParameterOneNumber` uses that master's ordinary MAST-list
  position instead of `0xFE000000 | (lightIndexAmongDeclaredLightMasters << 12) | objId`. The result is a
  FormID that resolves to the wrong record (or no record) -- the same "silently means something else now"
  failure mode KNOWLEDGE.md's ITO section describes for hand-rolled master-index math elsewhere in this
  tool, just reintroduced here for the ESL case specifically.

**Expected Behavior (xEdit Parity):**
* When encoding a manual FormID reference into a declared master, the encoder must first determine whether
  that master is ESL/light (`MutagenLoader.MasterIsEsl` already has this). If so, use
  `0xFE000000 | (lightIndex << 12) | (objId & 0xFFF)`, where `lightIndex` is that master's 0-based position
  counted only among the *other* declared masters that are also light (mirroring xEdit's `GetMasterIndexForFileID`
  light-branch). If not, keep the existing `(masterListPosition << 24) | (objId & 0xFFFFFF)` path.

**Technical Specification & Required Changes:**
1. In `WriteService.cs`, after building `masters` (the probe-derived + augmented master list, ~line 2246),
   compute a separate light-only index: iterate `masters` in order, maintain a running counter that only
   increments for entries where `MutagenLoader.MasterIsEsl.TryGetValue(m, out var esl) && esl`.
2. Change the `enc` dictionary construction to store, per master, either the full-master encoding
   `((uint)i << 24, 0x00FFFFFFu)` or the light-master encoding `((0xFEu << 24) | ((uint)lightIndex << 12), 0x00000FFFu)`,
   selected by that master's ESL flag.
3. Update the comment at `WriteService.cs:2242-2244` to stop asserting the universal formula; it is only
   valid for full/medium masters.
4. Add a regression test mirroring the existing `Ba2NextGenDecompressionTests`-style pattern: a plugin
   declaring one full master and one ESL master, a condition parameter pointing at an AVIF in the ESL
   master, assert the written `ParameterOneNumber` decodes as `0xFE000000 | objId` (light index 0), not
   `0x01000000 | objId`.
5. Cross-check `docs/internal/KNOWLEDGE.md` Rule 4 (lines 479-484): its "CORRECT" formula needs a
   light-master carve-out, or it will keep being copied into future hand-rolled encoders the same way it
   was here.

**Labels:** `record-definitions`, `xedit-parity`, `bug`

---

### [BUG]: `create_seq_file` FormID encoding is wrong for a light-master (ESL) plugin's own quest records

**Issue Summary:**

`WriteService.CreateSeqFile` writes each start-game-enabled quest's FormID into the `.seq` file as

```csharp
ids.Add(((uint)masterCount << 24) | (q.FormKey.ID & 0x00FFFFFF));   // WriteService.XEdit2.cs:176
```

using `mod.ModHeader.MasterReferences.Count` (the plugin's *total* declared-master count) as the "self"
high byte unconditionally. xEdit's own SEQ export writes `MainRecord.FixedFormID`, and for a record that
belongs to the *current* file, `FixedFormID` resolves through `TwbFile.GetFileFileID`, which -- for a
plugin whose own module type is light (`mtLight`, i.e. ESL-flagged) -- returns `TwbFileID.CreateLight(GetLightMasterCount())`,
not `TwbFileID.CreateFull(GetMasterCount())`. That serializes to a FormID starting with byte `0xFE`
(with the *light*-master count, not the total master count, in bits 12-23), not `(totalMasterCount << 24)`.

**Source Reference (xEdit):**
* File: `TES5Edit-dev-4.1.6/xEdit/xeMainForm.pas`
* `TfrmMain.mniNavCreateSEQFileClick`, lines 4181-4204 (esp. line 4203):
  `FormIDs[High(FormIDs)] := MainRecord.FixedFormID;` -- the SEQ export literally uses `FixedFormID`.
* File: `TES5Edit-dev-4.1.6/Core/wbImplementation.pas`
* `TwbMainRecord.DoGetFixedFormID`, lines 9554-9590: for a record whose `FileID` slot is `>=` the current
  master count in its own module type, it re-derives `Result.FileID := lFile.FileFileID[GetMastersUpdated]`
  -- i.e. a plugin's own new records resolve through `GetFileFileID`.
* `TwbFile.GetFileFileID`, lines 4104-4117: `mtLight: Result := TwbFileID.CreateLight(GetLightMasterCount(aNewMasters));`
  -- an ESL-flagged file's "self" FileID is `CreateLight(lightMasterCount)`, which per `TwbFileID.BaseFormID`
  (`wbInterface.pas:22831-22841`) serializes with high byte `$FE` (`LightFullSlot`), not the file's ordinal
  full-master count.

**Current Behavior (`fo4recordeditor`):**
* `FO4RecordEditor.Core/Services/WriteService.XEdit2.cs:160,176`: `masterCount = mod.ModHeader.MasterReferences.Count`
  (every declared master, full or light) is shifted into the FormID's top byte for **every** exported
  quest, with no check of whether `mod` itself is ESL/light-flagged (`Fallout4Mod.IsSmallMaster`, already
  used elsewhere in this codebase per `MutagenLoader.cs:647`).
* For a plugin that is itself saved/flagged as `.esl`/light master -- the exact category this tool's
  `compact_to_esl` exists to produce, and precisely the kind of plugin likely to also carry a
  start-game-enabled quest needing a SEQ file -- every FormID written to the `.seq` file will have the
  wrong high byte and object-count field, so the game will not recognize the entries as referring to the
  plugin's own quests at all (SEQ matching is exact-FormID). This silently defeats the SEQ file's entire
  purpose (auto-starting quests on existing saves) for light-master plugins specifically.

**Expected Behavior (xEdit Parity):**
* If `mod` is ESL/light-flagged, encode each of the plugin's own quest FormIDs as
  `0xFE000000 | (lightMasterCount << 12) | (objId & 0xFFF)`, where `lightMasterCount` is the count of
  *light* masters among `mod`'s own declared masters (mirrors `GetLightMasterCount`, i.e. filter
  `mod.MasterReferences` by `MutagenLoader.MasterIsEsl`). Non-ESL plugins keep the existing
  `(totalMasterCount << 24) | objId` path (this matches `GetFileFileID`'s `mtFull` branch, which uses the
  *full*-master count, not the *total* -- for a non-ESL plugin these are the same value only when it has no
  light masters declared at all, which is the common case but not guaranteed; see note below).

**Technical Specification & Required Changes:**
1. In `CreateSeqFile` (`WriteService.XEdit2.cs`), determine `mod`'s own ESL/light status
   (`Fallout4Mod`/`IFallout4ModGetter` exposes `IsSmallMaster`, already read in `Mo2ProfileLoader.cs:432`
   and `MutagenLoader.cs:647` -- reuse the same property here instead of re-deriving it).
2. If light-flagged: compute `lightMasterCount` = count of `mod.ModHeader.MasterReferences` whose file name
   is present and `true` in `MutagenLoader.MasterIsEsl`; encode as `0xFE000000u | ((uint)lightMasterCount << 12) | (q.FormKey.ID & 0xFFFu)`.
3. If not light-flagged: for full parity with `GetFileFileID`'s `mtFull` branch, the high byte should be the
   count of *full* masters specifically (not total masters) -- flag this as a secondary, lower-severity
   precision gap only relevant to a plugin that declares light masters but is not itself ESL; the common
   case (all-full-master dependency list) already produces the same value either way, so this does not need
   to block the primary fix in step 2.
4. Add a regression test: an ESL-flagged plugin with 0 declared light masters and one start-game-enabled
   quest with object id `0x000801` should produce SEQ bytes `01 08 00 FE` (little-endian `0xFE000801`), not
   `01 08 00 01`/whatever `(totalMasterCount<<24)` currently yields.
5. Same KNOWLEDGE.md Rule 4 cross-check as the sibling gap above -- both bugs trace to the same
   over-generalized rule.

**Labels:** `record-definitions`, `xedit-parity`, `bug`

---

## NPC_ (Non-Player Character)

**Subrecords/fields checked:** 52 (EDID, VMAD, OBND, PTRN, STCP, ACBS/Configuration incl. Level union,
Factions/SNAM, INAM Death Item, VTCK Voice, TPLT, LTPT, LTPC, TPTA Template Actors x13, RNAM Race,
SPCT/SPLO Actor Effects, DEST Destructible, WNAM Skin, ANAM Far Away Model, ATKR Attack Race, Attacks
(ATKD/ATKE/ATKW/ATKS/ATKT), SPOR/OCOR/GWOR/ECOR/FCPL/RCLR override package lists, PRKZ/PRKR Perks,
PRPS Properties, FTYP, NTRM, COCT/CNTO Items, AIDT AI Data, Packages/PKID, KSIZ/KWDA Keywords, APPR
Attach Parent Slots, Object Template (OBTE/OBTF/FULL/OBTS), CNAM Class, FULL Name, SHRT Short Name,
DATA Marker, DNAM struct, PNAM Head Parts, HCLF/BCLF hair/facial-hair color, ZNAM Combat Style, GNAM
Gift Filter, NAM5, NAM6/NAM7/NAM4 height, MWGT Weight, NAM8 Sound Level, CS2H/CS2K/CS2D/CS2E/CS2F Actor
Sounds, CSCR Inherits Sounds From, PFRN Power Armor Stand, DOFT/SOFT outfits, DPLT Default Package
List, CRIF Crime Faction, FTST Head Texture, QNAM Texture Lighting, MSDK/MSDV Morphs, Face Tinting
Layers (TETI/TEND), MRSV Body Morph Region Values, Face Morphs (FMRI/FMRS), FMIN Facial Morph
Intensity, ATTX Activate Text Override)

**Gaps found:** 0
**Confidence:** High

No genuine capability/field gaps were found for NPC_ in FO4RecordEditor versus xEdit.

**What was checked:**
1. Read the full `wbRecord(NPC_, ...)` definition in `TES5Edit-dev-4.1.6/Core/wbDefinitionsFO4.pas`
   (line 10617-10819) plus every shared struct/enum it references: `wbAttackData` (line 4440),
   `wbAIDT` (line 6570), `wbDEST` (line 4641), `wbObjectTemplate` (line 5888), `wbFaction`/
   `wbActorSounds`/`wbKeywords`/`wbNPCTemplateActorEntry` in `wbDefinitionsCommon.pas` (lines 5944,
   6976, 7070, 7117). Traced every ACBS flag bit, the PC-Level-Mult union, all 13 Template Actor
   entries, and both byte-array "Unknown" fields.
2. Read `Mutagen/Mutagen.Bethesda.Fallout4/Records/Major Records/Npc.cs` and `Npc_Generated.cs`, plus
   `TemplateActors_Generated.cs`, `Attack_Generated.cs`/`AttackData_Generated.cs`,
   `RankPlacement_Generated.cs`, `PerkPlacement_Generated.cs`, `NpcSound_Generated.cs`. Every xEdit
   subrecord has a matching C# property (renamed but semantically identical, e.g. ACBS -> `Flags` +
   `Level` union + scalar properties; DNAM -> flat scalar properties; TPTA -> `TemplateActors` with 13
   `IFormLink<INpcSpawnGetter>` properties named `*Template`). Confirmed via `Npc_Registration.cs`
   `AdditionalFieldCount = 88` / `FieldCount = 95` that nothing was silently dropped from the binary
   round-trip.
3. Checked the write/MCP-exposure layer (`FO4RecordEditor.Core/Services/WriteService.cs`,
   `ElementService.cs`, `FO4RecordEditor/Services/Ai/PluginToolExecutor.cs`, `docs/internal/
   KNOWLEDGE.md`). Unlike PERK/quest/message-button records, NPC_ has no dedicated struct-authoring
   MCP tool -- but it doesn't need one:
   - `element_add` / `element_remove` / `element_move` / `element_clear` (`ElementService.cs`) are
     fully reflection-generic over ANY `IList<T>`/`IReadOnlyList<T>`/`ICollection<T>` property reached
     by a dotted path, with no record-type special-casing. This covers every NPC_ list: Factions,
     Perks, Head Parts, Attacks, Packages, Keywords, Items (inventory), Object Templates, Actor
     Sounds, Face Tinting Layers, Face Morphs, and Morphs.
   - `set_field` (`WriteService.TrySet`, line ~2411) resolves dotted **and indexed** paths
     (`"Attacks[0].AttackData.DamageMult"`, `"Factions[0].Rank"`, `"TemplateActors.AiDataTemplate"`)
     and, critically, writes boxed struct values back up the hop chain -- so nested value-type structs
     (AttackData, RankPlacement, NpcBodyMorphRegionValues, DNAM, ACBS) are genuinely settable field by
     field, not just top-level scalars. This was previously a documented bug (`ObjectBounds` write-back
     silently no-op'd, see KNOWLEDGE.md "set_field CANNOT set ObjectBounds" 2026-07-16) but the current
     `TrySet` implementation (KNOWLEDGE.md "Write-layer hardening", 2026-07-19) has the write-back loop
     and appears to have fixed this class of bug generally, not just for OBND.
   - `add_list_item` covers all the plain FormLink lists (Head Parts, Keywords, Attach Parent Slots,
     Actor Effects/Spells, Packages) as a convenience on top of `element_add`.
   - `attach_script` / `set_script_property` cover VMAD (Papyrus scripts on the NPC).
   - `get_record` reads everything back, including nested lists/structs, for verification.

**Notes / uncertain items:**
- Two fields are modeled as raw `MemorySlice<Byte>` (`NAM5`, an unlabeled "Unknown" 2-byte block xEdit
  itself only shows as `wbUnknown(NAM5)` with no semantic meaning; and `SoundsFinalize`/CS2F, a 1-byte
  structural marker at the end of the Actor Sounds sub-chain). `set_field`'s `SetLeaf` only converts to
  string/enum/bool/numeric/Color -- there is no byte-array branch, so neither field is settable via
  `set_field`, and no dedicated tool covers raw byte-array leaves anywhere in the tool. Not filed as an
  NPC_ gap: it's a generic, tool-wide limitation (not NPC_-specific), and both fields are themselves
  semantically opaque/structural in xEdit's own definition (not real authoring surface even there).
- Did not independently verify a live round-trip (open a real NPC_ record and exercise `set_field` /
  `element_add` end-to-end) -- this audit is a static source-level comparison of xEdit's definition,
  Mutagen's binary model, and the tool's exposed write paths, per the task's 3-step scope.

---

## WEAP (Weapon)

**Subrecords/fields checked:** 38 (EDID, VMAD, OBND, PTRN, STCP, FULL, Model, ICON, MICO, Enchantment[EITM/EAMT],
DEST, ETYP, BIDS, BAMT, YNAM, ZNAM, Keywords, DESC, INRD, APPR, ObjectTemplate, NNAM, 1st-Person Model[MOD4/MO4T/MO4S/MO4C/MO4F],
DNAM struct [34 sub-fields], FNAM struct [11 sub-fields], CRDT struct, INAM, LNAM, WAMD, WZMD, CNAM, DAMA array
[Type/Amount/Curve Table], FLTR, MASE)

**Gaps found:** 1
**Confidence:** High

### Summary of method

Traced `wbRecord(WEAP, ...)` at `TES5Edit-dev-4.1.6/Core/wbDefinitionsFO4.pas:13237-13392`, plus the shared
struct helpers it calls (`wbOBTSReq` object-template item at line 5867, `wbObjectTemplate` at 5888, `wbDamageTypeArray`
in `wbDefinitionsCommon.pas:5677`, `wbEnchantment` at `wbDefinitionsCommon.pas:5689`). Cross-checked every field
against `Mutagen/Mutagen.Bethesda.Fallout4/Records/Major Records/Weapon.cs`, `Weapon_Generated.cs`,
`WeaponExtraData_Generated.cs` (the FNAM struct, modeled as `Weapon.ExtraData`), and `WeaponDamageType_Generated.cs`
(the DAMA array). Every xEdit-named subrecord/sub-field has a real, correctly-typed C# property -- this is one
of the most completely-ported record types checked so far; no missing Mutagen model fields of any real-world
consequence (see Notes for the one low-confidence exception).

The one genuine gap is not in the data model but in the write/MCP-exposure layer: two of WEAP's struct-typed
lists cannot be populated at all on a freshly created record.

### [BUG]: `element_add` cannot populate WEAP's DamageTypes or ObjectTemplates on a newly created record

**Issue Summary:**
`create_record WEAP` leaves `Weapon.DamageTypes` (the DAMA array -- xEdit's "Damage Type", e.g. splitting a
weapon's damage into Energy/Poison/etc.) and `Weapon.ObjectTemplates` (the OBTE/OBTS block -- xEdit's "Object
Template", the entire attach-point/mod-combination system that lets a weapon show receiver/barrel/etc. variants
in the workshop) both `null`, because `Weapon`'s constructor never initializes them. Neither field has a
dedicated authoring tool (unlike LVLI/LVLN entries, PERK effects, or SPEL/ENCH effects, which each get
`add_leveled_entry`/`set_perk_effects`/`set_magic_effects`). The only generic path for a struct list is
`element_add`, and its path-resolution helper does not initialize a null list the way `add_list_item`'s does --
so `element_add(plugin, weapon, path="DamageTypes")` fails immediately with a misleading error, even though
`DamageTypes` genuinely is a list field.

**Source Reference (xEdit):**
* File: `TES5Edit-dev-4.1.6/Core/wbDefinitionsFO4.pas`
* Line / Definition: `wbRecord(WEAP, ...)` line 13237; `wbDamageTypeArray('Damage Type')` used at line 13383
  (defined in `wbDefinitionsCommon.pas:5677`); `wbObjectTemplate` used at line 13262 (defined at
  `wbDefinitionsFO4.pas:5888`, itself built from `wbOBTSReq` at line 5867).

**Current Behavior (`fo4recordeditor`):**
* `FO4RecordEditor.Core/Services/WriteService.cs:2349` -- `AddNewBySig` creates a WEAP with
  `new Weapon(fk, r) { EditorID = editorId }` and nothing else; per
  `Mutagen/.../Weapon_Generated.cs:628` (`private ExtendedList<WeaponDamageType>? _DamageTypes;`) and
  `Weapon_Generated.cs:326` (`private ExtendedList<ObjectTemplate<Weapon.Property>>? _ObjectTemplates;`), both
  fields have no default initializer and start `null`.
* `FO4RecordEditor.Core/Services/ElementService.cs:260-291` (`TryResolve`, used by `AddElement`/`element_add`)
  walks the path with plain reflection: `cur = prop.GetValue(cur);` then `if (cur is IList asList) ...`. When
  the property value is `null` this branch is simply skipped -- `lastList` stays `null` and the walk silently
  "succeeds" with nothing to add to.
* `AddElement` (`ElementService.cs:151-178`) then hits `if (r.TargetList is not { } list) return ToolError.Fail($"'{path}' is not inside a list, so there is nothing to add to.");`
  and refuses, even though `path` names a real list field -- it is only `null`, not missing.
* Contrast with `add_list_item`'s `AddListItemToRecord` (`WriteService.cs:246-261`), which explicitly handles
  this case: `if (listObj == null) { ... listObj = Activator.CreateInstance(listType); prop.SetValue(rec, listObj); }`.
  And with the dedicated struct-list tools, e.g. `AddLeveledEntry` (`WriteService.Authoring.cs:67`):
  `(li.Entries ??= new ExtendedList<LeveledItemEntry>()).Add(e);` -- the same null-coalesce pattern
  `element_add` lacks.
* Net effect, reproduced by code inspection: `create_record("WEAP", ...)` -> `element_add(path="DamageTypes")`
  or `element_add(path="ObjectTemplates")` both fail with "not inside a list, so there is nothing to add to."
  `set_field` cannot substitute -- assigning a whole list is the same class of failure already documented for
  `ObjectBounds` in `docs/internal/KNOWLEDGE.md` ("`set_field` CANNOT set ObjectBounds").

**Expected Behavior (xEdit Parity):**
* xEdit's own "Add" (`GetAddElement`/`TargetElement.Assign`, referenced in `ElementService.cs`'s own doc
  comment) works on an empty/never-populated DAMA or Object Template block exactly the same as on a populated
  one -- there is no xEdit-visible difference between "field never had any entries" and "field currently has
  0 entries." A new WEAP record should be able to receive its first Damage Type or Object Template entry
  through the same element-menu affordance used for every other struct list.

**Technical Specification & Required Changes:**
1. File: `FO4RecordEditor.Core/Services/ElementService.cs`
   - In `TryResolve` (and ideally `TryDescribe`, for accurate `element_describe` reporting on a fresh record),
     when a segment's property value is `null` but the property's declared TYPE is list-shaped
     (`IList`/`IList<T>`/`ExtendedList<T>`), construct an empty instance and write it back via
     `prop.SetValue(cur, instance)` before continuing the walk -- mirroring the null-coalesce pattern already
     used in `AddListItemToRecord` (`WriteService.cs`) and `AddLeveledEntry`/`SetPerkEffects`/`SetMagicEffects`
     (`WriteService.Authoring.cs`).
   - This is a generic fix (not WEAP-specific plumbing): it will also unblock any other record whose only
     struct-list authoring path is the generic `element_add`/`element_describe` pair and whose constructor
     leaves that list `null` -- WEAP's `DamageTypes`/`ObjectTemplates` are simply the two hit here.
2. Unit / Integration Tests:
   - Add a test that creates a fresh WEAP (`create_record WEAP`), calls `element_add` with `path="DamageTypes"`,
     confirms an entry is inserted, then `set_field` on `DamageTypes[0].DamageType` (FormLink) and
     `DamageTypes[0].Value` (uint), and round-trips through `save_plugin` + reload to verify the DAMA subrecord
     is written and re-read intact.
   - Repeat for `path="ObjectTemplates"` (verifying at least the OBTE count / OBTS struct on a `Combinations[0]`
     entry can be built up via chained `element_add` + `set_field` calls) since it is the more consequential of
     the two fields (it is what makes a weapon moddable in the workshop at all).

**Labels:** `write-service`, `xedit-parity`, `enhancement`

### Notes / uncertain items

* **DAMA's optional "Curve Table" field (form version >= 152) is not modeled in Mutagen, but this is low
  confidence as a real gap.** `wbDamageTypeArray` (`wbDefinitionsCommon.pas:5677-5687`) defines
  `wbFromVersion(152, wbFormIDCk('Curve Table', [CURV, NULL]))` inside the DAMA struct (used by both WEAP's
  "Damage Type" and ARMO's "Resistance"). `Mutagen/.../WeaponDamageType_Generated.cs` has only `DamageType`
  (FormLink) and `Value` (UInt32) -- no `CurveTable` property. However, the `TES5Edit-dev-4.1.6` checkout used
  as ground truth for this audit does not itself define a `CURV` record type anywhere in
  `wbDefinitionsFO4.pas` (only references it as an allowed link target) -- so even the reference tool's own
  support for this field looks incomplete/aspirational. Given neither side of the comparison has a verified,
  working implementation, not confident this is a real, exploitable gap in current retail content. See the
  matching finding under ARMO below (`Resistance`/`DAMA`), which is drafted as a formal issue since ARMO's
  binary reader assumes a hardcoded fixed stride -- a stronger, correctness-risk claim than WEAP's.
* First-person model (`MOD4`/`MO4T`/`MO4S`/`MO4C`, mapped to `Weapon.FirstPersonModel`) and the main model
  (`MODL`/`MODT`/etc., mapped to `Weapon.Model`) both reuse Mutagen's shared `Model` type. Not independently
  verified by a round-trip test in this audit -- treated as very likely fine given `Model` is used identically
  by dozens of other already-verified record types, not flagged as a gap.

---

## ARMO (Armor)

**Subrecords/fields checked:** 30 (EDID, VMAD, OBND, PTRN, FULL, Enchantment/ENAM, Male WorldModel struct [MOD2/MO2T/ICON/MICO], Female WorldModel struct [MOD4/MO4T/ICO2/MIC2], BOD2, DEST, YNAM, ZNAM, ETYP, BIDS, BAMT, RNAM, KWDA/KSIZ, DESC, INRD, Models/APPR-adjacent INDX+MODL list, DATA[Value/Weight/Health], FNAM[ArmorRating/BaseAddonIndex/StaggerRating], DAMA Resistance array [Type/Amount/CurveTable], TNAM, APPR, Object Template [OBTE/OBTF/FULL/OBTS/STOP]) plus ARMA's mirrored fields (BOD2, RNAM, DNAM, Biped Model, 1st Person, NAM0-3, Additional Races, SNDD, ONAM, Sculpt Data/BSMP-BSMS-BSMB).

**Gaps found:** 2
**Confidence:** High

### [BUG]: `set_field` cannot initialize a null `WorldModel`/`FirstPersonModel` on a freshly created ARMO or ARMA

**Issue Summary:**
A brand-new `ARMO` (or `ARMA`) record created with `create_record` has a null `WorldModel` (and, for `ARMA`, a null `FirstPersonModel` too). The generic `set_field` dotted-path auto-initializer cannot create a `GenderedItem<T>` container to fill that null, because `GenderedItem<T>` has no parameterless constructor -- only `GenderedItem(T male, T female)`. This makes it impossible to set the Male/Female World Model, Icon, or Message Icon on a new armor via `set_field`, which is arguably the single most important authoring field on the record (without it the item is invisible in-game).

**Source Reference (xEdit):**
* File: `wbDefinitionsFO4.pas`
* Line / Definition: lines 6164-6175 (`ARMO`'s `wbRStruct('Male', [wbTexturedModel('World Model', [MOD2, MO2T], ...), ICON, MICO])` / `'Female'` with `MOD4/MO4T/ICO2/MIC2`); lines 6236-6245 (`ARMA`'s `'Biped Model'` and `'1st Person'` gendered `wbTexturedModel` structs).

**Current Behavior (`fo4recordeditor`):**
* `Mutagen/Mutagen.Bethesda.Fallout4/Records/Major Records/Armor_Generated.cs:156` -- `public IGenderedItem<ArmorModel?>? WorldModel { get; set; }` has no default value, so a fresh `new Armor(fk, r)` (used by `create_record`, `WriteService.cs:2350`) leaves it `null`.
* Same pattern in `ArmorAddon_Generated.cs:95` (`WorldModel`) and `:99` (`FirstPersonModel`), both left `null` by a fresh `new ArmorAddon(fk, r)` (`WriteService.cs:2354`), while sibling gendered fields on the same record (`Priority`, `BoneData`) DO get a default `new GenderedItem<T>(default, default)` in their constructors and are fine.
* `GenderedItem<T>` (`Mutagen/Mutagen.Bethesda.Core/Plugins/Records/GenderedItem.cs:53-88`) is a `sealed class` whose only constructor is `GenderedItem(T male, T female)` -- no parameterless overload.
* `WriteService.TrySet`'s path-hop initializer (`WriteService.cs:2449-2455`) does:
  ```csharp
  var next = p.CanRead ? p.GetValue(cur) : null;
  if (next == null)
  {
      var pt = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
      try { next = System.Activator.CreateInstance(pt); } catch { next = null; }
      if (next == null || !p.CanWrite) { msg = $"Can't initialise '{seg}'."; return false; }
      p.SetValue(cur, next);
  }
  ```
  `Activator.CreateInstance(typeof(IGenderedItem<ArmorModel?>))` (an interface, not even the concrete `GenderedItem<T>`) always throws, is swallowed by the `catch`, and the call returns `Can't initialise 'WorldModel'.` -- verified by static trace, not run live.
* No other tool in `PluginToolExecutor.cs` or `WriteService*.cs` has any special-case handling for `WorldModel`, `FirstPersonModel`, or `GenderedItem` (confirmed via grep -- zero hits). `element_add`/`element_describe` (`ElementService.cs`) only handle `IList`-typed properties, so they cannot help either (`WorldModel` is a single nested reference, not a list).
* The only working path is the already-documented OBND workaround: `copy_as_override` a vanilla armor/armor-addon (whose `WorldModel` is already non-null from the binary read) and edit the cloned struct's leaves -- `set_field` CAN write leaves once the container already exists, since the failure is only in auto-*creating* the container.

**Expected Behavior (xEdit Parity):**
* xEdit lets you add a Male/Female World Model (and Icon/Message Icon) to a brand-new `ARMO`/`ARMA` with no special ceremony -- Add on the `Male`/`Female` struct just default-constructs it in place.
* The tool should be able to do the same: `set_field <new ARMO> WorldModel.Male.Model.File "..."` should succeed on a freshly created record, not just on a cloned one.

**Technical Specification & Required Changes:**
1. File: `FO4RecordEditor.Core/Services/WriteService.cs`
   - In the path-hop initializer (~line 2449), special-case `GenderedItem<T>`/`IGenderedItem<T>`: when `pt` is (or closes) `IGenderedItem<>`/`GenderedItem<>`, construct via `Activator.CreateInstance(typeof(GenderedItem<>).MakeGenericType(genericArg), defaultOfArg, defaultOfArg)` instead of the no-arg `Activator.CreateInstance(pt)`.
   - Apply the same fix path for any other Mutagen wrapper type with a required-args-only constructor that could appear as an intermediate hop (grep the FO4 record tree for other non-Loqui hand-written wrapper types before assuming `GenderedItem` is the only one).
2. Unit / Integration Tests:
   - Add a test that calls `create_record("ARMO", ...)` then `set_field` on `WorldModel.Male.Model.File`, `WorldModel.Male.Icons.Icon`, and `WorldModel.Female.Model.File` on the fresh record, then re-reads the record and asserts the values round-trip after `save_plugin` + reload. Repeat for `ARMA`'s `WorldModel` and `FirstPersonModel`.

**Labels:** `record-definitions`, `xedit-parity`, `bug`, `enhancement`

---

### [MISSING-FIELD]: ARMO's `Resistance`/`DAMA` struct has no `Curve Table` (`CURV`) field -- and the binary reader assumes a fixed 8-byte stride

**Issue Summary:**
xEdit's FO4 definitions add a version-gated `Curve Table` FormLink (`CURV`) to each `DAMA` resistance entry for plugins at form version >=152 (`wbFromVersion(152, wbFormIDCk('Curve Table', [CURV, NULL]))`). Mutagen's `ArmorResistance` model has no such field anywhere -- not in the generated class, not in the Loqui XML source it was generated from -- and its binary overlay reads each entry at a hardcoded fixed length of exactly 8 bytes.

**Source Reference (xEdit):**
* File: `wbDefinitionsCommon.pas`
* Line / Definition: `wbDamageTypeArray` (lines 5677-5687): `wbArrayS(DAMA, aItemName+'s', wbStructSK([0], aItemName, [wbFormIDCk('Type',[DMGT]), wbInteger('Amount',itU32), wbFromVersion(152, wbFormIDCk('Curve Table',[CURV, NULL]))]))`, invoked for `ARMO` at `wbDefinitionsFO4.pas:6204` (`wbDamageTypeArray('Resistance')`).

**Current Behavior (`fo4recordeditor`):**
* `Mutagen/Mutagen.Bethesda.Fallout4/Records/Major Records/Armor.xml:93-98` defines `ArmorResistance` with only `DamageType` (FormLink) and `Value` (UInt32) -- no `CurveTable` field exists in the schema Mutagen was generated from.
* `Mutagen/Mutagen.Bethesda.Fallout4/Records/Major Records/ArmorResistance_Generated.cs:1143` hardcodes `length: 0x8` when spawning each array entry from the binary overlay (`stream.Position += 0x8` immediately after, line 1149) -- i.e. the reader unconditionally treats every `DAMA` entry as exactly 8 bytes (4-byte FormLink + 4-byte UInt32).
* Grepped the whole vendored Mutagen tree for `CurveTable`/`Curve Table`: zero hits anywhere, confirming this isn't modeled under a different name.
* No tool code (`WriteService*.cs`, `PluginToolExecutor.cs`) references `CurveTable` either, since there is no property to reference.

**Expected Behavior (xEdit Parity):**
* On a plugin whose header form version is >=152 (the FO4 "Next-Gen"/Creation Club-era update), each `DAMA` entry is 12 bytes (FormLink Type + UInt32 Amount + FormLink CurveTable), and xEdit reads/writes that third field.
* If Mutagen's fixed 8-byte stride is fed a real 12-byte-per-entry `DAMA` list from such a plugin, it will misread the array (each parsed "entry" straddling two real entries) rather than cleanly ignoring the extra field -- a correctness risk, not just a missing convenience field. (Not verified against an actual vanilla/CC file containing populated `CURV` data in this pass -- flagging as high-confidence from the code/schema evidence, but the specific misparse consequence at runtime is inferred, not reproduced.)

**Technical Specification & Required Changes:**
1. File: `Mutagen/Mutagen.Bethesda.Fallout4/Records/Major Records/Armor.xml`
   - Add a `CurveTable` `FormLink` field (`recordType` not applicable -- it's a positional sub-field of `DAMA`, matching xEdit's `wbFromVersion(152, ...)` conditional member) to the `ArmorResistance` object definition, gated on `FormVersion >= 152` the way Mutagen expresses other version-gated fields elsewhere in the FO4 schema (grep other `.xml` files for an existing `FormVersion` gate pattern to match conventions).
2. File: `Mutagen/Mutagen.Bethesda.Fallout4/Records/Major Records/ArmorResistance_Generated.cs` (regenerate from the updated XML rather than hand-edit)
   - Binary read/write/overlay length must become conditional (8 or 12 bytes) based on the containing mod's `FormVersion`, not the current hardcoded `0x8`.
3. Unit / Integration Tests:
   - Round-trip test loading a real FO4 plugin with form version >=152 and at least one `ARMO` record whose `Resistance` list has a populated `CURV` field; assert byte-identical round-trip and that `CurveTable` is readable/settable via `set_field`/`element_add`.

**Labels:** `record-definitions`, `xedit-parity`, `enhancement`, `mutagen-fork`

### Notes / uncertain items

- The `Resistance`/`CurveTable` gap's real-world *impact* (how often FO4 plugins in the wild actually populate `CURV`) was not empirically checked against a live modlist plugin in this pass -- the finding rests on cross-referencing xEdit's own definitions against Mutagen's schema/binary-stride code, which is solid evidence the field is unmodeled, but not proof of an observed misparse in a real file.
- Everything else on `ARMO`/`ARMA` checked out as fully modeled in Mutagen AND fully reachable through the tool's existing generic mechanisms: `set_field` (scalars, enums, FormLinks via `SetTo`, struct leaves with write-back) for `DATA`, `FNAM`, `BOD2`/biped flags, `ETYP`/`BIDS`/`BAMT`/`RNAM`/`TNAM`/`INRD` FormLinks, `DESC`/`FULL` translated strings, `Enchantment`; and `element_add`/`element_remove`/`element_move`/`element_clear`/`element_describe` (fully reflection-generic over any `IList`-typed path) for `Keywords`, `Models`/`Armatures` (INDX+MODL), `Resistance` list entries themselves (excluding the missing `CurveTable` leaf above), `APPR`/`AttachParentSlots`, and the full `ObjectTemplate` struct tree (`Combinations`/`Includes`/`Properties`, including the typed `ObjectMod*Property` union subclasses) -- a stronger parity story than initially expected given how complex Object Template's binary layout is in xEdit's own definition.
- `OBND`/`ObjectBounds` was flagged as unsettable via `set_field` in `docs/internal/KNOWLEDGE.md` (2026-07-16 entry), but reading the current `WriteService.TrySet` code shows this has since been fixed generically (struct write-back on dotted paths, `WriteService.cs:2469-2484`) -- not re-flagged here as it is stale, already-fixed, and not ARMO-specific.

---

## RACE

**Subrecords/fields checked:** ~90 (full `DATA` struct floats/flags/enums, EDID/FULL/DESC/SPCT/SPLOs/WNAM/BOD2/
Keywords/PRPS/APPR, MNAM/FNAM markers, Male/Female Skeletal Model, Movement Type Names, Voices, Default Hair
Colors, TINL, PNAM/UNAM, ATKR + Attacks list, Body Data (Male/Female Parts), GNAM, Male/Female Behavior Graph,
NAM4/NAM5/NAM7/CNAM/NAM2/ONAM/LNAM, Biped Object Names + RBPC, Movement Data Overrides, VNAM + Equip Slots,
UNWP, Phoneme Target Names + PHWT, WKMV/SWMV/FLMV/SNMV, Male/Female Neck Fat Adjustment, Head Parts, Race
Presets, Hair Colors, Face Details, Default Face Texture, Tint Layers, Morph Groups, Face Morphs, Wrinkle Map
Path, NAM8/RNAM/SRAC/SADD, Subgraph Data, PTOP/NTOP, Morph Values, MLSI, HNAM/HLTX, QSTI, BSMP bone data)

**Gaps found:** 3
**Confidence:** High for the tool-surface findings (verified against exact source lines in `WriteService.cs`,
`ElementService.cs`, and the Mutagen-generated model); Medium for the BodyData cardinality question specifically
(could not check against real game files in this environment whether any vanilla/DLC/mod RACE record actually
uses more than one body-data part per gender -- see that finding's own note).

### [GAP]: `create_record` has no case for RACE at all

**Issue Summary:**
Every other complex FO4 record type the tool supports for authoring from scratch (NPC_, SPEL, PERK, LVLI, LVLN,
ENCH, FURN, ARMO, WEAP, ...) can be shelled with `create_record` and then filled in with `set_field` +
the struct-list tools. RACE cannot: the signature switch that backs `create_record` simply has no `"RACE"`
case, so the call fails with `Record type 'RACE' is not supported for creation yet.`

**Source Reference (xEdit):**
* File: `TES5Edit-dev-4.1.6/Core/wbDefinitionsFO4.pas`
* Line / Definition: `wbRecord(RACE, 'Race', ...)` at line 11424 -- xEdit's "New Record" on the Race group works
  like any other record group; no special casing.

**Current Behavior (`fo4recordeditor`):**
* `FO4RecordEditor.Core/Services/WriteService.cs`, `AddNewBySig` (lines 2342-2399): the `switch (sig.ToUpperInvariant())`
  covers `BOOK/HOLOTAPE, TERM, WEAP, ARMO, ARMA, ANIO, IDLE, MISC, COBJ, KYWD, AMMO, ALCH, ACTI, CONT, FLST, MGEF,
  PERK, NPC_, QUST, MESG, GLOB*, AVIF, LVLI, LVLN, SPEL, ENCH, FURN, IMAD, LIGH, STAT` and `default: return null;`.
  `RACE` is absent from both the switch and the `SupportedTypes` string shown in the error message (line 2311-2314).

**Expected Behavior (xEdit Parity):**
* `create_record("RACE", editorId)` should add a new, empty `Race` record to the plugin's `mod.Races` group the
  same way `create_record("NPC_", ...)` adds to `mod.Npcs`, so a modder can build a wholly new playable/creature
  race without first finding a vanilla record to clone.
* A working (if more roundabout) alternative already exists and should keep working: `copy_as_override` a vanilla
  race, then `renumber_formid` to detach it into a new FormID in the target plugin. That path is not broken by
  this gap, it is just the only path.

**Technical Specification & Required Changes:**
1. File: `FO4RecordEditor.Core/Services/WriteService.cs`
   - Add `case "RACE": { var x = new Race(fk, r) { EditorID = editorId }; mod.Races.Add(x); return x; }` to
     `AddNewBySig`, and append `RACE` to the `SupportedTypes` constant.
2. File: `FO4RecordEditor/Services/Ai/PluginToolExecutor.cs`
   - No dispatch change needed (RACE would flow through the existing `create_record` case); update the
     `create_record` tool description's supported-types list if it enumerates them.
3. Unit / Integration Tests:
   - Add a test that creates a RACE shell, sets a few `DATA` scalar fields via `set_field` (e.g.
     `DATA.MaleHeight`), saves, and re-reads to confirm the record round-trips with a valid empty shell
     (matching the "new records are empty shells" contract documented for other types).

**Labels:** `record-definitions`, `xedit-parity`, `enhancement`

---

### [GAP]: Nested FormLink lists inside RACE's HeadData/Subgraph structs can be appended empty but never filled in

**Issue Summary:**
`HeadData.Male/Female.RacePresets`, `.AvailableHairColors`, `.FaceDetails`, and `Subgraphs[n].ActorKeywords` /
`.TargetKeywords` are all `ExtendedList<IFormLinkGetter<T>>` -- lists of *bare* FormLinks that live one or more
property-hops below the record root. `element_add` can insert a new null-FormLink placeholder into any of them,
but nothing in the tool's generic surface can subsequently set that placeholder's target FormKey: `add_list_item`
(the one tool that both adds *and* sets a FormLink value) only resolves a single, non-dotted property name
directly on the record itself, and `set_field`'s dotted-path resolver explicitly refuses to address a bare list
entry by index. The net effect is these five lists can be grown but never populated through the tool.

**Source Reference (xEdit):**
* File: `TES5Edit-dev-4.1.6/Core/wbDefinitionsFO4.pas`
* Line / Definition:
  - `wbRArray('Male Race Presets', wbFormIDCk(RPRM, 'Preset NPC', [NPC_, NULL]))` -- line 11645 (Female: 11663)
  - `wbRArray('Male Hair Colors', wbFormIDCk(AHCM, 'Hair Color', [CLFM, NULL]))` -- line 11646 (Female: 11664)
  - `wbRArrayS('Male Face Details', wbFormIDCk(FTSM, 'Texture Set', [TXST, NULL]))` -- line 11647 (Female: 11665)
  - `wbRArray('Actor Keywords', wbFormIDCk(SAKD, 'Keyword', [KYWD]))` / `'Target Keywords'` (`STKD`) inside the
    `'Subgraph Data'` array -- lines 11678 and 11681. All are plain xEdit "Add -> pick a record" lists.

**Current Behavior (`fo4recordeditor`):**
* `FO4RecordEditor.Core/Services/WriteService.cs`, `AddListItemToRecord` (lines 246-299): resolves `field` with
  `rec.GetType().GetProperty(field, ...)` directly against the record passed in -- there is no call to
  `SplitFieldPath` or any dotted-path walk (contrast with `TrySet`, which does split on `.`/`[n]`). A call like
  `add_list_item(record=<race>, field="HeadData.Male.RacePresets", value="...")` fails with
  `No field 'HeadData.Male.RacePresets' on Race.` because the literal string is looked up as one property name.
* `FO4RecordEditor.Core/Services/WriteService.cs`, `TrySet`/`SetLeaf` (lines 2411-2486, 2516+): dotted paths
  *are* walked here, including through `[n]` list indices and `IGenderedItem<T>`'s plain `Male`/`Female`
  properties. But the last path segment is required to be a property name, not an index -- the code explicitly
  returns `"Address a field INSIDE the list element, e.g. '{field}.<Field>' -- setting a whole element by index
  isn't supported."` (line 2464) whenever the final segment is `[n]`. A bare `FormLink<T>` list element has no
  named sub-field to redirect to (only its own read-only `FormKey` property, reached by naming the FormLink
  itself, which is exactly the case being refused).
* `FO4RecordEditor.Core/Services/ElementService.cs`, `AddElement`/`CreateInstance` (lines 151-178, 376-387):
  `element_add` on one of these paths succeeds and inserts a `FormLink<T>` constructed with `FormKey.Null` -- a
  real, saved, empty entry -- but leaves it permanently null because nothing calls it back with a value.

**Expected Behavior (xEdit Parity):**
* In xEdit, right-click -> Add on any of these lists prompts you to add and immediately shows an editable
  FormID/EditorID cell for the new row; picking a record is a single, obvious step. The FO4RecordEditor MCP
  surface should let an agent reach the same end state, e.g. by making `add_list_item`'s `field` parameter
  accept the same dotted/indexed path syntax `set_field` and `element_add` already understand (so
  `add_list_item(field="HeadData.Male.RacePresets", value="0012AB:MyRace.esp")` both resolves the nested list
  *and* sets the value in one call), or by adding a narrow `element_set` tool that assigns a scalar/FormKey
  value to a list entry addressed by `path` (the one case `set_field` deliberately declines).

**Technical Specification & Required Changes:**
1. File: `FO4RecordEditor.Core/Services/WriteService.cs`
   - Extend `AddListItemToRecord` to accept a dotted/indexed `field` path (reuse `SplitFieldPath`/the hop-walking
     loop already written for `TrySet`) so it can locate a nested `IList` before doing its existing FormLink-list
     append logic.
   - Alternatively/additionally, add a small `SetListElement(rec, path, value, env)` that mirrors `SetLeaf`'s
     FormKey-resolution logic but targets `list[idx] = newFormLink` when the final path segment IS the index
     (currently the one case `TrySet` refuses).
2. File: `FO4RecordEditor/Services/Ai/PluginToolExecutor.cs`
   - Update the `add_list_item` and/or `element_add` tool descriptions once nested paths are supported, and wire
     any new `element_set`-style dispatch case.
3. Unit / Integration Tests:
   - Load a vanilla plugin with a RACE record, `element_add` a `HeadData.Male.RacePresets` entry, set its value
     via the new path, save, and reload to confirm the FormKey round-trips (not still null).

**Labels:** `record-definitions`, `xedit-parity`, `enhancement`

---

### [BUG]: RACE `BodyData` is modeled (and thus authorable) as one part per gender, not the array xEdit defines

**Issue Summary:**
xEdit defines each gender's Body Data as an *array* of `Part` structs (`INDX` + model). Mutagen's `Race.BodyData`
is `IGenderedItem<BodyData?>` -- a single `Index`+`Model` pair per gender -- and `BodyData.PartIndex` is an enum
with exactly one member, `BodyTexture`. The tool inherits this: there is no way, through any tool, to author a
second body-data part for a race, even though the on-disk format has room for one.

**Source Reference (xEdit):**
* File: `TES5Edit-dev-4.1.6/Core/wbDefinitionsFO4.pas`
* Line / Definition: `wbBodyParts := wbRArrayS('Parts', wbRStructSK([0], 'Part', [wbUnused(INDX, 0), wbGenericModel]) ...)`
  (line 11410-11422), used for both `'Male Body Data'` and `'Female Body Data'` inside the `'Body Data'` wrapper
  (lines 11583-11593). `wbRArrayS` is explicitly a variable-length array, not a fixed 0/1-element struct.

**Current Behavior (`fo4recordeditor`):**
* `Mutagen/Mutagen.Bethesda.Fallout4/Records/Major Records/Race_Generated.cs` line 464:
  `public IGenderedItem<BodyData?> BodyData { get; set; } = new GenderedItem<BodyData?>(default, default);` --
  one `BodyData?` per gender, not a list.
* `Mutagen/Mutagen.Bethesda.Fallout4/Records/Major Records/BodyData.cs`: `public enum PartIndex { BodyTexture }`
  -- only one recognized index value exists to parse or write.
* `Mutagen/Mutagen.Bethesda.Fallout4/Records/Major Records/BodyData_Generated.cs`'s
  `BodyDataBinaryCreateTranslation.FillBinaryRecordTypes` parses exactly one `INDX`/`MODL` pair per gender and
  stops (`default: return ParseResult.Stop;` on any subsequent, unrecognized record type). Because there is no
  list here, `element_add`/`element_describe` (which only ever operate on `IList`-typed properties, per
  `ElementService.SequenceElementType`) cannot reach or extend it either -- this is a model-level ceiling, not
  just a missing tool.

**Expected Behavior (xEdit Parity):**
* `Race.BodyData` should be a `GenderedItem<ExtendedList<BodyData>>` (or equivalent), parsed/written as a
  repeating `INDX`+`MODL` group per gender, matching xEdit's array. Once the model supports it, `element_add`
  would reach it generically with no further tool-specific work (it already walks `IGenderedItem`'s `Male`/
  `Female` properties and `IList` bodies today).

**Technical Specification & Required Changes:**
1. File: `Mutagen/Mutagen.Bethesda.Fallout4/Records/Major Records/BodyData_Generated.cs` (and the accompanying
   `Race_Generated.cs` / `Race.cs` custom parse/write hooks)
   - Change `Race.BodyData`'s type to a gendered list of `BodyData` entries; update
     `RaceBinaryCreateTranslation`/`RaceBinaryWriteTranslation` (in `Race.cs`) to loop over repeated `INDX`/`MODL`
     pairs per gender instead of parsing a single pair, mirroring the existing `BoneData` custom-parse pattern in
     the same file (which already loops until the marker changes).
2. Unit / Integration Tests:
   - Round-trip test loading a plugin containing a RACE record whose Male/Female Body Data legitimately has more
     than one `Part` entry, verifying no data is silently dropped on read and both entries are written back
     byte-identical.

**Labels:** `record-definitions`, `xedit-parity`, `bug`

**Note on confidence:** every vanilla FO4 RACE record checked by inspection of the Mutagen model/enum appears to
use at most one Body Data part per gender (`BodyTexture`, index 0), which is presumably why Mutagen's authors
collapsed this to a single struct. Could not load actual game/mod `.esp` files in this sandbox to confirm
whether any real record (vanilla, DLC, or a published mod) ever emits a second entry, so treat the *practical*
severity as unverified even though the *model gap itself* (array in the spec, singleton in the code) is
confirmed by direct source inspection.

### Notes / uncertain items

* **`Race.BipedObjects` (RBPC "Biped Object Conditions" + the 32 `NAME` biped-slot names) is completely
  unreachable through any tool.** It is modeled as `IReadOnlyDictionary<BipedObject, IBipedObjectDataGetter>`
  (`Race_Generated.cs`, `RaceBinaryOverlay.BipedObjects`), and every generic path-walking tool in the codebase
  (`ElementService.TryResolve`/`TryDescribe`, `WriteService.TrySet`) only recognizes `[n]` as a numeric `IList`
  index, never a dictionary key -- so a path like `BipedObjects[Torso].Conditions` does not resolve. This is
  verified, not speculative, but left out of the main gap list because per-biped-slot AVIF gating is a
  genuinely obscure, engine-internal feature rather than something most race modders reach for, unlike body
  data, head parts, movement speed, or race presets/hair colors. Flagging here in case that judgment call
  should go the other way.
* Everything else checked -- `DATA` scalar/flag/enum fields, Attacks, Movement Data Overrides, Equip Slots, Head
  Parts, Tint Layers/Morph Groups/Face Morphs (as long as their parent `HeadData` already exists, e.g. on a
  cloned vanilla race), Subgraph structural fields other than the two keyword lists, and all top-level FormLinks
  -- round-trips through the existing generic tools (`set_field` dotted paths, `element_add`/`remove`/`move`/
  `clear`, `set_conditions_at`) with no dedicated RACE tool needed.

---

## CELL

**Subrecords/fields checked:** 30 (EDID, FULL, DATA flags, VISI, RVIS, PCMB, XCLC/Grid, XCLL/Lighting [full 20+ member struct], CNAM, ZNAM, TVDT/MHDT/MaxHeightData, LTMP, XCLW, XCLR, XLCN, XWCN/XWCU/WaterVelocity, XCWT, Ownership, XILL, XILW/ExteriorLod, XWEM, XCCM, XCAS, XEZN, XCMO, XCIM, XGDR, XPRI/PhysicsReferences, XCRI/CombinedMesh*, plus the Persistent/Temporary child-group tree and NAVM/LAND children)

**Gaps found:** 1
**Confidence:** High

**Summary of method:** Read the full CELL definition at `wbDefinitionsFO4.pas:6318-6457`, then checked every subrecord against `Cell_Generated.cs`/`Cell.cs` (all are modeled with real, settable properties -- including the full `XCLL` lighting struct, `XILW` exterior LOD, `XWCN/XWCU` water-current-velocity struct, and `XPRI` as `PhysicsReferences`). Then checked the tool's write surface: `set_field` (`WriteService.cs:2411`, `TrySet`/`SetLeaf`) turned out to be a fully generic, reflection-based, dotted/indexed-path setter that initializes and writes back nested structs, resolves `FormLink`s via `SetTo`, and parses enums/scalars -- this already covers Lighting/XCLL, Water/XCLW/XCWT, ExteriorLod/XILW, Ownership, and every scalar/FormLink subrecord generically, with no CELL-specific code needed. `add_list_item` (generic FormLink-list appender) covers `XCLR` Regions and `XPRI` PhysicsReferences. `element_add`/`element_describe` (`ElementService.cs`) covers struct-list entries like `XWCU` water-current velocities. `get_record` (`MutagenLoader.QueryRecordFields` -> `WalkObject`, depth 4) is an equally generic reflection walker, so all of the above is also readable. This means the specific gaps flagged in earlier audit history (lighting XCLL, water XCLW/XCWT, acoustic space, encounter zone, music, exterior LOD) are now **closed** -- they were closed by the generic `set_field`/`add_list_item`/`element_add` machinery landing session-wide, not by CELL-specific code.

The one remaining gap is structural, not field-level: **there is no way to author a brand-new EXTERIOR cell record** (extend a worldspace's grid with a cell at coordinates not already defined by any master). Traced all three record-creation paths to confirm:
- `create_record`/`AddNewBySig` (`WriteService.cs:2342`) has no `CELL` case in its signature switch (top-level groups only).
- `create_cell` (`WriteService.Placed.cs:26`) is hard-coded interior-only: `Flags = Cell.Flag.IsInteriorCell` is unconditional, and the docstring in `PluginToolExecutor.cs:347` says "Create an interior CELL" -- exterior isn't a parameter at all.
- `create_placed_object`'s cell-override fallback (`WriteService.Placed.cs:78-88`) uses `ILinkCache.TryResolveContext<ICell,ICellGetter>` + `GetOrAddAsOverride`, which only resolves a cell that **already exists** somewhere in the load order -- it cannot originate a new one.
- `run_script`'s `host.New(sig, editorId)` (`PatchScriptHost.cs:66` -> `WriteService.Authoring.cs:25` `CreateForScript`) funnels through the same `AddNewBySig` switch, so it inherits the same gap.
- `element_add` (`ElementService.cs:151-178`) calls `Activator.CreateInstance(t)` with no constructor arguments (`ElementService.cs:376-387`); `Cell`'s only constructors require a `FormKey` (`Cell_Generated.cs:2478/2505/2512`), so even a contrived path through a `Worldspace`'s block/subblock tree would throw rather than create a valid cell.

### [Gap]: No way to author a brand-new exterior CELL (extend a worldspace's grid)

**Issue Summary:**
FO4RecordEditor can create interior cells (`create_cell`) and can override an *existing* exterior cell that some master already defines (`create_placed_object`'s cell-resolution fallback), but has no path to originate a genuinely new exterior CELL record parented into a `Worldspace`'s `WorldspaceBlock -> WorldspaceSubBlock` tree at a grid coordinate nothing currently occupies -- e.g. extending a settlement's playable area, or adding a new cell to a custom worldspace edge. xEdit supports this via its element "Add" on the worldspace's block/sub-block group.

**Source Reference (xEdit):**
* File: `wbDefinitionsFO4.pas`
* Line / Definition: `6318` (`wbRecord(CELL, 'Cell', ...)`) plus the worldspace's own `SubCells`/block-subblock group definitions that parent exterior cells (xEdit lets you right-click a `WorldspaceBlock`/`WorldspaceSubBlock` group and Add a new CELL).

**Current Behavior (`fo4recordeditor`):**
* `create_cell` unconditionally sets `Flags = Cell.Flag.IsInteriorCell` (`WriteService.Placed.cs:41`) -- there is no exterior/worldspace/grid-coordinate parameter anywhere in its signature.
* `create_placed_object`'s only exterior-cell path is `TryResolveContext<ICell,ICellGetter>` (`WriteService.Placed.cs:83`), which requires the cell to already resolve from the load order; it cannot create a cell that doesn't exist anywhere yet.
* `element_add` cannot substitute: `Activator.CreateInstance(t)` needs a parameterless constructor, and `Cell` has none.
* `run_script`'s `host.New("CELL", ...)` fails the same way `create_record` would, since `CreateForScript` -> `AddNewBySig` has no `CELL` case.

**Expected Behavior (xEdit Parity):**
* A way to create a new `Cell` (with `Flags` NOT including `IsInteriorCell`, a `Grid.Point` X/Y, and parented under the target `Worldspace`'s `WorldspaceBlock`/`WorldspaceSubBlock` tree, keyed by the standard block-number-from-grid-coordinate convention used for exterior cells) inside a plugin, analogous to how `AttachCellToBlocks` (`WriteService.Placed.cs:167`) already does this for interior `mod.Cells` blocks.

**Technical Specification & Required Changes:**
1. File: `FO4RecordEditor.Core/Services/WriteService.Placed.cs`
   - Add an overload/parameter to `CreateCell` (or a new `CreateExteriorCell(plugin, worldspaceId, gridX, gridY, editorId?, name?, env)`) that:
     - Resolves the target `Worldspace` (mutable, via the same `GetOrAddAsOverride`-style pattern `CreatePlacedObject` already uses for cells, since the worldspace itself may need overriding into this plugin first).
     - Verifies no existing cell already occupies `(gridX, gridY)` in that worldspace (reuse `CellService.ResolveExteriorCellFormKey`'s walk logic to check).
     - Allocates a `Cell` with `Grid = new CellGrid { Point = new P2Int16(gridX, gridY) }` and `Flags` without `IsInteriorCell`.
     - Parents it into the worldspace's `WorldspaceBlock`/`WorldspaceSubBlock` tree using Bethesda's real exterior block-numbering convention.
   - Expose it as a new/extended MCP tool parameter set in `PluginToolExecutor.cs` (`create_cell`'s schema already documents itself as interior-only; either branch on a new `worldspace`/`gridX`/`gridY` parameter set or add a distinct tool name).
2. Unit / Integration Tests:
   - Add a test that creates a new exterior cell at an unoccupied grid coordinate in a real worldspace (e.g. `Commonwealth`), saves, reloads, and confirms `ResolveExteriorCellFormKey` now finds it, and that the worldspace's block/sub-block tree round-trips through Mutagen's own reader without a `NotImplementedException`/`ArgumentException`.

**Labels:** `record-definitions`, `xedit-parity`, `enhancement`

---

## REFR (Placed Object Reference)

**Subrecords/fields checked:** 60+ (every subrecord in the xEdit `REFR` definition, `wbDefinitionsFO4.pas:11717-12238`, cross-checked against Mutagen's `PlacedObject`/`PlacedObject_Generated.cs` model and empirically round-tripped through the live `create_placed_object`/`set_field`/`add_list_item`/`get_record` MCP tools)

**Gaps found:** 2 (one confirmed data-loss bug, one ergonomic/parity gap with a working manual workaround)
**Confidence:** High -- verified two ways: (1) static read of `wbDefinitionsFO4.pas` REFR block vs. `PlacedObject_Generated.cs`'s member list (both are near 1:1 -- Mutagen models essentially the entire xEdit REFR field surface); (2) a live empirical test against the running `fo4editor` MCP server: created a scratch plugin (`REFR_Audit_Test.esp`, never saved to disk), a cell, and a REFR, then wrote and `get_record`-verified ~20 fields spanning scalars, FormLinks, and 1-3-level-deep nested structs (`Primitive.Bounds.X`, `TeleportDestination.Door`, `Lock.Level`, `Ownership.Owner`, `ActivateParents.Flags`, `Radio.Frequency`, `Rotation.X/Y`, etc.) -- all round-tripped correctly except the one bug below.

### [BUG]: `set_field` cannot write any `Percent`-typed field, including REFR's `XHLT` (Health %)

**Issue Summary:**
`HealthPercent` (the REFR `XHLT` subrecord, "Health %" in xEdit) is modeled correctly by Mutagen (`PlacedObject_Generated.cs:540`, `public Percent? HealthPercent { get; set; }`), but `set_field`'s scalar-type conversion ladder in `WriteService.cs` (`SetLeaf`) has no case for `Noggog.Percent`, so the field cannot be written through any tool. `create_placed_object` also has no parameter for it. This was confirmed live, not just read from source: `set_field REFR_Audit_Test.esp REFRAuditRef01 HealthPercent 0.5` returned `Field 'HealthPercent' has type Percent, which set_field can't set yet (scalar/text only).`

**Source Reference (xEdit):**
* File: `TES5Edit-dev-4.1.6/Core/wbDefinitionsFO4.pas`
* Line / Definition: line 12061, `wbInteger(XHLT, 'Health %', itU32)`

**Current Behavior (`fo4recordeditor`):**
* `Mutagen/Mutagen.Bethesda.Fallout4/Records/Major Records/PlacedObject_Generated.cs:540` models the field correctly as `Percent? HealthPercent`.
* `FO4RecordEditor.Core/Services/WriteService.cs`'s `SetLeaf` (~line 2516-2578) has an explicit type ladder (`string`, `TranslatedString`, enum, `bool`, `float`, `double`, `int`, `uint`, `short`, `ushort`, `byte`, `long`, `System.Drawing.Color`) that falls through to `"Field '{field}' has type {t.Name}, which set_field can't set yet (scalar/text only)."` for anything else -- `Percent` is not one of the handled cases. Verified this is not a REFR-only gap: `Percent` IS used elsewhere in the codebase (`WriteService.Authoring.cs:54`, leveled-list "chance none"), but only via a bespoke hand-built `new Percent(value / 100.0)` call in that one authoring path -- never through the generic `set_field`/`SetLeaf` machinery. So every `Percent`-typed field on every record type is unreachable through `set_field`, not just REFR's `HealthPercent`.
* `create_placed_object` (`WriteService.Placed.cs`) has no `healthPercent` parameter either, so there is genuinely no way to author this field on a newly created REFR at all.

**Expected Behavior (xEdit Parity):**
* xEdit exposes `XHLT` as a plain editable percentage/integer field on any REFR. A user (or the AI) should be able to set REFR Health % the same way they can set `ItemCount` or `CollisionLayer`.

**Technical Specification & Required Changes:**
1. File: `FO4RecordEditor.Core/Services/WriteService.cs`
   - In `SetLeaf`, add a case for `t == typeof(Noggog.Percent)` before the fallback `else` branch, e.g. `converted = new Percent(double.Parse(value, Inv) / 100.0);` (matching the existing `WriteService.Authoring.cs:54` convention of accepting a 0-100 input) or `Percent.FactoryPutInRange(...)` if a normalized-0-1 input is preferred -- pick one convention and document it, since xEdit displays `XHLT` as a raw 0-100 value.
2. Unit / Integration Tests:
   - Add a test that creates a REFR, calls `set_field` with `HealthPercent`/`50`, and asserts `get_record` reports back a `Percent` matching 0.5 (50%).

**Labels:** `record-definitions`, `xedit-parity`, `bug`

---

### [ENHANCEMENT]: `create_placed_object` still only accepts a single Z-axis rotation parameter

**Issue Summary:**
`create_placed_object`'s parameter list is `x, y, z, rotZ` -- there is no `rotX`/`rotY`. The underlying Mutagen `Rotation` property (`P3Float`) and the full 3-axis DATA rotation xEdit exposes are both fully modeled and, in practice, fully writable -- but only via two follow-up `set_field Rotation.X` / `set_field Rotation.Y` calls after creation, which is undocumented in `KNOWLEDGE.md`'s `create_placed_object` section and easy for an agent to miss (project memory already flagged this exact file/function as rotZ-only from an earlier session, and it is confirmed still true today).

**Source Reference (xEdit):**
* File: `TES5Edit-dev-4.1.6/Core/wbDefinitionsFO4.pas`
* Line / Definition: line 12236, `wbDataPosRot` (the DATA subrecord: `Position` + full `Rotation` X/Y/Z, shared macro `wbPosRot`)

**Current Behavior (`fo4recordeditor`):**
* `FO4RecordEditor.Core/Services/WriteService.Placed.cs:118-119`: `Position = new P3Float(x, y, z), Rotation = new P3Float(0f, 0f, rotZ)` -- X and Y rotation are hard-coded to `0f` at creation time with no parameter to set them.
* Confirmed live that the *data model* has no restriction: after creation, `set_field REFRAuditRef01 Rotation.X 15` and `set_field REFRAuditRef01 Rotation.Y 25` both succeeded and `get_record` showed `Rotation: {X: 15, Y: 25, Z: 1.5}` -- so this is a tool-ergonomics/API-completeness gap, not a data-loss bug. A user CAN get full 3-axis rotation, just not in one call, and `KNOWLEDGE.md`'s `create_placed_object` doc block (lines 782-786) doesn't mention the follow-up `set_field` step is required for anything beyond Z.

**Expected Behavior (xEdit Parity):**
* xEdit lets you set full X/Y/Z rotation on a reference's DATA subrecord directly when placing it; no separate action is needed for the other two axes.

**Technical Specification & Required Changes:**
1. File: `FO4RecordEditor.Core/Services/WriteService.Placed.cs`
   - Extend `CreatePlacedObject`'s signature with optional `rotX`/`rotY` (default `0f`, preserving current callers) and use them in `Rotation = new P3Float(rotX, rotY, rotZ)`.
2. File: `FO4RecordEditor/Services/Ai/PluginToolExecutor.cs`
   - Add the corresponding optional `rotX`/`rotY` parameters to the `create_placed_object` tool schema/dispatch.
3. Docs: `docs/internal/KNOWLEDGE.md`'s `create_placed_object` section (~line 774-816) -- update the signature example and, until fixed, explicitly document the `Rotation.X`/`Rotation.Y` `set_field` follow-up as the current workaround.
4. Unit / Integration Tests:
   - Extend whatever test currently covers `CreatePlacedObject` to assert all three rotation axes round-trip when supplied.

**Labels:** `xedit-parity`, `enhancement`

### Notes / uncertain items

- **`add_list_item` cannot append to `LinkedReferences`** (a list of a 2-FormLink struct, `Keyword/Ref` + `Ref`) -- confirmed live (`Field 'LinkedReferences' is a list of LinkedReferences; add_list_item supports FormLink lists ... for now.`). Not counted as a gap: `element_add`/`ElementService.cs` is the documented, by-design path for exactly this case. Could not empirically confirm `element_add` against `LinkedReferences` specifically because the currently running MCP server session was a stale build (`element_add`/`element_describe`/`element_remove`/`element_move`/`element_clear` and several other documented tools were not present in the live tool list even though they exist in source) -- an environment/deployment staleness issue, not a code gap.
- **`XCVR`/`XCVL`** ("Water Current Zone Data", 3 unnamed floats each) were not individually round-tripped -- xEdit itself only labels their fields "Unknown", suggesting these are vestigial/unused in practice; low value to verify further.
- **`Patrol` is modeled as a single nullable object** (`IPatrolGetter? Patrol`), not a list, while xEdit defines it as `wbRArray('Patrol', ...)`. Not flagged as a gap: real FO4 REFRs carry at most one Patrol block in practice, and Mutagen's singular modeling matches observed vanilla data.

---

## QUST (Quest)

**Subrecords/fields checked:** 70+ (DNAM, ENAM, LNAM, XNAM, QTGL, FLTR, dialogue/story-manager Conditions,
Stages/INDX/QSDT/Log Entries/NAM2/CNAM/NAM0, Objectives/QOBJ/FNAM/NNAM, Targets/QSTA/Keyword, ANAM,
3 Alias subtypes with ~30 combined subrecords: ALST/ALLS/ALCS/ALMI/ALID/FNAM(alias flags,25 bits)/ALFI/
ALFR/ALUA/ALFA/KNAM/ALRT/ALEQ/ALEA/ALCO/ALCA/ALCL/ALNA/ALNT/ALFE/ALFD/ALCC/Conditions/Keywords/COCT/CNTO/
SPOR/OCOR/GWOR/ECOR/ALLA/ALDN/ALFV/ALDI/ALSP/ALFC/ALPC/VTCK/ALED/ALFL, NNAM, GNAM, SNAM)

**Gaps found:** 3
**Confidence:** High

Mutagen's C# model (`Quest.cs`/`Quest_Generated.cs`, `AQuestAlias.cs`/`_Generated.cs`,
`QuestReferenceAlias(_Generated).cs`, `QuestLocationAlias_Generated.cs`, `QuestCollectionAlias_Generated.cs`,
`CollectionAlias_Generated.cs`, `QuestStage(_Generated).cs`, `QuestLogEntry(_Generated).cs`,
`QuestObjective(_Generated).cs`, `QuestObjectiveTarget_Generated.cs`) is a COMPLETE, faithful port of the
xEdit `wbDefinitionsFO4.pas` QUST definition -- every subrecord, struct, and flag/enum bit traced in
xEdit (including all 25 `wbQUSTAliasFlags` bits, all 3 alias union subtypes, `QuestStage.Flag`,
`QuestLogEntry.Flag` incl. `FailQuest`, `QuestObjective.Flag`, `Quest.TargetFlag`) has a real property.
`AliasIDToForceIntoWhenFilled`, the six sub-structs on Reference Alias (`Location`/`External`/
`CreateReferenceToObject`/`FindMatchingRefNearAlias`/`FindMatchingRefFromEvent`), `ClosestToAlias`,
`Keywords`, `Items` (COCT/CNTO), the four override package lists, `LinkedAliases`, `DisplayName`,
`ForcedVoice`, `DeathItem`, `Spells`, `Factions`, `PackageData`, `VoiceTypes` are all present. ANAM
("Next Alias ID") is deliberately not a settable property -- it's computed as `max(alias IDs)+1` on
write in `Quest.cs`'s `WriteBinaryAliasParseCustom`, which is a correct, lossless simplification, not
a gap.

**The gaps are entirely in the tool's dedicated struct-authoring surface** (`set_quest_aliases`,
`set_quest_stages`, `set_quest_objectives` in `FO4RecordEditor.Core/Services/WriteService.Authoring.cs`
lines 254-352, schemas in `FO4RecordEditor/Services/Ai/PluginToolExecutor.cs` lines 938-985), which
expose only a small hand-picked subset of what Mutagen models. The generic `element_add` (backed by
`FO4RecordEditor.Core/Services/ElementService.cs`'s `TemplatesFor`, which reflects over concrete
subclasses of an abstract element type) plus `set_field`'s dotted/indexed path support
(`WriteService.cs` `TrySet`) CAN reach most of the missing fields as a workaround -- e.g.
`element_add` on `Aliases` does offer `QuestLocationAlias`/`QuestCollectionAlias`/`QuestReferenceAlias`
as templates, and `set_conditions_at` can target `Aliases[0].Conditions` or
`Stages[0].LogEntries[0].Conditions` once an entry exists -- but this is undocumented, clunky
(15+ manual `set_field` calls per alias), and not what the tool's own docs/AI guidance point an agent
to. Flagged as functional gaps in the *dedicated* tool per the audit brief.

### [FUNCTIONAL-GAP]: set_quest_aliases can only build Reference Aliases, and only 2 of ~20 of their fields

**Issue Summary:**
`set_quest_aliases` always constructs a `QuestReferenceAlias` (`WriteService.Authoring.cs:263-280` --
`var a = new QuestReferenceAlias { ... }` is hardcoded); there is no way to select `QuestLocationAlias`
or `QuestCollectionAlias` (xEdit's "Location Alias" / "Ref Collection Alias") through this tool's JSON
schema. Even for the one subtype it does build, the schema only reads `id`, `name`, `forcedReference`,
`uniqueActor`, and `flags` -- every other field Mutagen exposes on `QuestReferenceAlias` is silently
never set.

**Source Reference (xEdit):**
* File: `wbDefinitionsFO4.pas`
* Line / Definition: `wbRecord(QUST, ...)` alias union at lines 11191-11311 -- three
  `wbRStructSK([0], 'Alias', [...])` arms keyed on `ALST`/`ALLS`/`ALCS`. Reference Alias struct fields:
  lines 11195-11267 (`ALFI`, `ALFR`, `ALUA`, "Location Alias Reference" `ALFA/KNAM/ALRT`, "External
  Alias Reference" `ALEQ/ALEA`, "Create Reference to Object" `ALCO/ALCA/ALCL`, "Find Matching Reference
  Near Alias" `ALNA/ALNT`, "Find Matching Reference From Event" `ALFE/ALFD`, `ALCC`, Conditions,
  Keywords, `COCT`/`CNTO`, `SPOR/OCOR/GWOR/ECOR`, `ALLA`, `ALDN`, `ALFV`, `ALDI`, `ALSP`, `ALFC`,
  `ALPC`, `VTCK`). Location Alias struct: lines 11270-11297. Ref Collection Alias struct:
  lines 11300-11308.

**Current Behavior (`fo4recordeditor`):**
* `set_quest_aliases` (schema: `PluginToolExecutor.cs:938-953`, impl: `WriteService.Authoring.cs:254-289`)
  always emits `QuestReferenceAlias` objects with only `ID`, `Name`, `ForcedReference`, `UniqueActor`,
  `Flags` populated.
* `AliasIDToForceIntoWhenFilled`, `Location`, `External`, `CreateReferenceToObject`,
  `FindMatchingRefNearAlias`, `FindMatchingRefFromEvent`, `ClosestToAlias`, `Keywords`, `Items`,
  `SpectatorOverridePackageList`/`ObserveDeadBodyOverridePackageList`/`GuardWarnOverridePackageList`/
  `CombatOverridePackageList`, `LinkedAliases`, `DisplayName`, `ForcedVoice`, `DeathItem`, `Spells`,
  `Factions`, `PackageData`, `VoiceTypes` are never touched -- an agent authoring a "find nearest actor
  of faction X and route its death item to alias" pattern, or a location-based alias, cannot do it
  through this tool at all.
* `QuestLocationAlias` / `QuestCollectionAlias` cannot be produced by this tool under any input.

**Expected Behavior (xEdit Parity):**
* Accept a `kind` (or infer from which key is present: `location`/`collection`) discriminator and build
  the matching Mutagen subtype.
* Accept the full Reference Alias field set listed above (at minimum: `aliasIdToForceIntoWhenFilled`,
  `location:{alias,keyword,refType}`, `external:{quest,alias}`, `createReferenceToObject:{object,alias,
  create,level}`, `findMatchingRefNearAlias:{alias,type}`, `findMatchingRefFromEvent:{fromEvent,eventData}`,
  `closestToAlias`, `keywords[]`, `deathItem`, `voiceTypes`, `spells[]`, `factions[]`).

**Technical Specification & Required Changes:**
1. File: `FO4RecordEditor.Core/Services/WriteService.Authoring.cs`
   - In `SetQuestAliases`, branch on a new `kind`/type discriminator to construct `QuestReferenceAlias`,
     `QuestLocationAlias`, or `QuestCollectionAlias` (the last needs its own `collection` array of
     `{aliasId, maxInitialFillCount}` per `CollectionAlias_Generated.cs`).
   - Wire up the remaining `QuestReferenceAlias`/`QuestLocationAlias` fields enumerated above.
2. File: `FO4RecordEditor/Services/Ai/PluginToolExecutor.cs`
   - Extend the `set_quest_aliases` tool description/schema (lines 938-953) to document the new fields
     and the alias-kind discriminator.
3. Unit / Integration Tests:
   - Add a test that round-trips a vanilla FO4 plugin QUST record containing at least one Location
     Alias and one Ref Collection Alias through `set_quest_aliases`/save/reload, verifying byte-exact
     `ALLS`/`ALCS`/`ALMI` output.

**Labels:** `record-definitions`, `xedit-parity`, `enhancement`

---

### [FUNCTIONAL-GAP]: set_quest_stages supports only one Log Entry per stage and drops Note/NextQuest/FailQuest

**Issue Summary:**
xEdit's Stages -> Log Entries is a real array (a stage can carry several journal-text variants gated by
different Conditions), and each Log Entry has `Note` (NAM2), `Entry`/CNAM text, `Conditions`, and
`NextQuest` (NAM0). `set_quest_stages` hardcodes at most one `QuestLogEntry` per stage and only ever
sets `Entry` and (conditionally) the `CompleteQuest` flag bit.

**Source Reference (xEdit):**
* File: `wbDefinitionsFO4.pas`
* Line / Definition: `wbRArray('Log Entries', wbRStruct('Log Entry', [...]))`, lines 11134-11149:
  `QSDT` (Stage Flags: `Complete Quest`/`Fail Quest`), `wbConditions`, `NAM2` ('Note'), `CNAM`
  ('Log Entry' text), `NAM0` ('Next Quest').

**Current Behavior (`fo4recordeditor`):**
* `SetQuestStages` (`WriteService.Authoring.cs:293-320`): for each stage element, if a `logEntry`
  string is present it creates exactly one `QuestLogEntry { Entry = leStr }`, and if `complete:true` is
  passed it sets `Flags = CompleteQuest` via `TrySetNullableEnumFlags`. There is no `notes`/array-of-
  entries input, and `FailQuest`, `Note`, and `NextQuest` are never reachable through this schema --
  a stage cannot have more than one log entry, and there is no way to chain `NAM0` into a follow-on
  quest or mark a stage-failure entry.

**Expected Behavior (xEdit Parity):**
* `stages[].logEntries` should be an array, each with `{text, note?, nextQuest?, complete?, fail?,
  conditions?}`, producing one `QuestLogEntry` per array element (mirroring xEdit's real Log Entries
  array), with `QuestLogEntry.Flag` set from `complete`/`fail` (both bits exist:
  `CompleteQuest = 0x01`, `FailQuest = 0x02` in `QuestLogEntry.cs`).

**Technical Specification & Required Changes:**
1. File: `FO4RecordEditor.Core/Services/WriteService.Authoring.cs`
   - Replace the single `logEntry` string with a `logEntries` JSON array in `SetQuestStages`; for each
     element populate `Entry`, `Note`, `NextQuest` (via `ResolveFk`), and `Flags` from `complete`/`fail`
     booleans; optionally accept inline `conditions` per entry (reusing `BuildConditionFromJson`).
2. File: `FO4RecordEditor/Services/Ai/PluginToolExecutor.cs`
   - Update the `set_quest_stages` schema/description (lines 955-969) accordingly; keep the old
     `logEntry` key working as sugar for a single-element `logEntries` array to avoid breaking existing
     callers.
3. Unit / Integration Tests:
   - Load a vanilla plugin with a multi-log-entry stage (e.g. a quest stage with both a normal and a
     `FailQuest` log entry) and verify `set_quest_stages` round-trips both entries bit-exact.

**Labels:** `record-definitions`, `xedit-parity`, `enhancement`

---

### [FUNCTIONAL-GAP]: set_quest_objectives never builds the Targets array (QSTA), so objectives have no alias/compass target

**Issue Summary:**
An Objective's `Targets` array is what ties an on-screen objective to a specific quest alias (for the
compass marker / map target) with its own flags and Conditions. `set_quest_objectives`'s schema has no
`targets` input at all -- `QuestObjective.Targets` is left as an empty `ExtendedList`, so every
objective authored through this tool is compass/map-target-less by construction.

**Source Reference (xEdit):**
* File: `wbDefinitionsFO4.pas`
* Line / Definition: `wbRArray('Targets', wbRStruct('Target', [wbStruct(QSTA, 'Target', [...]),
  wbConditions]))`, lines 11162-11181: `Alias` (int, alias index), `Flags` (`Compass Marker Ignores
  Locks` / `Hostile` / `Use Straight Line Pathing`), `Keyword` (FromVersion 82), plus per-target
  Conditions.

**Current Behavior (`fo4recordeditor`):**
* `SetQuestObjectives` (`WriteService.Authoring.cs:329-352`) builds `QuestObjective { Index,
  DisplayText, Flags }` only; `ob.Targets` is never assigned or appended to, matching
  `QuestObjectiveTarget_Generated.cs`'s `AliasID`/`Flags`/`Keyword`/`Conditions`/`QSTADataTypeState`
  properties, none of which are reachable from this tool's schema.

**Expected Behavior (xEdit Parity):**
* `objectives[].targets` should accept an array of `{alias, flags?, keyword?, conditions?}`, each
  producing a `QuestObjectiveTarget` with `AliasID` (int, matching an alias's `ID` on the same quest),
  `Flags` (`Quest.TargetFlag`: `CompassMarkerIgnoresLocks`/`Hostile`/`UseStraightLinePathing`),
  optional `Keyword` FormLink, and optional Conditions.

**Technical Specification & Required Changes:**
1. File: `FO4RecordEditor.Core/Services/WriteService.Authoring.cs`
   - In `SetQuestObjectives`, parse an optional `targets` array per objective element and populate
     `ob.Targets` with `QuestObjectiveTarget` instances (`AliasID` from `alias`, `Flags` via
     `TrySetNullableEnumFlags`/direct enum parse, `Keyword` via `ResolveFk`, `Conditions` via
     `BuildConditionFromJson`).
2. File: `FO4RecordEditor/Services/Ai/PluginToolExecutor.cs`
   - Extend the `set_quest_objectives` schema/description (lines 970-984) to document `targets`.
3. Unit / Integration Tests:
   - Round-trip a vanilla FO4 quest whose objective has a `QSTA` target with a non-zero `Flags` value
     and a `Keyword`, verifying byte-exact `QSTA` output through `set_quest_objectives`.

**Labels:** `record-definitions`, `xedit-parity`, `enhancement`

### Notes / uncertain items

- Whether `element_add` + `set_field` genuinely lets an agent build a full `QuestLocationAlias`/
  `QuestCollectionAlias` today was confirmed by reading `ElementService.cs`'s `TemplatesFor` and
  `WriteService.cs`'s `TrySet`/`SplitFieldPath` -- not by actually invoking the MCP tools end-to-end
  against a live plugin. High confidence based on code reading, but not runtime-verified.
- `ANAM` ("Next Alias ID") not being independently settable is intentional/correct (computed on write)
  and is called out above as NOT a gap.

---

## PROJ (Projectile)

**Subrecords/fields checked:** 34
**Gaps found:** 1
**Confidence:** High

### [GAP]: create_record (and run_script's host.New) cannot author a new PROJ, EXPL, or HAZD record

**Issue Summary:**
`create_record`'s type switch -- the tool's only path for making a brand-new top-level record from
scratch -- has no case for `PROJ` (Projectile), and neither do its two closest companions, `EXPL`
(Explosion) and `HAZD` (Hazard), both of which a typical grenade/missile/trap PROJ references via
its `DNAM.Explosion` FormLink or a spawned Hazard. xEdit lets you right-click any record group and
create a blank record of any type, including PROJ/EXPL/HAZD; this tool cannot.

**Source Reference (xEdit):**
* File: `wbDefinitionsFO4.pas`
* Line / Definition: `wbRecord(PROJ, 'Projectile', [...])` at line 7460; `wbRecord(HAZD, 'Hazard', [...])`
  at line 7525; `wbRecord(EXPL, 'Explosion', [...])` at line 7679. xEdit has no allowlist for which
  record signatures can be freshly created -- any defined `wbRecord` type can be added via the group
  context menu.

**Current Behavior (`fo4recordeditor`):**
* `FO4RecordEditor.Core/Services/WriteService.cs`, `AddNewBySig(IFallout4Mod mod, string sig, ...)`
  (switch starting at line 2345) enumerates ~30 supported signatures (`BOOK/HOLOTAPE, TERM, WEAP,
  ARMO, ARMA, ANIO, IDLE, MISC, COBJ, KYWD, AMMO, ALCH, ACTI, CONT, FLST, MGEF, PERK, NPC_, QUST,
  MESG, GLOB*, AVIF, LVLI, LVLN, SPEL, ENCH, FURN, IMAD, LIGH, STAT`) and falls through to
  `default: return null` for anything else -- `PROJ`, `EXPL`, and `HAZD` are all absent.
* `create_record`'s MCP tool description (`FO4RecordEditor/Services/Ai/PluginToolExecutor.cs` line
  332) explicitly enumerates the same allowlist to the calling AI agent, so an agent will either be
  told up front it's unsupported or will call it and get a null/failure result.
* `run_script`'s scripting escape hatch (`host.New(sig, editorId)` in `PatchScriptHost.cs` line 66)
  was added specifically so scripts could create record types `create_record` doesn't cover, but it
  calls `WriteService.CreateForScript` (`WriteService.Authoring.cs` line 25), which itself just calls
  the same `AddNewBySig` -- so it fails identically with "unsupported signature" for PROJ/EXPL/HAZD.
  There is no code path anywhere in the tool that constructs a `new Projectile(...)`,
  `new Explosion(...)`, or `new Hazard(...)` and adds it to `mod.Projectiles`/`mod.Explosions`/
  `mod.Hazards`.
* Grepping the whole `FO4RecordEditor/Services/Ai/PluginToolExecutor.cs` and every
  `FO4RecordEditor.Core/Services/WriteService*.cs` file for `PROJ`/`Projectile` (case-sensitive and
  case-insensitive) returns zero hits outside the Mutagen library itself -- there is no
  projectile-specific tool of any kind (no `set_projectile_*`, no special-case authoring helper),
  unlike LIGH/STAT which got dedicated `AddNewBySig` cases specifically to close this same class of
  gap for those types.
* All 25 scalar/FormLink fields inside the `DNAM` struct (Flags, Type, Gravity, Speed, Range, Light,
  MuzzleFlash-Light, Explosion-Alt.-Trigger Proximity/Timer, Explosion, Sound, MuzzleFlashDuration,
  FadeDuration, ImpactForce, CountdownSound, DisaleSound, DefaultWeaponSource, ConeSpread,
  CollisionRadius, Lifetime, RelaunchInterval, DecalData, CollisionLayer, TracerFrequency,
  VATSProjectile), plus `NAM1`/`MuzzleFlashModel` (string) and `VNAM`/`SoundLevel` (raw uint),
  **are** reachable with plain, non-dotted `set_field` calls once a Projectile record exists --
  Mutagen's generated `Projectile` class flattens the whole `DNAM` struct directly onto the record
  (`Mutagen/Mutagen.Bethesda.Fallout4/Records/Major Records/Projectile_Generated.cs`, e.g.
  `public Single Gravity { get; set; }` at line 169), it is not modeled as a nested `Data` sub-object.
  So the only real capability gap for this record type is creating it in the first place, not
  editing its fields once it exists.

**Expected Behavior (xEdit Parity):**
* `create_record(plugin, "PROJ", editorId)` should add an empty `Projectile` to `mod.Projectiles`
  with a valid FormKey, the same way `create_record(plugin, "LIGH", editorId)` already does for
  `Light`.
* Same for `EXPL` -> `mod.Explosions` and `HAZD` -> `mod.Hazards`, since a from-scratch PROJ is very
  commonly built together with a matching Explosion (grenades/missiles) or Hazard (mines/traps) that
  its own `DNAM.Explosion` field references.

**Technical Specification & Required Changes:**
1. File: `FO4RecordEditor.Core/Services/WriteService.cs`
   - In `AddNewBySig` (around line 2396, next to the existing `LIGH`/`STAT` cases), add:
     ```csharp
     case "PROJ": { var x = new Projectile(fk, r) { EditorID = editorId };  mod.Projectiles.Add(x);  return x; }
     case "EXPL": { var x = new Explosion(fk, r)  { EditorID = editorId };  mod.Explosions.Add(x);   return x; }
     case "HAZD": { var x = new Hazard(fk, r)     { EditorID = editorId };  mod.Hazards.Add(x);      return x; }
     ```
   - No new struct-list authoring helper is needed for the DNAM fields themselves (see above --
     ordinary `set_field` already reaches every scalar/FormLink DNAM member once the record exists).
     A new record will still ship with a zeroed `Type` (0, not a named `TypeEnum` value, since
     `Missile` = `0x01` is the lowest defined bit) and zero `ObjectBounds` -- callers should be told to
     follow up with `set_field <rec> Type Missile` (or another named type) and either accept
     zero-bounds or clone-and-renumber a vanilla PROJ instead.
2. File: `FO4RecordEditor/Services/Ai/PluginToolExecutor.cs`
   - Update the `create_record` tool description string (line 332) to add `PROJ, EXPL, HAZD` to the
     advertised "Supported types" list so the AI agent knows the capability exists.
3. Unit / Integration Tests:
   - Add a test that calls `create_record` (or `WriteService.CreateForScript`) with `"PROJ"`, asserts
     the returned record is a `Projectile` added to `mod.Projectiles`, then round-trips a `save_plugin`
     / reload and confirms the `PROJ` record and a `set_field`-written `Type`/`Speed`/`Range` survive
     bit-exact. Repeat for `EXPL` and `HAZD`.

**Labels:** `record-definitions`, `xedit-parity`, `enhancement`

### Notes / uncertain items

- **`VNAM` (Sound Level) is modeled as a raw `UInt32`, not a C# enum**, in
  `Projectile_Generated.cs` (`public UInt32 SoundLevel { get; set; }`), whereas xEdit shows it through
  `wbSoundLevelEnum` (named values like `Loud`/`Normal`/`Silent`/`VeryLoud`). `set_field` can still
  write a numeric value to it (the `uint` branch in `SetLeaf`), so this is not a blocked capability,
  just a discoverability wrinkle specific to how Mutagen generated this one field -- not counted as a
  formal gap.
- **`NAM2` (`TextureFilesHashes`, the muzzle-flash model's texture-hash blob) is a raw
  `MemorySlice<Byte>?`** and is not one of the scalar types `SetLeaf` can convert, so `set_field`
  cannot write it directly. In practice this field is a cache/checksum of referenced texture
  filenames, not something xEdit users hand-edit either, and it round-trips fine when the record is
  only edited through other fields. Not counted as a functional gap.
- **`DEST` (Destructible) was checked and is NOT a gap.** It looked like a candidate, but the
  project's generic xEdit-parity "element menu" (`element_add`/`element_remove`/`element_move`/
  `element_clear`) plus ordinary `set_field` on the new element's scalar sub-fields already covers
  authoring `Destructible.Stages` from scratch. This is a cross-record capability (Destructible is
  shared by many record types, not PROJ-specific).
- Did not attempt to independently verify whether the ObjectBounds/OBND struct-write-back bug
  documented in `docs/internal/KNOWLEDGE.md` ("set_field CANNOT set ObjectBounds -- and the dotted
  form LIES about it", dated 2026-07-16) is still live. Reading the current `TrySet` implementation
  in `WriteService.cs` shows code that appears to specifically fix that exact bug -- but this was not
  empirically re-tested against a live plugin, and OBND is a cross-record concern anyway, not
  specific to PROJ.
