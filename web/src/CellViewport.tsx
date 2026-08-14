import { useEffect, useRef } from 'react';
import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { TransformControls } from 'three/addons/controls/TransformControls.js';
import type { NifGeo } from './NifViewport';
import type { CellPlacedReference } from './backend';
import { billboardMatrix, vertsCentroid } from './util/billboard';
import { cellLayerOf, MARKER_LAYER } from './util/cellLayer';
import { makeEffectMaterial, applyVertexColors } from './util/effectMaterial';




export type CellGeoMap = Record<string, NifGeo | { error: string }>;
















type InstanceRef = {
  mesh: THREE.InstancedMesh; id: number; ref: CellPlacedReference; billboard: boolean;






  local?: THREE.Matrix4;
};




export type CellTextureStats = {
  ok: number;
  noPath: number;
  resolveFail: number;
  decodeFail: number;
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
    hiddenRefs?: ReadonlySet<string>;
    selectedKey?: string | null;
    onSelect?: (ref: CellPlacedReference | null) => void;
    onHideSelected?: () => void;
    onUnhideAll?: () => void;
    onToggleMarkers?: () => void;
    onUndo?: () => void;
    onRedo?: () => void;



    onMoveEnd?: (ref: CellPlacedReference) => void;
  }
) {
  const mountRef = useRef<HTMLDivElement>(null);


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



  const gizmoAttachRef = useRef<((key: string | null) => void) | null>(null);


  const worldCenterRef = useRef(new THREE.Vector3());


  useEffect(() => {
    const mount = mountRef.current;
    if (!mount || references.length === 0) return;
    let cancelled = false;

    const width = mount.clientWidth || 600;
    const height = mount.clientHeight || 400;

    const scene = new THREE.Scene();
    scene.background = new THREE.Color(0x17181c);

    const camera = new THREE.PerspectiveCamera(55, width / height, 0.1, 5_000_000);
    camera.up.set(0, 0, 1);

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


    const byModel = new Map<string, CellPlacedReference[]>();
    const markers: CellPlacedReference[] = [];


    const byDecal = new Map<string, CellPlacedReference[]>();





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













    const worldCenter = posBounds.getCenter(new THREE.Vector3());
    worldCenterRef.current.copy(worldCenter);

    const texLoader = loadTexture ? new THREE.TextureLoader() : null;
    const textures: THREE.Texture[] = [];

    const texStats: CellTextureStats = { ok: 0, noPath: 0, resolveFail: 0, decodeFail: 0, firstFailure: null };
    let texStatsTimer: ReturnType<typeof setTimeout> | undefined;


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





      const mat = shape.effectShader
        ? makeEffectMaterial(shape.effect, true)
        : new THREE.MeshStandardMaterial({ color: 0x9aa0aa, metalness: 0.1, roughness: 0.8, side: THREE.DoubleSide });
      applyVertexColors(g, mat, shape.vertexColors);
      disposables.push(mat);




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



              tex.wrapS = THREE.RepeatWrapping;
              tex.wrapT = THREE.RepeatWrapping;
              tex.anisotropy = 4;
              mat.map = tex;




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






    const scolBBox = new Map<string, THREE.Box3>();
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











    const decalGeo = new THREE.PlaneGeometry(1, 1);
    disposables.push(decalGeo);
    for (const [texPath, refs] of byDecal) {
      const mat = new THREE.MeshBasicMaterial({
        color: 0xffffff, side: THREE.DoubleSide, transparent: true, depthWrite: false,
      });
      disposables.push(mat);

      if (texLoader && loadTexture) {



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


        meshEntries.push({ mesh: inst, recordType: MARKER_LAYER });
      }
    }


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






    const transformControls = new TransformControls(camera, renderer.domElement);
    transformControls.size = 0.9;
    scene.add(transformControls.getHelper());
    const gizmoAnchor = new THREE.Object3D();
    scene.add(gizmoAnchor);







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


    const raycaster = new THREE.Raycaster();
    const ndc = new THREE.Vector2();
    let downX = 0, downY = 0, downT = 0;
    const onPointerDown = (e: PointerEvent) => { downX = e.clientX; downY = e.clientY; downT = e.timeStamp; };
    const onPointerUp = (e: PointerEvent) => {
      if (transformControls.dragging) return;
      if (e.button !== 0) return;
      if (Math.hypot(e.clientX - downX, e.clientY - downY) > 5 || e.timeStamp - downT > 400) return;
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
        onSelectRef.current?.(null);
      }
    };
    renderer.domElement.addEventListener('pointerdown', onPointerDown);
    renderer.domElement.addEventListener('pointerup', onPointerUp);


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







    const setOrbitTarget = (k: string | null) => {
      const r = k ? refByKey.get(k) : undefined;
      if (r) controls.target.set(r.position.x - worldCenter.x, r.position.y - worldCenter.y, r.position.z - worldCenter.z);
      else controls.target.set(0, 0, 0);
      controls.update();
    };
    cameraApiRef.current = { focus: focusOn, topDown: topDownOn, setOrbitTarget };
    setOrbitTarget(selectedKey ?? null);







    const onKeyDown = (e: KeyboardEvent) => {
      const tag = (e.target as HTMLElement | null)?.tagName;
      if (tag === 'INPUT' || tag === 'TEXTAREA') return;
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
  }, [references, geometry, loadTexture]);


  useEffect(() => {
    applyVisibility(meshEntriesRef.current, hiddenTypes);
  }, [hiddenTypes]);


  useEffect(() => {
    if (highlightRef.current)
      applySelection(highlightRef.current, modelBBoxRef.current, refByKeyRef.current, selectedKey ?? null,
        worldCenterRef.current, scolBBoxRef.current);
    gizmoAttachRef.current?.(selectedKey ?? null);
    cameraApiRef.current?.setOrbitTarget(selectedKey ?? null);
  }, [selectedKey]);


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





function applyHidden(keyToInstances: Map<string, InstanceRef[]>, hidden: ReadonlySet<string> | undefined, worldCenter: THREE.Vector3) {
  const dirty = new Set<THREE.InstancedMesh>();
  for (const [formKey, entries] of keyToInstances) {
    const isHidden = hidden?.has(formKey) ?? false;
    for (const e of entries) {
      if (e.billboard) { continue; }
      if (isHidden) {
        e.mesh.setMatrixAt(e.id, _zeroM);
      } else {
        _hidePos.set(e.ref.position.x - worldCenter.x, e.ref.position.y - worldCenter.y, e.ref.position.z - worldCenter.z);
        _hideQuat.setFromEuler(new THREE.Euler(e.ref.rotation.x, e.ref.rotation.y, e.ref.rotation.z, 'XYZ'));
        _hideScale.setScalar(e.ref.scale || 1);
        _hideM.compose(_hidePos, _hideQuat, _hideScale);


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
  else { _selCenter.set(0, 0, 0); _selSize.set(24, 24, 24); }

  _selSize.set(Math.max(_selSize.x, 1), Math.max(_selSize.y, 1), Math.max(_selSize.z, 1));

  _selPos.set(ref.position.x - worldCenter.x, ref.position.y - worldCenter.y, ref.position.z - worldCenter.z);
  _selQuat.setFromEuler(new THREE.Euler(ref.rotation.x, ref.rotation.y, ref.rotation.z, 'XYZ'));
  _selScale.setScalar(ref.scale || 1);
  _selMat.compose(_selPos, _selQuat, _selScale);
  _selLocal.makeTranslation(_selCenter.x, _selCenter.y, _selCenter.z);
  _selLocal.scale(_selSize);
  highlight.matrix.multiplyMatrices(_selMat, _selLocal);
  highlight.visible = true;
}
