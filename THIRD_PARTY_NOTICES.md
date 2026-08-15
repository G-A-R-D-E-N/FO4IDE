# Third-party notices

FO4RecordEditor is distributed under the **GNU General Public License v3.0** (see [LICENSE](LICENSE)).

It has to be. The editor is built directly against **Mutagen** (GPL-3.0) to read and write plugin
binaries, and bundles **niftool**, which embeds **nifly** (GPL-3.0). GPL-3.0 is a copyleft license: a
program that links GPL-3.0 code and is then distributed to anyone else must itself be distributed
under GPL-3.0, with its source made available to the people who receive it. That applies whether it
is published publicly or handed to one person privately.

So: when you send this package to someone, you also owe them the source. Mutagen's patched source is
vendored directly in this repository, under [`Mutagen/`](Mutagen/) -- publishing this repository is how
that obligation is satisfied; there is no separate location to also track.

## Components

| Component | License | Used for |
|---|---|---|
| [Mutagen](https://github.com/Mutagen-Modding/Mutagen) | GPL-3.0 | Reading and writing `.esp`/`.esm`/`.esl` records. Vendored in-repo under `Mutagen/`. **Patched** -- see below. |
| [nifly](https://github.com/ousnius/nifly) (inside `niftool`) | GPL-3.0 | NIF parsing and repair, behind the `nif_*` tools. |

### Patch carried against nifly

One functional change, in the vendored checkout at `Tools/FO4AnimForge/extern/nifly` (commit
`b05bb5b`), which `niftool` builds against:

**`BSPackedGeomData::Sync` overran the block for the SHARED combined-geometry variant.** It read
`numVertices` vertices and `triCountLod*` triangles after `vertexDesc` unconditionally. That is right
for `BSPackedCombinedGeomDataExtra`, which embeds its geometry, and wrong for
`BSPackedCombinedSharedGeomDataExtra`, where the geometry lives in the sibling shape block -- which is
what "shared" means. The block ends after `vertexDesc`.

The overrun desyncs the rest of the file, so the error surfaces later and elsewhere: a garbage string
or block index in whichever block follows. Every real Fallout 4 precombined mesh
(`Meshes\PreCombined\*_OC.NIF`) hit it.

Verified against real vanilla data: across 166 such blocks in 8 real precombines, the on-disk block
size matches the no-embedded-geometry layout exactly, zero mismatches.
| [bcdec.h](https://github.com/iOrange/bcdec) | MIT / Unlicense (dual) | BC1-BC7 block decoding, ported to C# as `Services/Textures/BcnDecoder.cs`. |
| [texconv](https://github.com/microsoft/DirectXTex) (DirectXTex) | MIT | DDS conversion fallback, for the few formats `BcnDecoder` does not handle. |
| [WPF-UI](https://github.com/lepoco/wpfui) | MIT | The desktop UI theme and controls. |
| [Markdig.Wpf](https://github.com/Kryptos-FR/markdig.wpf) | MIT | Markdown rendering in the docs panel. |
| [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json) | MIT | JSON, including the MCP wire format. |
| [Roslyn scripting](https://github.com/dotnet/roslyn) (`Microsoft.CodeAnalysis.CSharp.Scripting`) | MIT | The `run_script` C# escape hatch. |
| [WebView2](https://developer.microsoft.com/microsoft-edge/webview2/) | Microsoft proprietary, redistributable | Hosts the editor's web UI. |
| [ffmpeg](https://ffmpeg.org/) ([BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds) win64-lgpl static build) | LGPL-3.0 | Decodes/transcodes audio (and video's audio track) for the `audio_*` tools. Run as a separate process, not linked -- see below. |
| xWMAEncode.exe (Microsoft, classic DirectX SDK, 2009) | Microsoft, unmodified redistributable | Encodes/decodes Fallout 4's xWMA (`.xwm`) format for the `audio_*` tools. |
| BmlFuzEncode.exe / BmlFuzDecode.exe (BowmoreLover) | Freely redistributed alongside Yakitori Audio Converter | Packs/splits the `.fuz` voice container (xwm + lip) for the `audio_*` tools. |

### bcdec is a port, not a copy

`Services/Textures/BcnDecoder.cs` is a C# port of bcdec.h v0.97 by Sergii Kudlai. It is the same
decoder Godot ships as `modules/bcdec`. Two things are carried across verbatim because they are
fixed spec data and cannot be derived: the 64 two-subset and 64 three-subset BC7 partition tables,
and the per-mode endpoint bit widths. BC6H (HDR) was not ported.

The port was checked against DirectXTex's own conversion of the same files, pixel for pixel, over
484 real mod textures covering every format: BC2, BC3, BC5, BC7 and the uncompressed layouts match
exactly, and BC1 and BC4 differ by at most 1 in the rounding of a rare interpolated value.

## Our Mutagen is patched

The vendored Mutagen (`Mutagen/`) is a fork with seven functional fixes that upstream does not have.
Without them the editor writes broken perk data, misreads Next-Gen archives, or cannot resolve any
record at all on Linux, so a clean upstream Mutagen is **not** a drop-in replacement:

- `Mutagen.Bethesda.Core/Archives/DI/IArchiveListingDetailsProvider.cs` -- a load order listing that
  cannot be determined is no longer fatal. `listingsProvider.Get()` resolves the game-managed load
  order file, which does not exist off Windows (no `%LocalAppData%\<Game>\Plugins.txt`), so it threw
  `InvalidOperationException` there. That `Lazy` is reached while resolving a localized record's
  strings -- which for Fallout 4 happens during **parse**, not only when a name is read -- so merely
  walking a vanilla record was fatal on Linux, and it surfaced as search, referenced-by and record
  views all reporting nothing found. Degrading to an empty list is safe: the set only supplies
  archive ordering and the modKey-less `Contains()` path, while applicability by name
  (`<ModName> - *.ba2`) is untouched, so strings still resolve out of the game archives and **record
  names keep working**.
- `Mutagen.Bethesda.Core/Plugins/Records/AGroup.cs` (`GroupMajorRecordCacheWrapper.Factory`) -- a
  duplicate FormKey inside one group no longer throws `RecordCollisionException`. Groups parse
  lazily while the link cache resolves *any* record, so one malformed plugin anywhere in the load
  order made every record in every plugin unresolvable, with nothing indicating which plugin was at
  fault. Real modlists contain such plugins (`VELDT.esp` ships two grass records sharing `000804`)
  and xEdit loads them. Last occurrence wins, matching the engine, which loads a file's records in
  order into one form table. Nothing is hidden: `check_plugin` and `scan_broken_refs` still report
  the plugin's real problems.

- `Mutagen.Bethesda.Core/Translations/Binary/EnumBinaryTranslation.cs` -- adds the missing case for
  short-backed (2-byte) enums, which previously threw `NotImplementedException` on write.
- `Mutagen.Bethesda.Fallout4/Records/Major Records/Perk.cs` -- writes the EPF3 perk-choice flags as a
  raw 2-byte value, mirroring how the read side already parses them.
- `Mutagen.Bethesda.Core/Archives/Ba2/Ba2Reader.cs` (`BA2FileEntry`) -- fixes Next-Gen (BA2 version
  >= 7) archives reading back as raw, still-compressed bytes instead of the real file. The old code
  guessed at a sentinel-based compressed-flag rule for the newer format (`_realSize != 0xBAADF00D`)
  that never actually holds on real archives; fixed to use the same `size != 0` rule version 1
  already uses, which is empirically correct for Next-Gen too (confirmed identical in upstream
  Mutagen's own `dev` branch before this fix -- not a stale-fork issue). See
  `docs/Code Notes/FO4RecordEditor.md` for the verification.
- `Mutagen.Bethesda.Core/Archives/Ba2/Ba2Reader.cs` (`Ba2Reader`, `BA2FileEntry`, `BA2DX10Entry`,
  `BA2TextureChunk`) -- fixes the BA2 **version-7** entry layout, for BOTH the GNRL (general) and DX10
  (texture) archive classes. v7 is the classic v1 layout in both, but Mutagen read phantom `uint32`s
  and skipped the trailing `0xBAADF00D` align field, mis-striding the whole entry table so every file
  resolved to the wrong data. For GNRL that made general archives (e.g. `DLCRobot - Voices_en.ba2`,
  `.fuz` voice lines) unreadable; for DX10 it threw `Unsupported DDS header format` / arithmetic
  overflow on every vanilla texture (all of which live in the v7 `Fallout4 - Textures*.ba2`) -- i.e.
  the Cell Viewer showed no textures at all. Hex-verified against both `DLCRobot - Voices_en.ba2` and
  `Fallout4 - Textures1.ba2`. Scoped strictly to v7 (v8 already handled above, v8 DX10 left untouched).
  See `docs/Code Notes/FO4RecordEditor.md` for the full byte-level verification.

- `Mutagen.Bethesda.Core/Archives/DI/IArchiveListingDetailsProvider.cs` (`Comparer`) -- an archive
  whose owning plugin is not in the load order threw `KeyNotFoundException` from an unguarded
  dictionary index in `FindListedIndex`, which the sort surfaced as `InvalidOperationException:
  Failed to compare two elements in the array` and took down every call that touched the game
  environment. A Data folder can legitimately hold archives for plugins that are not enabled, so
  this is ordinary input rather than corruption. Unowned archives now sort last. `Compare` also threw
  `NotImplementedException` when two archives shared both priority and suffix, which is reachable
  once unowned archives share a sentinel priority, and a comparer has to return a total order
  regardless; it now falls back to comparing file names.

`Mutagen/Directory.Build.targets` additionally carries one build-configuration-only local change,
clearly marked in a comment at the top of that file: NuGet packaging is turned off (we compile and
link this directly, we never `dotnet pack` it) and a set of warning codes are suppressed. Every one of
them is a pre-existing upstream characteristic of Mutagen's own generated code (missing XML doc
comments, a few harmless member-hiding patterns, and similar), confirmed to be zero editor-code
warnings by checking attribution on every one before suppressing it -- not a functional change, only
build noise reduction so a first `dotnet build` after cloning is not a wall of unrelated warnings.

Only the files listed above are functional patches. Everything else in `Mutagen/` is an unmodified
copy of the upstream source at the point this fork was vendored.

## Offline Creation Kit Wiki mirror

`tools\ckwiki\fallout4\` bundles a static HTML mirror of the community-run Creation Kit Wiki
(creationkit.com/fallout4), used by `papyrus_function_lookup`/`papyrus_script_info` so those tools
work without a network call. It is **not** Bethesda content -- it's wiki text written by the FO4
modding community, originally packaged for reuse as a Nexus "Modder's Resource":

- **Title:** Offline Fallout 4 Creation Kit Wiki
- **Content by:** FO4 CK Wiki Editors
- **Mirror packaged by:** tondabayashi (Nexus Mods)
- **Version bundled:** 21.05.20 (matches this package's `tools\ckwiki\fallout4\` exactly)
- **Tags on the source page:** Modder's Resource, Non-Playable Resource, Utilities for Modders

## Audio tools (`tools\audio\`)

Three third-party executables back the `audio_*` tools, all run as **separate processes** the editor
shells out to (like niftool and texconv) -- none of them are linked into `FO4RecordEditor.dll`, so
none of the "combined work" complexity that applies to Mutagen/nifly above applies here.

- **ffmpeg** -- `tools\audio\ffmpeg.exe` is a static Windows build from
  [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds) (the `win64-lgpl` variant, which
  excludes GPL-only components like x264/x265 -- everything in it is LGPL-3.0 or more permissive).
  LGPL-3.0's source-availability obligation is satisfied by ffmpeg's own upstream source
  (ffmpeg.org) and BtbN's public, reproducible build scripts; nothing about this project modifies
  ffmpeg itself. Replaced the workspace's previous bundled copy, a 2012 nightly (`N-36890`) with
  13 years of unpatched codec parsers -- see the Code Notes entry for that decision.
- **xWMAEncode.exe** -- Microsoft's own tool from the classic DirectX SDK (June 2010 era; this build
  reports itself as "build 9.29.1962.0", copyright 2009). Not open-source, but it is the *only* tool
  that reads/writes Fallout 4's proprietary xWMA format, and it has been freely redistributed by
  essentially every FO4/Skyrim audio-conversion modding tool for over a decade (JohnB's Skyrim Audio
  Converter, BowmoreLover's Yakitori Audio Converter, Backporter's SSE/FO4 Sound-Music Converter,
  and the raw batch-file tools in this workspace's `Tools\Audio Converter\` all bundle the identical
  binary) -- there is no indication Microsoft has ever objected to this, and no alternative exists.
- **BmlFuzEncode.exe / BmlFuzDecode.exe** -- small command-line tools by BowmoreLover, distributed
  alongside Yakitori Audio Converter on Nexus Mods, that pack/split the `.fuz` voice container
  (an xwm audio stream + a `.lip` lip-sync file). Freely redistributed the same way as the tools
  above; no separate license file ships with them.

## What is not here

This package contains **no Bethesda assets** -- no game files, no vanilla scripts, no Creation Kit
binaries. `compile_papyrus` calls the Creation Kit's own `PapyrusCompiler.exe` from your local
install; it is not redistributable and is not included.

xEdit is not bundled either. The editor does not use it or need it.
