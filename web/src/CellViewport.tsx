import { useEffect, useRef } from 'react';
import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { TransformControls } from 'three/addons/controls/TransformControls.js';
import type { NifGeo } from './NifViewport';
import type { CellPlacedReference } from './backend';
import { billboardMatrix, vertsCentroid } from './util/billboard';
import { cellLayerOf, MARKER_LAYER } from './util/cellLayer';
import { makeEffectMaterial, applyVertexColors } from './util/effectMaterial';

/** A geometry batch result keyed by the exact modelPath string GetPlacedReferences returned --
 * either niftool's raw geo JSON (NifGeo) or an { error } for anything that couldn't be
 * resolved/converted (not found loose or in any archive). */
export type CellGeoMap = Record<string, NifGeo | { error: string }>;

/**
 * Live three.js viewport for a whole cell's placed references -- many instances at their real
 * Position/Rotation/Scale, like Creation Kit's render window. Same NIF Z-up convention.
 *
 * Interaction (added 2026-07-20 -- the "big jumbled mess, can't click individual meshes / hide
 * layers" feedback): click a mesh to select its reference (raycast against the InstancedMeshes,
 * mapping the hit instanceId back to its CellPlacedReference); hide/show whole record types via
 * `hiddenTypes`. Selection is a wireframe box hugging the picked instance so it reads regardless of
 * the material underneath. Neither rebuilds the scene -- visibility toggles `.visible` and selection
 * just repositions one highlight box, so toggling a layer or clicking around is instant.
 *
 * Perf: refs are grouped by (model, shape) into one InstancedMesh each, not one Mesh per reference
 * (real cells run into the tens of thousands of refs). Diffuse texture fetched once per unique
 * (model, shape) and shared across its instances.
 */
type InstanceRef = {
  mesh: THREE.InstancedMesh; id: number; ref: CellPlacedReference; billboard: boolean;
  // Static Collection (SCOL) member instances only: this instance's fixed offset from its PARENT
  // ref's own transform (the member static's placement within the collection). undefined for every
  // normal (non-SCOL-member) instance, whose world matrix is just the ref's own transform. Recompute
  // sites (applyHidden's restore path, the gizmo's live-drag update) must compose ref-transform *
  // local, not just use the ref-transform alone, or a moved/hidden SCOL's member pieces would all
  // collapse onto the parent's single origin instead of keeping their authored layout.
  local?: THREE.Matrix4;
};

// Texture resolution is a fire-and-forget async side path: a shape whose diffuse never resolves, or
// resolves to bytes three.js cannot decode, simply stays flat grey with nothing reported anywhere.
// These counters are what turn that silence into a line the user can act on.
export type CellTextureStats = {
  ok: number;
  noPath: number;        // shape has UVs but neither a texture slot nor a bgsm to fall back to
  resolveFail: number;   // TextureService returned no path (missing from every data root/archive)
  decodeFail: number;    // path resolved but three.js could not load the image
  firstFailure: string | null;
};

