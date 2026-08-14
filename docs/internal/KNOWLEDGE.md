# PluginEditTool Knowledge Base

Knowledge accumulated across sessions. Read this before doing any plugin editing work.

> **Location (2026-07-19):** this doc lives at `E:\F4SE OG\Tools\PluginEditTool\KNOWLEDGE.md`,
> beside the tool it documents — the same folder as `CLAUDE.md`, which points here. It briefly sat
> in the central docs hub (`docs\Plugin Tool\`) and then in `Tools\PluginEditTool\docs\`; both are
> gone. Every Plugin Tool doc is now flat in this one folder (`INDEX.md`, `ARCHITECTURE.md`,
> `CONFLICT_ENGINE.md`, `NIF_TOOLCHAIN.md`, ...) with no `docs\` subfolder and no redirect stubs.
> The **working tool** — FO4RecordEditor app + its vendored local Mutagen source — is in this same
> folder. Build/run the editor from here.
> The editor now ProjectReferences the **local** Mutagen source, so library fixes (see the
> 2026-06-30 perk/enum entries below) take effect on editor rebuild. Most recent driving use case:
> writing the Wasteland Hunter activate-choice PERK (see `docs\WastelandHunter\PROGRESS.md`).

> **FO4 reference:** an offline mirror of the Creation Kit Wiki (creationkit.com/fallout4, ~3055
> pages) is at `E:\F4SE OG\docs\Knowledge Materials\Creation Kit Wiki\...\FO4CKWiki_210520\fallout4\`.
> Grep/Read it before guessing vanilla FO4 Papyrus function signatures, events/timers, record &
> subrecord (TES4) layouts, perk entry points, or magic-effect archetypes. F4SE-added functions are
> NOT there (see f4se source + Garden of Eden docs). Authoritative for vanilla CK/Papyrus behavior.

---

## FO4RecordEditor MCP Tool

The primary tool for reading and modifying plugin records without opening xEdit.

### Launch Path (CRITICAL)

**Always launch from:**
```
E:\F4SE OG\Tools\PluginEditTool\FO4RecordEditor\FO4RecordEditor\bin\Release\net9.0-windows\FO4RecordEditor.exe
```

Do NOT launch from any of these (stale builds, wrong runtime target):
- `bin\Release\net9.0-windows\win-x64\FO4RecordEditor.exe` (June 12, outdated)
- `bin\Release\net9.0-windows\win-x64\publish\FO4RecordEditor.exe` (June 12, outdated)
- `bin\Debug\net9.0-windows\FO4RecordEditor.exe` (debug build)

After running `publish.bat`, the fresh build is also deployed to:
`E:\Modlists\Fallen World Alpha 2\Tools\FO4Editor\FO4RecordEditor.exe`

### Type Names

The tool uses full English names, not TES4 record codes (for `list_records`/`list_records_summary`):

| TES4 Code | Tool Name |
|-----------|-----------|
| COBJ | `ConstructibleObject` |
| KYWD | `Keyword` |
| BOOK | `Book` |
| PERK | `Perk` |
| LVLI | `LeveledItem` |

Using `COBJ` in `list_records` returns "No records of that type" -- always use the full name.

**`create_record` uses TES4 SIGNATURES** (not full names). Supported (extended 2026-06-29/30):
BOOK/HOLOTAPE, TERM, WEAP, ARMO, MISC, COBJ, KYWD, AMMO, ALCH, ACTI, CONT, FLST, MGEF, PERK, NPC_,
QUST, MESG, and **GLOB** (defaults to float; `GLOBINT`/`GLOBSHORT`/`GLOBBOOL` variants), **AVIF**,
**LVLI**, **LVLN**, **SPEL**, **ENCH**, **FURN**, **IMAD**. Set a global's value with `set_field` on
`Data`. New records are empty shells — fill them with `set_field` and the struct-list tools below.

### Workflow Pattern

```
open_plugin → list_records → get_record → set_field → save_plugin → check_plugin
```

- `open_plugin` is required before any read or write on a plugin
- `save_plugin` writes back to the original file it was opened from (no separate output path needed for modlist plugins)
- Always run `check_plugin` after saving to verify no dangling references or deleted records

### Stale Masters Cannot Be Removed by Saving

If a plugin has a master listed in its TES4 header that no active records actually reference (stale/orphaned master), calling `save_plugin` does NOT remove it. The editor does not recompute masters on save.

Fix: use `Patched Data\Source\strip_stale_master.py` to surgically remove the MAST+DATA subrecord pair from the binary TES4 header. The script patches the TES4 data-size field to match and writes a `.bak` backup before modifying.

For **ITO-style master-index corruption** (where binary surgery shifted master indices but left condition FormID high-bytes pointing at old positions), use the MCP tool `strip_masters_clean` instead -- it loads the .bak via Mutagen, drops conditions that reference the stripped masters, and lets Mutagen recompute all indices on save.

**Why binary surgery corrupts (the mechanism — this is why `strip_masters_clean` exists).** The old
`Patched Data\Source\fix_ito_conditions.py` removed ITO CTDA conditions and the ITO MAST+DATA header entries, but did NOT
re-index the FormID high bytes of the *remaining* conditions. Any condition referencing a master at an
index **above** a removed one silently repoints. Worked example: a `GetGlobalValue([5600009A])`
condition resolved to `Stalker Suit.esp` (index `0x56` after the strip) instead of
`RadiationOverhaul.esp`. Nothing errors — the plugin just means something else now.

The Mutagen route cannot have this bug by construction: it resolves raw FormID bytes to typed
`FormKey` objects **on load** using the declared master list, so the links survive the edit
symbolically; on save it recomputes the master list and re-serializes correct FormID bytes. Never
hand-edit master indices in a binary again.

**Verified non-bug — S7 null `GetBaseValue` in xEdit.** Binary inspection confirmed this is NOT a data
problem: xEdit simply needs `S7 System.esp` loaded to resolve the custom ActorValue. No code change
needed. Recorded here so it does not get "fixed" a third time. (Distinct from the real
Mutagen-serialization null-AV bug in Rule 8 below — that one *was* a data bug.)

### check_plugin False Positives After save_plugin

`check_plugin` runs against the in-memory session state. After `save_plugin` writes new master references to disk, the in-memory state may still be stale and report those masters as "undeclared". Use `reload_plugin` to evict and re-open the plugin from disk, then re-run `check_plugin`.

### list_records returned "No records of that type" — it was the TYPE TOKEN, not a bug (2026-06-30)

Symptom that looked like a bug: `list_records WH_Core.esp KYWD` returned "No records of that type" while
`check_plugin` saw 33 records. ACTUAL cause (two usage errors, NOT a tool bug):
1. The read tools take param `type` (list_records) / `id` (get_record), NOT `record_type` / `record_id`.
   Wrong param names bind to empty string → empty result.
2. `list_records` / `list_records_summary` expect the **full Mutagen type name** (`Keyword`, `MiscItem`,
   `GlobalFloat`, `ConstructibleObject`, ...), NOT the 4-char record signature (`KYWD`, `MISC`, `COBJ`).
   The per-plugin index is keyed by `rec.Registration.Name` (the full name). See the "use the full name,
   not TES4 record codes" note earlier in this file. `run_script`'s `host.Records("Keyword", plugin)`
   works because it uses the full name too. `create_record` is the opposite — it takes the 4-char SIG.
   Use `list_record_types <plugin>` (no type arg) to see the exact full names + counts the index holds.

Defensive hardening shipped alongside (this was NOT the cause above, but is a real latent fix): `ModIndex`
now stores its `Source` mod reference and `GetModIndex` rebuilds when `Source` != the resolved mod, so a
stale index can never survive an instance replacement (reload_plugin / open / env refresh / editable copy
superseding the deployed copy). O(1) ref check; same-instance mutations still covered by `InvalidateModIndex`.

Known API inconsistency (candidate future polish): create_record uses 4-char sigs but list_records uses
full type names. Making list_records ALSO accept the sig would remove this footgun. Not yet done.

### PERK activate-choice write was broken; fixed via local-Mutagen retarget (2026-06-30)

Symptom: creating a PERK with a `PerkEntryPointAddActivateChoice` effect (the "Add Activate Choice"
entry point used to add a contextual button like "Field Dress" on a corpse) and saving threw
`NotImplementedException` ("One or more errors occurred ... method or operation is not implemented").
A plain ability-effect perk saved fine. Root cause (two layers):
1. The editor's `FO4RecordEditor.csproj` referenced **Mutagen as NuGet package `0.53.1`**, not the local
   Mutagen source under `Tools\PluginEditTool\tools\Mutagen\`. So source edits to Mutagen had no effect.
2. The actual write bug: `Mutagen.Bethesda.Core\Translations\Binary\EnumBinaryTranslation.cs`
   `WriteValue` has no `UnderlyingType` case for a **short-backed enum** (throws at its `default`).
   `PerkEntryPointAddActivateChoice.Flag` is 2-byte, and `Perk.cs` `WriteBinaryEffectsCustom` wrote the
   EPF3 flags via `EnumBinaryTranslation<...>.Write(writer, choice.Flags, 2)` -> threw. (The READ side
   uses a raw `ReadInt16`, which is why reading vanilla activate-choice perks always worked.)

Fix (both needed):
- Retargeted `FO4RecordEditor.csproj`: replaced `<PackageReference Include="Mutagen.Bethesda.Fallout4"
  Version="0.53.1"/>` with `<ProjectReference Include="..\..\Mutagen\Mutagen.Bethesda.Fallout4\
  Mutagen.Bethesda.Fallout4.csproj"/>`. The local source is API-compatible (editor compiled with 0 C#
  errors). Now ALL Mutagen source edits take effect on the next editor rebuild.
- Patched `Mutagen\Mutagen.Bethesda.Fallout4\Records\Major Records\Perk.cs` EPF3 write to
  `writer.Write((ushort)choice.Flags.GetValueOrDefault());` (mirrors the read's ReadInt16), bypassing
  the unimplemented enum-translation path.

Validated: created an activate-choice perk (button label + GetDead condition), saved (249-byte esp),
re-read it -> all subrecords present (PRKE/DATA/EPFT/EPF2/EPF3/PRKC/CTDA/PRKF) and structure round-trips.
This also repairs the toolchain `set_perk_effects` activateChoice path. Rebuild cost: the editor now
builds local Mutagen too (~first build slower; incremental fast). Same close+rebuild+relaunch dance as
any editor change (stop FO4RecordEditor.exe, `dotnet build -c Release`, relaunch; MCP auto-reconnects).

### Custom-AV condition params (S7 skill-gate bug) now serialize correctly (2026-06-30)

The historical "Mutagen nulls a custom ActorValue condition parameter" bug (GetBaseValue/GetValue with
a custom AVIF as param1 written as 00000000 - the week-long S7 skill-gate bug) was in the published
**Mutagen NuGet 0.53.1**. Now that the editor builds against the LOCAL Mutagen source (the perk-write
retarget above), the local `FunctionConditionDataBinaryWriteTranslation` correctly categorizes
`ActorValue` params as `Form` and writes `ParameterOneRecord` as a master-mapped FormKey. VERIFIED by
decoding a saved esp: `GetBaseValue(WH_AV_HunterXP) >= 1` -> `param1 = 0x0000080D` (master 0 = WH_Core,
obj 0x80D), not null. So just set `ParameterOneRecord` (the AV FormLink) via set_conditions/run_script;
no special handling is needed. The obsolete `WriteService.FixActorValueConditionParams` shim was
removed: local Mutagen discovers the FormLink for MAST generation and writes the same FormLink into
CTDA, so hand-maintaining a second raw integer encoder only creates another place for header order and
record bytes to drift. Still verify AV-gated conditions by decoding bytes or xEdit, not `get_record`
(which displays from the in-memory record).

### Mutagen serialization audit (2026-06-30) - EnumBinaryTranslation root fixes + round-trip regression

Root cause behind the perk EPF3 failure was in Core `Translations\Binary\EnumBinaryTranslation.cs`:
1. The nullable `Write(TWriter, TEnum?, long)` overload **threw NotImplementedException when the value
   was null** - that's what actually killed the activate-choice perk write (its EPF3 Flags were unset).
   Fixed: write `default(TEnum)` (0) instead of throwing.
2. `UnderlyingType` had no `Short`/`SByte` case, so the static ctor + `WriteValue` threw for any
   short/sbyte-backed enum. Fixed: added both (latent - no short enums in FO4 yet, but correct hardening
   for all games).
These are in the LOCAL Mutagen (now the editor's source), so they take effect on editor rebuild.

Validated by a round-trip regression (create -> save -> fresh-read from the written bytes): a plugin
with a PERK carrying 4 entry-point effect types (ModifyValue, AddActivateChoice with NULL flags->0,
AddLeveledItem, Ability), a SPEL with a magic effect, a LVLI with entries, a QUST with stage+objective,
and a COBJ with 3 conditions (custom-AV GetBaseValue / keyword HasKeyword / integer GetRandomPercent) -
ALL serialized and read back intact. The ~40 other `NotImplementedException` sites in the FO4 records
are intentional abstract-overlay base stubs (concrete subclasses override them; the round-trip read
confirms they are not hit) - not bugs. Net: the local-Mutagen retarget + these EnumBinaryTranslation
fixes resolve the FO4 serialization-bug class (perk write, custom-AV condition params, nullable enums).

### Deleted Records (UDRs)

`check_plugin` reports hard-deleted records. Use `clean_plugin` followed by `save_plugin` to convert them to disabled (safe) records. This is an xEdit-style UDR fix and is safe to do on any plugin.

### Condition `flags` Field in set_conditions

The `set_conditions` tool now accepts an optional `flags` key in each condition object. Pass a comma-separated string of flag names:

```json
{"function": "GetGlobalValue", "param1": "000126:MAIM.esp", "operator": "==", "value": 0, "flags": "UseOr"}
```

Supported flag names match the `Condition.Flag` enum: `UseOr`, `UseRunOnTarget`, etc. Omit `flags` or leave it blank for the default (UseAnd, runs on Subject).

---

## New Authoring + Papyrus Tools (2026-06-30) — READ THIS

The editor was substantially extended. **49 MCP tools** now. After any rebuild you must stop all
`FO4RecordEditor.exe`, `dotnet build <csproj> -c Release`, then the user runs `/mcp reconnect` to
load new tools. Verify the tool list by reflecting the static `_specs` field (a raw byte-scan of the
DLL gives false negatives — string literals live in a compressed metadata heap).

### Struct-list authoring (lists of structs that `add_list_item` CANNOT build)

`add_list_item` only appends **FormLink** lists (Keywords, FLST Items). For struct lists use:

- **`add_leveled_entry`** (LVLI/LVLN) — one weighted entry: `reference`, `level`, `count`, `chance_none` (%). Call once per entry.
- **`set_perk_effects`** (PERK) — replaces the Effects list. JSON array; each `{"kind": ...}`:
  - `"ability"` `{ability, rank?, priority?}`
  - `"modifyValue"` `{entryPoint, modification:"Set|Add|Multiply", value, rank?, priority?, conditions?}`
  - `"activateChoice"` `{buttonLabel, spell?, entryPoint?(=Activate), conditions?}`
  - `entryPoint` = an `APerkEntryPointEffect.EntryType` name (e.g. `Activate`, `ModAttackDamage`). `conditions` use the same objects as `set_conditions` (+ optional `tabIndex`).
- **`set_magic_effects`** (SPEL/ENCH) — replaces the Effect chain. JSON array: `{effect:"<MGEF>", magnitude?, area?, duration?, conditions?}`.
- **`set_quest_aliases`** (QUST) — `{id, name, forcedReference?, uniqueActor?, flags?}`. Player ref = `000014:Fallout4.esm`. A persistent quest + forced player alias is the standard PrismaUI-bridge / OnPlayerLoadGame host.
- **`set_quest_stages`** (QUST) — `{index, logEntry?, flags?:"RunOnStart", complete?}`.
- **`set_quest_objectives`** (QUST) — `{index, displayText, flags?}`.

`run_script` can now also CREATE records: `host.New(sig, editorId)` / `host.New<T>(sig, editorId)`
returns the new mutable record (was override-only). Use it for any struct list the dedicated tools
don't cover, via the full Mutagen API.

### Read / analysis additions

- **`search_all`** — whole load order (matches display Name too), optional `type` filter. (`search_records` is single-plugin.)
- **`resolve_editorid`** — EditorID → `XXXXXX:Plugin.esp` FormKey across the load order. Use before set_field/conditions/script-prop when you have an EditorID.
- **`get_scripts`** — read back a record's attached Papyrus (VMAD) + property values. Verify `attach_script`/`set_script_property` worked.
- **`diff_records`** — field-level diff of two records (verify `copy_as_override`).
- **`offset`** pagination on `list_records`/`list_records_summary`/`search_records` (list_records shows "Showing X-Y of TOTAL" + next offset). `list_record_types` now shows per-type counts. `search_robco_configs` globs `*.ini*` (catches .ini.bak/.DISABLED).

### Papyrus compile + decompile (built in — no Champollion)

- **`compile_papyrus`** — .psc → .pex via the CK PapyrusCompiler. Auto-detects file vs folder.
  **Namespaces are automatic**: a single namespaced file is targeted by its object name; a folder is
  staged into a namespace tree first, so even a FLAT folder of `IHO:Foo` scripts compiles.
  **F4SE + vanilla base + Institute flags are on the import path automatically** (F4SE source =
  `E:\F4SE OG\Tools\f4se_0_06_23\Data\Scripts\Source`, FIRST so its extended Game.psc/Actor.psc with
  `GetCameraState` etc. win). Add other extenders via `imports`. Passing a `.pas` is rejected.
  Returns a leading `RESULT: X succeeded, Y failed`; on failure a **DEPENDENCY HELP** block names the
  missing extender + a Nexus link (HUDFramework 20309, Workshop Framework 35004, MCM 21497, ...).
- **`decompile_papyrus`** — .pex → .psc (our own decompiler). Auto-detects single file vs folder
  (recurses subfolders / whole mod). Saves when `write=true` OR an `output` folder is set; writes
  namespaced output into namespace subfolders so it recompiles as-is. `assembly=true` = faithful
  `.pas` bytecode listing (inspect-only, NOT compilable). Returns `RESULT: X/Y decompiled` + OUTPUT.
  VALIDATED at full parity with original source (IHO: ours compiles 89/90 vs original 90/90; the 1
  gap is a source-only helper script with no .pex to decompile, not a tool bug).

Code: `Services\PapyrusService.cs` (compile/decompile/staging/dependency help),
`Services\Papyrus\PexFile.cs` (FO4 PEX reader), `Services\Papyrus\PapyrusDecompiler.cs` (emitter).
The patched standalone compiler at `Tools\PapyrusCompiler` is the fallback if the CK one is absent.

### GUI (web frontend, WebView2)

A Papyrus activity-bar button (scroll icon) opens a compile/decompile workspace: drag-drop a
.pex/.psc, Open-output-folder button, result banner + Papyrus syntax highlighting, persisted fields,
write-on by default. Frontend = `FO4RecordEditor\web\src\PapyrusPanel.tsx` (host object `papyrus` =
`Services\PapyrusInterop.cs`). Edit web → `npm run build` in `web\` → rebuild C# Release (it
Content-copies `web\dist`). The GUI works without the MCP connection.

---

## StandaloneWorkbenches.esl Removal (2026-06-22)

SW was removed from the Fallen World Alpha 2 modlist. Any plugin with SW in its master list will refuse to load.

### Vanilla WorkbenchKeyword Mapping

All 14 SW bench keywords map to one of three vanilla routing keywords:

| SW FormKey | Bench Category | Vanilla FormKey | Vanilla Name |
|---|---|---|---|
| `000801:StandaloneWorkbenches.esl` | Ammunition | `102158:Fallout4.esm` | WorkbenchChemlab |
| `000804:StandaloneWorkbenches.esl` | Armorsmith | `0657FF:Fallout4.esm` | workbencharmor |
| `000807:StandaloneWorkbenches.esl` | Clothing | `0657FF:Fallout4.esm` | workbencharmor |
| `00080A:StandaloneWorkbenches.esl` | Decal | `0657FF:Fallout4.esm` | workbencharmor |
| `00080D:StandaloneWorkbenches.esl` | Electronics | `102158:Fallout4.esm` | WorkbenchChemlab |
| `000810:StandaloneWorkbenches.esl` | Engineering | `102158:Fallout4.esm` | WorkbenchChemlab |
| `000813:StandaloneWorkbenches.esl` | Explosives | `102158:Fallout4.esm` | WorkbenchChemlab |
| `000822:StandaloneWorkbenches.esl` | Manufacturing | `102158:Fallout4.esm` | WorkbenchChemlab |
| `000816:StandaloneWorkbenches.esl` | Packaging | `102158:Fallout4.esm` | WorkbenchChemlab |
| `000819:StandaloneWorkbenches.esl` | Paint | `0657FF:Fallout4.esm` | workbencharmor |
| `000824:StandaloneWorkbenches.esl` | Produce | `102152:Fallout4.esm` | WorkbenchCooking |
| `00081C:StandaloneWorkbenches.esl` | Utility | `102158:Fallout4.esm` | WorkbenchChemlab |
| `00081F:StandaloneWorkbenches.esl` | Weaponsmith | `0657FF:Fallout4.esm` | workbencharmor |
| `00082A:StandaloneWorkbenches.esl` | Chooseable | `0657FF:Fallout4.esm` | workbencharmor |

The authoritative mapping lives in `E:\F4SE OG\Tools\PluginEditTool\Patched Data\ModOutput\Source\benchmap.py` (`SW_TO_VANILLA` dict).

### Plugins Fixed (2026-06-22)

| Plugin | Mod Folder | Fix Applied | Records |
|---|---|---|---|
| `FallenWorld_Ballistics.esp` | FallenWorldCraftingFramework | WorkbenchKeyword updated | 32 COBJs |
| `M84FlashBang.esl` | Fallout Anomaly Master Files | WorkbenchKeyword updated | 1 COBJ |
| `Defusing Kit Constructible.esp` | Fallout Anomaly Master Files | WorkbenchKeyword updated | 1 COBJ |
| `Fallout Anomaly Overwrites.esp` | Fallout Anomaly Master Files | WorkbenchKeyword updated + UDR cleaned | 1 COBJ |
| `FW_GunsmithGating.esp` | FW Gunsmith Gating | Stale master stripped from header | 0 records (header only) |
| `FWC_NonFWC_Schematics.esp` | FWC NonFWC Schematics | Stale master stripped from header | 0 records (header only) |

All 6 verified clean with `check_plugin` post-fix (0 deleted, 0 undeclared masters).

### Pipeline Fixes (same session)

The crafting patch pipeline also had SW contamination:

- `7_repoint.py` was missing SW normalization on `WorkbenchKeyword` when copying FWBallistics foundation records. Fix: add `SW_TO_VANILLA` remap after `deep_remap()`.
- `14_s7compat.py` (FallenWorldCrafting_S7) had stale esl_src from pre-fix pipeline. Fix: re-run stage 14.

Both pipeline output ESPs (`FallenWorldCrafting_Compat.esp`, `FallenWorldCrafting_S7.esp`) rebuilt and deployed clean.

---

## Binary Plugin Header Manipulation

When an editor cannot clean a stale master, it must be removed at the binary level.

### TES4 Subrecord Layout

```
[pos+0]  4 bytes  subrecord type ("MAST", "DATA", "HEDR", etc.)
[pos+4]  2 bytes  payload size (little-endian uint16)
[pos+6]  N bytes  payload (size bytes)
```

A MAST+DATA pair for one master takes exactly 46 bytes:
- MAST: 4+2+26 = 32 bytes (type+size+"StandaloneWorkbenches.esl\0")
- DATA: 4+2+8 = 14 bytes (type+size+uint64 filesize)

After deleting subrecord bytes, patch TES4 data-size at offset 4 (4-byte LE uint32) by the same amount removed.

Script: `E:\F4SE OG\Tools\PluginEditTool\Patched Data\Source\strip_stale_master.py`

---

## Binary Scan for SW References

To check which plugins in the modlist still reference SW in their headers:

```powershell
Get-ChildItem "E:\Modlists\Fallen World Alpha 2\mods" -Recurse -Include "*.esp","*.esm","*.esl" |
ForEach-Object {
    try {
        $bytes = [System.IO.File]::ReadAllBytes($_.FullName)
        $text = [System.Text.Encoding]::Latin1.GetString($bytes, 0, [Math]::Min(8192, $bytes.Length))
        if ($text.Contains("StandaloneWorkbenches.esl")) { $_.FullName }
    } catch {}
}
```

Note: wrap in `try/catch` to skip locked files (Voice/Sound subdirs).

---

## MAIM Recipe Conflicts in FallenWorldCrafting_S7 and Compat (2026-06-22)

### Root Cause

MAIM.esp defines 18 COBJ records for its healing items (IFAK, Splint, Ibuprofen, etc., plus HC_ variants). These records use `RecipeMAIMHealing [00078D:MAIM.esp]` as their category keyword (a MAIM-specific station keyword). The pipeline stage `14_s7compat.py` previously sourced ONLY from `FallenWorldCrafting.esp` records, so these MAIM overrides were never processed, leading to three bugs:

1. **Wrong category keyword**: S7 overrides kept `RecipeMAIMHealing` instead of using the vanilla `RecipeHealing [102150:Fallout4.esm]`.
2. **Null GetBaseValue conditions**: Old pipeline build had stale `_s7sys_ser` data -- `ParameterOneRecord` was `00000000` (null ActorValue).
3. **Missing schematic perk gate**: S7 overrides had no `HasPerk(Unlock_NF_xxx_Perk)` condition, so players could craft without the schematic.

Additionally, `FallenWorldCrafting_Compat.esp` had 6 stale MAIM overrides (CO_IFAK, CO_QuickClotInjector, CO_Splint, CO_Ibuprofen, CO_Bandage, CO_TXA) with CM2-template components that should not exist in Compat. These were deleted.

### Rule: MAIM Category Keyword

NEVER use `RecipeMAIMHealing [00078D:MAIM.esp]` in any pipeline-generated record. Always use:

| Use Case | FormKey | Name |
|---|---|---|
| Healing items at chem bench | `102150:Fallout4.esm` | `RecipeHealing` |

The `RecipeMAIMHealing` keyword routes to MAIM's custom station, which is not present in the modlist. Records using it will appear under a non-existent station category.

### Rule: FWC_NonFWC_Schematics Must Be in S7 Master List

`FallenWorldCrafting_S7.esp` overrides MAIM records and adds `HasPerk` conditions referencing schematic unlock perks from `FWC_NonFWC_Schematics.esp`. That plugin MUST be declared as a master of S7. Verify with `Patched Data\Source\add_master.py` after any pipeline rebuild.

### The 18 MAIM Records in S7 -- Correct Condition Table

All 18 records use this condition pattern:
1. `GetGlobalValue(MAIMHardcoreCrafting [00009A:MAIM.esp]) == <0 or 1>` (0 = non-HC, 1 = HC)
2. `GetBaseValue(<S7 Medicine AV>) >= <threshold>` RunOn player (000014:Fallout4.esm)
   - S7 Medicine AV: `000164:S7 System.esp`
3. `HasPerk(<schematic perk>) == 1` RunOn player (000014:Fallout4.esm)

For records with MAIMLiteInstalled, also add:
4. `GetGlobalValue(MAIMLiteInstalled [000126:MAIM.esp]) == <0 or 1>` between conditions 1 and 2.

| EditorID | FormKey | HC | MAIMLiteInstalled | Threshold | Schematic Perk FormKey |
|---|---|---|---|---|---|
| CO_IFAK | 000011:MAIM.esp | No (==0) | No | 45 | 000A94:FWC_NonFWC_Schematics.esp |
| CO_QuickClotInjector | 000026:MAIM.esp | No | No | 25 | 000A96:FWC_NonFWC_Schematics.esp |
| CO_Splint | 000028:MAIM.esp | No | Yes (==0) | 25 | 000A98:FWC_NonFWC_Schematics.esp |
| CO_Ibuprofen | 00002A:MAIM.esp | No | Yes (==0) | 25 | 000A9A:FWC_NonFWC_Schematics.esp |
| CO_SP2 | 000054:MAIM.esp | No | No | 25 | 000A9C:FWC_NonFWC_Schematics.esp |
| CO_Bandage | 00008E:MAIM.esp | No | Yes (==0) | 25 | 000A9E:FWC_NonFWC_Schematics.esp |
| CO_PotassiumIodide | 00004D:MAIM.esp | No | No | 45 | 80001D:FallenWorldCrafting.esp |
| CO_T10 | 00004E:MAIM.esp | No | No | 45 | 80001D:FallenWorldCrafting.esp |
| CO_TXA | 0000D5:MAIM.esp | No | No | 45 | 000AA0:FWC_NonFWC_Schematics.esp |
| HC_CO_IFAK | 0000D8:MAIM.esp | Yes (==1) | No | 45 | 000A94:FWC_NonFWC_Schematics.esp |
| HC_CO_QuickClotInjector | 0000DA:MAIM.esp | Yes | No | 25 | 000A96:FWC_NonFWC_Schematics.esp |
| HC_CO_Splint | 0000DB:MAIM.esp | Yes | Yes (==1) | 25 | 000A98:FWC_NonFWC_Schematics.esp |
| HC_CO_Ibuprofen | 000100:MAIM.esp | Yes | Yes (==1) | 25 | 000A9A:FWC_NonFWC_Schematics.esp |
| HC_CO_PotassiumIodide | 000115:MAIM.esp | Yes | No | 45 | 80001D:FallenWorldCrafting.esp |
| HC_CO_T10 | 000116:MAIM.esp | Yes | No | 45 | 80001D:FallenWorldCrafting.esp |
| HC_CO_SP2 | 00011A:MAIM.esp | Yes | No | 25 | 000A9C:FWC_NonFWC_Schematics.esp |
| HC_CO_Bandage | 00011D:MAIM.esp | Yes | Yes (==1) | 25 | 000A9E:FWC_NonFWC_Schematics.esp |
| HC_CO_TXA | 000121:MAIM.esp | Yes | No | 45 | 000AA0:FWC_NonFWC_Schematics.esp |

### Fix Applied (MCP Editor)

For all 18 S7 MAIM records: `set_conditions` with correct 3-condition set (HC global, GetBaseValue, HasPerk), then `remove_list_item` + `add_list_item` on Categories field (RecipeMAIMHealing -> RecipeHealing). Both plugins saved and verified clean.

### Pipeline Gap -- MAIM Records Not Generated by 14_s7compat.py

`14_s7compat.py` only processes records whose FormKey ends with `FallenWorldCrafting.esp`. MAIM records (FormKey ends with `MAIM.esp`) are silently skipped. If stage 14 is re-run, the 18 MAIM overrides in the deployed S7.esp will be LOST.

**Prevention:** After any re-run of `14_s7compat.py`, immediately re-apply the 18 MAIM overrides using the MCP editor (`set_conditions` + `remove/add_list_item` as documented above). A future pipeline improvement should source MAIM records from `FWC_NonFWC_Schematics` esl_src and include them in the S7 output automatically.

### MCP Editor Workflow for Categories List Fields

The `set_field` tool cannot target a whole list element by index. Use:
```
remove_list_item(plugin, record, field="Categories", value="<formkey>")
add_list_item(plugin, record, field="Categories", value="<formkey>")
```

Also: use `id=` not `record=` as the parameter name in `get_record`.

---

## run_script Best Practices (Critical — Read Before Writing Any Patch Script)

### Rule 1: Always use `host.GetBestBase()` for deep-copying

**NEVER** do `var c = host.Cobj(g)` when `g` comes from a mid-chain patch plugin (FallenWorldCrafting_S7, or any pipeline-generated patch). That plugin's records are often:
- **Partial overrides** — only conditions written to binary, all other fields (CNAM, FVPA, BNAM) null. The deep copy copies those nulls → blank record in the patch.
- **Missing prior-patch conditions** — if FallenWorldCrafting_Compat.esp (or any other compatibility patch) adds conditions AFTER the source plugin in load order, the source plugin's version never saw those conditions. The deep copy silently drops them → CMWeapons, CMImmersiveMode, HasPerk gates lost.

**Correct pattern:**
```csharp
// g = source plugin getter (used to READ skill/threshold/conditions for classification)
// best = the winning full-record version, skipping your own patch and broken intermediaries
var best = host.GetBestBase(g, "FallenWorldCrafting_S7.esp", "FW_SkillGates.esp");
if (!host.CnamIsResolvable(best)) { skippedBroken++; continue; }
var c = host.Cobj(best);
host.RemoveConditions(c, "GetBaseValue"); // clean any old nulls
host.AddCondition(c, "GetBaseValue", param1: avFk, op: ">=", value: threshold, ...);
```

`GetBestBase` walks `ResolveAllSimpleContexts` from winning to original, returning the first plugin NOT in the skip list. If Compat (FE 1FD) overrides the same record and comes before FC_S7 (FE 1FE) in the chain winner order, Compat's version is returned — full fields + CM conditions intact. If no compat override exists, you get the original plugin's version (e.g., SuperMutantRedux.esp) with all fields present.

### Rule 2: Always check CNAM before deep-copying

FC_S7.esp was generated by a broken pipeline. Some of its COBJ overrides have `CreatedObject = FE0D4EE2` (or other null/unresolvable FormIDs). Deep-copying those creates a COBJ with no model/keyword/header data.

```csharp
if (!host.CnamIsResolvable(best)) { skippedBroken++; continue; }
```

This guard prevents blank overrides in your patch plugin.

### Rule 3: create_plugin BEFORE run_script when target plugin exists on disk

If `FW_SkillGates.esp` (or any patch target) already exists on disk AND is in the env's path map (loaded at startup), its path is registered in `_mutable`. Deleting and recreating the file on disk does NOT update the path map — `OverrideForScript` calls `OpenPlugin` on the stale path and silently returns null. Fix:

```
create_plugin FW_SkillGates.esp   ← call this via MCP first
run_script ...                    ← now GetMutable finds it immediately
```

### Rule 4: Fallout 4 plugin-file FormIDs use one MAST-order namespace

- **WRONG for FO4:** ESL/small-master special case `0xFE000000 | (lightIndex << 12) | objId`
- **CORRECT for FO4:** Same as any full master: `(mastIndex << 24) | objId`

The `0xFExxxxxx` format is the engine's **runtime remapping only**. In an FO4 ESP/ESM/ESL, every declared master uses its ordinary MAST-table position byte as the high byte, and the file's own records use `masterCount << 24`. The local Mutagen FormLink writer and `create_seq_file` deliberately follow that rule.

The easy trap is copying xEdit's generic `TwbFileID` light-slot branch without its gate. In xEdit `dev-4.1.6`, separate full/light/medium file-local counters run only when `wbComplexFileFileID = True`; `xeInit.pas` enables that mode for **Starfield only**, not FO4. With the flag false, `GetFileFileID` falls back to `CreateFull(GetMasterCount(...))`, and master references are keyed by their ordinary MAST ordinal. Regression tests save a mixed full+light CTDA reference and generate a light-plugin SEQ file, then assert the raw bytes. Do not import the Starfield slot formula into Fallout 4 without evidence that FO4's file format changed.

### Rule 5: Read skill/threshold from source getter; copy from GetBestBase

For two-pass scripts:
- `g` = source plugin (FC_S7, FC.esp) — read `HasPerk` perk EditorID, threshold from broken GBV conditions, EDID classification
- `best = host.GetBestBase(g, ...)` — use as the base for `host.Cobj(best)`
- The skill classification reads from `g`; the record content comes from `best`

### Rule 0 (ABSOLUTE): CNAM "could not be resolved" errors are NEVER a real data problem

The FO4RecordEditor MCP always loads via the full MO2 modlist — every plugin is present. A CNAM FormKey that appears unresolvable in an xEdit session is an **xEdit session artifact** caused by xEdit not having all mods loaded, NOT a defect in the plugin data.

**NEVER:**
- Report CNAM errors as a bug or flag them for investigation
- Suggest that FW_SkillGates.esp or any pipeline-generated plugin has broken CNAM references
- Treat "multiple Cobj objects = created object error could not be resolved" xEdit warnings as actionable

**ALWAYS:**
- Assume the CNAM is valid. The MO2 full load order resolves all FormLinks.
- If a script needs to check resolvability, run `TryResolve` against `MutagenLoader.LinkCache` (the full load-order cache). A miss there would be a real issue; xEdit warnings are not.

This is a permanent rule. Do not revisit or re-diagnose CNAM errors from xEdit screenshots.

### Rule 6: patch_plugin MUST differ from the source plugin being iterated

If `host.Cobjs("FW_SkillGates.esp")` iterates records FROM FW_SkillGates.esp, calling `host.Cobj(g)` with `patch_plugin="FW_SkillGates.esp"` fails with "type 'ConstructibleObject' may not be overridable". The engine can't override a record from the same-named mod into itself.

**Fix:** Use a different patch plugin name (e.g., `FW_SkillGatesCatFix.esp`) and place it after the source plugin in load order. The override deep-copies from the source record and adds the fix on top — conditions and other fields from the source are preserved.

```csharp
// WRONG:
// run_script(patch_plugin="FW_SkillGates.esp") + iterate FW_SkillGates records → crash

// CORRECT:
// run_script(patch_plugin="FW_SkillGatesCatFix.esp") + iterate FW_SkillGates records
// → saves FW_SkillGatesCatFix.esp as a thin override patch, placed after FW_SkillGates.esp
```

This means fixes to an existing plugin are applied as a separate override plugin loaded after it, not by overwriting the original. This is also safer (preserves original on disk).

### Rule 7: FNAM/Categories null from shallow Compat overrides — use GetCategoriesFromChain

When FallenWorldCrafting_Compat.esp (or any compat plugin) creates a partial override that only sets Conditions and leaves Categories null, `GetOrAddAsOverride` deep-copies those nulls, canceling inherited FNAM keywords from lower-priority plugins (like the original weapon mod). xEdit's Compat column may SHOW categories (via inheritance display), but the actual override record has explicit null categories.

**Fix pattern — added to PatchScriptHost.cs:**
```csharp
// Walk override chain, skip the compat/patch plugin, find first plugin with non-empty categories
var chainCats = host.GetCategoriesFromChain(g, "FW_SkillGates.esp");
// Copy those categories into the mutable override
host.CopyCategories(c, chainCats);
```

`GetCategoriesFromChain(cobj, skipPlugins...)` uses `ResolveAllSimpleContexts<IConstructibleObjectGetter>`, skips named plugins, returns first non-empty Categories list. Returns null when no source exists.

`CopyCategories(target, cats)` clears existing categories and copies FormLinks. No-op if cats is null/empty.

**Key diagnostic:** In a dry_run script, count `g.Categories?.Count == 0` AND `!g.WorkbenchKeyword.FormKey.IsNull`. Records with workbench but no categories are candidates for chain-walking. Those with no chain source (`GetCategoriesFromChain` returns null) genuinely have no categories in any plugin — leave them alone.

### Rule 8: FC_S7 null-AV GetBaseValue — Spriggit/Mutagen serialization bug

`FallenWorldCrafting_S7.esp` was generated by the Spriggit pipeline. ALL GetBaseValue() conditions targeting custom ActorValues (S7 skills) have `ParameterOneRecord = null` (binary zero). ParameterOneNumber is also 0x00000000 — Spriggit drops the AV entirely. xEdit shows `GetBaseValue() >= N` with empty parentheses; the condition always passes.

**Reading the correct AV from FW_SkillGates:**
FW_SkillGates.esp was saved by FO4RecordEditor using the local Mutagen FormLink writer. Mutagen CAN read `ParameterOneRecord` from it correctly:
```csharp
// This works for FW_SkillGates records; returns proper FormKey like 000165:S7 System.esp
if (cond.Data is IFunctionConditionDataGetter fcd && !fcd.ParameterOneRecord.FormKey.IsNull)
    var avFk = fcd.ParameterOneRecord.FormKey; // valid!
```

**Correct approach for Mutagen condition access:**
```csharp
foreach (var cond in g.Conditions)
{
    if (cond.Data is Mutagen.Bethesda.Fallout4.IFunctionConditionDataGetter fcd)
    {
        var func = fcd.Function; // Condition.Function enum
        var p1 = fcd.ParameterOneRecord.FormKey;
        float val = 0;
        if (cond is Mutagen.Bethesda.Fallout4.IConditionFloatGetter cf) val = cf.ComparisonValue;
    }
}
```

Note: `cond.ComparisonValue` does NOT exist — it's on `IConditionFloatGetter`, not `IConditionGetter`. Cast first.

**S7 skill AV FormKeys in S7 System.esp (from FW_SkillGates analysis):**
| Use Case | FormKey |
|---|---|
| Gunsmith / weapon crafting (most FC records) | `000165:S7 System.esp` |
| Robot workbench (DLCRobot.esm CNAM records) | `00016B:S7 System.esp` |
| Medicine / healing items (MAIM records) | `000164:S7 System.esp` |
| Science / energy (some records) | `000166:S7 System.esp` |
| Chaos / misc (some records) | `0001E6:S7 System.esp` |

To identify which AV applies, look up the winning record's CNAM ModKey (DLCRobot.esm = robot skill, MAIM.esp = medicine, etc.).

**Mercenary scrap stubs:** FC_S7 also overrides Mercenary.esp COBJ records (no workbench keyword). These should NOT have GetBaseValue conditions — remove them with `host.RemoveConditions(c, "GetBaseValue")`.

**Using MutagenLoader.LinkCache in scripts:**
```csharp
var cache = MutagenLoader.LinkCache;
if (cache.TryResolve<Mutagen.Bethesda.Fallout4.IConstructibleObjectGetter>(formKey, out var winner))
{
    var cnamMod = winner.CreatedObject.FormKey.ModKey.FileName.String; // e.g. "DLCRobot.esm"
}
```

### Rule 9: `remove_list_item` removes ALL occurrences — not just one

`remove_list_item` removes every entry matching the given FormKey/EditorID from the target list field. If a list accidentally has duplicates (e.g., two copies of the same category keyword) and you call `remove_list_item` to drop one duplicate, it removes BOTH, leaving the list empty.

**Recovery pattern:** remove-all then re-add once.
```
remove_list_item(plugin, record, field="Categories", value="00D6A2:Remington700.esp")
# → removes ALL copies (both if there were 2)
add_list_item(plugin, record, field="Categories", value="00D6A2:Remington700.esp")
# → adds back 1 clean copy
```

**Root cause of duplicates:** When the MCP session has stale state from a deleted plugin (e.g., a fix plugin was removed from plugins.txt but is still in the MCP's in-memory load order), `add_list_item` on the source plugin may deep-copy from the winning stale record (which already has 1 category) before appending, resulting in 2 identical entries. Always verify item counts after adds with a follow-up get_record.

### Rule 10: Prefer direct source edits over separate fix plugins

**Do NOT** create a new patch plugin to fix conditions or categories on an existing plugin if you can directly edit that plugin with `set_conditions`, `add_list_item`, or `remove_list_item`. Separate fix plugins cause "clearing out" issues when the MCP deep-copies partial overrides from the fix plugin's source.

**Direct edit pattern (preferred):**
```
# Fix conditions in-place — no new plugin needed
set_conditions(plugin="E:\...\FallenWorldCrafting_S7.esp", record="0038E5:Mercenary.esp", conditions=[])
add_list_item(plugin="E:\...\FW_SkillGates.esp", record="00D6A1:Remington700.esp", field="Categories", value="00D6A2:Remington700.esp")
save_plugin(plugin="E:\...\FW_SkillGates.esp")
```

**Always use full absolute paths** (e.g. `E:\Modlists\Fallen World Alpha 2\mods\...`) when calling these tools — short plugin names ("FW_SkillGates.esp") will fail with "Could not locate on disk."

---

## Crafting Pipeline Location

`E:\F4SE OG\Tools\PluginEditTool\Patched Data\ModOutput\Source\`

Stage scripts numbered by order of execution. Key stages:

| Script | Output | Notes |
|---|---|---|
| `5_absorb.py` | `FallenWorldCrafting.esp` esl_src | Absorbs CM2 + add-ons; normalizes SW keys |
| `7_repoint.py` | `ZZ_CraftPatch_FWBallistics.esp` esl_src | Repoints FWBallistics COBJs from CM2 to framework; must also normalize SW |
| `14_s7compat.py` | `FallenWorldCrafting_S7.esp` esl_src | S7 compat patch; re-run if base esl_src changes. **MAIM overrides not generated -- re-apply manually after each run.** |
| `21_merge.py` | Final deployed ESPs | Calls Spriggit compile + deploys to modlist |

---

## External tool paths are now resolved, not hardcoded (2026-07-14)

`niftool`, `texconv`, the Papyrus compiler and the Papyrus base-script roots used to be hardcoded to
`E:\F4SE OG\...` in `NifService` / `PapyrusService` / `TextureService`. They now all go through
**`Services/ToolPaths.cs`**, which resolves in this order:

1. environment variable — `NIFTOOL_PATH`, `TEXCONV_PATH`, `PAPYRUS_COMPILER_PATH`, `PAPYRUS_BASE_IMPORTS`
2. `%APPDATA%\FO4RecordEditor\settings.json` — `niftoolPath`, `texconvPath`, `papyrusCompilerPath`,
   `papyrusBaseImports`, `fallout4Path`
3. a copy bundled next to the exe — `tools\niftool\`, `tools\texconv\`, `tools\papyrus\`
4. the local Fallout 4 install — registry (`HKLM\SOFTWARE\WOW6432Node\Bethesda Softworks\Fallout4`,
   `installed path`), then a Steam-library disk probe
5. the old dev paths, last

**[FIXED] `compile_papyrus` was running with 1 of its 3 import roots.** Two of the three hardcoded
base-script roots (`Tools\f4se_0_06_23\Data\Scripts\Source` and
`Tools\Scripting Resources\BaaseGameScripts\Base`) **do not exist on this machine** and never did —
only `Tools\PluginEditTool\papyrus\Base` was real. The F4SE-scripts-must-come-first comment in
`PapyrusService` was therefore aspirational: F4SE's extended `Game.psc`/`Actor.psc` were never on the
import path, so any script calling an F4SE-only native would have failed to compile with a confusing
"unknown function" error. Auto-detection from the FO4/CK install now supplies the real roots.

**When adding a new external tool, add it to `ToolPaths`** — do not hardcode another absolute path.

## Packaging the editor for other people (2026-07-14)

`FO4RecordEditor\package.ps1` produces the release zip (~18 MB). It builds the web UI, publishes,
bundles `niftool` + `texconv`, adds the docs and `mcp.sample.json`, and zips.

- **Framework-dependent on purpose — never "fix" this to self-contained.** MO2's usvfs hooks file
  access, and a self-contained .NET host cannot load its bundled runtime through those hooks: the
  process dies before managed code runs, so the app "launches" and instantly closes with no window,
  no log, no error. Recipients install the **.NET 9 Desktop Runtime (x64)**; that is the only prereq.
- **The tool is GPL-3.0 and has to be.** It links Mutagen (GPL-3.0) and bundles nifly (GPL-3.0), so
  distributing the binary obliges us to offer source to whoever receives it — publicly or privately.
- **Never bundle the vanilla Papyrus base scripts.** They are Bethesda's. They are also unnecessary:
  `compile_papyrus` needs the CK's non-redistributable `PapyrusCompiler.exe` anyway, and anyone with
  the CK already has the base scripts, which `ToolPaths` auto-detects.
- Smoke-test after packaging: extract the zip clean, run the exe with `--mcp`, write an `initialize`
  and a `tools/list` JSON-RPC line to stdin. It must answer and advertise **109 tools**.

### The docs it ships are an ALLOWLIST — never make it recursive again (2026-07-16)

`package.ps1` used to do `Copy-Item (Join-Path $Root 'docs') $Staging -Recurse -Force`, i.e. ship
whatever happened to be sitting in `docs/`. That is a trap, and it sprang: after the workspace docs
migration turned four of those files into *"Moved. This document now lives at
`docs/Plugin Tool/...`"* stubs, the **public GPL-3.0 release shipped four dead pointers to a private
path no recipient has** — plus `docs/superpowers/`, ~250 KB of internal AI planning notes.
`dist/FO4RecordEditor-1.0.0/docs/` had both.

It now copies an explicit `$ShippedDocs` list and **throws** if a listed file is missing. Adding a doc
to the release is a deliberate one-line edit.

**The rule: what SHIPS is the allowlist, and only the allowlist.** Today the whole shipped set is
`README.md` + `LICENSE` + `THIRD_PARTY_NOTICES.md` + `docs/MCP_SETUP.md`. Before shipping, diff the
staged folder against that list — a redirect stub is worse than a missing file, because it looks
authored.

> **Superseded 2026-08-07.** This section used to add "this repo's `docs/` is end-user-only —
> internal engineering docs live in the workspace hub and must never be added here." That rule
> existed because packaging was recursive, and it stopped being necessary the moment packaging
> became an allowlist. Keeping the docs outside the repo then cost more than it saved: they drifted
> from the code they described and were invisible to anyone who cloned it. Every internal doc,
> including this file, now lives in [`docs/internal/`](README.md) and is versioned with the code.
> The allowlist is what keeps them out of the release, and it is the thing to protect — **never make
> `package.ps1` recursive again.**

**The patched Mutagen fork is load-bearing.** `EnumBinaryTranslation.cs` (short-backed enum write) and
`Perk.cs` (EPF3 choice flags) are our patches; a clean upstream Mutagen builds an editor that cannot
write perk EPF3 data. Both are committed to the fork now — if you ever re-clone Mutagen, re-apply them.

## set_field CANNOT set ObjectBounds — and the dotted form LIES about it (2026-07-16)

`set_field <rec> ObjectBounds.First.X -27` returns **`Set ObjectBounds.First.X = '-27'`** — a success
message — and then **silently does nothing**. A follow-up `get_record` shows the bounds still
`0,0,0 / 0,0,0`. Only caught by re-reading the record; the tool's own response is not evidence.

The non-dotted forms at least fail honestly:
- `set_field ObjectBounds "-27,-12,0,27,10,23"` → *"Field 'ObjectBounds' has type ObjectBounds, which
  set_field can't set yet (scalar/text only)."*
- `set_field ObjectBounds.First.Point "..."` → *"Field 'Point' is read-only."*

So there is **no way to author OBND on a new record with this tooling**. Two consequences:
1. Records made with `create_record` ship with zero bounds unless you clone something.
2. **Always `get_record` after any nested/dotted `set_field`** — the success string is not proof.
   (Scalar/text/FormLink/Model.File sets do work and were verified the same way.)

**Workaround that does work — clone a vanilla record instead of building a shell:**
`copy_as_override <src> <patch>` then `renumber_formid <patch> <EditorID> <new_id>` converts the
override into a NEW record in the patch, carrying OBND, sounds, keywords and everything else. Verified
2026-07-16 building `Permadeath.esp`: cloned `Loot_Prewar_TrunkSilver_Empty [1504FD:Fallout4.esm]`
(an already-empty container with correct bounds) → `000800:Permadeath.esp`, then fixed up EditorID/Name
and cleared its `Respawns` flag with ordinary `set_field` calls.

**Verify the clone did not leave an override behind.** After `renumber_formid`, run
`list_records <plugin> Container` — it must show ONLY your new record and no trace of the source
FormKey. If the override survived, every instance of that vanilla record in the game changes. (Note
`list_records` wants the FULL type name — `Container`, not `CONT` — the documented sig-vs-name
inconsistency; `create_record` is the opposite.)

## check_plugin reports masters from MEMORY, not from the saved file (2026-07-16)

After `save_plugin`, `check_plugin` reported *"4 reference(s) to undeclared masters"* for a plugin
whose records legitimately reference `Fallout4.esm`. Reading the **raw TES4 header of the file on
disk** showed `MAST -> Fallout4.esm` correctly present — the save had recomputed masters and
`check_plugin` was describing pre-save in-memory state.

So a `check_plugin` "undeclared master" hit right after a save is **not** proof of a broken plugin.
Confirm against the binary before acting, per [[feedback_check_master_order_every_save]]:

```python
import struct
b = open(esp,'rb').read(); size = struct.unpack_from('<I', b, 4)[0]
off, end = 24, 24 + size
while off < end:
    sig = b[off:off+4].decode('latin1'); ssz = struct.unpack_from('<H', b, off+4)[0]
    if sig == 'MAST': print(b[off+6:off+6+ssz].rstrip(b'\x00').decode())
    off += 6 + ssz
```

## FO4's AVIF record has no literal Minimum/Maximum float fields (2026-07-17)

Confirmed via the Mutagen schema the editor builds against
(`ActorValueInformation.xml`): the only numeric field is `DefaultValue` (`NAM0`). There is no
`DNAM`-style min/max pair like some other Bethesda games' AVIF. Range is expressed only as coarse
preset bits in the `Flags` (`AVFL`) enum — `MinimumOne`, `MaximumTen`, `MaximumOneHundred`,
`DefaultToZero`/`DefaultToOne`/`DefaultToOneHundred`, etc. (`ActorValueInformation.cs` partial
class has the full `Flag` enum). If a task spec asks for an AVIF with an arbitrary
`Minimum: X` / `Maximum: Y`, there is no field to write it into — use the nearest preset flag
(`MaximumOneHundred` for "cap around 100") and note in the record's consuming code that real
range enforcement has to happen there, not in the record. Verified against both a vanilla record
(`Strength [0002C2:Fallout4.esm]`) and a modded one (S7 System.esp's own skill AVs, which use only
`DefaultToZero` — no maximum flag at all, i.e. that mod's "0-100" skill range was never enforced by
the AVIF record either). See `docs\Code Notes\FallenWorldSkills.md` Task 3 section for the full
writeup.

## create_cell + create_placed_object — authoring CELL and REFR (added 2026-07-16)

`create_record` cannot make either, and this is structural, not an oversight: its `AddNewBySig` switch
adds **top-level** records to a group on the mod (`mod.Weapons.Add(x)` etc). A placed object is not
top-level — it lives inside a `Cell`'s `Persistent`/`Temporary` list, and `Cell`s live in a
Block → SubBlock → Cell tree under `mod.Cells`. `run_script` only *overrides* existing records, and
`copy_as_override` needs something to clone. So map markers were unauthorable until these were added.

```
create_cell(plugin, editorId, name?)
create_placed_object(plugin, cell, baseObject, editorId?, x, y, z, rotZ,
                     persistent?, initiallyDisabled?, mapMarkerName?, mapMarkerType?, mapMarkerVisible?)
```

**Map marker recipe** — a marker is just a REFR over the vanilla `MapMarker` static
**`000010:Fallout4.esm`** *carrying map-marker data*. Without `mapMarkerName` the ref is an invisible
static and no marker appears:

```
create_cell           plugin=MyMod.esp editorId=MyModMarkerHolding
create_placed_object  plugin=MyMod.esp cell=MyModMarkerHolding baseObject=000010:Fallout4.esm \
                      editorId=MyModMarker01 mapMarkerName="Grave" mapMarkerType=Graveyard \
                      persistent=true initiallyDisabled=true
```

Notes worth keeping:
- **Interior cells are the safe holder.** Self-contained: no worldspace, no vanilla CELL override, so
  no conflict surface. Park refs there and relocate at runtime (`MoveRefToNewSpace`).
- **The cell must belong to the plugin.** `create_cell` one, or `copy_as_override` an existing cell in
  first. Placing into a cell the plugin doesn't own is rejected rather than silently misparented.
- **Persistent means two things and both are set.** The ref goes in the cell's `Persistent` list AND
  gets the `Persistent` major flag — the flag alone does not move it between lists, and the game reads
  the list it is actually in.
- Block numbering follows Bethesda's interior convention (block = `FormID % 10`, sub-block =
  `(FormID / 10) % 10`); the parenting mirrors Mutagen's own `ModContextExt` cell-copy logic.
- `mapMarkerType` accepts the marker icon names (`Graveyard`, `Settlement`, `Cave`, `Vault`, …); an
  invalid one is rejected with the full valid list rather than silently defaulting.
- Both reuse `NextFreeFormKey`, so ESL FormID-range safety is unchanged.
- **Two independent sources agree on the Visible flag.** Mutagen's
  `PlacedObjectMapMarker.Flag.Visible = 0x01` matches the engine decompile of `MapMarkerData::SetVisible`
  (`this[0x10] |= 1`, RVA `0xdea50`) exactly — see `docs/Code Notes/Permadeath.md` (outer workspace, not
  in this repo).
  Record-library and disassembly corroborating each other is what confirmed the earlier visibility bug.

**Deploying a change to this server:** the MCP host holds `bin\Release\net9.0-windows\*.dll` open, so
a normal `dotnet build` fails with MSB3021/MSB3027 "being used by another process" — that is a **file
lock, not a code error**. `dotnet build -t:Compile` compiles without the copy step and is how to prove
the code is good while the server is live. The binary swap needs every `FO4RecordEditor.exe --mcp`
instance stopped (a Claude Code restart does it). Note stale instances accumulate — there were 9 live
on 2026-07-16, most orphaned from earlier sessions.

---

## Write-layer hardening — behaviour changes you must know (2026-07-19)

Six defects fixed in FO4RecordEditor. Full reasoning in
`docs/Code Notes/FO4RecordEditor.md` (outer workspace, not in this repo); what changes for a caller:

**1. Failed tools now report `isError: true`.** Both MCP transports used to hardcode `isError: false`
on every outcome — caught exceptions, `Unknown tool`, `Record not found`, `Save failed while
writing`. An agent whose `set_field` failed saw a non-error result, went on to `save_plugin`, and
reported the edit as applied while the plugin shipped unchanged. A tool result that reads like a
failure now *is* one.

**2. Vanilla masters are write-protected.** `open_plugin` / `save_plugin` / `revert_overrides` refuse
`Fallout4.esm` and the DLC ESMs outright. Previously `set_field("Fallout4.esm", ...)` +
`save_plugin("Fallout4.esm")` overwrote the game master — a typo was enough, and recovery is a Steam
re-validate. Patch the vanilla record into your own plugin with `copy_as_override` instead.

**3. `save_plugin` validates its `path`.** Must end `.esp`/`.esm`/`.esl`, and must not resolve to a
vanilla master name (checked *after* normalization, so `..\Fallout4.esm` is caught). It previously
created the parent directory and `File.Replace`d over whatever was there.

**4. `save_plugin` warns when master order was not set.** Ordering MAST by the real load order needs
an `env`; four internal callers never passed one (`batch_patch_records`, `run_script`,
`revert_overrides`, the GUI patch save — plus `strip_masters_clean`), so those could emit a dependent
ESM before its dependency, which **hangs the game on load with no crash log**. Fixed; and if ordering
is still unavailable the result now says so, but only for 2+ master files. `WriteService.ReadMasterNames(path)`
is public now — the raw TES4 header is the only trustworthy check, since `check_plugin` reports from
memory (see the 2026-07-16 entry above).

**5. `compact_to_esl` refuses plugins whose out-of-range records are cell-placed.** `Fallout4Mod.Cells`
is a `Fallout4ListGroup`, which does **not** implement `IGroup`, so CELLs / PlacedObjects /
worldspace sub-cells were silently skipped by the re-key loop while still entering the remap set —
then `RemapLinks` repointed references onto FormKeys no record owned, and it reported success. It now
refuses up front, naming the types. Compact those in xEdit, or keep cell-placed records within
0x800-0xFFF when authoring. `renumber_formid` cannot move a cell-placed record either, and now says
so as an error.

**6. `run_script` is bounded at 2 minutes.** It used to hang the server forever on `while(true){}`.
Note a `CancellationToken` does not stop a tight loop (Roslyn only checks it at await points), so the
script is abandoned on a background thread — .NET cannot kill it, so restart the editor after a
timeout. `dry_run` no longer claims "Nothing was written": it discards *record edits* only, and any
file/process/network side effect the script performed has already happened. `run_script` is full
trust by design — treat it as running as you.

**Param footgun confirmed again:** `delete_record` takes **`id`**, not `record`. The wrong name binds
to `""` and the call silently no-ops. Same family as the `create_record` (SIG) vs `list_records`
(full type name) inconsistency.

---

## Workshop menu placement — no bespoke tool, use this reference instead (2026-08-05)

`GitHub #62` asked for a "Workshop Menu Editor" tool. There isn't one, on purpose: the fields involved
are all reachable with the existing generic tools (`create_record`, `add_list_item`, `set_field`,
`copy_as_override`); what a human needs from xEdit's version is the domain knowledge of which records
and fields matter, not a bespoke wizard. Read the real xEdit script
(`TES5Edit-dev-4.1.6/Build/Edit Scripts/Fallout4 - Workshop Menu Editor.pas`) before writing this —
it is NOT a per-COBJ field editor the way the issue's title suggests. It edits the workshop menu
**tree structure** itself:

- The whole in-game workshop menu is one nested tree of `FLST` (FormList) records. The root is
  `WorkshopMenuMain` "Main Menu" — `000106DA2:Fallout4.esm` (exact FormID straight from the script's
  own comment, not recalled). Each `FLST`'s `FormIDs` list holds either child `FLST`s (subcategories,
  e.g. Structures → Wood → Walls) or `KYWD` records (leaf categories).
