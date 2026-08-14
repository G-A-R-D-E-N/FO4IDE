# Mutagen fork - the two in-tree copies, compared

There are two Mutagen source trees under `Tools/PluginEditTool/`. They are **not** two
different forks; the second is a trimmed copy of the first. Compared 2026-08-05.

| | A - full fork | B - vendored subset |
|---|---|---|
| Path | `Tools/PluginEditTool/tools/Mutagen/` | `Tools/PluginEditTool/FO4RecordEditor/Mutagen/` |
| Git repo | its own clone of `Mutagen-Modding/Mutagen`, branch `dev` | files tracked inside the `PluginEditTool` repo |
| Projects | all ~30 (Skyrim, Oblivion, Starfield, generators, WPF, tests) | 3 only: Kernel, Core, Fallout4 |
| `.cs` files | 12,607 | 1,460 |
| On disk | 1.3 GB (incl. `.git` and the untracked `Mutagen-dev/` pristine copy) | 370 MB |
| Referenced by a build? | no | yes - both `FO4RecordEditor.csproj` and `FO4RecordEditor.Core.csproj` |

**GodotCK does not carry its own Mutagen.** `FO4RecordEditorGodot.csproj` project-references
`FO4RecordEditor.Core`, which project-references B. So the Godot app and the WPF app compile the
exact same Mutagen - B.

## What actually differs

Across the three shared projects (Kernel, Core, Fallout4), **two files differ. Nothing else.**
`diff -rq` over 1,460 files reports one changed file; the Kernel and Fallout4 projects are
byte-identical.

### 1. `Mutagen.Bethesda.Core/Archives/Ba2/Ba2Reader.cs` - comments only

Both copies carry the same Next-Gen BA2 fix (v7 GNRL / v7 DX10 entry striding, and v8's real
packed-size field). The only difference is presentation:

- **A** keeps it terse and points at `docs/Code Notes/FO4RecordEditor.md`, and uses a
  `hasPackedSizeField` local.
- **B** inlines the full hex-verification write-up (which archives were checked, why `_realSize` is
  garbage for v8, why v7 was excluded from each phantom read) and tests `version > 7` directly.

Same branches, same reads, same results. `git diff` between the two branches over the shared
subset is 41 insertions / 9 deletions, all in this file's comments.

### 2. `Directory.Build.targets` - B adds a build-noise block

B (and only B) forces `IsPackable`, `GenerateDocumentationFile` and `GeneratePackageOnBuild` off and
adds a long `NoWarn` list. Without it a cold build emits ~2,300 warnings, all from upstream Mutagen
code and none from editor code. `GenerateDocumentationFile=false` alone does **not** stop the XML
doc file being generated (root cause still undiagnosed), so `CS1591` is suppressed directly.

`Directory.Build.props` and `Directory.Packages.props` are identical in both.

### Local patches on top of upstream (present in both)

1. `a464f6828` - write short-backed enums directly (EPF3 perk choice flags).
2. `7034bf2de` - Next-Gen BA2 (v7/v8) GNRL and DX10 entry parsing.

## GitLab mirror

Pushed 2026-08-05 to **https://gitlab.com/nomadsreach/mutagen** (private).

| Branch | Contents |
|---|---|
| `plugineditool-full` (default) | A verbatim - full upstream `dev` history plus the two patches |
| `godot-vendored` | B - the same history trimmed to Kernel/Core/Fallout4 with the two deltas above |

Both branches share upstream history, so GitLab's branch compare shows the vendoring delta
directly: https://gitlab.com/nomadsreach/mutagen/-/compare/plugineditool-full...godot-vendored

Upstream is GPL-3.0; the fork keeps `LICENSE.txt` and the patches are documented in
`FO4RecordEditor/THIRD_PARTY_NOTICES.md`.

## Practical notes

- A's working tree shows ~7,000 modified files on Linux. These are **CRLF-only**;
  `git diff --ignore-cr-at-eol` is empty. Do not commit them.
- `Tools/PluginEditTool/tools/Mutagen/Mutagen-dev/` is an untracked pristine upstream copy kept for
  reference - it has none of the patches.
- If a patch is made in B, port it to A (or vice versa) or the two drift silently. Today they agree.