export default function CellViewport(
  { references, geometry, loadTexture, hiddenTypes, hiddenRefs, selectedKey, onSelect,
    onHideSelected, onUnhideAll, onToggleMarkers, onUndo, onRedo, onMoveEnd, onTextureStats }: {
    references: CellPlacedReference[];
    geometry: CellGeoMap;
    loadTexture?: (modelPath: string, relTexPath: string) => Promise<string>;
    onTextureStats?: (stats: CellTextureStats) => void;
    hiddenTypes?: ReadonlySet<string>;
    hiddenRefs?: ReadonlySet<string>;              // per-reference hide (CK "1" cycle -> hidden)
    selectedKey?: string | null;
    onSelect?: (ref: CellPlacedReference | null) => void;
    onHideSelected?: () => void;                   // CK "1"
    onUnhideAll?: () => void;                       // CK "ALT+1"
    onToggleMarkers?: () => void;                   // CK "M"
    onUndo?: () => void;                            // CK "Ctrl+Z"
    onRedo?: () => void;                            // CK "Ctrl+Y"
    // Fires once per completed gizmo drag (mouse-up), not per frame. `ref` is the SAME object held
    // in this component's internal maps, already mutated in place with the final REFR-space
    // position/rotation -- the caller reads ref.formKey/position/rotation to persist it.
    onMoveEnd?: (ref: CellPlacedReference) => void;
  }
) {
  const mountRef = useRef<HTMLDivElement>(null);

  // Cross-effect handles so visibility/selection/hotkeys update WITHOUT rebuilding the scene.
  const onSelectRef = useRef(onSelect); onSelectRef.current = onSelect;
  const onHideSelectedRef = useRef(onHideSelected); onHideSelectedRef.current = onHideSelected;
  const onUnhideAllRef = useRef(onUnhideAll); onUnhideAllRef.current = onUnhideAll;
  const onToggleMarkersRef = useRef(onToggleMarkers); onToggleMarkersRef.current = onToggleMarkers;
  const onUndoRef = useRef(onUndo); onUndoRef.current = onUndo;
  const onRedoRef = useRef(onRedo); onRedoRef.current = onRedo;
  const onMoveEndRef = useRef(onMoveEnd); onMoveEndRef.current = onMoveEnd;
  const selectedKeyRef = useRef(selectedKey); selectedKeyRef.current = selectedKey;
  const hiddenRefsRef = useRef(hiddenRefs); hiddenRefsRef.current = hiddenRefs;
  const meshEntriesRef = useRef<{ mesh: THREE.Object3D; recordType: string }[]>([]);
  const modelBBoxRef = useRef<Map<string, THREE.Box3>>(new Map());
  const scolBBoxRef = useRef<Map<string, THREE.Box3>>(new Map());
  const highlightRef = useRef<THREE.LineSegments | null>(null);
  const refByKeyRef = useRef<Map<string, CellPlacedReference>>(new Map());
  const keyToInstancesRef = useRef<Map<string, InstanceRef[]>>(new Map());
  const cameraApiRef = useRef<{
    focus: (key: string | null) => void; topDown: (key: string | null) => void;
    setOrbitTarget: (key: string | null) => void;
  } | null>(null);
  // Set once per scene build (inside the main effect, where the gizmo/TransformControls actually
  // live) so the separate selection-change effect below can attach/detach the gizmo without needing
  // its own copy of the TransformControls instance.
  const gizmoAttachRef = useRef<((key: string | null) => void) | null>(null);
  // The whole scene is built with every position relative to this point (the cell's own bounding-box
  // center), not absolute REFR world coordinates -- see the long comment where it's computed for why.
  const worldCenterRef = useRef(new THREE.Vector3());

  // ---- build the scene (only when the data actually changes) ----
  useEffect(() => {
    const mount = mountRef.current;
    if (!mount || references.length === 0) return;
    let cancelled = false;

    const width = mount.clientWidth || 600;
    const height = mount.clientHeight || 400;

    const scene = new THREE.Scene();
    scene.background = new THREE.Color(0x17181c);

    const camera = new THREE.PerspectiveCamera(55, width / height, 0.1, 5_000_000);
    camera.up.set(0, 0, 1); // NIF/REFR Z-up

    const renderer = new THREE.WebGLRenderer({ antialias: true });
    renderer.setSize(width, height);
    renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    mount.appendChild(renderer.domElement);

    const controls = new OrbitControls(camera, renderer.domElement);
    controls.enableDamping = true;
    controls.dampingFactor = 0.08;

    scene.add(new THREE.AmbientLight(0xffffff, 0.7));
    const key = new THREE.DirectionalLight(0xffffff, 1.0); key.position.set(1, 1, 2); scene.add(key);
    const fill = new THREE.DirectionalLight(0xffffff, 0.35); fill.position.set(-1.5, -1, 0.5); scene.add(fill);

    const disposables: { dispose: () => void }[] = [];
    const posBounds = new THREE.Box3();
    const dummy = new THREE.Object3D();

    // Pickable instanced meshes (each carries userData.refs so a raycast instanceId maps back to a
    // reference) and the per-type entries the visibility effect toggles.
    const pickables: THREE.InstancedMesh[] = [];
    const meshEntries: { mesh: THREE.Object3D; recordType: string }[] = [];
    const modelBBox = new Map<string, THREE.Box3>();
    const refByKey = new Map<string, CellPlacedReference>();
    const keyToInstances = new Map<string, InstanceRef[]>();
    for (const r of references) refByKey.set(r.formKey, r);
    const addInstance = (
      inst: THREE.InstancedMesh, id: number, r: CellPlacedReference, billboard: boolean, local?: THREE.Matrix4,
    ) => {
      const entry: InstanceRef = { mesh: inst, id, ref: r, billboard, local };
      const arr = keyToInstances.get(r.formKey);
      if (arr) arr.push(entry); else keyToInstances.set(r.formKey, [entry]);
    };

    const billboardGroups: {
      inst: THREE.InstancedMesh; refs: CellPlacedReference[]; centroid: THREE.Vector3; mode?: number;
    }[] = [];

    // Group refs by modelPath so each unique mesh becomes one (or a few, if multi-shape) InstancedMesh.
    const byModel = new Map<string, CellPlacedReference[]>();
    const markers: CellPlacedReference[] = [];       // no model, or model resolve/convert failed
    // Ground decals (TXST-based base, no Model at all) grouped by their diffuse texture path -- one
    // InstancedMesh of a shared unit plane per unique texture, same instancing pattern as byModel.
    const byDecal = new Map<string, CellPlacedReference[]>();
    // Static Collection (SCOL) fallback: when the SCOL's own precombined modelPath failed to resolve
    // (see backend.ts's CellPlacedReference.scolParts doc), explode it into its member statics instead
    // of a "mesh unavailable" marker. Grouped by the MEMBER's own modelPath -- a distinct InstancedMesh
    // bucket from byModel, kept separate rather than merged so a shared base static used both normally
    // and inside a SCOL doesn't mix two different recordType classifications into one mesh's layer tag.
    type ScolPlacement = { x: number; y: number; z: number; rx: number; ry: number; rz: number; scale: number };
    const byScolModel = new Map<string, { parent: CellPlacedReference; placement: ScolPlacement }[]>();
    const goodGeo = (path: string | null | undefined) => {
      const g = path ? geometry[path] : undefined;
      return g && 'shapes' in g && g.shapes.some(s => s.verts.length > 0 && s.tris.length > 0);
    };
    for (const r of references) {
      posBounds.expandByPoint(new THREE.Vector3(r.position.x, r.position.y, r.position.z));
      if (r.decalDiffuse) {
        const list = byDecal.get(r.decalDiffuse);
        if (list) list.push(r); else byDecal.set(r.decalDiffuse, [r]);
        continue;
      }
      if (r.modelPath && goodGeo(r.modelPath)) {
        const list = byModel.get(r.modelPath);
        if (list) list.push(r); else byModel.set(r.modelPath, [r]);
        continue;
      }
      let anyPartOk = false;
      for (const part of r.scolParts ?? []) {
        if (!goodGeo(part.modelPath)) continue;
        anyPartOk = true;
        const list = byScolModel.get(part.modelPath);
        const entries = part.placements.map(placement => ({ parent: r, placement }));
        if (list) list.push(...entries); else byScolModel.set(part.modelPath, entries);
      }
      if (!anyPartOk) markers.push(r);
    }

    // Recenter the whole scene around the cell's own bounding-box center instead of using REFR
    // world coordinates directly. A cell far from the game's own world origin (e.g. an exterior
    // industrial complex at REFR positions in the thousands, like Switchboard) puts every vertex/
    // instance-matrix position at a large absolute magnitude; WebGL stores that in 32-bit floats,
    // which only carry ~7 significant decimal digits, so a position of "6767.xxx" leaves very
    // little headroom for sub-unit precision -- the classic "orbiting camera makes geometry jitter/
    // swim" artifact, worst exactly during rotation (reported: "with rotation I can not rotate
    // properly"). Subtracting a fixed per-cell center keeps every GPU-side coordinate within the
    // cell's own extent (its SIZE, not its distance from world 0,0,0), which is what actually
    // matters for float32 precision. Camera/controls/grid/axes are recentered to match (target
    // becomes (0,0,0) instead of the world-space center) so the visual result is identical, just
    // computed in a numerically-safer coordinate space.
    const worldCenter = posBounds.getCenter(new THREE.Vector3());
    worldCenterRef.current.copy(worldCenter);

    const texLoader = loadTexture ? new THREE.TextureLoader() : null;
    const textures: THREE.Texture[] = [];

    const texStats: CellTextureStats = { ok: 0, noPath: 0, resolveFail: 0, decodeFail: 0, firstFailure: null };
    let texStatsTimer: ReturnType<typeof setTimeout> | undefined;
    // Every texture settles on its own promise, so reporting per-settle would fire thousands of
    // renders on a real cell. Coalesce into one update per quiet period instead.
    const reportTexStats = () => {
      if (!onTextureStats || texStatsTimer) return;
      texStatsTimer = setTimeout(() => {
        texStatsTimer = undefined;
        if (!cancelled) onTextureStats({ ...texStats });
      }, 300);
    };
    const noteTexFailure = (relTexPath: string, modelPath: string) => {
      texStats.firstFailure ??= modelPath ? `${relTexPath} (nif: ${modelPath})` : relTexPath;
    };

    // Shared by the byModel loop below AND the SCOL-member loop that follows it -- both need the exact
    // same per-shape BufferGeometry/material/texture-loading logic, just with different INSTANCING
    // (a plain ref transform vs. a ref transform composed with a fixed member-placement offset).
    const buildShapeMesh = (shape: NifGeo['shapes'][number], modelPath: string) => {
      const g = new THREE.BufferGeometry();
      g.setAttribute('position', new THREE.Float32BufferAttribute(shape.verts, 3));
      const hasUV = shape.uvs.length > 0;
      if (hasUV) g.setAttribute('uv', new THREE.Float32BufferAttribute(shape.uvs, 2));
      g.setIndex(shape.tris);
      if (shape.normals.length === shape.verts.length)
        g.setAttribute('normal', new THREE.Float32BufferAttribute(shape.normals, 3));
      else
        g.computeVertexNormals();
      g.computeBoundingBox();
      disposables.push(g);

      // Effect-shaded shapes (fog/mist/glow/decal FX) get a real unlit/blended material built from
      // the shape's actual NiAlphaProperty + BSEffectShaderProperty params -- NOT the same opaque lit
      // MeshStandardMaterial every structural shape gets, which is why FX used to render as hard
      // "circular planes" instead of soft glowing effects.
      const mat = shape.effectShader
        ? makeEffectMaterial(shape.effect, true)
        : new THREE.MeshStandardMaterial({ color: 0x9aa0aa, metalness: 0.1, roughness: 0.8, side: THREE.DoubleSide });
      applyVertexColors(g, mat, shape.vertexColors);
      disposables.push(mat);

      // Diffuse only (slot 0) for v1. Fall back to the shape's linked .bgsm material (bgsmPath)
      // when it has no texture of its own -- common for shared/template materials; TextureService
      // resolves+parses the .bgsm transparently given the same (modelPath, path) call.
      if (hasUV && texLoader && loadTexture && !(shape.textures?.length || shape.bgsmPath)) {
        texStats.noPath++;
        reportTexStats();
      }
      if (hasUV && texLoader && loadTexture && (shape.textures?.length || shape.bgsmPath)) {
        const diffuse = shape.textures?.find(t => t.slot === 0)
          ?? (shape.bgsmPath ? { slot: 0, path: shape.bgsmPath } : undefined);
        if (diffuse) {
          loadTexture(modelPath, diffuse.path).then(url => {
            if (cancelled) return;
            if (!url) {
              texStats.resolveFail++;
              noteTexFailure(diffuse.path, modelPath);
              reportTexStats();
              return;
            }
            texLoader.load(url, tex => {
              if (cancelled) { tex.dispose(); return; }
              tex.flipY = false;
              tex.colorSpace = THREE.SRGBColorSpace;
              // FO4 terrain/rock/floor meshes tile their diffuse (UVs run 0..N, not 0..1). Without
              // RepeatWrapping three.js clamps to the edge texel, smearing it across the whole
              // surface -- the "warped/streaky dirt" look. Small props (UVs in 0..1) are unaffected.
              tex.wrapS = THREE.RepeatWrapping;
              tex.wrapT = THREE.RepeatWrapping;
              tex.anisotropy = 4;
              mat.map = tex;
              // Structural shapes: the flat-gray placeholder color must reset to white so the real
              // texture shows unmodified. Effect shapes: KEEP the tint -- baseColor*baseColorScale is
              // the actual glow color the diffuse texture is meant to be multiplied by, not a
              // placeholder to discard.
              if (!shape.effectShader) mat.color.setHex(0xffffff);
              mat.needsUpdate = true;
              textures.push(tex);
              texStats.ok++;
              reportTexStats();
            }, undefined, () => {
              texStats.decodeFail++;
              noteTexFailure(diffuse.path, modelPath);
              reportTexStats();
            });
          });
        }
      }
      return { geometry: g, material: mat };
    };

    for (const [modelPath, refs] of byModel) {
      const nifGeo = geometry[modelPath] as NifGeo;
      const modelBox = new THREE.Box3();
      for (const shape of nifGeo.shapes) {
        if (!shape.verts.length || !shape.tris.length) continue;
        const { geometry: g, material: mat } = buildShapeMesh(shape, modelPath);
        if (g.boundingBox) modelBox.union(g.boundingBox);

        const inst = new THREE.InstancedMesh(g, mat, refs.length);
        refs.forEach((r, i) => {
          dummy.position.set(r.position.x - worldCenter.x, r.position.y - worldCenter.y, r.position.z - worldCenter.z);
          dummy.rotation.set(r.rotation.x, r.rotation.y, r.rotation.z, 'XYZ');
          dummy.scale.setScalar(r.scale || 1);
          dummy.updateMatrix();
          inst.setMatrixAt(i, dummy.matrix);
        });
        inst.instanceMatrix.needsUpdate = true;
        inst.userData.refs = refs;
        scene.add(inst);
        pickables.push(inst);
        meshEntries.push({ mesh: inst, recordType: cellLayerOf(refs[0], nifGeo) });
        refs.forEach((r, i) => addInstance(inst, i, r, !!shape.billboard));

        if (shape.billboard) {
          inst.frustumCulled = false;
          billboardGroups.push({ inst, refs, centroid: vertsCentroid(shape.verts), mode: shape.billboardMode });
        }
      }
      if (!modelBox.isEmpty()) modelBBox.set(modelPath, modelBox);
    }

    // SCOL member statics (see byScolModel's own comment above for why these are separate from
    // byModel). Each instance's world matrix is the PARENT reference's own transform composed with
    // the member's fixed local placement -- moving/hiding the parent moves the whole rigid group.
    // Billboard shapes inside a SCOL member aren't specially handled (real-world case not seen yet):
    // they render as a static instance, never re-facing the camera, rather than being left out.
    const scolBBox = new Map<string, THREE.Box3>();   // combined member bbox, keyed by the PARENT's formKey
    const _scolRefPos = new THREE.Vector3();
    const _scolRefQuat = new THREE.Quaternion();
    const _scolRefScale = new THREE.Vector3();
    const _scolRefM = new THREE.Matrix4();
    const _scolLocPos = new THREE.Vector3();
    const _scolLocQuat = new THREE.Quaternion();
    const _scolLocScale = new THREE.Vector3();
    for (const [modelPath, entries] of byScolModel) {
      const nifGeo = geometry[modelPath] as NifGeo;
      for (const shape of nifGeo.shapes) {
        if (!shape.verts.length || !shape.tris.length) continue;
        const { geometry: g, material: mat } = buildShapeMesh(shape, modelPath);

        const inst = new THREE.InstancedMesh(g, mat, entries.length);
        const locals: THREE.Matrix4[] = [];
        entries.forEach(({ parent, placement }, i) => {
          _scolRefPos.set(parent.position.x - worldCenter.x, parent.position.y - worldCenter.y, parent.position.z - worldCenter.z);
          _scolRefQuat.setFromEuler(new THREE.Euler(parent.rotation.x, parent.rotation.y, parent.rotation.z, 'XYZ'));
          _scolRefScale.setScalar(parent.scale || 1);
          _scolRefM.compose(_scolRefPos, _scolRefQuat, _scolRefScale);

          _scolLocPos.set(placement.x, placement.y, placement.z);
          _scolLocQuat.setFromEuler(new THREE.Euler(placement.rx, placement.ry, placement.rz, 'XYZ'));
          _scolLocScale.setScalar(placement.scale || 1);
          const local = new THREE.Matrix4().compose(_scolLocPos, _scolLocQuat, _scolLocScale);
          locals.push(local);

          dummy.matrix.multiplyMatrices(_scolRefM, local);
          inst.setMatrixAt(i, dummy.matrix);

          // This shape's own bbox mapped into the PARENT's local space via the placement offset --
          // same "local-space AABB to compose with the ref's world transform later" convention
          // modelBBox already uses, so applySelection's existing math works unmodified.
          if (g.boundingBox) {
            const box = g.boundingBox.clone().applyMatrix4(local);
            const existing = scolBBox.get(parent.formKey);
            if (existing) existing.union(box); else scolBBox.set(parent.formKey, box);
          }
        });
        inst.instanceMatrix.needsUpdate = true;
        inst.userData.refs = entries.map(e => e.parent);
        scene.add(inst);
        pickables.push(inst);
        meshEntries.push({ mesh: inst, recordType: cellLayerOf(entries[0].parent, nifGeo) });
        entries.forEach(({ parent }, i) => addInstance(inst, i, parent, false, locals[i]));
      }
    }

    // Ground decals: a real FO4 ground decal has no mesh at all (its base is a TextureSet, not
    // anything with a Model) -- the engine projects the TXST's diffuse texture directly onto whatever
    // geometry sits underneath it. This viewer approximates that with a flat plane, using the ref's
    // own Position/Rotation (the SAME transform convention as every other placed reference -- a real
    // decal's rotation already orients it flush to its surface, confirmed against a real record: a
    // ground decal's rotation was ~90 degrees about X, which is exactly what rotates a default
    // XY-facing plane to lie flat pointing up). Width/Height come from the TXST's ObjectBounds
    // (CellService.cs), in the same world-unit convention as everything else already rendering
    // correctly here -- baked as a non-uniform instance scale on a shared 1x1 unit plane, the same
    // instancing pattern as byModel, rather than one geometry per decal size.
    const decalGeo = new THREE.PlaneGeometry(1, 1);
    disposables.push(decalGeo);
    for (const [texPath, refs] of byDecal) {
      const mat = new THREE.MeshBasicMaterial({
        color: 0xffffff, side: THREE.DoubleSide, transparent: true, depthWrite: false,
      });
      disposables.push(mat);

      if (texLoader && loadTexture) {
        // No NIF anchors a decal texture -- pass "" for modelPath; TextureService's archive-search
        // Tier 2 (the configured Data/mods roots) resolves a Data-relative path independent of any
        // NIF-local anchor, same as every other diffuse fetch here.
        loadTexture('', texPath).then(url => {
          if (cancelled) return;
          if (!url) {
            texStats.resolveFail++;
            noteTexFailure(texPath, '');
            reportTexStats();
            return;
          }
          texLoader.load(url, tex => {
            if (cancelled) { tex.dispose(); return; }
            tex.flipY = false;
            tex.colorSpace = THREE.SRGBColorSpace;
            mat.map = tex;
            mat.needsUpdate = true;
            textures.push(tex);
            texStats.ok++;
            reportTexStats();
          }, undefined, () => {
            texStats.decodeFail++;
            noteTexFailure(texPath, '');
            reportTexStats();
          });
        });
      }

      const inst = new THREE.InstancedMesh(decalGeo, mat, refs.length);
      refs.forEach((r, i) => {
        dummy.position.set(r.position.x - worldCenter.x, r.position.y - worldCenter.y, r.position.z - worldCenter.z);
        dummy.rotation.set(r.rotation.x, r.rotation.y, r.rotation.z, 'XYZ');
        const w = (r.decalWidth || 32) * (r.scale || 1);
        const h = (r.decalHeight || 32) * (r.scale || 1);
        dummy.scale.set(w, h, 1);
        dummy.updateMatrix();
        inst.setMatrixAt(i, dummy.matrix);
      });
      inst.instanceMatrix.needsUpdate = true;
      inst.userData.refs = refs;
      scene.add(inst);
      pickables.push(inst);
      meshEntries.push({ mesh: inst, recordType: cellLayerOf(refs[0], undefined) });
      refs.forEach((r, i) => addInstance(inst, i, r, false));
    }

    // Markers for refs with no renderable mesh (actors/traps, or an unresolved/failed model).
    if (markers.length > 0) {
      const markerGeo = new THREE.SphereGeometry(8, 8, 6);
      disposables.push(markerGeo);
      const noModelMat = new THREE.MeshBasicMaterial({ color: 0x5a6472, transparent: true, opacity: 0.55 });
      const failedMat = new THREE.MeshBasicMaterial({ color: 0xd08a4a, transparent: true, opacity: 0.75 });
      disposables.push(noModelMat, failedMat);
      const noModelRefs = markers.filter(r => !r.modelPath);
      const failedRefs = markers.filter(r => r.modelPath);
      for (const [mat, refs] of [[noModelMat, noModelRefs], [failedMat, failedRefs]] as const) {
        if (refs.length === 0) continue;
        const inst = new THREE.InstancedMesh(markerGeo, mat, refs.length);
        refs.forEach((r, i) => {
          dummy.position.set(r.position.x - worldCenter.x, r.position.y - worldCenter.y, r.position.z - worldCenter.z);
          dummy.rotation.set(0, 0, 0);
          dummy.scale.setScalar(1);
          dummy.updateMatrix();
          inst.setMatrixAt(i, dummy.matrix);
        });
        inst.instanceMatrix.needsUpdate = true;
        inst.userData.refs = refs;
        scene.add(inst);
        pickables.push(inst);
        refs.forEach((r, i) => addInstance(inst, i, r, false));
        // Markers can mix types (actors + traps + failed statics); group them under a synthetic key so
        // "hide markers" is one toggle rather than fragmenting the type list.
        meshEntries.push({ mesh: inst, recordType: MARKER_LAYER });
      }
    }

    // Selection highlight: a unit wireframe box we reposition onto the picked instance.
    const hlGeo = new THREE.EdgesGeometry(new THREE.BoxGeometry(1, 1, 1));
    const hlMat = new THREE.LineBasicMaterial({ color: 0x59d0ff, depthTest: false, transparent: true });
    const highlight = new THREE.LineSegments(hlGeo, hlMat);
    highlight.matrixAutoUpdate = false;
    highlight.visible = false;
    highlight.renderOrder = 999;
    scene.add(highlight);
    disposables.push(hlGeo, hlMat);

    meshEntriesRef.current = meshEntries;
    modelBBoxRef.current = modelBBox;
    scolBBoxRef.current = scolBBox;
    refByKeyRef.current = refByKey;
    highlightRef.current = highlight;
    keyToInstancesRef.current = keyToInstances;

    const size = posBounds.getSize(new THREE.Vector3());
    const radius = Math.max(size.x, size.y, size.z, 50);

    // Everything below is relative to worldCenter now, so the cell's own center sits at local (0,0,0).
    const grid = new THREE.GridHelper(radius * 2.5, 24, 0x3a5a80, 0x2a2a30);
    grid.rotation.x = Math.PI / 2;
    grid.position.set(0, 0, posBounds.min.z - worldCenter.z);
    scene.add(grid);
    const axes = new THREE.AxesHelper(radius * 0.5);
    scene.add(axes);

    controls.target.set(0, 0, 0);
    camera.near = Math.max(radius / 500, 0.1);
    camera.far = radius * 50;
    camera.position.set(radius * 1.2, -radius * 1.4, radius * 0.9);
    camera.updateProjectionMatrix();
    controls.update();

    // ---- gizmo: move/rotate the selected reference ("a full gyro" -- 2026-07-20) ----
    // A dedicated anchor object, NOT the highlight box above -- the highlight's matrix bakes in the
    // model bbox's own center/size offset (see applySelection), which is the wrong pivot to drag
    // around. The anchor always sits exactly at the REFR's own position/rotation (bbox-center-free),
    // matching what Position/Rotation actually mean on the record.
    const transformControls = new TransformControls(camera, renderer.domElement);
    transformControls.size = 0.9;
    scene.add(transformControls.getHelper());
    const gizmoAnchor = new THREE.Object3D();
    scene.add(gizmoAnchor);

    // Recompute one reference's live instances from its (just-mutated) position/rotation, preserving
    // each instance's ALREADY-BAKED scale -- decal instances carry a non-uniform (width, height, 1)
    // scale from build time (see the byDecal loop above), not ref.scale, so scale must be read back
    // off the existing matrix rather than recomputed from ref.scale like applyHidden's restore path
    // does. Billboard instances aren't touched here -- the per-frame updateBillboards() loop below
    // already reads r.position/r.rotation live every frame, so mutating the ref is enough for those.
    const _giCurM = new THREE.Matrix4();
    const _giCurPos = new THREE.Vector3();
    const _giCurQuat = new THREE.Quaternion();
    const _giCurScale = new THREE.Vector3();
    const _giNewPos = new THREE.Vector3();
    const _giNewQuat = new THREE.Quaternion();
    const _giRefScale = new THREE.Vector3();
    const _giNewM = new THREE.Matrix4();
    const applyGizmoTransformToMesh = (formKey: string) => {
      const entries = keyToInstances.get(formKey);
      const r = refByKey.get(formKey);
      if (!entries || !r) return;
      _giNewPos.set(r.position.x - worldCenter.x, r.position.y - worldCenter.y, r.position.z - worldCenter.z);
      _giNewQuat.setFromEuler(new THREE.Euler(r.rotation.x, r.rotation.y, r.rotation.z, 'XYZ'));
      const dirty = new Set<THREE.InstancedMesh>();
      for (const e of entries) {
        if (e.billboard) continue;
        if (e.local) {
          // SCOL member: the parent's own scale is uniform (ref.scale), unlike a decal's baked
          // non-uniform scale below -- the member's OWN scale already lives inside e.local and never
          // changes, so there's nothing to read back off the current instance matrix here.
          _giRefScale.setScalar(r.scale || 1);
          _giNewM.compose(_giNewPos, _giNewQuat, _giRefScale);
          _giNewM.multiply(e.local);
        } else {
          e.mesh.getMatrixAt(e.id, _giCurM);
          _giCurM.decompose(_giCurPos, _giCurQuat, _giCurScale);
          _giNewM.compose(_giNewPos, _giNewQuat, _giCurScale);
        }
        e.mesh.setMatrixAt(e.id, _giNewM);
        dirty.add(e.mesh);
      }
      for (const m of dirty) m.instanceMatrix.needsUpdate = true;
      applySelection(highlight, modelBBox, refByKey, formKey, worldCenter, scolBBox);
    };

    // Fires continuously while dragging -- write the anchor's live transform back onto the selected
    // ref (REFR-space: undo the worldCenter recenter) and push it into the live meshes immediately.
    transformControls.addEventListener('objectChange', () => {
      const key = selectedKeyRef.current;
      const r = key ? refByKey.get(key) : undefined;
      if (!key || !r) return;
      r.position.x = gizmoAnchor.position.x + worldCenter.x;
      r.position.y = gizmoAnchor.position.y + worldCenter.y;
      r.position.z = gizmoAnchor.position.z + worldCenter.z;
      const e = new THREE.Euler().setFromQuaternion(gizmoAnchor.quaternion, 'XYZ');
      r.rotation.x = e.x; r.rotation.y = e.y; r.rotation.z = e.z;
      applyGizmoTransformToMesh(key);
    });
    // OrbitControls must not fight the gizmo drag for the same mouse input.
    transformControls.addEventListener('mouseDown', () => { controls.enabled = false; });
    transformControls.addEventListener('mouseUp', () => {
      controls.enabled = true;
      const key = selectedKeyRef.current;
      const r = key ? refByKey.get(key) : undefined;
      if (r) onMoveEndRef.current?.(r);
    });

    const attachGizmoTo = (key: string | null) => {
      const r = key ? refByKey.get(key) : undefined;
      if (!r) { transformControls.detach(); return; }
      gizmoAnchor.position.set(r.position.x - worldCenter.x, r.position.y - worldCenter.y, r.position.z - worldCenter.z);
      gizmoAnchor.quaternion.setFromEuler(new THREE.Euler(r.rotation.x, r.rotation.y, r.rotation.z, 'XYZ'));
      transformControls.attach(gizmoAnchor);
    };
    gizmoAttachRef.current = attachGizmoTo;

    // ---- picking: pointerdown/up with a small move threshold so orbiting never selects ----
    const raycaster = new THREE.Raycaster();
    const ndc = new THREE.Vector2();
    let downX = 0, downY = 0, downT = 0;
    const onPointerDown = (e: PointerEvent) => { downX = e.clientX; downY = e.clientY; downT = e.timeStamp; };
    const onPointerUp = (e: PointerEvent) => {
      if (transformControls.dragging) return; // a gizmo drag, not a selection click
      if (e.button !== 0) return;
      if (Math.hypot(e.clientX - downX, e.clientY - downY) > 5 || e.timeStamp - downT > 400) return; // a drag, not a click
      const rect = renderer.domElement.getBoundingClientRect();
      ndc.x = ((e.clientX - rect.left) / rect.width) * 2 - 1;
      ndc.y = -((e.clientY - rect.top) / rect.height) * 2 + 1;
      raycaster.setFromCamera(ndc, camera);
      const visiblePickables = pickables.filter(m => m.visible);
      const hits = raycaster.intersectObjects(visiblePickables, false);
      const hit = hits.find(h => h.instanceId != null);
      if (hit && hit.instanceId != null) {
        const refs = hit.object.userData.refs as CellPlacedReference[] | undefined;
        const ref = refs?.[hit.instanceId];
        onSelectRef.current?.(ref ?? null);
      } else {
        onSelectRef.current?.(null); // clicked empty space -> deselect
      }
    };
    renderer.domElement.addEventListener('pointerdown', onPointerDown);
    renderer.domElement.addEventListener('pointerup', onPointerUp);

    // Camera framing for the CK hotkeys (SHIFT+F anchor on selection, T top-down of selection).
    const _v = new THREE.Vector3();
    const radiusOf = (r: CellPlacedReference) => {
      const box = (r.modelPath ? modelBBox.get(r.modelPath) : undefined) ?? scolBBox.get(r.formKey);
      const rad = box ? box.getSize(_v).length() * 0.5 * (r.scale || 1) : 60;
      return Math.max(rad, 30);
    };
    const focusOn = (k: string | null) => {
      const r = k ? refByKey.get(k) : undefined;
      if (!r) return;
      const c = new THREE.Vector3(r.position.x - worldCenter.x, r.position.y - worldCenter.y, r.position.z - worldCenter.z);
      const dist = radiusOf(r) * 3;
      controls.target.copy(c);
      camera.position.set(c.x + dist * 0.8, c.y - dist, c.z + dist * 0.6);
      camera.near = Math.max(dist / 500, 0.05); camera.far = dist * 200;
      camera.updateProjectionMatrix(); controls.update();
    };
    const topDownOn = (k: string | null) => {
      const r = k ? refByKey.get(k) : undefined;
      if (!r) return;
      const c = new THREE.Vector3(r.position.x - worldCenter.x, r.position.y - worldCenter.y, r.position.z - worldCenter.z);
      const dist = radiusOf(r) * 3;
      controls.target.copy(c);
      camera.position.set(c.x, c.y - 0.001, c.z + dist);
      camera.updateProjectionMatrix(); controls.update();
    };
    // Re-pivot the orbit around whatever's selected, Unreal-Editor-style, instead of always orbiting
    // around the cell's own center regardless of what's clicked (the actual bug behind "rotation still
    // isn't working properly" -- SHIFT+F already re-pivoted+repositioned the camera, but plain
    // drag-to-orbit never did, so the pivot silently stayed wherever it last was, usually the cell
    // center). Only moves controls.target, not camera.position -- OrbitControls' next update() then
    // reapplies the SAME viewing angle/distance around the new target, so the camera slides smoothly
    // onto the new pivot instead of snapping to a fixed framing distance like SHIFT+F does.
    const setOrbitTarget = (k: string | null) => {
      const r = k ? refByKey.get(k) : undefined;
      if (r) controls.target.set(r.position.x - worldCenter.x, r.position.y - worldCenter.y, r.position.z - worldCenter.z);
      else controls.target.set(0, 0, 0);
      controls.update();
    };
    cameraApiRef.current = { focus: focusOn, topDown: topDownOn, setOrbitTarget };
    setOrbitTarget(selectedKey ?? null);

    // CK render-window hotkeys (verified against the offline CK wiki's Keyboard Mapping page), plus
    // G/R for the gizmo (Blender-style move/rotate; CK's own move/rotate keys weren't free to reuse
    // without colliding with 1/D/T/M above):
    //   1 = cycle selected visibility -> hidden here, ALT+1 = unhide all, D = clear selection,
    //   SHIFT+F = anchor camera on selection, T = top-down of selection, M = toggle markers,
    //   CTRL+Z = undo (last hide/show), CTRL+Y = redo, G = move gizmo, R = rotate gizmo.
    const onKeyDown = (e: KeyboardEvent) => {
      const tag = (e.target as HTMLElement | null)?.tagName;
      if (tag === 'INPUT' || tag === 'TEXTAREA') return; // don't hijack the search box
      const k = e.key.toLowerCase();
      if (k === '1' && e.altKey) { onUnhideAllRef.current?.(); e.preventDefault(); }
      else if (k === '1') { onHideSelectedRef.current?.(); e.preventDefault(); }
      else if (k === 'd' && !e.ctrlKey && !e.altKey && !e.shiftKey) { onSelectRef.current?.(null); e.preventDefault(); }
      else if (k === 'f' && e.shiftKey) { focusOn(selectedKeyRef.current ?? null); e.preventDefault(); }
      else if (k === 't' && !e.ctrlKey && !e.altKey && !e.shiftKey) { topDownOn(selectedKeyRef.current ?? null); e.preventDefault(); }
      else if (k === 'm' && !e.ctrlKey && !e.altKey) { onToggleMarkersRef.current?.(); e.preventDefault(); }
      else if (k === 'z' && e.ctrlKey) { onUndoRef.current?.(); e.preventDefault(); }
      else if (k === 'y' && e.ctrlKey) { onRedoRef.current?.(); e.preventDefault(); }
      else if (k === 'g' && !e.ctrlKey && !e.altKey) { transformControls.setMode('translate'); e.preventDefault(); }
      else if (k === 'r' && !e.ctrlKey && !e.altKey) { transformControls.setMode('rotate'); e.preventDefault(); }
    };
    window.addEventListener('keydown', onKeyDown);

    // Apply the current visibility + selection + per-ref hide immediately (props may already be set).
    applyVisibility(meshEntries, hiddenTypes);
    applySelection(highlight, modelBBox, refByKey, selectedKey ?? null, worldCenter, scolBBox);
    applyHidden(keyToInstances, hiddenRefs, worldCenter);
    attachGizmoTo(selectedKey ?? null);

    const bbBase = new THREE.Matrix4();
    const bbOut = new THREE.Matrix4();
    const bbQuat = new THREE.Quaternion();
    const bbPos = new THREE.Vector3();
    const bbScale = new THREE.Vector3();
    const bbZero = new THREE.Matrix4().makeScale(0, 0, 0);
    const updateBillboards = () => {
      const hidden = hiddenRefsRef.current;
      for (const grp of billboardGroups) {
        if (!grp.inst.visible) continue;
        grp.refs.forEach((r, i) => {
          if (hidden?.has(r.formKey)) { grp.inst.setMatrixAt(i, bbZero); return; }
          bbPos.set(r.position.x - worldCenter.x, r.position.y - worldCenter.y, r.position.z - worldCenter.z);
          bbQuat.setFromEuler(new THREE.Euler(r.rotation.x, r.rotation.y, r.rotation.z, 'XYZ'));
          bbScale.setScalar(r.scale || 1);
          bbBase.compose(bbPos, bbQuat, bbScale);
          billboardMatrix(bbOut, bbBase, grp.centroid, camera, grp.mode);
          grp.inst.setMatrixAt(i, bbOut);
        });
        grp.inst.instanceMatrix.needsUpdate = true;
      }
    };

    let raf = 0;
    const animate = () => {
      controls.update();
      if (billboardGroups.length) updateBillboards();
      renderer.render(scene, camera);
      raf = requestAnimationFrame(animate);
    };
    animate();

    const onResize = () => {
      if (!mount) return;
      const w = mount.clientWidth || 600, h = mount.clientHeight || 400;
      camera.aspect = w / h; camera.updateProjectionMatrix(); renderer.setSize(w, h);
    };
    const ro = new ResizeObserver(onResize);
    ro.observe(mount);

    return () => {
      cancelled = true;
      cancelAnimationFrame(raf);
      ro.disconnect();
      renderer.domElement.removeEventListener('pointerdown', onPointerDown);
      renderer.domElement.removeEventListener('pointerup', onPointerUp);
      window.removeEventListener('keydown', onKeyDown);
      if (texStatsTimer) clearTimeout(texStatsTimer);
      transformControls.dispose();
      controls.dispose();
      disposables.forEach(d => d.dispose());
      textures.forEach(t => t.dispose());
      renderer.dispose();
      if (renderer.domElement.parentElement === mount) mount.removeChild(renderer.domElement);
      meshEntriesRef.current = [];
      highlightRef.current = null;
      gizmoAttachRef.current = null;
      scolBBoxRef.current = new Map();
    };
  }, [references, geometry, loadTexture]); // eslint-disable-line react-hooks/exhaustive-deps

  // ---- visibility: toggle .visible per record type, no rebuild ----
  useEffect(() => {
    applyVisibility(meshEntriesRef.current, hiddenTypes);
  }, [hiddenTypes]);

  // ---- selection: reposition the highlight box + gizmo + orbit pivot, no rebuild ----
  useEffect(() => {
    if (highlightRef.current)
      applySelection(highlightRef.current, modelBBoxRef.current, refByKeyRef.current, selectedKey ?? null,
        worldCenterRef.current, scolBBoxRef.current);
    gizmoAttachRef.current?.(selectedKey ?? null);
    cameraApiRef.current?.setOrbitTarget(selectedKey ?? null);
  }, [selectedKey]);

  // ---- per-reference hide (CK "1"): zero-scale hidden instances, no rebuild ----
  useEffect(() => {
    applyHidden(keyToInstancesRef.current, hiddenRefs, worldCenterRef.current);
  }, [hiddenRefs]);

  return <div ref={mountRef} className="nif-viewport cell-viewport" />;
}

