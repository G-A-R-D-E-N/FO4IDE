# Plugin Edit Tool — Master Index

Reference hub for editing Fallout 4 plugins (`.esp`/`.esm`/`.esl`). Two distinct things live under
`E:\F4SE OG\Tools\PluginEditTool\`:

- **FO4RecordEditor** — a Fallout 4 plugin IDE (desktop editor + MCP server, 53 tools) built on a
  patched Mutagen. Reads and writes real binaries; does not need xEdit. **This is the primary tool.**
- **The Spriggit text pipeline** — convert a plugin losslessly to a folder of per-record JSON, edit or
  generate it with scripts, compile it back. Best for diffing, git tracking, and bulk generation.

```
   binary .esp  <-->  Spriggit (CLI/UI)  <-->  folder of per-record JSON/YAML
                          built on
                         Mutagen (C# API, validation, generation)  <-- FO4RecordEditor also uses this
```

---

## Start here

| If you are… | Read |
|---|---|
| Using the MCP tools to edit records | [KNOWLEDGE.md](KNOWLEDGE.md) — **the one to read first**; hard rules, `run_script` rules, every gotcha that has cost a session |
| Working on the editor's own code | [ARCHITECTURE.md](ARCHITECTURE.md) — repo facts, invariants, dead code, the 4-site tool rule |
| Generating/compiling plugins as text | [PIPELINE.md](PIPELINE.md) → [SPRIGGIT.md](SPRIGGIT.md) → [JSON_RECORD_FORMAT.md](JSON_RECORD_FORMAT.md) |
| Authoring meshes | [NIF_TOOLCHAIN.md](NIF_TOOLCHAIN.md) |

## All docs in this folder

| File | What it covers |
|---|---|
| [KNOWLEDGE.md](KNOWLEDGE.md) | **The knowledge base.** MCP tool patterns, type-name rules, FormID encoding, `run_script` Rules 0–10, master-order traps, `create_cell`/`create_placed_object`, SW-keyword removal, binary TES4 surgery |
| [ARCHITECTURE.md](ARCHITECTURE.md) | FO4RecordEditor internals: two git roots, the WebView2/React IPC contract, rendering invariants, adding an MCP tool, orphaned WPF code |
| [HISTORY.md](HISTORY.md) | How the editor got here; the abandoned WPF shell; the React pivot; what was never started |
| [UI_REDESIGN_TASKS.md](UI_REDESIGN_TASKS.md) | **Live backlog** — approved 6-phase xEdit-style UI redesign, still unbuilt (`bd f4se_og-4wp`) |
| [CONFLICT_ENGINE.md](CONFLICT_ENGINE.md) | xEdit conflict-detection research (DisplaySortKey, conflict priority) + the plan to beat it |
| [findings.md](findings.md) | Per-session fix log for the `feat/schematic-dedupe` branch (plugins repaired, counts, commits) |
| [PIPELINE.md](PIPELINE.md) | Full round-trip: parse → build → merge → compile → deploy |
| [SPRIGGIT.md](SPRIGGIT.md) | serialize/deserialize CLI, packages, the compile gotcha + bypass, ESL corruption warning |
| [MUTAGEN.md](MUTAGEN.md) | C# generator API: load Fallout4.esm, validate FormKeys, create records |
| [JSON_RECORD_FORMAT.md](JSON_RECORD_FORMAT.md) | Spriggit's per-record JSON layout, naming convention, header/RecordData |
| [NIF_TOOLCHAIN.md](NIF_TOOLCHAIN.md) | `niftool.exe` + `nif_*` MCP tools + GUI panel with a textured 3D viewport (author/repair/verify/view FO4 NIFs, no NifSkope) |
| [SCRIPTS.md](SCRIPTS.md) | Helper scripts in the tool folder |
| [MO2_SETUP.md](MO2_SETUP.md) | Loading an MO2 modlist. ⚠ Contains an unresolved contradiction with the shipped README about running *through* MO2 — read the banner |

**Not here:** end-user docs for the distributable ship from the repo itself —
`Tools/PluginEditTool/FO4RecordEditor/README.md` and `docs/MCP_SETUP.md`. Keep it that way; a
recursive copy of that `docs/` folder is what shipped internal planning notes to end users once
already. `package.ps1` now allowlists exactly one file.

Related, outside this folder (outer workspace, not part of this repository):
`Patched Data/` (crafting/schematic pipeline, ESP build pipeline) · `docs/Code Notes/` ·
`Tools/PluginEditTool/CLAUDE.md` (the agent pointer that loads in that folder).

---

## Tool inventory

### Plugin record editing
| Tool | Path | Role |
|---|---|---|
| **FO4RecordEditor** | `Tools\PluginEditTool\FO4RecordEditor\` | The IDE + MCP server. GUI at `FO4RecordEditor\bin\Release\net9.0-windows\FO4RecordEditor.exe` |
| Mutagen (patched) | `Tools\PluginEditTool\tools\Mutagen\` | C# record library. **Our fork carries patches** (`Perk.cs` EPF3 choice flags, enum serialization) — a clean upstream clone cannot write perk EPF3 data |
| Spriggit (CLI) | `Tools\PluginEditTool\tools\Spriggit\Spriggit.CLI\bin\Release\net9.0\Spriggit.CLI.exe` | serialize/deserialize plugins |
| Spriggit (UI) | `Tools\PluginEditTool\tools\Spriggit\Spriggit.UI\bin\Release\net9.0\Spriggit.exe` | visual workflow, link manager |
| Translation exe cache | `%LOCALAPPDATA%\Temp\Spriggit\Translations\<pkg>\<ver>\<pkg>.exe` | per-game serializer (the compile bypass) |
| niftool | `Tools\PluginEditTool\tools\niftool\` | our C++ NIF engine — see [NIF_TOOLCHAIN.md](NIF_TOOLCHAIN.md) |
| mastersort | `Tools\PluginEditTool\tools\mastersort\` | master-order utility |

Papyrus compile/decompile is **built into FO4RecordEditor** (`compile_papyrus` / `decompile_papyrus`)
-- Champollion is no longer needed, and as of issue #78 neither is the Creation Kit's
`PapyrusCompiler.exe`: `compile_papyrus` has its own compiler and an `engine` parameter that picks
between it and the CK (`auto` prefers an installed CK). The built-in engine still needs the vanilla
base script *sources* on the import path, which are not the compiler and are just `.psc` text. See
[PAPYRUS.md](PAPYRUS.md) for the whole subsystem.

---

## Quick start — Spriggit round-trip

```powershell
cd "E:\F4SE OG\Tools\PluginEditTool\tools\Spriggit"

# Plugin -> JSON folder ("serialize" = parse/extract)
.\spriggit-cli.bat serialize --InputPath "Some.esp" --OutputPath "D:\SomeRepo" `
  --GameRelease Fallout4 --PackageName Spriggit.Json.Fallout4

# JSON folder -> Plugin ("deserialize" = compile)
.\spriggit-cli.bat deserialize --InputPath "D:\SomeRepo" --OutputPath "Some.esp"
```

The words are backwards from intuition — **serialize = plugin→text**, **deserialize = text→plugin**.

## Conventions

- Spriggit pinned to **0.40.1**, package **Spriggit.Json.Fallout4** (JSON, not YAML, for FO4 here).
- Plain ESP (header `Flags: []`) loads last and wins conflicts; ESL/Light loads early and loses.
  Choose deliberately — see [JSON_RECORD_FORMAT.md](JSON_RECORD_FORMAT.md).
- Spriggit `create-plugin`/`deserialize` can corrupt **ESL master FormID refs** on FO4. For ESL-heavy
  patches prefer the xEdit Pascal route — see the gotcha in [SPRIGGIT.md](SPRIGGIT.md).
- **Verify the master order after every save.** A plugin with out-of-order masters makes the game hang
  on load with no crash log. See [[feedback_check_master_order_every_save]].

## Worked reference example

`Projects\Rad Mod\` is a complete working instance of the Spriggit pipeline (Python generates a JSON
record tree → Spriggit compiles `RadiationOverhaul.esp`). Mine it for patterns:
`Tools\PluginEditTool\Patched Data\Source\gen_esl.py`, `merge_overrides.py` (corrected 2026-07-18 —
`Projects\Rad Mod\tooling\` doesn't exist anymore, the scripts moved here), and
`docs\Rad Mod\ESP_BUILD_PIPELINE.md`.
