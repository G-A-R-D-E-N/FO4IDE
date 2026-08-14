# Wiki map: which docs/internal files have wiki pages

Relationship between the FO4-IDE wiki (`github.com/G-A-R-D-E-N/FO4-IDE/wiki`)
and this `docs/internal` tree. Update this file whenever a page is added,
removed, or re-homed.

## Paired (docs/internal file <-> wiki page)

Each of these has one wiki page of the same name, kept in sync:

| docs/internal | wiki page | notes |
|---|---|---|
| `ARCHITECTURE.md` | ARCHITECTURE | public-scrubbed |
| `GRAPH.md` | GRAPH | public-scrubbed |
| `GRAPH_F4SE.md` | GRAPH_F4SE | public-scrubbed |
| `JSON_RECORD_FORMAT.md` | JSON_RECORD_FORMAT | |
| `MO2_SETUP.md` | MO2_SETUP | rewritten in a user voice |
| `MUTAGEN.md` | MUTAGEN | public-scrubbed |
| `NIF_TOOLCHAIN.md` | NIF_TOOLCHAIN | public-scrubbed |
| `PAPYRUS.md` | PAPYRUS | |
| `PIPELINE.md` | PIPELINE | public-scrubbed |
| `RUNNING_ON_LINUX.md` | RUNNING_ON_LINUX | public-scrubbed |
| `SCRIPTS.md` | SCRIPTS | rewritten in a user voice |
| `SPRIGGIT.md` | SPRIGGIT | public-scrubbed |
| `UI_REDESIGN_TASKS.md` | UI_REDESIGN_TASKS | public-scrubbed |
| `WINDOWS_CI_RUNNER.md` | WINDOWS_CI_RUNNER | public-scrubbed |

> `docs/internal/README.md` is the internal docs index and has **no** wiki
> page. The wiki's README page is a copy of the repository-root
> `README.md`, not this file.

## Wiki-only pages (no docs/internal counterpart)

| wiki page | source in the repo |
|---|---|
| Home | wiki landing page; no repo file |
| License | root `LICENSE` (summary page, not the full text) |
| MCP_SETUP | `docs/MCP_SETUP.md` |
| README | root `README.md` |
| THIRD_PARTY_NOTICES | root `THIRD_PARTY_NOTICES.md` |

## Internal-only docs (no wiki page)

Working notes, not public documentation. They travel in the repo tree
under `docs/internal` but never get a wiki page:

`CONFLICT_ENGINE.md` · `findings.md` · `HISTORY.md` · `HOUSECARL_PULLS.md`
· `INDEX.md` · `KNOWLEDGE.md` · `memory.md` · `MUTAGEN_FORK_COMPARISON.md`
· `PROJECT_BOARD.md` · `XEDIT_PARITY_AUDIT_2026-08.md`

## Rules for keeping this accurate

- **Shared docs are public-clean.** No em dashes, no machine-local paths
  (`E:\...`, `D:\...`), no session dates. The wiki is their public voice
  and `docs/internal` is the canonical source: edit one, mirror the other.
- **Link format differs per home.** Wiki pages link to pages without the
  `.md` suffix (e.g. `(KNOWLEDGE)`); repo copies keep the suffix (e.g.
  `(KNOWLEDGE.md)`) and use `../MCP_SETUP.md` for that doc. Adjust when
  copying between the two.
- **Adding a doc:** decide paired vs internal-only, add the wiki page if
  paired, and update the tables above.
- **Removing a doc:** delete from both places, then update the tables.
- **Re-homing:** a doc moving between the paired and internal-only sets
  must be (un)scrubbed and its wiki page (added or) removed, then the
  tables updated.
