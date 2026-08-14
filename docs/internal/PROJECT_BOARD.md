# PluginEditTool - Project Board

Tracking table for the FO4RecordEditor / PluginEditTool toolchain.

- **Issues:** https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues
- **Board:** https://github.com/orgs/PRISMA-USER-INTERFACE-FRAMEWORK/projects/7
- **Local tracker:** `bd` (`.beads/` at the workspace root) - the `bd` column is the source id.

Statuses match the board's own field: `Todo` / `In Progress` / `Done`.
Seeded 2026-08-05 from the `bd` tracker; the repo and board were both empty before this.

> **Stale as of 2026-08-08.** The tables below are a snapshot taken on 2026-08-05 and have not been
> re-seeded since, so issues filed after that date are not in them at all. Treat GitHub as the source
> of truth and this file as history. Open right now: **#55, #57, #73, #74, #77, #87**. Of those,
> #73/#74 are parked by priority rather than cancelled.
>
> **#78 is done** (2026-08-08). Phase 1 merged as PR #89 (the Papyrus front end, plus the panel's
> Analyze mode); phase 2 as PR #98 -- resolver, type checker, `.pex` writer, code generator, and the
> `engine` parameter on `compile_papyrus` that makes the Creation Kit's `PapyrusCompiler.exe`
> optional. See [PAPYRUS.md](PAPYRUS.md).

## Summary

| Status | Count |
|---|---|
| Todo | 8 |
| In Progress | 8 |
| Done | 28 |
| **Total** | **44** |

> Updated 2026-08-05. Several rows were seeded as open from stale notes but were already fixed in
> source (#4 BA2 v7, #5 the `_modIndexCache` LRU, #6 billboards, #29 the Settings fields); all were
> verified against the code and closed. #23 was fixed by the Papyrus import-root work, #7 shipped
> complete, #8 was closed as not needed, #24/#28 were fixed and merged via PR #32, and #25 (the UI
> redesign) was built in full via PR #34. A native Linux build landed via PR #33.

