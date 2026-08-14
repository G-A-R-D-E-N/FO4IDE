# houseCARL feature-pull evaluation

houseCARL (`Avick3110/houseCARL`, GPL-3.0-only, cloned to `Tools/PluginEditTool/tools/houseCARL/`) is a
sibling MCP tool built by the same maintainer's ecosystem, targeting Skyrim SE instead of Fallout 4,
on the same Mutagen library. Same license family as our already-vendored Mutagen fork (see
`THIRD_PARTY_NOTICES.md`), so review/porting needs no separate negotiation. This doc follows the
`MODKIT21_PULLS.md` convention: source read directly, verdicts traced to file:line, not judged by
feature name.

Scope note up front: houseCARL is **Windows + MO2-only** (its README states this explicitly) and its
whole design is organized around "author into a new MO2 mod folder by default, in-place editing is an
opt-in lane" -- a workflow choice, not something this doc evaluates for adoption; only the underlying
mechanisms are in scope.

## Architecture summary

houseCARL is a single C# process (`housecarl-mcp`, an ASP.NET Core / official `ModelContextProtocol`
SDK host) with a shared core library (`housecarl-core`) and a separate build-time reflection tool
(`housecarl-generator`). Directory reality (`src/`):

- **`housecarl-core/`** -- ~53 files. The proven read/write engines: `WriteEngine.cs` (3,594 lines,
  the generic write mechanism), `ReadEngine.cs` (1,116 lines), `RemapEngine.cs` (ESL/merge FormID
  renumbering), `CorpusRulebook.cs` (the runtime pre-flight validator, loads `corpus.json`), plus
  ~30 files of genuinely game-specific logic (dialogue graph validation, SkyPatcher parsing, SKSE DLL
  reading, NIF format I/O, VFS/MO2 asset resolution).
- **`housecarl-mcp/`** -- ~34 files. The MCP tool surface. Only **~50 `[McpServerTool]`-attributed
  methods total** (confirmed via `grep -c "\[McpServerTool"` across the directory), registered through
  the official SDK's assembly-scan mechanism (`Program.cs`), not a hand-built dispatch switch.
- **`housecarl-generator/`** -- ~140 files, almost all named `*Probe.cs`. This is **not** primarily a
  code generator in the sense the task brief anticipated -- it is a combined build-time schema
  extractor (`CorpusGenerator.cs`) *and* the project's entire CI/regression-test harness (`CiAll.cs`
  dispatches ~100+ named probes). One probe, `class-parents` (`ClassParentsEmitter.cs`), is unrelated
  to schema generation (Papyrus decompiler class-hierarchy baseline).
- **`housecarl-setup/`** -- one file, `Program.cs` (672 lines). A plain installer: copies the server +
  skills to `~/.claude/skills/` or `~/.codex/`, registers the MCP server in `~/.claude.json` /
  `~/.codex/config.toml`. No CK-path or compiler auto-detection logic worth pulling; MO2-instance
  discovery is a runtime chat prompt (`SetupTools.cs:housecarl_set_mo2_instance`), not installer-time.

**How a new capability gets added**: for a plain record-field capability, *nothing gets added* -- the
generic `housecarl_apply`/`housecarl_create`/`housecarl_remove`/`housecarl_records` tools already
reach any field Mutagen models, because `WriteEngine.ApplyVerb`
(`housecarl-core/WriteEngine.cs:1701`) walks an arbitrary dot/bracket field-path string via
reflection and applies a generic Set/Add/Remove/ReplaceAll/SetAtIndex/Merge verb at the leaf: *"the
corpus drives pre-flight ([`CorpusRulebook`]), reflection drives the write"* (comment at
`WriteEngine.cs:1697-1698`). For a genuinely new **kind of capability** (SkyPatcher awareness, SKSE
auditing, dialogue graphs), a new core file + a new `[McpServerTool]` method is still hand-written --
that part is exactly as hand-wired as our `PluginToolExecutor.cs`, just for a much smaller surface
area because record-field editing is off the table entirely.

## The reflection-driven generator -- how it works, and FO4 portability