const _zeroM = new THREE.Matrix4().makeScale(0, 0, 0);
const _hidePos = new THREE.Vector3();
const _hideQuat = new THREE.Quaternion();
const _hideScale = new THREE.Vector3();
const _hideM = new THREE.Matrix4();

/** Collapse hidden references' instances to zero scale (invisible + unpickable), restore the rest.
 * Billboard instances are left to the per-frame loop, which reads the same hidden set. worldCenter
 * must be the SAME point the scene was built recentered around (see where it's computed in the main
 * effect) or a restored instance snaps to the wrong place. */
function applyHidden(keyToInstances: Map<string, InstanceRef[]>, hidden: ReadonlySet<string> | undefined, worldCenter: THREE.Vector3) {
  const dirty = new Set<THREE.InstancedMesh>();
  for (const [formKey, entries] of keyToInstances) {
    const isHidden = hidden?.has(formKey) ?? false;
    for (const e of entries) {
      if (e.billboard) { continue; } // the animate loop zeroes/places billboard instances
      if (isHidden) {
        e.mesh.setMatrixAt(e.id, _zeroM);
      } else {
        _hidePos.set(e.ref.position.x - worldCenter.x, e.ref.position.y - worldCenter.y, e.ref.position.z - worldCenter.z);
        _hideQuat.setFromEuler(new THREE.Euler(e.ref.rotation.x, e.ref.rotation.y, e.ref.rotation.z, 'XYZ'));
        _hideScale.setScalar(e.ref.scale || 1);
        _hideM.compose(_hidePos, _hideQuat, _hideScale);
        // SCOL member: compose the parent's transform with this instance's own fixed placement offset,
        // or every member piece would snap back onto the parent's single origin instead of its own spot.
        if (e.local) _hideM.multiply(e.local);
        e.mesh.setMatrixAt(e.id, _hideM);
      }
      dirty.add(e.mesh);
    }
  }
  for (const m of dirty) m.instanceMatrix.needsUpdate = true;
}

