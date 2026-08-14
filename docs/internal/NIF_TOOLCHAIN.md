# NIF Toolchain: author / repair / verify / **view** FO4 NIFs (kill NifSkope)

Self-contained Fallout 4 NIF authoring built into the FO4RecordEditor toolchain. Take a
Blender-exported OBJ → a game-ready FO4 NIF (shader, textures, tangents, BSX, collision), **see it
in a live 3D viewport with its DDS textures**, verify it, and wire it onto a record's `Model`, with
no NifSkope and no Creation Kit.

The engine is `niftool.exe`; it and the app live in this repository (`tools/niftool/`,
`FO4RecordEditor/`). This page documents the whole NIF toolchain.

---

## Architecture

```
Blender ──OBJ──▶ niftool.exe (C++/nifly) ──▶ FO4 .nif ──▶ record Model.File (set_field) ──▶ game
                     ▲   │                                     (existing record tools)
                     │   └─ geo JSON / verify / inspect / fix
   FO4RecordEditor (C#/WPF + React WebView2)
     • MCP tools  nif_import/nif_inspect/nif_verify/nif_fix   → NifService  → niftool.exe
     • GUI panel  View / Import / Inspect / Verify / Fix       → NifInterop  → NifService/TextureService
     • 3D viewport (three.js) with DDS textures via Texconv
```

- **Engine = our own `niftool.exe`** (C++, xmake/MSVC) linking the ONE complete nifly copy at
  `Tools\FO4AnimForge\extern\nifly`. Shelled out from C# (the `PapyrusService` pattern) for crash
  isolation, the "repair broken NIFs" path feeds malformed files to native code. **Not** P/Invoke of
  the external `NiflyDLL.dll` (self-containment + crash safety). Collision recipe ported from
  pynifly's `NiflyWrapper.cpp` (raw nifly has no collision helpers).
- **Record tie-in already solved:** `set_field` maps `Model`/`nif`/`Model.File` onto any
  STAT/WEAP/MISC/ARMO, no Creation Kit.

> **Planned replacement.** The nifly-based engine is **temporary**: embedding nifly makes
> the whole binary GPL-3.0, which is why the tool ships as an external exe rather than in-process.
> We will eventually write our own NIF implementation. The drop-in surface is the CLI contract
> (`NifService` → `niftool <cmd>` with JSON on stdout) and the `ToolPaths` resolution order, a
> replacement binary needs only to implement the same commands, not touch the C# side. Until then,
> a release built without `niftool.exe` simply has dead `nif_*` tools (package.ps1 warns and
> continues).

---

## Implemented

### Engine: `niftool.exe`
- `import <obj> <nif>`: OBJ → FO4 `BSTriShape` + `BSLightingShaderProperty` + texture slots +
  computed normals/tangents + `BSXFlags` + optional `--collision box`. Options: `--material`,
  `--tex-diffuse`, `--tex-normal`, `--collision box`, `--from-blender` (Y-up→Z-up). Auto-verifies.
- `inspect <nif>`: JSON: FO4?, per-shape {name, verts, tris, skinned, shader, textures, tangents},
  hasBSX, hasCollision.
- `geo <nif>`: full flat vert/tri/normal/uv arrays + per-shape texture paths (feeds the 3D viewer).
- `verify <nif>`: `RESULT: N checks, M failed` + `[ok]/[FAIL]` per check.
- `fix <in> <out>`: recompute tangents, add missing BSX, trim texture paths, fix shader/BSX flags.
- Hand-written OBJ parser (dedup by v/vt/vn, fan triangulation), box-collision wiring
  (`bhkCollisionObject → bhkRigidBodyT → bhkBoxShape`, Havok scale `0.0142875`, layer OL_STATIC).
- **Verified:** cube OBJ → NIF, verify 9/9, `--collision box` → inspect `hasCollision:true`.

### MCP tools (for the AI): in `PluginToolExecutor`
- `nif_import`, `nif_inspect`, `nif_verify`, `nif_fix` → `NifService.cs` (shell-out). Activate with
  **`/mcp reconnect`** after a C# rebuild.

