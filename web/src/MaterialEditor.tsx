import { useMemo, useState } from 'react';
import { Save, RotateCcw, ChevronDown, ChevronRight, Undo2 } from 'lucide-react';
import type { MaterialHost, MaterialField } from './backend';

// [r,g,b] 0..1  <->  #rrggbb -- same convention NifEditor's color fields use, so the two panels
// behave identically even though BGSM values are stored/edited as strings, not numbers.
const toHex = (v: number[]) =>
  '#' + v.slice(0, 3).map(c => Math.round(Math.min(1, Math.max(0, c)) * 255).toString(16).padStart(2, '0')).join('');
const fromHex = (h: string): number[] => {
  const n = parseInt(h.slice(1), 16);
  return [((n >> 16) & 255) / 255, ((n >> 8) & 255) / 255, (n & 255) / 255];
};
const parseColor = (s: string): number[] => {
  const parts = s.split(',').map(x => Number(x.trim()));
  return parts.length === 3 && parts.every(n => !Number.isNaN(n)) ? parts : [0, 0, 0];
};
const formatColor = (arr: number[]) => arr.map(n => Number(n.toFixed(3))).join(', ');

/**
 * Material shader field editor (.bgsm and .bgem): bool -> switch, float/int -> number input, color -> picker + raw
 * value, everything else -> text input. Mirrors NifEditor's dirty-tracking/revert UX exactly (same
 * CSS classes) but groups fields by section (Material / Header) instead of by NIF block, and saves
 * through MaterialInterop.SetFields (one field-name -> new-value-string map per Save) instead of
 * NIF's typed edit list.
 */
export default function MaterialEditor(
  { fields, path, material, onSaved, appendLog }: {
    fields: MaterialField[];
    path: string;
    material: MaterialHost;
    onSaved: () => void;
    appendLog: (line: string) => void;
  }
) {
  const [edits, setEdits] = useState<Record<string, string>>({});
  const [headerOpen, setHeaderOpen] = useState(false);
  const [saving, setSaving] = useState(false);

  const orig = useMemo(() => {
    const m: Record<string, string> = {};
    for (const f of fields) m[f.name] = f.value;
    return m;
  }, [fields]);

  const dirtyKeys = Object.keys(edits);
  const dirtyCount = dirtyKeys.length;

  const cur = (f: MaterialField) => (f.name in edits ? edits[f.name] : f.value);
  const setVal = (name: string, value: string) => {
    setEdits(prev => {
      const next = { ...prev };
      if (value === orig[name]) delete next[name];
      else next[name] = value;
      return next;
    });
  };
  const revertField = (name: string) => setEdits(prev => { const n = { ...prev }; delete n[name]; return n; });
  const isDirty = (name: string) => name in edits;

  const doSave = async () => {
    if (!dirtyCount || saving) return;
    setSaving(true);
    try {
      const payload = JSON.stringify(edits);
      const res = await material.SetFields(path, payload, '');   // overwrite in place
      const ok = /^Set \d+ field/.test(res);
      appendLog(`${ok ? '✓' : '✗'} ${ok ? res : 'save failed -- ' + res}`);
      if (ok) setEdits({});
      onSaved();
    } catch (e) {
      appendLog('✗ save failed -- ' + (e instanceof Error ? e.message : String(e)));
    } finally { setSaving(false); }
  };

  const materialFields = fields.filter(f => f.section === 'material');
  const headerFields = fields.filter(f => f.section === 'header');

  return (
    <div className="nif-editor">
      <div className="nif-editor-scroll">
        <div className="nif-egroup">
          <div className="nif-egroup-head">Material <span className="nif-egroup-count">{materialFields.length}</span></div>
          <div className="nif-efields">
            {materialFields.map(f => (
              <MaterialFieldRow key={f.name} field={f} value={cur(f)} dirty={isDirty(f.name)}
                onChange={v => setVal(f.name, v)} onRevert={() => revertField(f.name)} />
            ))}
          </div>
        </div>

        {headerFields.length > 0 && (
          <div className="nif-egroup">
            <button className="nif-eblock-head" onClick={() => setHeaderOpen(o => !o)}>
              {headerOpen ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
              <span className="nif-eblock-name">Header (tiling, alpha blend, ...)</span>
              <span className="nif-eblock-type">{headerFields.length}</span>
            </button>
            {headerOpen && (
              <div className="nif-efields">
                {headerFields.map(f => (
                  <MaterialFieldRow key={f.name} field={f} value={cur(f)} dirty={isDirty(f.name)}
                    onChange={v => setVal(f.name, v)} onRevert={() => revertField(f.name)} />
                ))}
              </div>
            )}
          </div>
        )}
      </div>

      <div className="nif-editor-footer">
        <span className={`nif-dirty-count ${dirtyCount ? 'on' : ''}`}>
          {dirtyCount ? `${dirtyCount} unsaved change${dirtyCount > 1 ? 's' : ''}` : 'No changes'}
        </span>
        <div className="nif-editor-actions">
          <button className="sidebar-action-btn" disabled={!dirtyCount || saving} onClick={() => setEdits({})} title="Discard all changes">
            <RotateCcw size={13} /> Revert
          </button>
          <button className="papyrus-run nif-save-btn" disabled={!dirtyCount || saving} onClick={doSave}>
            <Save size={14} /> {saving ? 'Saving…' : 'Save'}
          </button>
        </div>
      </div>
    </div>
  );
}

function MaterialFieldRow(
  { field, value, dirty, onChange, onRevert }: {
    field: MaterialField; value: string; dirty: boolean;
    onChange: (v: string) => void; onRevert: () => void;
  }
) {
  const t = field.type;
  let control: React.ReactNode;

  if (t === 'bool') {
    control = (
      <label className="nif-switch">
        <input type="checkbox" checked={value === 'true'} onChange={e => onChange(e.target.checked ? 'true' : 'false')} />
        <span />
      </label>
    );
  } else if (t === 'float' || t === 'int') {
    control = (
      <input className="nif-in nif-num" type="number" step={t === 'int' ? 1 : 'any'}
             value={value} onChange={e => onChange(e.target.value)} />
    );
  } else if (t === 'color') {
    const arr = parseColor(value);
    control = (
      <div className="nif-color">
        <input type="color" value={toHex(arr)} onChange={e => onChange(formatColor(fromHex(e.target.value)))} />
        <span className="nif-color-hex">{value}</span>
      </div>
    );
  } else {
    control = <input className="nif-in" value={value} onChange={e => onChange(e.target.value)} />;
  }

  const stacked = t === 'string' || t === 'color';
  return (
    <div className={`nif-frow ${stacked ? 'nif-frow-stack' : ''} ${dirty ? 'dirty' : ''}`}>
      <div className="nif-frow-labelline">
        <span className="nif-flabel" title={field.name}>{field.name}</span>
      </div>
      <div className="nif-fctl">
        {control}
        {dirty && <button className="nif-revert" onClick={onRevert} title="Revert this field"><Undo2 size={12} /></button>}
      </div>
    </div>
  );
}