function applyVisibility(
  entries: { mesh: THREE.Object3D; recordType: string }[],
  hiddenTypes?: ReadonlySet<string>,
) {
  for (const e of entries) e.mesh.visible = !hiddenTypes || !hiddenTypes.has(e.recordType);
}

const _selMat = new THREE.Matrix4();
const _selPos = new THREE.Vector3();
const _selQuat = new THREE.Quaternion();
const _selScale = new THREE.Vector3();
const _selCenter = new THREE.Vector3();
const _selSize = new THREE.Vector3();
const _selLocal = new THREE.Matrix4();

/** Position the highlight box onto the selected reference (model bbox at the REFR transform), or hide
 * it. worldCenter must be the SAME point the scene was built recentered around, or the highlight box
 * lands at the wrong (un-recentered) position relative to everything else. scolBBox is the combined
 * member-static bbox for a SCOL fallback reference (see the byScolModel build loop) -- tried when the
 * ref has no own modelPath bbox. */
function applySelection(
  highlight: THREE.LineSegments,
  modelBBox: Map<string, THREE.Box3>,
  refByKey: Map<string, CellPlacedReference>,
  selectedKey: string | null,
  worldCenter: THREE.Vector3,
  scolBBox?: Map<string, THREE.Box3>,
) {
  const ref = selectedKey ? refByKey.get(selectedKey) : undefined;
  if (!ref) { highlight.visible = false; return; }

  const box = (ref.modelPath ? modelBBox.get(ref.modelPath) : undefined) ?? scolBBox?.get(ref.formKey);
  if (box) { box.getCenter(_selCenter); box.getSize(_selSize); }
  else { _selCenter.set(0, 0, 0); _selSize.set(24, 24, 24); } // markers: a small fixed box
  // Guard against a zero-thickness axis (flat planes) so the wireframe still shows.
  _selSize.set(Math.max(_selSize.x, 1), Math.max(_selSize.y, 1), Math.max(_selSize.z, 1));

  _selPos.set(ref.position.x - worldCenter.x, ref.position.y - worldCenter.y, ref.position.z - worldCenter.z);
  _selQuat.setFromEuler(new THREE.Euler(ref.rotation.x, ref.rotation.y, ref.rotation.z, 'XYZ'));
  _selScale.setScalar(ref.scale || 1);
  _selMat.compose(_selPos, _selQuat, _selScale);                          // REFR transform
  _selLocal.makeTranslation(_selCenter.x, _selCenter.y, _selCenter.z);    // bbox center in model space
  _selLocal.scale(_selSize);                                              // unit box -> bbox size
  highlight.matrix.multiplyMatrices(_selMat, _selLocal);
  highlight.visible = true;
}
