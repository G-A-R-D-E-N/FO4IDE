import { useMemo, useState } from 'react';
import { Save, RotateCcw, ChevronDown, ChevronRight, FolderOpen, Undo2, Layers, Boxes, Puzzle, Info } from 'lucide-react';
import type { NifHost } from './backend';


const FIELD_HELP: Record<string, string> = {
  name: "This block's name. Node/shape names are just labels -- renaming is safe because other blocks reference each other by index, not name.",
  translation: 'Position offset from the parent block, in game units (X, Y, Z). NIF space is Z-up.',
  scale: 'Uniform size multiplier for this block and its children. 1 = original size.',
  verts: 'Number of vertices in this mesh (read-only).',
  tris: 'Number of triangles in this mesh (read-only).',
  shaderType: 'The shader block driving how this mesh is lit and textured (read-only).',
  material: 'Optional path to a .bgsm material file (relative to Data\\) that overrides the shader settings below. Leave blank to use the in-NIF values.',
  emissiveColor: 'Self-illumination (glow) color of the surface. Black = no glow.',
  emissiveMultiple: 'Strength multiplier for the emissive glow color.',
  specularColor: 'Color of the shiny specular highlights.',
  specularStrength: 'Intensity of the specular highlights (0 = matte).',
  glossiness: 'Tightness of the specular highlight -- higher looks smoother/shinier with a smaller hotspot.',
  alpha: 'Overall surface opacity from the shader (1 = fully opaque). For see-through transparency you usually also enable Alpha Blend below.',
  uvOffset: 'Shifts the texture across the surface (U, V). Use to pan/align a texture.',
  uvScale: 'Tiles/scales the texture (U, V). Values above 1 repeat the texture.',
  shaderFlags: 'Rendering feature toggles for the shader. Only the commonly-edited flags are shown; any others already in the file are left untouched.',
  alphaBlend: 'Smooth transparency using the diffuse texture’s alpha channel (for glass, foliage edges, decals).',
  alphaTest: 'Hard cutout transparency -- pixels are either fully shown or fully hidden using the Threshold below (for chain-link, leaves).',
  alphaThreshold: 'Cutoff value (0-255) for Alpha Test: pixels with alpha below this are discarded.',

  eff_baseColor: 'Base tint color of the effect shader.',
  eff_baseColorScale: 'Brightness multiplier for the base color.',
  eff_emittanceColor: 'Emissive glow color of the effect.',
  eff_envMapScale: 'Strength of environment (cubemap) reflection.',
  eff_falloffStartOpacity: 'Opacity where the view-angle edge fade begins.',
  eff_falloffStopOpacity: 'Opacity where the view-angle edge fade ends.',
  eff_softFalloffDepth: 'Distance over which the effect softly fades where it meets other geometry.',
  eff_sourceTexture: 'Main texture for the effect (relative to Data\\).',
  eff_greyscaleTexture: 'Greyscale-to-palette lookup texture for the effect.',
  eff_normalTexture: 'Normal/bump map for the effect.',

  layer: 'Havok collision layer. 1 = static world geometry. Controls what this collides with.',
  boxDimensions: 'Half-extents of the collision box in Havok units (roughly game units × 0.0143).',
  boxRadius: 'Rounding/convex radius applied to the collision box edges.',
};
const TEX_HELP: Record<string, string> = {
  tex0: 'Diffuse / base color map (usually _d.dds).',
  tex1: 'Normal / bump map (usually _n.dds).',
  tex2: 'Specular, glow, or subsurface map depending on the shader.',
  tex3: 'Greyscale-to-palette or detail map.',
  tex4: 'Environment cubemap for reflections.',
  tex5: 'Environment/specular mask.',
  tex6: 'Tint or subsurface-scattering map.',
  tex7: 'Smoothness / specular map.',
};

const fieldHelp = (key: string, type: string): string => {
  if (key in TEX_HELP) return TEX_HELP[key];
  if (key === 'material' && type === 'int') return 'Havok material index -- sets the impact/footstep sound and friction of the collision.';
  return FIELD_HELP[key] ?? '';
};


export interface TreeField { key: string; label: string; type: string; value: unknown; }
export interface TreeBlock { id: number; type: string; name: string; group: string; cat: string; fields: TreeField[]; }
export interface NifTree { fo4: boolean; file: string; blocks: TreeBlock[]; }

const groupIcon = (g: string) =>
  g === 'Nodes' ? <Layers size={13} /> : g === 'Shapes' ? <Boxes size={13} /> : <Puzzle size={13} />;


const toHex = (v: number[]) =>
  '#' + v.slice(0, 3).map(c => Math.round(Math.min(1, Math.max(0, c)) * 255).toString(16).padStart(2, '0')).join('');
