// Builds a real three.js material for BSEffectShaderProperty shapes (fog/mist/glow/decal FX), instead
// of the default MeshStandardMaterial every other shape gets. That default was flat opaque and lit by
// the scene's directional lights -- exactly why effect meshes read as hard-edged "circular planes"
// instead of soft glowing FX: no alpha blending, no tint, no additive glow, and the vertex-alpha edge
// fade baked into most fog/mist cards was never even read off the mesh.
//
// v1 scope, stated honestly: this reproduces the material-level look (unlit, tinted, blended,
// vertex-alpha faded) using the real per-shape params niftool now emits (verified against the actual
// NIF data, not guessed -- see niftool/src/main.cpp's cmdGeo). It does NOT reimplement the engine's
// per-pixel view-angle fresnel falloff (softFalloffDepth / falloffStart-StopAngle) or the
// greyscale-texture palette remap (SLSF1_GREYSCALETOPALETTE_*) -- both need a custom ShaderMaterial
// with view-space normal/depth math, a genuinely separate follow-up. falloffStart/StopOpacity IS
// applied, as a flat opacity multiplier (their average), which is a reasonable stand-in for meshes
// that use falloff mainly to dim the whole effect rather than rim-shade it.
import * as THREE from 'three';
import type { NifEffectParams } from '../NifViewport';

// AlphaFunction values verified against NifSkope's own nif.xml format spec (Tools/KnownModTools/
// NifSkope/nif.xml, <enum name="AlphaFunction">) -- not guessed. Only the two that matter for
// three.js blending decisions are named here.
const ALPHA_ONE = 0;

export function makeEffectMaterial(effect: NifEffectParams | undefined, doubleSided: boolean): THREE.MeshBasicMaterial {
  const mat = new THREE.MeshBasicMaterial({ side: doubleSided ? THREE.DoubleSide : THREE.FrontSide });
  if (!effect) return mat; // effectShader:true but no `effect` block (shouldn't happen) -- safe default

  const [r, g, b, a] = effect.baseColor;
  // baseColorScale multiplies the tint (can exceed 1 for a strong glow -- e.g. 10x on a real glow-
  // flare shape seen in testing; WebGL naturally saturates on output, which is an acceptable stand-in
  // for real HDR bloom, which this viewer doesn't do).
  mat.color.setRGB(r * effect.baseColorScale, g * effect.baseColorScale, b * effect.baseColorScale);

  mat.transparent = effect.alphaBlend;
  mat.opacity = effect.useFalloff
    ? THREE.MathUtils.clamp((effect.falloffStartOpacity + effect.falloffStopOpacity) / 2, 0, 1)
    : a;
  // dst == ONE is the classic additive-glow blend (src+dst*1, i.e. pure add); anything else that's
  // alpha-blended uses normal (Porter-Duff) blending. Opaque (alphaBlend false) stays NormalBlending
  // with transparent:false, i.e. behaves like ordinary opaque geometry.
  mat.blending = effect.alphaBlend && effect.alphaDstBlend === ALPHA_ONE
    ? THREE.AdditiveBlending
    : THREE.NormalBlending;
  // Blended FX shouldn't occlude what's behind it in the depth buffer (matches how additive/alpha
  // glows are conventionally drawn) -- only opaque effect shapes keep normal depth writes.
  mat.depthWrite = !effect.alphaBlend;

  return mat;
}

/** Attach a per-vertex RGBA color attribute if the shape carried one, and tell the material to use
 * it. three.js multiplies material.color/opacity by the vertex color when the 'color' attribute has
 * 4 components (rgba) -- this is what actually produces the soft-edged "cloud" look instead of a
 * hard-edged disc, since FO4 fog cards paint vertex ALPHA fading to 0 at the edges. */
export function applyVertexColors(geo: THREE.BufferGeometry, mat: THREE.Material, vertexColors?: number[]) {
  if (!vertexColors || vertexColors.length === 0) return;
  geo.setAttribute('color', new THREE.Float32BufferAttribute(vertexColors, 4));
  mat.vertexColors = true;
}