**Mechanism.** `CorpusGenerator.BuildCorpus()` (`housecarl-generator/CorpusGenerator.cs:35-155`) walks
every concrete class in `Mutagen.Bethesda.Skyrim` that implements `IMajorRecordGetter`
(`CorpusGenerator.cs:52-68`), seeds the mod header and mod container, then does a **transitive
reflection closure**: for every field on every discovered type, `ClassifyField`
(`CorpusGenerator.cs:243-377`) recurses into sub-structs, list/dict elements, and polymorphic unions
(the `EnqueueModeledRef` helper, `CorpusGenerator.cs:388-407`, is explicitly the "one place arm
detection happens" fix for a prior bug where union arms were silently dropped inside
lists/dicts/groups). The result is a flat `corpus.json` -- every type, every field, its writability,
cardinality, and (for record types) its xEdit 4-char signature read straight off Mutagen's own
`StaticRegistration.TriggeringRecordType` (`CorpusGenerator.cs:521-533`).

**This corpus is not merely documentation.** It ships *with the binary*
(`housecarl-mcp/Program.cs:74`: `Path.Combine(AppContext.BaseDirectory, "corpus.json")`) and is loaded
at server startup into `CorpusRulebook` (`housecarl-core/CorpusRulebook.cs`), which is the runtime
**pre-flight validator** for every `housecarl_apply`/`housecarl_create` call -- it checks a requested
field path/verb against the corpus before `WriteEngine` touches Mutagen via reflection. A second,
independent emission (`ReferenceEmitter.cs`) renders the same corpus as a slim JSONL tree bundled into
the `mutagen-reference` Claude *skill*, so the LLM can look up "what fields does Armor have" via grep
instead of guessing field names from training data. Same walk, two consumers, so schema and validator
"physically can't disagree" (`ReferenceEmitter.cs:13`).

**Verdict: portable to Fallout4, effort MEDIUM, not small.** The reflection mechanics themselves are
**game-agnostic by construction** -- `CollectAllProperties`, `IsFormLink`, `IsDictionary`,
`MutableInPlaceDefs`, `IsAuthorableArm`, `IsOverlayTwin`, the whole `ClassifyField` cardinality
dispatch -- none of it references Skyrim-specific types; it pattern-matches against
`Mutagen.Bethesda.Plugins`/`Noggog`/BCL generics and the "I{X}Getter"/"strip Getter for mutable"
Loqui naming convention that **every** Mutagen game library follows, FO4 included. What *would* need
to change for a direct port:
1. **Assembly anchor + namespace-string swaps.** The generator anchors on `typeof(IArmorGetter).Assembly`
   (`CorpusGenerator.cs:38`) and seeds `Mutagen.Bethesda.Skyrim.ISkyrimModHeaderGetter`
   (`CorpusGenerator.cs:46`). `WriteEngine.cs`/`ReadEngine.cs` independently hardcode
   `"Mutagen.Bethesda.Skyrim." + X` string concatenation at **11 call sites** (confirmed via grep,
   e.g. `WriteEngine.cs:251,422,729,1176,2079,2229,3248,3275,3353,3465`, `ReadEngine.cs:145`) using
   `typeof(SkyrimMod)`/`typeof(IArmorGetter)` as assembly anchors. Every one is a mechanical
   find-and-replace to `Mutagen.Bethesda.Fallout4`/`typeof(Fallout4Mod)`, but there are enough of them,
   spread across two 1,000+/3,500+-line files, that this is a real (if boring) porting task, not a
   one-line config change.
2. **Container-type literals.** `IsList()` (`CorpusGenerator.cs:633-649`) hardcodes the literal
   fullnames `Mutagen.Bethesda.Skyrim.SkyrimGroup\`1`/`SkyrimListGroup\`1` etc. as the GRUP-container
   recognizer. FO4's Mutagen equivalents are `Fallout4Group<T>`/`Fallout4ListGroup<T>` -- these need
   parallel literal entries (or, better, a namespace-relative check instead of a literal), which is
   exactly the class of bug our own `memory.md` already documents biting us:
   `Fallout4Mod.Cells` (`Fallout4ListGroup<CellBlock>`) not implementing `IGroup`, silently skipping
   CELL records in generic record-walking code (see "highest-severity defects" entry in
   `docs/internal/memory.md`). A port would need to specifically re-verify this container-recognition
   list against every FO4 group shape, CellBlock included, or reintroduce that exact class of bug.
