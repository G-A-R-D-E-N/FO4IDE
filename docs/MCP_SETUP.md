# FO4RecordEditor as an MCP server

## What this actually is

MCP (Model Context Protocol) is a standard way to hand an AI assistant a set of tools it can call.
FO4RecordEditor implements it: launched with `--mcp`, it does not open a window. It sits on stdin and
stdout speaking JSON-RPC, and it advertises **109 tools** for reading and writing Fallout 4 plugins.

The practical effect is that your AI stops guessing about your load order and starts *querying* it. It
can open a plugin, resolve an EditorID, read the winning override of a record, edit a field, save, and
run a validity check -- without you opening xEdit once, and without the AI ever hand-writing a binary
record. Every write goes through Mutagen, so the file it produces is structurally valid or the call
fails loudly.

It is not a chatbot and it has no model inside it. It is the hands; your AI client is the brain.

## Wiring it up

Create `.mcp.json` in the folder you run your AI from (Claude Code reads it from the project root). A
ready-made copy sits next to the exe as `mcp.sample.json`.

```jsonc
{
  "mcpServers": {
    "fo4editor": {
      "type": "stdio",
      "command": "C:\\Tools\\FO4RecordEditor\\FO4RecordEditor.exe",
      "args": ["--mcp", "--mo2", "C:\\Modlists\\My Modlist"]
    }
  }
}
```

Use **backslashes, doubled** -- it is JSON, so `C:\Tools` must be written `C:\\Tools`. Use the absolute
path to the exe.

### Choosing the load-order source

The server needs to know what your game sees. Pass exactly one of:

| Argument | Use when | What it does |
|---|---|---|
| `--mo2 <instance>` | You use Mod Organizer 2 | Reads the active profile's `plugins.txt` and rebuilds that exact load order from `mods\`. This is the accurate one -- conflicts and overrides resolve the way they do in game. |
| `--data <folder>` | Vanilla / no mod manager | Uses a plain `Data` folder. |

