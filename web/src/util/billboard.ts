// Shared billboard math for the NIF/Cell viewports.
//
// FO4 light fixtures parent their glow/lens-flare sprite under a NiBillboardNode so the engine
// rotates it every frame to face the camera. niftool now flags those shapes (`shape.billboard`,
// with `shape.billboardMode`); a plain three.js mesh/InstancedMesh has no billboard behavior of its
// own, so we apply the rotation ourselves each frame. Without this the sprite renders as a static
// flat plane -- the reported "lights are flat circular planes" bug.
//
// v1 scope: full camera-facing (screen-aligned) for every mode. The real modes seen in vanilla
// content are cylindrical ROTATE_ABOUT_UP (1) and camera/center-facing (2/4); cylindrical is
// approximated as full-face here. `billboardMode` is carried through the pipeline so a later pass
// can add true yaw-only cylindrical behavior without another niftool rebuild.
import * as THREE from 'three';

/** nifly::BillboardMode values that keep the sprite's up along world Z and yaw only. */
export function isRotateAboutUp(mode: number | undefined): boolean {
  return mode === 1 || mode === 8 || mode === 9;
}

/** Bounding-box center of a flat vert array [x,y,z, x,y,z, ...] -- the pivot a sprite rotates about. */
export function vertsCentroid(verts: number[], out = new THREE.Vector3()): THREE.Vector3 {
  if (verts.length < 3) return out.set(0, 0, 0);
  let minX = Infinity, minY = Infinity, minZ = Infinity;
  let maxX = -Infinity, maxY = -Infinity, maxZ = -Infinity;
  for (let i = 0; i < verts.length; i += 3) {
    const x = verts[i], y = verts[i + 1], z = verts[i + 2];
    if (x < minX) minX = x; if (x > maxX) maxX = x;
    if (y < minY) minY = y; if (y > maxY) maxY = y;
    if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
  }
  return out.set((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2);
}

const _wc = new THREE.Vector3();
const _quat = new THREE.Quaternion();
const _scaleV = new THREE.Vector3();
const _pivot = new THREE.Matrix4();
const _camPos = new THREE.Vector3();

/**
 * Build the per-frame instance/world matrix for one billboard sprite.
 *
 * `base` is the sprite's base placement (identity for the single-NIF preview; the REFR
 * Position/Rotation/Scale matrix in the Cell viewer). `centroid` is the sprite's pivot in its own
 * geometry space. The result rotates the geometry to face the camera about the world position that
 * `centroid` maps to under `base`, so the sprite spins in place rather than swinging around the NIF
 * origin. Verts transform as:  v -> R_face * S * (v - centroid) + (base * centroid).
 */
export function billboardMatrix(
  out: THREE.Matrix4,
  base: THREE.Matrix4,
  centroid: THREE.Vector3,
  camera: THREE.Camera,
  mode: number | undefined,
): THREE.Matrix4 {
  _wc.copy(centroid).applyMatrix4(base);            // world pivot
  const scale = _scaleV.setFromMatrixScale(base).x; // uniform REFR scale

  camera.getWorldPosition(_camPos);
  if (isRotateAboutUp(mode)) {
    // Cylindrical: keep world-Z up, yaw to face the camera horizontally.
    const yaw = Math.atan2(_camPos.x - _wc.x, _camPos.y - _wc.y);
    _quat.setFromEuler(new THREE.Euler(0, 0, yaw, 'XYZ'));
  } else {
    // Full face-camera: align the sprite plane to the screen.
    camera.getWorldQuaternion(_quat);
  }
  _scaleV.setScalar(scale);
  out.compose(_wc, _quat, _scaleV);
  _pivot.makeTranslation(-centroid.x, -centroid.y, -centroid.z);
  return out.multiply(_pivot);
}