### Editable block tree: **Edit** mode
The step toward real NifSkope parity: a *curated, user-friendly* property editor (not a raw block dump).
- **Engine:** `niftool tree <nif>` dumps a grouped JSON tree: **Nodes** (name, transform), **Shapes**
  (name, transform, material `.bgsm`, emissive/specular color, glossiness, opacity, UV offset/scale,
  all 8 texture slots), **Collision** (box), **Extra** (BSXFlags). `niftool set <in> <out> <edits.json>`
  applies a JSON array of `{id,cat,key,value}` edits and saves. Each field carries a stable address
  (block id + key). **Smoke-tested:** rename + opacity + emissive color + normal-slot + node-translation
  + BSX all round-tripped and re-verified.
- **Phase 2:** the shape cards also carry a curated **Shader Flags** checkbox grid (17
  common SLSF1/2 bits, Specular, Cast/Receive Shadows, Environment Map, Decal, Double-Sided,
  Z-Buffer, Glow Map, Tree Anim, …, only curated bits are touched, others preserved);
  **Alpha property** (Alpha Blend / Alpha Test / Threshold, *creates a `NiAlphaProperty` on save if
  the shape has none*); **effect-shader** (`BSEffectShaderProperty`) fields (base/emittance color,
  base-color scale, env-map scale, falloff opacity, soft-falloff depth, source/greyscale/normal
  textures); and a **Collision** group (rigid-body layer, box half-extents, radius, Havok material).
  All round-trip-tested. Flags work on lighting *and* effect shaders.
- **C#:** `NifService.Tree()` / `NifService.ApplyEdits()` (saves to a sibling `.niftmp` then swaps over
  the target, so a failed save can't corrupt the original; blank out-path = save in place). Bridged by
  `NifInterop.Tree` / `NifInterop.ApplyEdits`.
- **UI:** `NifEditor.tsx`: grouped collapsible cards; one input per field type (text, number, toggle
  switch, vec2/3, **color swatch**, texture path + Browse); changed fields highlighted with per-field
  revert; sticky footer with a live unsaved-change count and **Save / Save As… / Revert**. Edit mode is
  a **two-pane** layout: editor on the left, the live 3D textured viewport on the right; a save reloads
  both. Fields route shader/material/texture edits through the shape so everything for a mesh is in one
  place.

### GUI panel: activity-rail **Box** icon (under Papyrus)
- React `NifPanel.tsx`, modes **View / Edit / Import / Inspect / Verify / Fix**, drag-drop, native pickers.
  Bridged by WebView2 host object `NifInterop.cs` (registered `"nif"`).
- **3D viewport (View mode):** live three.js WebGL canvas: rotate/zoom/pan like NifSkope, Z-up,
  grid + colored axes, auto-frame, **Wireframe** toggle. GPU canvas inside WebView2 (NOT
  headless-Chrome, no render-lag).
