import { useEffect, useRef } from 'react';
import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { billboardMatrix, vertsCentroid } from './util/billboard';
import { makeEffectMaterial, applyVertexColors } from './util/effectMaterial';

export interface NifTexRef { slot: number; path: string; }
export interface NifShapeGeo {
  name: string;
  verts: number[];
  tris: number[];
  normals: number[];
  uvs: number[];
  textures?: NifTexRef[];
  skinned?: boolean;
  billboard?: boolean;
  billboardMode?: number;
  effectShader?: boolean;
  bgsmPath?: string;

  effect?: NifEffectParams;
  vertexColors?: number[];
}

export interface NifEffectParams {
  baseColor: [number, number, number, number];
  baseColorScale: number;
  emissiveColor: [number, number, number];
  emissiveMultiple: number;
  useFalloff: boolean;
  falloffStartAngle: number; falloffStopAngle: number;
  falloffStartOpacity: number; falloffStopOpacity: number;
  softFalloffDepth: number;
  vertexAlpha: boolean;
  alphaBlend: boolean;
  alphaSrcBlend?: number;
  alphaDstBlend?: number;
}
export interface NifGeo { fo4: boolean; shapes: NifShapeGeo[]; }

type ShapeMat = THREE.MeshStandardMaterial | THREE.MeshBasicMaterial;

export default function NifViewport(
  { data, wireframe, textured, loadTexture }: {
    data: NifGeo | null;
    wireframe: boolean;
    textured: boolean;
    loadTexture?: (relPath: string) => Promise<string>;
  }
) {
  const mountRef = useRef<HTMLDivElement>(null);
  const matsRef = useRef<ShapeMat[]>([]);

  useEffect(() => {
    const mount = mountRef.current;
    if (!mount || !data || data.shapes.length === 0) return;
    let cancelled = false;

    const width = mount.clientWidth || 600;
    const height = mount.clientHeight || 400;

    const scene = new THREE.Scene();
    scene.background = new THREE.Color(0x17181c);

    const camera = new THREE.PerspectiveCamera(50, width / height, 0.01, 1_000_000);
    camera.up.set(0, 0, 1);

    const renderer = new THREE.WebGLRenderer({ antialias: true });
    renderer.setSize(width, height);
    renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    mount.appendChild(renderer.domElement);

    const controls = new OrbitControls(camera, renderer.domElement);
    controls.enableDamping = true;
    controls.dampingFactor = 0.08;

    scene.add(new THREE.AmbientLight(0xffffff, 0.65));
    const key = new THREE.DirectionalLight(0xffffff, 1.0); key.position.set(1, 1, 2); scene.add(key);
    const fill = new THREE.DirectionalLight(0xffffff, 0.35); fill.position.set(-1.5, -1, 0.5); scene.add(fill);

    const group = new THREE.Group();
    const box = new THREE.Box3();
    const mats: ShapeMat[] = [];
    const geos: THREE.BufferGeometry[] = [];
    const texes: THREE.Texture[] = [];
    const pairs: { mat: ShapeMat; shape: NifShapeGeo; hasUV: boolean }[] = [];

    const billboards: { mesh: THREE.Mesh; centroid: THREE.Vector3; mode?: number }[] = [];

    for (const shape of data.shapes) {
      if (!shape.verts.length || !shape.tris.length) continue;
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
      if (g.boundingBox) box.union(g.boundingBox);

      const mat: ShapeMat = shape.effectShader
        ? makeEffectMaterial(shape.effect, true)
        : new THREE.MeshStandardMaterial({
            color: 0x9aa0aa, metalness: 0.1, roughness: 0.75,
            side: THREE.DoubleSide, wireframe, flatShading: false,
          });
      applyVertexColors(g, mat, shape.vertexColors);
      const mesh = new THREE.Mesh(g, mat);
      group.add(mesh);
      if (shape.billboard) {
        mesh.matrixAutoUpdate = false;
        mesh.frustumCulled = false;
        billboards.push({ mesh, centroid: vertsCentroid(shape.verts), mode: shape.billboardMode });
      }
      mats.push(mat);
      geos.push(g);
      pairs.push({ mat, shape, hasUV });
    }
    scene.add(group);
    matsRef.current = mats;

    if (textured && loadTexture) {
      const texLoader = new THREE.TextureLoader();
      const applyUrl = (url: string): Promise<THREE.Texture | null> =>
        new Promise(res => {
          if (!url) return res(null);
          texLoader.load(url, t => res(t), undefined, () => res(null));
        });
      for (const { mat, shape, hasUV } of pairs) {
        if (!hasUV || !(shape.textures?.length || shape.bgsmPath)) continue;

        const diffuse = shape.textures?.find(t => t.slot === 0)
          ?? (shape.bgsmPath ? { slot: 0, path: shape.bgsmPath } : undefined);

        const normal = shape.effectShader ? undefined : shape.textures?.find(t => t.slot === 1);
        if (diffuse) {
          loadTexture(diffuse.path).then(applyUrl).then(tex => {
            if (cancelled || !tex) return;
            tex.flipY = false; tex.colorSpace = THREE.SRGBColorSpace;

            tex.wrapS = THREE.RepeatWrapping; tex.wrapT = THREE.RepeatWrapping;
            tex.anisotropy = 4;
            mat.map = tex;

            if (!shape.effectShader) mat.color.setHex(0xffffff);
            mat.needsUpdate = true;
            texes.push(tex);
          });
        }
        if (normal && mat instanceof THREE.MeshStandardMaterial) {
          loadTexture(normal.path).then(applyUrl).then(tex => {
            if (cancelled || !tex) return;
            tex.flipY = false; tex.colorSpace = THREE.NoColorSpace;
            tex.wrapS = THREE.RepeatWrapping; tex.wrapT = THREE.RepeatWrapping;
            mat.normalMap = tex; mat.needsUpdate = true;
            texes.push(tex);
          });
        }
      }
    }

    const size = box.getSize(new THREE.Vector3());
    const center = box.getCenter(new THREE.Vector3());
    const radius = Math.max(size.x, size.y, size.z, 1e-3);

    const grid = new THREE.GridHelper(radius * 4, 24, 0x3a5a80, 0x2a2a30);
    grid.rotation.x = Math.PI / 2;
    grid.position.set(center.x, center.y, box.min.z);
    scene.add(grid);
    const axes = new THREE.AxesHelper(radius * 1.2);
    axes.position.copy(center);
    scene.add(axes);

    controls.target.copy(center);
    camera.near = radius / 200;
    camera.far = radius * 200;
    camera.position.set(center.x + radius * 1.9, center.y - radius * 2.3, center.z + radius * 1.5);
    camera.updateProjectionMatrix();
    controls.update();

    const identity = new THREE.Matrix4();
    let raf = 0;
    const animate = () => {
      controls.update();
      for (const b of billboards) billboardMatrix(b.mesh.matrix, identity, b.centroid, camera, b.mode);
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
      controls.dispose();
      geos.forEach(g => g.dispose());
      mats.forEach(m => m.dispose());
      texes.forEach(t => t.dispose());
      renderer.dispose();
      if (renderer.domElement.parentElement === mount) mount.removeChild(renderer.domElement);
      matsRef.current = [];
    };
  }, [data, textured, loadTexture]);

  useEffect(() => {
    for (const m of matsRef.current) m.wireframe = wireframe;
  }, [wireframe]);

  return <div ref={mountRef} className="nif-viewport" />;
}