3. **Field-writability audit categories** (`CorpusGenerator.cs:685-728`, the `IsExpectedReadOnly` R0-R4
   rules) are Skyrim-record-name-keyed (`"Region*"`.DataType, `"Global*"`.TypeChar) and would need a
   fresh audit pass against FO4's actual read-only-field surface -- not copy-paste, but the *method*
   (reflect once, categorize every non-writable field, treat any uncategorized one as a regression) is
   directly reusable.
4. **What is NOT a blocker**: license (GPL-3.0, matches our already-vendored Mutagen fork), the
   `Mutagen.Bethesda.Skyrim` NuGet version pin (FO4 has its own `Mutagen.Bethesda.Fallout4` package,
   same Loqui codegen family, same 0.53.x release train), and the polymorphic-arm / enum-collision /
   nullable-substruct fixes baked into `ClassifyField` -- those are corpus-quality fixes earned the
   hard way (see the code comments citing specific historical bugs) and transfer as-is.

Net: a competent port is a multi-day, not multi-week, project -- swap ~15 anchor points, re-run and
re-audit the corpus against FO4's actual type universe (a `write-census`-equivalent proof pass, which
the generator already has a probe pattern for), fix whatever FO4-specific container/union shape trips
the (now-generic) recognizers. The **payoff is structural, not incremental**: our `set_field`
(`FO4RecordEditor/Services/Ai/PluginToolExecutor.cs`) and the per-record-type cases scattered across
`WriteService*.cs` currently grow only when a human notices a missing field and hand-adds a case; a
ported generator would make "every field Mutagen's FO4 model exposes is settable" a build-time
guarantee instead of an ongoing manual backlog, mirroring exactly the cornerstone houseCARL's own
`CLAUDE.md` §3 states this rebuild exists to prove ("coverage is not a scope choice").

**What this is not**: it is not a swap-in replacement for our existing 105 tools overnight. houseCARL
gets its "one generic verb reaches everything" property by giving up per-capability MCP tools --
`set_conditions`, `set_perk_effects`, `add_leveled_entry`, `set_quest_stages` etc. all become the SAME
`housecarl_apply` call with a different field path, which is architecturally cleaner but is also a
different tool-calling contract than the one our existing skills/docs/muscle-memory (KNOWLEDGE.md) are
built around. Porting the generator is a genuine architecture change, not an additive pull.

## Capability-by-capability