- **DDS textures:** **Textured** toggle loads diffuse (slot 0) + normal (slot 1). `TextureService.cs`
  resolves the game-relative DDS (user **Texture folder** override, else the NIF's `Data\`
  ancestors, else **inside BA2 archives**), converts DDS→PNG with the toolchain's **Texconv.exe**
  (DirectXTex, handles BC1/3/5/7 the browser's DDSLoader cannot), caches by path+mtime, returns a
  PNG data URL.
- **BA2-packed textures:** loose miss → search FO4 `.ba2` archives via Mutagen's
  `Ba2Reader`. Tiered + session-cached: cheap scan of the NIF-local / user-root `Data\` folders
  first, then the big scan of the configured `DataFolder` + `Mo2InstancePath\mods` tree (read from
  `%AppData%\FO4RecordEditor\settings.json`). Each archive's `.dds` file table is indexed once
  (innerPath→archive, first/most-local wins); a hit is extracted to a temp `.dds` for Texconv.
  Mutagen's `GetBytes()` reconstructs a valid DDS header (incl. BC5/ATI2).
- **BC5 normal-map Z reconstruction:** `ConvertToPng` detects BC5 by DDS header
  (ATI2 FourCC, or DX10 header dxgiFormat 83/84) and adds Texconv `-reconstructz` so the blue
  channel is rebuilt (`Z=√(1−x²−y²)`), gated to BC5 only, with a fallback to plain conversion if an
  older Texconv rejects the flag. PNG cache key salted `|v2` so pre-fix PNGs aren't reused.

---

## Roadmap

1. **In-game proof** (user): OBJ → import (collision, from-blender) → verify → `set_field` a STAT's
   `Model` → load in game, confirm it renders + collides. This is the last unproven link.
2. **Normal-map green-channel convention**, FO4 normals are DirectX-style (Y-down); three.js
   `normalMap` is OpenGL-style (Y-up). Now that Z is reconstructed, set `material.normalScale.y = -1`
   (or flip G) in `NifViewport.tsx` if normals still light inverted. (Noticed while doing BC5-Z; not
   yet verified in the viewport.)
3. **glTF importer**, richer than OBJ (multiple materials, proper normals/UV sets). Own parser.
4. **Convex / mesh collision + MOPP**, box only today. Port `bhkConvexVerticesShape` /
   `bhkMoppBvTreeShape` (needs precomputed MOPP) from the pynifly recipe.
5. **Phase-2 MCP tools**, `nif_set_shader`, `nif_set_material`, `nif_set_collision`, `nif_set_skin`,
   `nif_diff` (vs a known-good reference), `nif_view` (headless render for the AI to *see*).
6. **Skinned mesh support in the viewport** (armor/equippables), currently static shading only.
7. **Editable-tree phase 3**, remaining structural/less-common bits: **add / remove blocks**
   (new shape, delete collision, add/remove BSX), full alpha src/dst **blend-function** dropdowns,
   NiNode rotation (only translation/scale today), and convex/mesh collision editing. All extend
   `niftool tree`/`set` + `NifEditor.tsx` the same way.

**Implemented:** BA2-packed texture resolution, BC5 normal-map Z
reconstruction (both `TextureService.cs`), the **editable block tree / Edit mode** (`niftool
tree`+`set`, `NifService.Tree`/`ApplyEdits`, `NifEditor.tsx`), and **Edit phase 2**, shader-flag grid,
alpha property (create-on-save), effect-shader fields, and collision editing. See the DONE section.

---

## File map

| File | Role |
|---|---|
| `tools/niftool/src/main.cpp` | the C++ CLI (import/geo/inspect/verify/fix + **tree/set** + OBJ parser + box collision) |
| `tools/niftool/xmake.lua` | build; `includes("../../../FO4AnimForge/extern/nifly")` |
| `…\FO4RecordEditor\Services\NifService.cs` | shells out to niftool.exe, returns stdout |
| `…\FO4RecordEditor\Services\TextureService.cs` | DDS resolve (loose **+ BA2 archive index**) + Texconv DDS→PNG (**BC5 `-reconstructz`**) + cache |
| `…\FO4RecordEditor\Services\NifInterop.cs` | WebView2 host object (`"nif"`) for the GUI |
| `…\FO4RecordEditor\Services\Ai\PluginToolExecutor.cs` | MCP `nif_*` specs + dispatch |
| `…\FO4RecordEditor\MainWindow.xaml.cs` | registers the `nif` host object |
| `…\web\src\NifPanel.tsx` | the panel (modes, forms, texture-root field) |
| `…\web\src\NifViewport.tsx` | three.js viewport + texture application |
| `…\web\src\NifEditor.tsx` | **Edit-mode property editor** (grouped cards, typed inputs, dirty tracking, Save) |
| `…\web\src\backend.ts` | `NifHost` interface (**+ Tree/ApplyEdits**) + `getNif()` |

Reused, not owned: nifly (the NIF library the engine is built on; GPL-3.0, which is why
niftool ships as a separate exe) and Texconv.exe for DDS conversion.

---

## Rebuild / run

```powershell
# 1) engine (C++), from tools/niftool/ : MUST be PowerShell, not Git Bash
xmake f -p windows -a x64 -m release -y ; xmake -y     # -> build\windows\x64\release\niftool.exe

# 2) web frontend, from FO4RecordEditor\web\  (needed after any .tsx / backend.ts change)
npm run build                                          # tsc -b && vite build -> web\dist

# 3) C# app (copies web\dist in), from anywhere
dotnet build "…\FO4RecordEditor\FO4RecordEditor\FO4RecordEditor.csproj" -c Release
# then run bin\Release\net9.0-windows\FO4RecordEditor.exe ; for the MCP tools: /mcp reconnect
```

Gotchas: build niftool from **PowerShell** (Git Bash picks MinGW and fails on `half`). Texconv needs
**backslash** paths (forward slashes give `ERROR_INVALID_NAME 0x8007007B`). niftool bundles nifly
(GPL-3.0).