The MO2 instance path is the folder containing `mods\` and `profiles\` -- not the MO2 program folder,
and not a single mod.

`papyrus_function_lookup` and `papyrus_script_info` read from an offline Creation Kit Wiki HTML
mirror, and work out of the box -- a copy ships bundled with the release package under
`tools\ckwiki\fallout4\`, so there's nothing to configure. Pass `--ck-wiki <folder>` only if you want
to point at a different or newer mirror; this is independent of `--mo2`/`--data` and unrelated to
plugin loading. In the desktop app, the same override lives under Settings -> CK Wiki Path -- the tool
executor reads it live, so it takes effect immediately with no restart and no launch flag. If neither
override is set AND the bundled copy is missing (e.g. a `-SkipCkWiki` dev build), those two tools
return a "not configured" message instead of silently failing.

### Confirming it connected

Restart the client, or in Claude Code run `/mcp reconnect`. Then ask it to call `list_plugins` -- if
you get your load order back, it works. (`ping` is a JSON-RPC method the client uses for its own
health checks, not a tool; asking the AI to "call ping" will not work.)

> **After you rebuild the exe, the running server is the old code.** Stop every `FO4RecordEditor.exe`
> process, rebuild, then `/mcp reconnect`. Skipping the reconnect is the single most common way to
> spend twenty minutes debugging a fix that already worked.

---

## The tool surface

### Plugin lifecycle
`open_plugin` · `create_plugin` · `save_plugin` · `reload_plugin` · `backup_plugin` · `list_plugins`
· `check_plugin` · `clean_plugin` · `strip_masters_clean` · `list_masters` · `reorder_masters`

### Reading and analysis
`get_record` · `get_winning_record` · `list_records` · `list_records_summary` · `list_record_types` ·
`search_records` · `search_all` · `resolve_editorid` · `get_scripts` · `diff_records` ·
`get_referenced_by` · `get_problems` · `search_robco_configs`

### Conflict detection
`get_conflicts` · `scan_conflicts` · `scan_all_plugins` · `scan_broken_refs`

### ModGroups (xEdit's "these plugins are meant to be used together")
`create_mod_group` · `update_mod_group` · `delete_mod_group` · `list_mod_groups`

A ModGroup names a set of two or more plugins that ship as a suite -- a framework plus its official
patches, a compatibility-patch bundle for one mod. Conflicts where **every** plugin touching the
record belongs to one common group are the intended kind, so `scan_conflicts` hides them by default
(`hide_grouped: false` shows them again, tagged `[MODGROUP]`, and the header always reports how many
were suppressed). Membership in *some* group is not enough: the group has to cover every party to
that specific conflict, since a plugin can belong to several groups for several reasons.

On a real 651-plugin load order the raw scan reports ~45,000 conflicting records; this is what makes
that number actionable. Groups are stored as plain JSON in `%APPDATA%\FO4RecordEditor\modgroups.json`
-- a per-installation preference, not something that travels with a plugin. xEdit's GUI half of the
feature (group-picker dialogs, CRC staleness tracking) is deliberately not ported: it exists to
declutter an always-on colored grid a human stares at, and there is no such grid here.

### Writing records
`create_record` · `set_field` · `get_conditions` · `set_conditions` · `set_components` ·
`add_list_item` · `remove_list_item` · `attach_script` · `set_script_property` · `copy_as_override` ·
`copy_as_new_record` · `set_conditions_at` · `delete_record` · `renumber_formid` · `compact_to_esl` ·
`check_esl_eligibility` · `set_light_flag` · `set_localized_flag` ·
`batch_patch_records` · `revert_overrides`

`check_esl_eligibility` is the read-only half of `compact_to_esl`: it reports whether a plugin's
record count and FormID range fit the ESL window before you commit to rewriting anything.
`set_light_flag` / `set_localized_flag` toggle the two TES4 header flags directly.

`set_conditions` REPLACES the whole Conditions list, so to add or change one condition read the
current list with `get_conditions` first, edit that array, and hand it back. `get_conditions` returns
exactly the schema `set_conditions` accepts.

Conditions are not always the record's own list. A magic effect keeps its own at
`Effects[0].Conditions`; a perk keeps them two deep at `Effects[0].Conditions[0].Conditions`, where
the outer list holds the run-on tab wrapper rather than conditions. Pass that path to
`get_conditions` and write it back with `set_conditions_at`. The resolver accepts the list itself,
one entry in it, or a perk tab wrapper, and refuses the tab list with a message saying which level
to use.

`copy_as_new_record` is the other half of xEdit's copy menu: it duplicates a record into the target
plugin under a **brand new FormID** rather than overriding the original, which is how you make a
variant of an existing item.

`deep_copy_as_override` is xEdit's **Deep copy as override**: for a container-type record it brings
the children across too, not just the record you pointed at. `change_referencing_records` is xEdit's
**Change referencing records**, which repoints every record that references one FormID at another --
the tool you want when replacing a record rather than editing it. Both dry-run unless `apply: true`.

### The element menu (xEdit's Add / Remove / Move / Clear on a list row)
`element_describe` · `element_add` · `element_remove` · `element_move` · `element_clear`

This is xEdit's element menu as it actually behaves, read out of
`xeMainForm.pas` (`pmuViewPopup`, `mniViewAddClick`, `mniViewRemoveClick`, `mniViewMoveUpClick`):

- **Add never asks for a value.** xEdit calls `TargetElement.Assign(TargetIndex, template, False)`,
  which constructs an empty element of the container's own type and then focuses it for editing.
  `element_add` does the same: it creates the entry, you fill it in with `set_field`.
- **Add inserts at the clicked row's position.** `GetAddElement` walks up from the focused node
  keeping its index, so right-clicking `Conditions[2]` inserts *before* entry 2. Pass the list path
  instead (`Conditions`) to append.
- **Add offers a choice when the list accepts more than one type.** That is xEdit's
  `GetAssignTemplates`; here it is the concrete subclasses of an abstract element type. A
  `Conditions` list holds the abstract `Condition`, so the templates are `ConditionFloat` and
  `ConditionGlobal`; a perk's `Effects` list offers all twelve `APerkEffect` types.
- `element_describe` reports which of these are legal at a path, so a UI can show only what applies,
  the way xEdit enables each item from `IsRemovable` / `CanMoveUp` / `CanMoveDown` / `IsClearable`.

Unlike `add_list_item`, which only ever appends a FormLink, these work on STRUCT lists: conditions,
effects, leveled entries, components. A FormLink list still works too, and adds an empty link for
you to point somewhere.

Not implemented, because they depend on xEdit's multi-record selection and sibling compare, which
this editor does not have: Copy to selected records, Remove from selected, Compare referenced row,
Sort, Stick, and member switching for unions.

### Whole-plugin xEdit passes
`remove_identical_to_master` · `create_merged_patch` · `add_masters` · `renumber_plugin_formids` ·
`create_seq_file` · `check_circular_leveled_lists`

`remove_identical_to_master` is the ITM half of the standard cleaning pass (`clean_plugin` does the
UDR half). An ITM is an override whose every field equals the version it overrides, so it costs a
conflict row and a load-order slot while changing nothing. Comparison is Mutagen's generated
structural equality over the whole record, and it includes the record header's version control
bytes, so the check errs toward keeping a record: a false "not identical" is harmless, a false
"identical" would delete real edits.

`create_merged_patch` builds a patch carrying the winning version of every record two or more of the
selected plugins touch.

`add_masters` declares a plugin as a master. Needed BEFORE authoring a reference into a file the
plugin does not already master, since the reference has nowhere valid to point otherwise.
`save_plugin` re-derives the master list from actual references, so write the reference before
saving or the added master will not survive.

`renumber_plugin_formids` renumbers every record in a plugin consecutively from a base object id and
repoints all in-plugin references (`renumber_formid` does one record). References from OTHER plugins
into this one will break: they still point at the old ids.

`create_seq_file` writes the `.seq` a plugin's start-game-enabled quests need. Without it those
quests silently never start on an existing save, so it is a required build step for quest mods. The
file is a bare array of little-endian 4-byte FormIDs written with the plugin's own file-local index,
which is what the Creation Kit produces; output was verified byte-identical to a CK-generated SEQ
file from a real modlist. Deploy it as `Data/Seq/<plugin>.seq`.

`check_circular_leveled_lists` finds leveled lists that contain themselves, directly or through
another list. The engine hangs or crashes resolving one. Read-only.

`remove_identical_to_master`, `create_merged_patch` and `renumber_plugin_formids` default to a
**dry run** that reports what they would do; pass `apply: true` to write.

### Cells and placed objects
`create_cell` · `create_placed_object` · `cleanup_placed_references` · `disable_previs` · `precombine_plan`

`create_record` only adds **top-level** records, so it cannot make either of these -- a placed object
lives inside a cell's persistent/temporary list, and cells live in a block tree. This is the pair you
need for map markers and for parking references you relocate at runtime.

`cleanup_placed_references` is xEdit's `Remove duplicate references` / `Remove excess references`
pass: it drops references that duplicate another at the same position with the same base object, and
references beyond what a cell should carry. Dry run by default; pass `apply: true` to write.

`precombine_plan` is phase 1 of generating precombines **without the Creation Kit**: for one interior
cell it reports which placed references are eligible to be baked into a combined mesh, grouped by the
model they use. Read-only -- nothing is written. It also reports why every other reference was
skipped, with counts and examples, because on a real cell the useful question is usually "why did so
few qualify". A real vanilla interior gives a sense of the ratio: `Vault111Cryo` has 2,385 temporary
references, of which 710 are eligible across 108 model groups; the rest are actors and other
non-statics, references with scripts or enable parents or links, and statics with a material swap.

A reference qualifies only if it is not deleted or initially disabled, has no script, enable parent,
teleport destination, activate parent or linked reference, belongs to this plugin rather than being
an override of another's, and its base object is a Static with a model, no script and no material
swap. The reference must be this plugin's own because phase 3 stamps `XCRI` from that FormID
verbatim.

This is also what closes the half of #59 that was given up on. "Filter for precombined statics"
failed because `CellCombinedMeshReference` exposes only a bare mesh index with no link back to a
reference, so `XCRI` cannot be read forwards; this computes the same association from the other
direction and never touches `XCRI`.

Interiors only. Exterior precombines interact with worldspace object LOD, and the tool refuses an
exterior cell rather than producing a plan that would bake the wrong thing.

`disable_previs` is xEdit's **Fallout4 - Disable PreVis** script. Editing an exterior cell without
invalidating its previs/precombine data is one of the most common real FO4 failures -- your edit
silently does not show, or the cell breaks. It clears `PreVisFilesTimestamp`, `InPreVisFileOf`,
`PreCombinedFilesTimestamp` and the combined-mesh lists, and sets the cell's `NoPreVis` flag. Dry run
by default; refuses interior cells and cells that have no precombine data, matching the script's own
guards. The other half of that xEdit script pair (**Filter for precombined statics**) is not ported:
Mutagen's `CellCombinedMeshReference` exposes only a bare mesh index with no FormLink back to the
placed reference, so there is no way to map a combined mesh to its object without new reverse
engineering. Tracked as issue #59.

### Struct lists that `add_list_item` cannot express
`add_leveled_entry` · `set_perk_effects` · `set_magic_effects` · `set_quest_aliases` ·
`set_quest_stages` · `set_quest_objectives` · `set_furniture_markers` · `set_message_buttons`

### Papyrus (built in -- no Champollion needed)
`compile_papyrus` (`.psc` → `.pex`) · `decompile_papyrus` (`.pex` → `.psc`, recursive)

`compile_papyrus` has two engines, chosen with `engine`:

- **`builtin`** is this tool's own compiler -- lexer, parser, resolver, type checker, code generator
  and `.pex` writer -- and needs **no `PapyrusCompiler.exe`**. It does still need the vanilla base
  script *sources* on the import path, to resolve `Form`, `ObjectReference` and the rest; those are
  plain `.psc` text rather than the Windows-only compiler, and `PAPYRUS_BASE_IMPORTS` or
  **Settings → Papyrus base imports** is where you point it at them.
- **`creationkit`** shells out to `PapyrusCompiler.exe`.
- **`auto`**, the default, uses the Creation Kit when one is installed and the built-in engine when
  there is not.

`release` means the same thing to both engines: strip `DebugOnly` and `BetaOnly` calls, which is the
Creation Kit's `-r`. `optimize` is a Creation Kit switch and is reported as ignored by the built-in
engine rather than silently dropped.

The built-in engine **refuses rather than guessing** when a script a call goes into is not on the
import roots: the number of operands a call emits depends on that script's optional parameters, so
there is no safe approximation. A failure naming `PAP0050` or `PAP0051` is almost always a missing
import root and not bad source.

### Papyrus source analysis (built in -- **no Creation Kit needed**)
`papyrus_check` · `papyrus_outline` · `papyrus_definition`

These read `.psc` source with the tool's own Papyrus front end, so they need no `PapyrusCompiler.exe`
and work on a machine with no Creation Kit installed.

- `papyrus_check` checks a script or a whole folder and reports `file(line,col): error CODE: message`
  for each problem: syntax first, then -- with `semantic=true`, the default -- name resolution and
  type checking. Name and type reporting **switches off** for a file whose parent, import or a named
  type is not on the roots, because otherwise every inherited member reads as undefined; those files
  are counted separately in the summary, so a "clean" count never quietly means "could not tell".
  Pass `imports` (semicolon-separated roots) to widen what it can see.
- `papyrus_outline` lists everything a script declares -- header, imports, structs and their members,
  custom events, groups, properties, variables, functions, events, states -- with signatures and
  positions. It still works on a file that has syntax errors, reporting what could be read.
- `papyrus_definition` takes a 1-based line and column and reports where that symbol is declared,
  with its signature and doc comment.

The desktop app exposes the same front end as the Papyrus panel's **Analyze** mode: an editor with a
line gutter, syntax errors and an outline that update as you type, click-to-inspect and
Ctrl+click / F12 go-to-definition (following `Extends` and imports into other files), and Ctrl+S to
save. It edits the buffer, not the file on disk, so the errors it shows are the ones in front of you.

**They check; they do not emit.** A clean `papyrus_check` now covers syntax, names and types, which
is most of what a compile would tell you, but only `compile_papyrus` actually produces a `.pex`.
`papyrus_outline` and `papyrus_definition` are still name-based: `papyrus_definition` answers
`not resolved` for a member reached through an expression whose type is not written in the source
(`GetOwner().MyProperty`) rather than guessing at it.

### Papyrus node graphs
`graph_validate` · `graph_compile` · `graph_palette_search` · `graph_node_info`

These operate on the editor's readable `.fograph` node-graph format:

- `graph_validate` checks graph structure, node types, pins, value types, required connections and
  control-flow rules without writing output.
- `graph_compile` generates readable `.psc` source and optionally compiles it to `.pex`;
  `source_only: true` stops after source generation.
- `graph_palette_search` discovers node definitions from the active Papyrus import roots, including
  functions, events, properties and built-in operators.
- `graph_node_info` describes one definition's input/output pins, control-flow pins, value types,
  optional values and defaults so a graph can be authored without guessing its schema.

### NIF
`nif_import` · `nif_inspect` · `nif_fix` · `nif_verify`

### Archives (BA2/BSA)
`archive_list` · `archive_extract` · `archive_extract_all` -- read packed archives. `archive_pack`
is the other direction, and it is now **written in process: the Creation Kit is not needed**.

Packing used to shell out to the CK's `Archive2.exe`, which is not redistributable and therefore
could not be bundled -- so a user without the Creation Kit installed simply could not pack. The
writer produces General (GNRL) archives, zlib-compressed, storing any file that would grow.

`format: "DDS"` is in process as well. A texture archive stores a per-file texture header and one
chunk per mip range, so each DDS is read for its dimensions, mip count and DXGI format, and its
payload split the way vanilla splits it: **a mip gets its own chunk while it is at least 512x512
pixels, up to three such chunks, then one chunk for all the rest.** That rule reproduces the chunk
layout of all 42,036 texture entries in the 37 vanilla DX10 archives exactly, and the mip arithmetic
behind it reconciles every one of their stored chunk sizes with no slack. A DDS archive holds `.dds`
files only; anything else under the source folder is an error rather than a silent skip.
`use_archive2: true` forces the old Archive2 path, if you specifically need its own byte-for-byte
output.

Textures are also **decoded in process**. The Cell Viewer used to write a temp `.dds`, launch
`Texconv.exe` and read a temp `.png` back for every texture it showed; it now decodes BC1 through
BC7 and the uncompressed layouts directly from the bytes. `Texconv.exe` is still the fallback for
BC6H, the signed BCn spellings, the float formats and 24bpp uncompressed, none of which appear on a
mesh. The decoder was checked against DirectXTex's own output pixel for pixel over 484 real mod
textures: BC2/BC3/BC5/BC7 and the uncompressed layouts exact, BC1 and BC4 within 1 on a rounding
tie. One deliberate difference: an sRGB-tagged texture keeps its stored values, where Texconv
converting to `R8G8B8A8_UNORM` also converted sRGB to linear and darkened it.

The format was taken from the real archives rather than from any spec, and the writer is checked by
rewriting every `.ba2` in a vanilla Data folder and comparing whole files: **all 79 archives, ~31 GB,
versions 1/7/8, both General and DirectX, rewrite byte-for-byte identical.** Two things that sweep
caught, both invisible to a sampling test:

- `DLCCoast - Main.ba2` stores `Strings/DLCCoast_cn.DLSTRINGS` with a **forward slash** while hashing
  the backslash form. Names are stored exactly as authored, never normalized.
- Three names in `Fallout4 - Voices.ba2` are **Windows-1252, not UTF-8** (`María_F.fuz`,
  `María_M.fuz`, `Sánchez_F.fuz`). Entries keep raw name bytes, because decoding those to a
  string and re-encoding replaces the byte and grows the file.

Typical flow to re-encode a mod's packed sounds: `archive_extract_all` its BA2 ->
`audio_convert_to_xwm` on the extracted Sound folder -> `archive_pack` that same folder back into a
new BA2.

### Cells
`cell_get_placed_references` -- list a CELL's placed references (position/rotation/scale + base object
model path), unioned across the whole load order rather than just the winning record (a plugin can
"win" a CELL with an empty reference list while every other plugin's own placed objects still load
in-game). Powers the desktop app's Cell Viewer panel; useful for an AI agent to inspect what's actually
placed in a cell without opening the UI.

`cell_search` -- type-ahead search for CELL records by EditorID/Name/FormKey substring. Use this
instead of `search_all` with `type=CELL`: `search_all` builds a full index of every record type in
every loaded plugin (and caches it for the process's lifetime), which on a real large modlist measured
~2.3 GB and 7 seconds for a single cell search. `cell_search` only ever touches CELL records and caches
nothing.

### Papyrus wiki lookup
`papyrus_function_lookup` · `papyrus_script_info` -- reads an offline Creation Kit Wiki HTML mirror
directly instead of you grepping wiki pages yourself. Works out of the box against the mirror bundled
with the app; `--ck-wiki <folder>` at launch (headless server) or Settings -> CK Wiki Path in the
desktop app only override which mirror is used. Also reachable by hand in the desktop app's Papyrus
panel -> "Wiki Lookup" tab.

### Materials
`bgsm_inspect` · `bgsm_set_field` -- read and edit individual shader fields directly, no
material-editor GUI needed. Both FO4 material formats are supported:

- **`.bgsm`** (lighting materials): Smoothness, SpecularColor, texture paths, PBR/porosity,
  emissive, translucency, terrain, tessellation.
- **`.bgem`** (effect materials -- glowing, additive, refractive and animated surfaces): BaseColor
  and scale, falloff angles and opacities, lighting influence, soft depth, glass (fresnel colour,
  blur and refraction scales), adaptive emissive.

The format is detected from the file's **own magic**, not its extension, because mods do ship
`.bgsm`-named files that are really BGEM and the engine reads the magic. The tool names keep the
`bgsm_` prefix for compatibility.

Both codecs are byte-for-byte ports of `native/materials/src/{base.rs,bgsm.rs,bgem.rs}` from
`Bryant-21/py-creation-lib` (GPL-3.0, permission granted), verified against every material in
`Fallout4 - Materials.ba2`: all 6,623 `.bgsm` and all 283 `.bgem` files round-trip byte-identical,
zero parse failures.

One trap the port preserves deliberately: an empty texture slot is written as `len=1, byte=0x00`,
never `len=0`. A zero-length string there misaligns FO4's own parser and every field after it, which
shows up in game as a pink material rather than as an error.

### Mod folder triage
### Asset resolution
`resolve_asset` -- xEdit's `ResourceExists` / `ResourceContainerList`: given a game-relative path
(`Meshes\Clutter\Rock01.nif`), answer whether it exists anywhere in the load order and **which mod
folder or BA2 actually serves it**, without you having to already know which archive to open. Pass
`extract: true` to also materialize the winning copy as a real file (pulled out of its BA2 if
needed), which is how you hand a packed mesh to `nif_inspect`.

Search order is MO2's own: the overwrite folder, then every enabled mod top-to-bottom of
`modlist.txt`, then the vanilla game Data folder; within one of those, a loose file beats that
folder's archives. Loose lookups are a direct existence check per root, so nothing is scanned; only
archives get indexed, and only the first time a given root is consulted.

This is the tool that makes "verify the MODL path instead of recalling it" a single call. It needs a
loaded modlist (`Open MO2`) or a configured Data folder -- with neither there is nowhere to look, and
it says so rather than reporting "not found".

`audit_asset_usage` -- walk every record in a plugin for asset paths (models, textures, materials,
sounds, scripts) and report which of them actually exist as a loose file or inside a BA2, so a mod
ships without dangling references.

`catalog_mod_folder` -- bucket an unfamiliar mod's files by category (meshes/textures/materials/
sounds/scripts/plugins/archives/voice/...) with per-category counts, to get oriented before deciding
what to inspect next.

### Audio
`audio_convert_to_xwm` · `audio_convert_from_xwm` · `audio_make_fuz` · `audio_extract_fuz` -- convert
any ffmpeg-readable audio/video to Fallout 4's xWMA format (and back), and pack/split the `.fuz` voice
container (xwm + lip sync). `audio_convert_to_xwm` also accepts a whole folder as `source` -- recursed,
converted in parallel, with the original structure preserved (a mod's extracted `Sound\` folder, say).
Works out of the box against ffmpeg/xWMAEncode/BmlFuzEncode/BmlFuzDecode bundled with the app --
nothing to configure. Also reachable by hand in the desktop app's Audio panel.

### Escape hatch
`run_script` -- executes C# against the loaded mod through Mutagen, for anything the tools above do not
cover. `host.New(sig, editorId)` creates records from inside a script.

---

## Rules that keep it from corrupting your plugins

These are not style preferences. Each one is a real failure that has happened.

**1. `create_record` takes signatures; `list_records` takes full names.** `create_record` wants
`COBJ`, `WEAP`, `GLOB`. `list_records` wants `ConstructibleObject`, `Weapon`, `GlobalVariable`. Passing
the wrong form to either returns "no records of that type", which reads like a bug and is not one.

**2. Check the master order after every save on a multi-master plugin.** A plugin whose masters are
written out of load order makes the game **hang on load with no crash log** -- the worst possible
failure mode, because there is nothing to read. After any `save_plugin`, verify the master list.

**3. FormID encoding is `(masterIndex << 24) | objectId` for all masters.** Not just for ESLs.

**4. A new `GLOB`'s value is set via `set_field` on `Data`** -- not by a constructor argument.

**5. `.pas` is assembly, not source.** `compile_papyrus` compiles `.psc`. Feeding it the decompiler's
assembly listing is a common and confusing mistake; the tool now detects it and tells you.

**6. Edit the source plugin directly rather than stacking a fix plugin on top,** unless you have a
specific reason to want an override. Fewer plugins, fewer conflicts, fewer masters.

**7. Deleted records lose their EditorID.** Mutagen strips it. Do not try to filter a deleted-record
pool by EditorID; there is nothing to match on.

---

## When something is wrong

- **The AI reports "not found" for a tool that should exist** → the server is an old build, or it
  never started. Reconnect.
- **`compile_papyrus` says the compiler is missing** → you asked for `engine="creationkit"` and there
  is none. Install the Creation Kit, set `PAPYRUS_COMPILER_PATH` (the compiler is Bethesda's and
  cannot be shipped here), or use `engine="builtin"`, which does not need it.
- **The built-in engine reports `PAP0050` on every vanilla type** → it has no base script sources to
  resolve against. Set `PAPYRUS_BASE_IMPORTS`, or Settings → Papyrus base imports, to a folder of
  vanilla `.psc`.
- **The `nif_*` tools fail** → `tools\niftool\niftool.exe` is missing from the package, or
  `NIFTOOL_PATH` points somewhere stale.
- **Nothing starts at all, no window, no log** → almost always the .NET 9 **Desktop** Runtime is
  missing (the ASP.NET or plain runtime is not enough), or you are launching a self-contained build
  under MO2. Startup traces land next to the exe as `FO4RecordEditor.startup.log`, falling back to
  `%APPDATA%\FO4RecordEditor\startup.log` when the exe folder is read-only.