| Feature | houseCARL source | Portable to FO4? | Already covered by FO4RecordEditor? | Recommendation |
|---|---|---|---|---|
| Reflection-driven schema generator + runtime pre-flight validator | `housecarl-generator/CorpusGenerator.cs`, `ReferenceEmitter.cs`; `housecarl-core/CorpusRulebook.cs`, `WriteEngine.cs` (`ApplyVerb`, line 1701) | **Yes** -- game-agnostic mechanics, FO4-specific anchor/container swaps needed (medium effort, see above) | No -- our coverage is hand-wired per record type across `WriteService*.cs`/`PluginToolExecutor.cs` | **Port** -- highest structural leverage of anything reviewed |
| VFS asset-layer resolution (loose vs BSA/BA2, MO2 priority, ambiguity flag) | `housecarl-core/AssetResolver.cs`, `Mo2Instance.cs`, `Mo2LoadOrder.cs` | Yes -- pure MO2/archive-priority logic, no Skyrim-specific types | **Yes** -- `resolve_asset`/`audit_asset_usage` (`WriteService.AssetAudit.cs`) + `Mo2ProfileLoader.cs` already do this for FO4/BA2 | Already-have; houseCARL's "return ALL providers + Ambiguous flag" (vs. just the winner) is a minor UX idea worth a look, not a port |
| SkyPatcher layer awareness (INI parse, apply-order, conflict detection) | `housecarl-core/SkyPatcher*.cs` (6 files) | **No** | N/A | Skip -- SkyPatcher is a Skyrim-only third-party runtime-patching mod (Zzyxzz), no FO4 equivalent exists |
| SKSE plugin/DLL static auditing (version-blob read, native-function pairing, config-reference audit) | `housecarl-core/SksePluginReader.cs`, `SkseConfigReferenceExtractor.cs`, `NativePairing.cs`, `SksePeek.cs`; `housecarl-mcp/SkseTools.cs` | Yes **in concept** (F4SE has an analogous static-exported version-struct ABI) but **not** in bytes -- houseCARL's offset map is SKSE/CommonLib-specific (validated byte-for-byte against SKSE's `PluginVersionData` layout, `SksePluginReader.cs:24-26`) | No -- FO4RecordEditor has no F4SE DLL auditing tool at all | **Port the concept, not the code** -- genuine capability gap; needs its own from-scratch F4SE `F4SEPluginVersionData` struct-layout reverse-engineering pass (medium effort) before any code is reusable |
| Nexus Mods lookups (catalogue search, mod detail, update-check, MD5 identify) | `housecarl-mcp/NexusClient.cs`, `NexusTools.cs` | **Yes, trivially** -- keyless public GraphQL API, only a game-domain-id swap (`SkyrimSeGameId = 1704` at `NexusClient.cs:19` -> Fallout 4's Nexus id) | No | **Port** -- small effort, real value (mod version/requirement/update lookups without a browser) |
| Dialogue-graph authoring/validation (DIAL/INFO creation, quest/branch wiring, INFO merge-order model) | `housecarl-core/Dialogue*.cs` (6 files) | Partially -- FO4 has DIAL/INFO/quest aliases too, but the merge-order and CK-parity findings (`DialogueValidate.cs:14-24`, e.g. "topic winner's child list is NOT authoritative, every touching plugin's INFOs merge") are Skyrim-CK-behavior findings that would need independent FO4 verification, not an assumed carryover | Partially -- we have `set_quest_aliases`/`set_quest_stages`/`set_quest_objectives` (authoring) but no dialogue-graph *validator* (broken LinkTo chains, dangling PNAM, INFO merge-order audit) | Skip for now -- real idea, but the hard part (empirically re-deriving FO4's own INFO-merge semantics) is a standalone research project, not a code port |
| Mesh (.nif) internals read/write | `housecarl-core/NifService.cs` (NiflySharp, pure C#, no native shell-out) | Yes mechanically | **Yes** -- we already have a self-contained NIF toolchain (`niftool.exe` + `NifService.cs`/`NifPanel.tsx`/`NifEditor.tsx`, see `docs/internal/NIF_TOOLCHAIN.md`) | Already-have -- redundant, not a gap. (Side note, not a recommendation: houseCARL's NiflySharp is a pure-C# NuGet with no native process shell-out, vs. our C++ `niftool.exe` subprocess -- a possible future simplification, but replacing a working toolchain is out of scope here.) |

## Top 5 if we only port one thing

Ranked by value/effort, generator evaluated explicitly as candidate #1:

1. **The reflection-driven generator + runtime pre-flight validator** (`CorpusGenerator.cs` +
   `CorpusRulebook.cs`/`WriteEngine.ApplyVerb` pattern). Highest ceiling of anything in this review --
   it eliminates the entire class of "field X isn't exposed yet" gap reports our own `memory.md`
   session history shows recurring (Rule 10-style "prefer direct source edits", multiple sessions
   individually hand-adding `set_field` cases). Medium effort (est. multi-day: swap ~15 anchor points,
   re-run corpus against `Mutagen.Bethesda.Fallout4`, audit and fix FO4-specific container/union shapes
   like `Fallout4ListGroup<CellBlock>`). Architecture change, not a drop-in addition -- would coexist
   with, not replace, today's named tools unless we also decide to collapse the tool surface the way
   houseCARL did.
2. **Nexus Mods lookups.** Smallest effort of anything in this table (one HTTP client, one game-domain
   ID swap) for real, self-contained value: mod version/update/requirement checks without a browser,
   directly useful for load-order and compatibility work FO4RecordEditor already touches.
3. **SKSE/F4SE DLL static auditing -- concept port.** Real, currently-unfilled gap (no code today
   audits whether an F4SE plugin DLL is present/right-arch/right-runtime/debug-build, or whether a
   script's declared native function is actually implemented by an installed DLL). Not a code port --
   F4SE's version-struct ABI needs its own from-scratch byte-level verification pass, same rigor
   `SksePluginReader.cs`'s header comments show houseCARL applied to SKSE -- but the *shape* of the
   capability (static PE-header read, no execution, tiered "what can/can't be known statically"
   reporting) transfers directly.
4. **VFS "return all providers + ambiguity flag" idea.** Not a port (we already have equivalent
   coverage via `resolve_asset`/`audit_asset_usage`), but a genuinely useful small enhancement: surface
   every contending mod for an asset path instead of only the winner, which houseCARL's own
   `AssetResolver.cs` comments frame as valuable specifically for facegen/dark-face-style contention
   diagnosis.
5. **Dialogue-graph validator, deferred.** Real capability gap (we author DIAL/INFO structure but
   don't validate the resulting graph -- broken LinkTo chains, dangling PNAM, INFO merge-order), but
   houseCARL's own findings are explicitly Skyrim-CK-behavior discoveries (`DialogueValidate.cs`'s
   comment history documents a wrong initial model that had to be empirically corrected against real
   Skyrim data) -- porting the code without redoing that empirical verification against FO4's own CK
   behavior would just import unverified assumptions. Worth doing eventually, not worth rushing.

**Not recommended**: SkyPatcher (no FO4 equivalent, confirmed), NIF internals editing (redundant --
own `niftool` toolchain already covers this per `docs/internal/NIF_TOOLCHAIN.md`), houseCARL-setup's
installer plumbing (no CK/compiler auto-detection logic exists there to pull; MO2-instance discovery
is a runtime chat prompt in houseCARL too, not installer-time smarts).

## Claude_MO2 (deprecated predecessor) findings

`Claude_MO2` (`Avick3110/claude-mo2`, MIT, cloned to `Tools/PluginEditTool/tools/Claude_MO2/`) is explicitly
marked `***Depreciated***` in its own `README.md:1` ("New version here: houseCARL"). It predates
houseCARL and is architecturally distinct from it, from FO4RecordEditor, and from Skyrim-side tooling
generally: it runs **inside MO2's own process** as an MO2 Python plugin, not as a separate program.

### Architecture -- hosted-in-MO2 vs. standalone-external

`mo2_mcp/__init__.py:106` declares `class Mo2McpPlugin(mobase.IPluginTool)` -- a plugin MO2 itself
loads and drives via its own `mobase` Python API, registered through MO2's Tools menu ("Start/Stop
Claude Server", `README.md:17`). `init()` (`__init__.py:117-120`) receives MO2's live
`mobase.IOrganizer` handle and hooks `onAboutToRun`/`onFinishedRun` so the HTTP MCP server
auto-stops before MO2 launches a game/tool executable and restarts after
(`__init__.py:78-104,238-269` -- an explicit deny-list, `_AUTOSTOP_EXEMPT_PATTERN`
`__init__.py:94-101`, keeps the server alive across the whole xEdit family so a user can have xEdit
open for interactive viewing while Claude reads records concurrently). On first start it self-registers
into `~/.claude.json`'s `mcpServers.mo2` as an HTTP transport at `http://127.0.0.1:<port>/mcp`
(`_ensure_claude_mcp_config`, `__init__.py:33-70`, atomic temp-file + `os.replace` write).

**How it gets "live" VFS/profile truth**: every filesystem/modlist tool calls MO2's own C++ VFS engine
directly through the `mobase` bindings, not a re-parsed config file --
`organizer.resolvePath()` / `organizer.getFileOrigins()` / `organizer.findFiles()`
(`tools_filesystem.py:173,178,202,236,269,323`), `organizer.modsPath()` /
`organizer.overwritePath()` / `organizer.managedGame().dataDirectory()`
(`tools_filesystem.py:205-209`), and mod/plugin state via the live `mobase.ModState` /
`mobase.PluginState` enums (`tools_modlist.py:23,192,251,287,323,327,334,340`). There is no on-disk
`ModOrganizer.ini`/`modlist.txt` parsing anywhere in this plugin -- MO2 has already done that work and
exposes the resolved result live.

Contrast with our own `FO4RecordEditor.Core/Services/Mo2ProfileLoader.cs`: `ResolveOverwriteFolder`
(`Mo2ProfileLoader.cs:229-260`) and the active-profile lookup (`Mo2ProfileLoader.cs:197-228`) both
read `ModOrganizer.ini` off disk with `File.ReadAllLines` (`Mo2ProfileLoader.cs:202,235`), and `Load()`
(`Mo2ProfileLoader.cs:301-368`) separately reads `modlist.txt` (`:309,336`) and `plugins.txt` (`:314`)
-- a point-in-time re-implementation of MO2's own config parsing, done once at load, entirely external
to any running MO2 process, exactly the architecture the task brief asked to evaluate against.

**Verdict: real tradeoff, not a strict upgrade -- do not adopt in-process hosting.** The live-API model
is categorically more correct for *staleness*: it can never desync from a mid-session profile switch or
mod toggle, and `getFileOrigins()` is ground-truth conflict-winner provenance straight from the engine
that actually built the VFS, vs. our own re-derivation of the same ordering from text files. But it buys
that correctness by requiring the tool to live and run *inside* MO2's Python plugin runtime --
Windows-only, MO2-must-be-running-only, one HTTP server per MO2 launch. FO4RecordEditor is deliberately
a standalone process that also has to work with plugins loaded directly (no MO2 at all) or via other mod
managers, which the in-process model structurally cannot do. Adopting it would mean either forking the
architecture in two (a from-MO2 mode and a standalone mode) or dropping standalone/cross-manager support
entirely -- not worth it for a staleness class of bug that in practice is already mitigated by the
documented "launch once through MO2, then run standalone" workflow (`memory.md`'s
"Organize plugin tool docs..." entry; `docs/Plugin Tool/MO2_SETUP.md`).

**One narrow, real idea worth taking, not the architecture**: `Mo2ProfileLoader.Load()` parses instance
state exactly once per process launch with no re-validation exposed anywhere in the MCP tool surface --
if the user changes the active MO2 profile or toggles mods without restarting FO4RecordEditor, nothing
in chat can detect or fix that short of a full relaunch. Claude_MO2 (via its always-live API) and
houseCARL (via an explicit `housecarl_load_order_status` / `housecarl_set_mo2_instance` freshness-probe
pair, see next section) both have an answer to this; we have none. A thin "which instance/profile am I
reading, and is it still current" MCP tool over the already-parsed `Mo2ProfileLoader` state (plus a
callable re-`Load()`) would close this gap without touching the standalone architecture. Not filed as an
issue here since it's outside this task's scope -- noted for the next capability pass.

### Capability deltas vs. the houseCARL review (29-tool table, `README.md:86-96`)

Cross-checked every category in `Claude_MO2`'s tool-reference table against both the houseCARL findings
above and FO4RecordEditor's own current tool surface. Nearly everything is redundant with one or the
other; only one row needed real digging, and it turned out to be a further "skip," not a delta:

| Claude_MO2 category | Verdict |
|---|---|
| Modlist / File System / Write (`mo2_list_mods`, `mo2_resolve_path`, `mo2_list_files`, `mo2_read_file`, `mo2_write_file`) | Already assessed via houseCARL's VFS row (already-have: `resolve_asset`/`audit_asset_usage`/`Mo2ProfileLoader.cs`) -- skip |
| Records / Conflicts (`mo2_query_records`, `mo2_conflict_chain`, `mo2_plugin_conflicts`, `mo2_conflict_summary`) | Same territory as houseCARL's generic read engine, already covered by our own `get_record`/`get_conflicts`/`scan_conflicts`/`get_winning_record` -- skip |
| ESP Patching (`mo2_create_patch`) | Same territory as houseCARL's `WriteEngine`/`housecarl_apply` -- already covered by our named per-capability tool set -- skip |
| Papyrus (`mo2_compile_script`) | Already covered (`compile_papyrus`); Claude_MO2 explicitly has **no decompiler** ("no currently-available decompiler produces clean round-trip output", `README.md:32`) while we ship `decompile_papyrus` -- we're ahead here, not a gap, just a one-line note |
| BSA/BA2 (`mo2_list_bsa`, `mo2_extract_bsa*`, `mo2_validate_bsa`) | Already covered by our BA2 archive tools (`archive_list`/`archive_extract*`/`archive_pack`) -- skip |
| NIF (`mo2_nif_info`, `mo2_nif_list_textures`, `mo2_nif_shader_info`) | Redundant with our own `niftool` toolchain, already flagged in the houseCARL NIF row above -- skip |
| Audio (`mo2_audio_info`, `mo2_extract_fuz`) | Already covered by our own `audio_extract_fuz`/`audio_make_fuz`/`audio_convert_from_xwm`/`audio_convert_to_xwm` -- skip |
| **`mo2_analyze_dll`** (SKSE DLL PE analysis) | **Checked in depth, confirmed not a delta.** `tools_dll.py` shells out to the vendored pure-Python `pefile` library for generic PE metadata (imports/exports/version info/filtered strings, `tools_dll.py:19-22`) and detects "SKSE-ness" only by checking for the **presence of exported symbol names** -- `SKSEPlugin_Version`/`Query`/`Load` string matches against the export table (`tools_dll.py:230-239`). It does **not** parse the actual `SKSEPluginVersionData` struct bytes (version number, author, compatible-version bitfield) the way houseCARL's `SksePluginReader.cs` does (byte-validated against SKSE's real struct layout, per the houseCARL table above). This is the *same* capability class already assessed as houseCARL finding #3 ("SKSE/F4SE DLL static auditing -- concept port"), and it's a **shallower** implementation than houseCARL's, not a broader one -- confirms the existing recommendation (port the concept -- static PE-header + export-table read, tiered "what can/can't be known statically" -- not any specific codebase) rather than adding a new one |

**Net finding**: nothing in Claude_MO2 that houseCARL didn't already surface (or surface more deeply).
The only thing genuinely worth carrying forward from this repo is the architectural lesson above (the
in-process-vs-external tradeoff), not a capability.

## authoria-requiem-skills (skill-pattern) findings

`authoria-requiem-skills` (`DrHeisen`/`Avick3110` ecosystem, MIT, cloned to
`Tools/PluginEditTool/tools/authoria-requiem-skills/`) is not a code project -- it is eleven Claude Code
Skills (`authoria-requiem/skills/<name>/SKILL.md` + `references/` + `evals/`) that use houseCARL as a
**read-only live data layer** to auto-patch arbitrary new Skyrim SE mods for compatibility with one
specific total-overhaul mod, Requiem, emitting an ESP override for a separate build-time tool
(Reqtificator) to integrate. There is no code here to port; the transferable asset is the **method**.

### The method, concretely (from the router skill, `requiem-patching/SKILL.md`)

The router (`SKILL.md:1-4`) is loaded first for "patch mod X for Requiem" and walks a fixed sequence,
each step a specific houseCARL MCP call shape:

1. **Freshness probe** -- confirm the data layer is pointed at the right load order before trusting
   anything it returns: `housecarl_load_order_status`, and `housecarl_set_mo2_instance path="..."` to
   fix it if wrong (`SKILL.md:31-40`).
2. **Whole-plugin enumeration** -- `cross_plugin_query plugins=["<NewMod>.esp"]` returns *every* record
   the target plugin adds or overrides in one call, with type and override depth, becoming the worklist
   (`SKILL.md:41-48`).
3. **Triage** -- classify every enumerated record as new-content / brushed-override / cosmetic-skip;
   the full enumeration stays the coverage denominator a final reconciliation step checks against
   (`SKILL.md:49-56,147-150`) -- "a silently skipped type is the field failure this rule kills"
   (`SKILL.md:97`).
4. **Per-record-type routing** to one of nine domain skills (`SKILL.md:76-98`), each of which reads the
   record's own comparable in Requiem's live winning stack, derives from it (**"live-analogy, never
   hardcoded numbers"**, `README.md:11`), and authors the corresponding field/keyword/perk/placement.
5. **Verification gate before write** -- "every derived/carried FormID `housecarl_resolve`-verified...
   right FormID *and* right master suffix" (`SKILL.md:154-156`).
6. Authority throughout is "the live conflict winner" read through houseCARL (`SKILL.md:165-168`,
   `README.md:49`) -- never a hardcoded/cached snapshot.

### FO4 MCP-surface readiness -- call-by-call against FO4RecordEditor's current tool set

Mapping each call shape the router actually uses to what FO4RecordEditor exposes today (tool schemas
pulled live from the MCP registry, not recalled from docs):

| Router call shape | Purpose | FO4RecordEditor equivalent | Verdict |
|---|---|---|---|
| `housecarl_load_order_status` / `housecarl_set_mo2_instance` | Confirm/fix which MO2 instance the data layer reads before trusting it | **None found.** Searched the full tool surface for anything MO2-instance/profile-status-shaped -- no match. `list_plugins` reports what's loaded but not which profile/instance, and there is no chat-callable way to switch or re-validate it (see `Mo2ProfileLoader.cs:301-368`, parsed once at process launch, per the Claude_MO2 section above) | **Real gap** -- same one flagged against Claude_MO2 above, now confirmed twice from two independent angles |
| `cross_plugin_query plugins=[...]` (one call: every record a plugin touches, typed, with override depth) | Build the whole-mod worklist | Reconstructable via `list_record_types(plugin)` (type+count) then `list_records`/`list_records_summary(plugin, type)` per discovered type -- but that's N+1 calls, and none of them report override depth; getting depth per record needs a further `get_conflicts(id)` call each | **Minor gap** -- achievable, not efficient; no single-call whole-plugin enumeration-with-depth exists today |
| Record read with `conflict_tree=true` (full override chain) | Inspect every plugin touching a record before patching | `get_conflicts(id)` -- full override chain **plus** a per-field diff table and severity tags (`[OVERRIDE]`/`[CONFLICT]`/`[CRITICAL]`) | **Covered, arguably richer** |
| "Live conflict winner" as authority | The value to derive from | `get_winning_record(id)` | **Covered** |
| `housecarl_resolve` (pre-write FormID verification) | Confirm a derived/carried FormID is real before writing it | `resolve_editorid(id)` | **Covered** |
| Sanity-check the overhaul mod is present/active (`Requiem.esp` in a record's override chain) | Confirms the data layer sees the target overhaul at all | `scan_conflicts` (whole-load-order survey) / `get_conflicts` on a known vanilla record | **Covered** |
| Search by name when FormID unknown | Locate a comparable record | `search_all` | **Covered** |
| The actual patch-authoring step (each domain skill's field/keyword/perk/placement writes) | Write the derived override | `set_field`, `set_conditions`, `set_components`, `add_list_item`, `element_add`/`element_remove`/`element_move`, `copy_as_override`, `add_leveled_entry`, `set_perk_effects`, `set_magic_effects`, `batch_patch_records`, etc. | **Covered, broader** -- houseCARL collapses all authoring into one generic `housecarl_apply` verb (architecturally cleaner per the houseCARL section above, but not a wider capability); our named per-capability surface reaches the same field space through explicit, more discoverable tools |

**Verdict: the data-layer readiness is essentially there.** Every read primitive the router actually
calls has a direct -- in one case (`get_conflicts`) a richer -- FO4RecordEditor equivalent, and the
authoring surface a domain skill would call into is already broader than houseCARL's own. The one real,
novel gap is the freshness/instance-status pair, independently confirmed from both new repos in this
review; it's small and additive (a status tool over already-parsed `Mo2ProfileLoader` state, not an
architecture change), not a blocker. The per-plugin bulk-enumeration-with-depth gap is real but minor --
workable today in more calls, worth a convenience tool if this pattern is ever built, not urgent on its
own.

**Also transferable, independent of the MCP question**: the skill-pack's own authoring discipline --
one router skill dispatching to narrow domain skills, a binding written standard
(`standards/HOUSECARL_SKILL_AUTHORING.md`) with a numbered reviewer checklist, fresh-context
agent-fan-out validation of each skill description before shipping (`CLAUDE.md`'s "Authoring standards"
section), and a hard "every enumerated record gets a disposition, nothing silently dropped"
reconciliation rule (`SKILL.md:94-97,147-150`) -- is a proven, working process for building this *kind*
of tool-using skill pack, reusable regardless of which record types or overhaul mod it eventually
targets.

**Deliberately not addressed**: which Fallout 4 overhaul mod (if any) an analogous pack should target.
That's a product decision for the user, not something to infer from this review. This section only
answers "is the methodology sound and is our MCP surface ready to be its data layer" -- both yes, modulo
the one freshness-tool gap above.
