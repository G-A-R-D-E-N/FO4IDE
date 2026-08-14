











import * as THREE from 'three';


export function isRotateAboutUp(mode: number | undefined): boolean {
  return mode === 1 || mode === 8 || mode === 9;
}


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










export function billboardMatrix(
  out: THREE.Matrix4,
  base: THREE.Matrix4,
  centroid: THREE.Vector3,
  camera: THREE.Camera,
  mode: number | undefined,
): THREE.Matrix4 {
  _wc.copy(centroid).applyMatrix4(base);
  const scale = _scaleV.setFromMatrixScale(base).x;

  camera.getWorldPosition(_camPos);
  if (isRotateAboutUp(mode)) {

    const yaw = Math.atan2(_camPos.x - _wc.x, _camPos.y - _wc.y);
    _quat.setFromEuler(new THREE.Euler(0, 0, yaw, 'XYZ'));
  } else {

    camera.getWorldQuaternion(_quat);
  }
  _scaleV.setScalar(scale);
  out.compose(_wc, _quat, _scaleV);
  _pivot.makeTranslation(-centroid.x, -centroid.y, -centroid.z);
  return out.multiply(_pivot);
}