- A `KYWD` is only a valid *leaf category* if its `TNAM` field is literally the string `Recipe
  Filter`. That is the marker the editor itself checks (`MenuAddKeywordClick` filters the whole KYWD
  group on exactly this) — a keyword without it will not behave as a menu entry even if placed in the
  tree.
- A `ConstructibleObject` (COBJ) recipe is placed into a category by adding that leaf keyword's
  FormKey to the COBJ's own `FNAM` field (its keyword list) — **not** by touching the FLST tree at
  all. This is the field `set_components`/`add_list_item` already reach generically; the only thing
  missing was knowing it's `FNAM` and that the target must be a `Recipe Filter`-tagged `KYWD`.
- To author a brand-new leaf category (not just re-use an existing one): copy
  `WorkshopAlwaysShowIconKeyword` — `000237B63:Fallout4.esm` (again, the exact FormID the script
  itself uses as its template, not guessed) as a new `KYWD` via `copy_as_new_record`, then set its
  `EDID`/`FULL`/`TNAM` (`Recipe Filter`) with `set_field`, then splice it into the parent `FLST`'s
  `FormIDs` with `add_list_item`.

So the real recipe for "put my COBJ in the right workshop category" with tools already in this repo:
1. Find or create the leaf `KYWD` (see above; `search_all`/`get_record` to check an existing one's
   `TNAM` is really `Recipe Filter` before reusing it).
