# Internal engineering docs

Everything in this folder is written for people working **on** FO4RecordEditor, not for people using
it. It is the accumulated knowledge base, design notes and session history for the tool, and it lives
here so it is versioned with the code it describes rather than drifting in a folder beside it.

**These do not ship.** `package.ps1` copies an explicit allowlist into the release zip -- today that
is `docs/MCP_SETUP.md` and nothing else -- and throws if a listed file is missing. Adding a file to
this folder cannot leak it into a release; adding one to the release is a deliberate one-line edit to
`$ShippedDocs`. That allowlist is what makes keeping internal docs in `docs/` safe, and it is why the
older "this repo's `docs/` is end-user-only" rule no longer applies.

The end-user documentation is `../MCP_SETUP.md`, plus `README.md`, `LICENSE` and
`THIRD_PARTY_NOTICES.md` at the repo root.

## Start here

| Doc | What it is |
|---|---|
| [KNOWLEDGE.md](KNOWLEDGE.md) | **The main knowledge base.** Read before any plugin or script work: tool behaviour, the `run_script` rules, FormID encoding, the traps that have cost real time. |
| [INDEX.md](INDEX.md) | Older index of the doc set. |
| [memory.md](memory.md) | Condensed notes from past working sessions -- decisions made, what changed, and the gotchas found along the way. |
| [findings.md](findings.md) | Investigation results, mostly from the crafting and schematic work. |

## The tool itself

| Doc | What it is |
|---|---|
| [ARCHITECTURE.md](ARCHITECTURE.md) | How the app is put together. |
| [CONFLICT_ENGINE.md](CONFLICT_ENGINE.md) | How conflict detection works, and why it compares `DisplaySortKey` rather than strings. |
| [PAPYRUS.md](PAPYRUS.md) | The Papyrus subsystem, end to end: lexer, parser, script index, resolver, type checker, code generator, `.pex` reader/writer, and the two engines behind `compile_papyrus`. What is measured, the language rules the wiki gets wrong, and why the differential test compares instructions rather than bytes. |
| [MUTAGEN.md](MUTAGEN.md) | Notes on the Mutagen record library. |
| [MUTAGEN_FORK_COMPARISON.md](MUTAGEN_FORK_COMPARISON.md) | What the vendored fork changes against upstream, and why those patches are load-bearing. |
| [NIF_TOOLCHAIN.md](NIF_TOOLCHAIN.md) | The `niftool` C++ CLI, the NIF panel, and what is and is not implemented. |
| [JSON_RECORD_FORMAT.md](JSON_RECORD_FORMAT.md) | The record JSON shape the tools read and write. |
| [UI_REDESIGN_TASKS.md](UI_REDESIGN_TASKS.md) | The xEdit-style UI redesign backlog. Still largely unbuilt. |
| [PROJECT_BOARD.md](PROJECT_BOARD.md) | GitHub issue tracking snapshot. |
| [HISTORY.md](HISTORY.md) | How the project got here. |

## Running and building it

| Doc | What it is |
|---|---|
| [RUNNING_ON_LINUX.md](RUNNING_ON_LINUX.md) | The native Linux build: installing it, building the .deb, how the WebKitGTK host stands in for WebView2, and the gotchas. |
| [WINDOWS_CI_RUNNER.md](WINDOWS_CI_RUNNER.md) | Registering the Windows self-hosted runner that the gated `windows-tests` CI leg needs: download, labels, the `RUN_WINDOWS_TESTS` / `FO4RE_TEST_DATA` variables, and first-run expectations. |
| [MO2_SETUP.md](MO2_SETUP.md) | Using the tool with a Mod Organizer 2 instance. |
| [SCRIPTS.md](SCRIPTS.md) | The helper scripts in the tool folder. |
| [SPRIGGIT.md](SPRIGGIT.md) | Spriggit round-tripping notes. |
| [PIPELINE.md](PIPELINE.md) | The crafting-patch build pipeline. |

## A note on `memory.md`

It is a condensed record of past working sessions, so it is the most conversational thing here and it
names local paths and unrelated sibling projects. It is kept because the gotchas in it are real and
expensive to rediscover, but it is session history rather than documentation -- treat it as evidence
of what happened, not as a current description of how anything works. Where it disagrees with
`KNOWLEDGE.md` or the code, it is the one that is out of date.