> Updated 2026-08-05 (later). Fifteen more issues were filed after the note above: the
> xEdit-parity audit (#39-#52) and #55 (Mutagen.Bethesda.Analyzers). Round 1 of the parity work is
> built and verified against the real 651-plugin load order but sits on the unpushed local branch
> `feat/xedit-parity-round-1`, so those rows are In Progress, not Done, until it is pushed and
> merged.

## In Progress

Built and verified locally on `feat/xedit-parity-round-1` (unpushed). MCP tool count 75 -> 83.

| # | Title | Pri | Type | What landed |
|---|---|---|---|---|
| [39](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/39) | xEdit parity: remove Identical to Master (ITM) records | P1 | enhancement | `remove_identical_to_master` + "Remove identical to master" in the grid context menu; dry run by default |
| [40](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/40) | xEdit parity: Create Merged Patch | P1 | enhancement | `create_merged_patch`; dry run by default |
| [41](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/41) | xEdit parity: Copy as new record into... | P1 | enhancement | `copy_as_new_record` + "Copy as new record into..." in the context menu |
| [49](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/49) | xEdit parity: field clipboard helpers | P3 | enhancement | Copy path / value / FormKey / signature in the context menu |
| [44](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/44) | xEdit parity: Add Masters... | P2 | enhancement | `add_masters` + "Add masters..." in the context menu |
| [45](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/45) | xEdit parity: Renumber FormIDs across a whole plugin | P2 | enhancement | `renumber_plugin_formids`; dry run by default |
| [46](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/46) | xEdit parity: Create SEQ file | P2 | enhancement | `create_seq_file`; output verified byte-identical to a CK-generated SEQ from the modlist |
| [47](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/47) | xEdit parity: Check for Circular Leveled Lists | P3 | enhancement | `check_circular_leveled_lists`; read-only, walks winning LVLI/LVLN across the load order |

Also on that branch, and not previously filed as an issue: the grid's right-click menu was gated on
the row having no children, so no list container (Conditions, Components, Keywords) could be
right-clicked at all. That gate is lifted, list rows gained add/remove actions, and Conditions got
a real editor backed by the new `get_conditions` read-back.

## Todo

| # | Title | Pri | Type | bd |
|---|---|---|---|---|
| [42](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/42) | xEdit parity: Deep copy as override (record plus everything it references) | P2 | enhancement | - |
| [43](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/43) | xEdit parity: Change Referencing Records (repoint every reference to another record) | P2 | enhancement | - |
| [55](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/55) | Use Mutagen.Bethesda.Analyzers for the Problems drawer instead of hand-rolled checks | P2 | enhancement | - |
| [27](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/27) | Clean-build warnings all come from the vendored Mutagen fork | P3 | tech-debt | `f4se_og-dt3` |
| [48](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/48) | xEdit parity: ModGroups (suppress known-false conflicts) | P3 | enhancement | - |
| [50](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/50) | xEdit parity: sort the grid by a plugin column, and column width modes | P3 | enhancement | - |
| [51](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/51) | xEdit parity: spreadsheet bulk editors (Weapon, Armor, Ammunition) | P3 | enhancement | - |
| [52](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/52) | xEdit parity: Log Analyzer | P3 | enhancement | - |

## Done

| # | Title | Pri | Type | bd |
|---|---|---|---|---|
| [1](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/1) | AiGuidance advertised only 23 of 57 MCP tools | P1 | bug | `f4se_og-exi` |
| [2](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/2) | `MutagenLoader.SaveEsp` was a non-functional stub that reported success | P1 | bug | `f4se_og-de0` |
| [3](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/3) | BA2 Next-Gen (v8) decompression bug corrupted mesh/texture reads | P1 | bug | `f4se_og-akv` |
| [9](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/9) | `archive_pack` (BA2 packing), batch audio convert, Archive2 Next-Gen fix | P2 | enhancement | `f4se_og-r7g` |
| [10](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/10) | Audio-to-XWM converter (service, interop, GUI panel, MCP tools) | P2 | enhancement | `f4se_og-wc7` |
| [11](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/11) | BA2/BSA archive read tools | P2 | enhancement | `f4se_og-fhy` |
| [12](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/12) | BGSM material field editor | P2 | enhancement | `f4se_og-p6k` |
| [13](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/13) | Papyrus CK-wiki lookup tools | P2 | enhancement | `f4se_og-p1n` |
| [14](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/14) | Mod-folder catalog tool (deliberately MCP-only, no GUI) | P2 | enhancement | `f4se_og-2p4` |
| [15](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/15) | GUI parity pass for Papyrus wiki lookup and mod catalog | P2 | enhancement | `f4se_og-09h` |
| [16](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/16) | Explicit `list_masters` / `reorder_masters` MCP tools | P2 | enhancement | `f4se_og-e86` |
| [17](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/17) | Direct tool to set the ESL/Small header flag | P2 | enhancement | `f4se_og-t8c` |
| [18](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/18) | Audit for env-dropping `WriteService` calls outside `Services/` | P2 | bug | `f4se_og-k75` |
| [20](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/20) | Dead WPF view layer was still bound to live keybindings | P2 | tech-debt | `f4se_og-qmh` |
| [22](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/22) | Two pipe deadlocks in external-process helpers | P2 | bug | `f4se_og-0rn` |
| [19](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/19) | Documented tool counts disagreed with source | P3 | docs | `f4se_og-ij1` |
| [21](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/21) | `SpriggitLoader` and `RecordNode` dirty-tracking were dead code | P3 | tech-debt | `f4se_og-okx` |
| [4](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/4) | BA2 version-7 GNRL byte layout was already handled (v7 hex-verified) | P2 | bug | `f4se_og-o95` |
| [5](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/5) | `MutagenLoader._modIndexCache` LRU eviction, capped at 64 | P2 | tech-debt | `f4se_og-ths` |
| [6](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/6) | Cell Viewer: billboard shapes now camera-facing via util/billboard.ts | P2 | enhancement | `f4se_og-7tu` |
| [26](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/26) | `CompileService` (external Spriggit CLI wrapper) deleted (was dead code) | P3 | tech-debt | `f4se_og-2by` |
| [29](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/29) | Expose `PapyrusCompilerPath` / `PapyrusBaseImports` / `TexconvPath` in Settings | P3 | enhancement | - |
| [7](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/7) | Cell Viewer v1 - shipped complete | P2 | enhancement | `f4se_og-p8l` |
| [8](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/8) | Godot transfer - closed as not needed | P2 | enhancement | `f4se_og-md7` |
| [23](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/23) | Single-file `compile_papyrus` no longer walks the workspace | P2 | bug | `f4se_og-2c4` |
| [24](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/24) | Plugin file handles released; opt-in `ReadLargePluginsIntoMemory` (PR #32) | P2 | bug | `f4se_og-2fz` |
| [25](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/25) | Modern xEdit UI redesign - all 6 phases + 7 backend contracts built (PR #34) | P2 | enhancement | `f4se_og-4wp` |
| [28](https://github.com/PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool/issues/28) | BA2 regression tests verified against real archives and made runnable (PR #32) | P3 | tech-debt | - |

## Keeping this in sync

The GitHub board is the shared view; `bd` stays the working tracker. When an item moves:

1. `bd close <id>` / `bd update <id>` as usual.
2. `gh issue close <number> --repo PRISMA-USER-INTERFACE-FRAMEWORK/PluginEditTool` and move the
   board card to the matching column.
3. Update the row above.

Issues carry `P0`-`P3`, `bug` / `enhancement` / `tech-debt` / `docs`, and `imported` labels. The
`imported` label marks the 29 seeded from `bd`; drop it from anything filed natively on GitHub.