2. `copy_as_override` the target `FLST` in if it's still a vanilla record, `add_list_item` the
   keyword's FormKey into its `FormIDs`.
3. `add_list_item` (or `set_components`) the same keyword's FormKey onto the COBJ's `FNAM`.

No new tool needed; the gap was this writeup, not code.

---

## Papyrus front end -- we own the lexer, parser and script index now (2026-08-07, issue #78 phase 1)

`compile_papyrus` shells out to the Creation Kit's `PapyrusCompiler.exe`, so no CK meant no Papyrus
work at all. That is now only true of *compiling*. The tool has its own Papyrus front end in
`FO4RecordEditor.Core/Services/Papyrus/`, and three MCP tools on top of it that need no CK:

| Tool | Answers |
|---|---|
| `papyrus_check` | Syntax errors in a `.psc`, or every `.psc` under a folder. `file(line,col): error PAP00nn: message`. |
| `papyrus_outline` | Everything a script declares -- header, imports, structs + members, custom events, groups, properties, variables, functions, events, states -- with signatures and positions. |
| `papyrus_definition` | Given a 1-based line and column, where that symbol is declared, plus its signature and doc comment. |

**The line that matters: these three tools parse, they do not compile.** A clean `papyrus_check` does
NOT mean the script compiles -- a misspelled function or a bad cast still fails at `compile_papyrus`.
Do not report "the script is valid" off a `papyrus_check`; report "the syntax is valid". For the same
reason `papyrus_definition` answers `not resolved` for a member reached through an expression whose
type is not written in the source (`GetOwner().MyProp`) instead of guessing -- a wrong jump is worse
than none.

