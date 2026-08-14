













import * as THREE from 'three';
import type { NifEffectParams } from '../NifViewport';




const ALPHA_ONE = 0;

export function makeEffectMaterial(effect: NifEffectParams | undefined, doubleSided: boolean): THREE.MeshBasicMaterial {
  const mat = new THREE.MeshBasicMaterial({ side: doubleSided ? THREE.DoubleSide : THREE.FrontSide });
  if (!effect) return mat;

  const [r, g, b, a] = effect.baseColor;



  mat.color.setRGB(r * effect.baseColorScale, g * effect.baseColorScale, b * effect.baseColorScale);

  mat.transparent = effect.alphaBlend;
  mat.opacity = effect.useFalloff
    ? THREE.MathUtils.clamp((effect.falloffStartOpacity + effect.falloffStopOpacity) / 2, 0, 1)
    : a;



  mat.blending = effect.alphaBlend && effect.alphaDstBlend === ALPHA_ONE
    ? THREE.AdditiveBlending
    : THREE.NormalBlending;


  mat.depthWrite = !effect.alphaBlend;

  return mat;
}





export function applyVertexColors(geo: THREE.BufferGeometry, mat: THREE.Material, vertexColors?: number[]) {
  if (!vertexColors || vertexColors.length === 0) return;
  geo.setAttribute('color', new THREE.Float32BufferAttribute(vertexColors, 4));
  mat.vertexColors = true;
}