const fromHex = (h: string): number[] => {
  const n = parseInt(h.slice(1), 16);
  return [((n >> 16) & 255) / 255, ((n >> 8) & 255) / 255, (n & 255) / 255];
};






export default function NifEditor(
  { tree, nif, nifPath, onSaved, appendLog }: {
    tree: NifTree;
    nif: NifHost;
    nifPath: string;
    onSaved: (result: string) => void;
    appendLog: (line: string) => void;
  }
) {

  const [edits, setEdits] = useState<Record<string, unknown>>({});
  const [collapsed, setCollapsed] = useState<Record<number, boolean>>({});
  const [saving, setSaving] = useState(false);

  const orig = useMemo(() => {
    const m: Record<string, unknown> = {};
    for (const b of tree.blocks) for (const f of b.fields) m[`${b.id}::${f.key}`] = f.value;
    return m;
  }, [tree]);

  const dirtyKeys = Object.keys(edits);
  const dirtyCount = dirtyKeys.length;

  const cur = (id: number, f: TreeField) => {
    const k = `${id}::${f.key}`;
    return k in edits ? edits[k] : f.value;
  };
  const setVal = (id: number, key: string, value: unknown) => {
    const k = `${id}::${key}`;
    setEdits(prev => {
      const next = { ...prev };
      if (JSON.stringify(value) === JSON.stringify(orig[k])) delete next[k];
      else next[k] = value;
      return next;
    });
  };
  const revertField = (id: number, key: string) =>
    setEdits(prev => { const n = { ...prev }; delete n[`${id}::${key}`]; return n; });
  const isDirty = (id: number, key: string) => `${id}::${key}` in edits;

  const browseTex = async (id: number, key: string) => {
    const p = await nif.BrowseForFile('Select a DDS texture', 'DDS texture (*.dds)|*.dds|All files|*.*');
    if (p) setVal(id, key, p);
  };

  const buildEdits = () => dirtyKeys.map(k => {
    const [idStr, key] = k.split('::');
    const id = Number(idStr);
    const block = tree.blocks.find(b => b.id === id);
    return { id, cat: block?.cat ?? '', key, value: edits[k] };
  });

  const doSave = async (saveAs: boolean) => {
    if (!dirtyCount || saving) return;
    let outNif = '';
    if (saveAs) {
      outNif = await nif.BrowseForSave('Save NIF as', 'NIF mesh (*.nif)|*.nif');
      if (!outNif) return;
    }
    setSaving(true);
    try {
      const payload = JSON.stringify(buildEdits());
      const res = await nif.ApplyEdits(nifPath, payload, outNif);
      appendLog(`${/set OK/.test(res) ? '✓' : '✗'} saved ${dirtyCount} change(s)${saveAs ? ' (as new file)' : ''}`);
      if (/set OK/.test(res)) setEdits({});
      onSaved(res);
    } catch (e) {
      appendLog('✗ save failed -- ' + (e instanceof Error ? e.message : String(e)));
    } finally { setSaving(false); }
  };


  const groups = ['Nodes', 'Shapes', 'Collision', 'Extra'];
  const byGroup = groups
    .map(g => ({ g, blocks: tree.blocks.filter(b => b.group === g) }))
    .filter(x => x.blocks.length > 0);

  return (
    <div className="nif-editor">
      <div className="nif-editor-scroll">
        {byGroup.map(({ g, blocks }) => (
          <div key={g} className="nif-egroup">
            <div className="nif-egroup-head">{groupIcon(g)} {g} <span className="nif-egroup-count">{blocks.length}</span></div>
            {blocks.map(b => {
              const open = !collapsed[b.id];
              const blockDirty = b.fields.some(f => isDirty(b.id, f.key));
              return (
                <div key={b.id} className={`nif-eblock ${blockDirty ? 'dirty' : ''}`}>
                  <button className="nif-eblock-head" onClick={() => setCollapsed(p => ({ ...p, [b.id]: !p[b.id] }))}>
                    {open ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
                    <span className="nif-eblock-name">{b.name || '(unnamed)'}</span>
                    <span className="nif-eblock-type">{b.type}</span>
                    {blockDirty && <span className="nif-dot" title="unsaved changes" />}
                  </button>
                  {open && (
                    <div className="nif-efields">
                      {b.fields.map(f => (
                        <FieldRow
                          key={f.key} field={f}
                          value={cur(b.id, f)} dirty={isDirty(b.id, f.key)}
                          onChange={v => setVal(b.id, f.key, v)}
                          onRevert={() => revertField(b.id, f.key)}
                          onBrowseTex={() => browseTex(b.id, f.key)}
                        />
                      ))}
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        ))}
      </div>

      <div className="nif-editor-footer">
        <span className={`nif-dirty-count ${dirtyCount ? 'on' : ''}`}>
          {dirtyCount ? `${dirtyCount} unsaved change${dirtyCount > 1 ? 's' : ''}` : 'No changes'}
        </span>
        <div className="nif-editor-actions">
          <button className="sidebar-action-btn" disabled={!dirtyCount || saving} onClick={() => setEdits({})} title="Discard all changes">
            <RotateCcw size={13} /> Revert
          </button>
          <button className="sidebar-action-btn" disabled={!dirtyCount || saving} onClick={() => doSave(true)}>
            Save As…
          </button>
          <button className="papyrus-run nif-save-btn" disabled={!dirtyCount || saving} onClick={() => doSave(false)}>
            <Save size={14} /> {saving ? 'Saving…' : 'Save'}
          </button>
        </div>
      </div>
    </div>
  );
}

function FieldRow(
  { field, value, dirty, onChange, onRevert, onBrowseTex }: {
    field: TreeField; value: unknown; dirty: boolean;
    onChange: (v: unknown) => void; onRevert: () => void; onBrowseTex: () => void;
  }
) {
  const t = field.type;
  const help = fieldHelp(field.key, t);
  const num = (e: React.ChangeEvent<HTMLInputElement>) => (e.target.value === '' ? 0 : Number(e.target.value));
  const info = help ? <span className="nif-help" title={help}><Info size={12} /></span> : null;


  if (t === 'flags') {
    const flags = (Array.isArray(value) ? value : []) as { key: string; label: string; on: boolean }[];
    const toggle = (k: string) => onChange(flags.map(f => (f.key === k ? { ...f, on: !f.on } : f)));
    return (
      <div className={`nif-frow nif-frow-flags ${dirty ? 'dirty' : ''}`}>
        <div className="nif-flags-head">
          <span className="nif-flabel">{field.label}</span>
          {info}
          {dirty && <button className="nif-revert" onClick={onRevert} title="Revert flags"><Undo2 size={12} /></button>}
        </div>
        <div className="nif-flags-grid">
          {flags.map(f => (
            <label key={f.key} className={`nif-flagchip ${f.on ? 'on' : ''}`} title={f.key}>
              <input type="checkbox" checked={f.on} onChange={() => toggle(f.key)} />
              <span>{f.label}</span>
            </label>
          ))}
        </div>
      </div>
    );
  }

  let control: React.ReactNode;
  if (t === 'readonly') {
    control = <span className="nif-ro">{String(value)}</span>;
  } else if (t === 'string') {
    control = <input className="nif-in" value={String(value ?? '')} onChange={e => onChange(e.target.value)} />;
  } else if (t === 'tex') {
    control = (
      <div className="nif-tex-row">
        <input className="nif-in" value={String(value ?? '')} placeholder="(empty slot)"
               onChange={e => onChange(e.target.value)} />
        <button className="nif-mini-btn" onClick={onBrowseTex} title="Browse for a .dds"><FolderOpen size={12} /></button>
      </div>
    );
  } else if (t === 'bool') {
    control = <label className="nif-switch"><input type="checkbox" checked={!!value} onChange={e => onChange(e.target.checked)} /><span /></label>;
  } else if (t === 'float' || t === 'int') {
    control = <input className="nif-in nif-num" type="number" step={t === 'int' ? 1 : 'any'}
                     value={Number(value ?? 0)} onChange={e => onChange(num(e))} />;
  } else if (t === 'vec2' || t === 'vec3') {
    const arr = (Array.isArray(value) ? value : [0, 0, 0]) as number[];
    const n = t === 'vec2' ? 2 : 3;
    control = (
      <div className="nif-vec">
        {Array.from({ length: n }, (_, i) => (
          <input key={i} className="nif-in nif-num" type="number" step="any" value={Number(arr[i] ?? 0)}
                 onChange={e => { const a = arr.slice(); a[i] = Number(e.target.value); onChange(a); }} />
        ))}
      </div>
    );
  } else if (t === 'color') {
    const arr = (Array.isArray(value) ? value : [0, 0, 0]) as number[];
    control = (
      <div className="nif-color">
        <input type="color" value={toHex(arr)} onChange={e => onChange(fromHex(e.target.value))} />
        <span className="nif-color-hex">{toHex(arr)}</span>
      </div>
    );
  } else {
    control = <span className="nif-ro">{String(value)}</span>;
  }



  const stacked = t === 'string' || t === 'tex' || t === 'vec2' || t === 'vec3';
  return (
    <div className={`nif-frow ${stacked ? 'nif-frow-stack' : ''} ${dirty ? 'dirty' : ''}`}>
      <div className="nif-frow-labelline">
        <span className="nif-flabel" title={field.key}>{field.label}</span>
        {info}
      </div>
      <div className="nif-fctl">
        {control}
        {dirty && <button className="nif-revert" onClick={onRevert} title="Revert this field"><Undo2 size={12} /></button>}
      </div>
    </div>
  );
}