> **Superseded in part (2026-08-08, issue #78 phase 2).** This section said "there is no resolver and
> no type checker". There now are: `PapyrusResolver` binds every name and types every expression, and
> `PapyrusTypeChecker` judges the result, both measured at 100% clean over the vanilla scripts. There
> is also a `.pex` **writer**, byte-identical over 4,581 real files, and a **code generator**:
> `PapyrusCompiler.CompileFile` takes a `.psc` plus roots and writes a `.pex` with no Creation Kit
> anywhere in the path, matching the CK's own instruction sequences on 158 of the 161 real scripts
> compiled in the differential sweep. **So the "syntax only" line below is out of date**:
> `compile_papyrus` now takes an `engine` (`auto` prefers an installed CK and falls back to the
> built-in compiler), and `papyrus_check` resolves names and checks types by default rather than only
> parsing. No tool was added; the count is still 105. One caveat that is easy to overclaim past: the
> built-in engine needs no `PapyrusCompiler.exe`, but it does need the vanilla base script **sources**
> on the import path, and those ship with the CK rather than in the game archives. **See
> [PAPYRUS.md](PAPYRUS.md)** for the whole subsystem; the rest of this section is phase 1 detail that
> remains accurate.

**What it does resolve**, in this order: locals declared before the caret, parameters, this script's
own members, up the `Extends` chain, `Import`ed scripts' globals and structs, then script and type
names. `PapyrusScriptIndex` maps a script name to a file the same way the language does -- namespace
colons are folder separators, so `MyNS:Quests:MyQuest` is `MyNS/Quests/MyQuest.psc` -- and searches
roots in priority order, first match wins, so F4SE's extended `Game.psc`/`Actor.psc` shadow the
vanilla ones exactly as they must at compile time.

### Grammar facts worth not rediscovering

- The whole language is 56 BNF productions over 20 wiki pages, 45 keywords, and **five** statement
  forms: define, assign, return, if, while. No `for`, no `switch`, no `do/while`, no `break`, no
  exceptions. Hand-written recursive descent, no generator, and that was not a close call.
- **`Hidden` / `Conditional` / `Mandatory` / `CollapsedOnRef` / `Default` are NOT keywords.** They
  are user flags defined by `Institute_Papyrus_Flags.flg`, which ships with the CK and is *not* in
  the game archives. The parser accepts any identifier in a flag position, which is precisely what
  lets it read scripts with no CK present. A future back end would still need that file.
- **A local variable may carry `const`** (`string filename = "S7System" const`). The published
  in-function production has no flags at all; real shipping scripts that the CK compiled do this.
  The wiki grammar is incomplete here, not wrong.
- The `f` suffix on a float (`60.0f`) is likewise absent from the Literals production and present in
  the wiki's own Statement Reference example.
- A leading `-` is lexed as an operator, not as part of the literal, even though the Literals
  production writes it into the number. Otherwise `a-1` can never parse as subtraction.
- A property is the one declaration whose single-line and block forms are told apart only by what
  follows it: no `Auto` flag means a full property, which then swallows everything up to
  `EndProperty`. So a property header that fails to yield a name must NOT open a block, or one bad
  line eats the rest of the file. (Found by a recovery test, not by reading.)

### How it was validated

Parsed **18,802 real `.psc` files** off this machine (vanilla + DLC + F4SE + the whole modlist):
0 crashes, 18,782 clean, and all 20 failures hand-verified as genuinely malformed source -- 19 are
F4SE's `scripts/modified/` fragments, which are snippets to paste into base scripts and have no
`ScriptName` header, and one is a mod script with two `ScriptName` lines. The sweep is a real test
(`PapyrusCorpusTests`), opt-in via `FO4RE_PSC_CORPUS` (roots separated by `:` on Linux, `;` on
Windows) so a bare checkout stays green.

### The Papyrus panel's Analyze mode

The same front end drives a fourth mode in the Papyrus panel: an editor with a line gutter, syntax
errors and an outline that update as you type, click-to-inspect, Ctrl+click / F12 go-to-definition
(which follows `Extends` and `Import` into other files and opens them), and Ctrl+S to save.

Three things about it that are load-bearing:

- **It analyses the BUFFER, not the file.** `PapyrusInterop.Analyze` / `SymbolAt` take the editor
  text. Passing a path instead would report the last save, which is the opposite of "as you type".
- **The GUI path returns JSON, the MCP path returns prose.** `AnalyzeJson`/`SymbolAtJson` against
  `Check`/`Outline`/`Definition`. A model reads text; an editor needs offsets it can select on. Both
  come off the same parse.
- **The gutter's CSS `line-height` must equal `EDITOR_LINE_HEIGHT` in `PapyrusPanel.tsx` (19px).** A
  textarea cannot scroll a selection into view or draw a squiggle, so "jump to line" computes a
  `scrollTop` from that constant and the gutter is a separate scrolling element synced to it. Change
  one without the other and every jump lands off by a growing number of lines.

`WriteScript` refuses any path not ending `.psc`. The panel hands it whatever is in the source box,
which may still be pointing at a `.pex` from an earlier decompile, and writing source over a compiled
script would destroy it silently.

### `FO4RecordEditor.Core.Tests` -- a test project that RUNS on Linux

`FO4RecordEditor.Tests` targets `net9.0-windows` through its reference to the WPF project, so its
test host cannot run here at all; that is why suites in it get written and never executed. The new
`FO4RecordEditor.Core.Tests` targets plain `net8.0` and references **only** `FO4RecordEditor.Core`,
so `dotnet test` genuinely runs. Put anything testable without WPF there.

Note `dotnet build` on `FO4RecordEditor.csproj` needs `-p:EnableWindowsTargeting=true` on Linux, and
an XML comment in a `.csproj` still cannot contain `--` (MSB4025), which bit again writing this.
