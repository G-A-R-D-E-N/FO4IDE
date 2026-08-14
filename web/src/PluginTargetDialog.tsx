// xEdit's "into which plugin?" picker.
//
// This replaces window.prompt(). The prompt asked for an exact filename, pre-filled with
// editablePlugins[0] (blank in an ordinary session), with nothing to choose from, no validation, and
// a typo silently created a new plugin. xEdit instead shows the load order and puts <new file> in
// the same list, which is what this does.
//
// Order is the real load order from GetActivePlugins (index, kind, editable), not alphabetical and
// not editable-first: choosing a target is a load-order decision, so the list has to read like one.

import { useEffect, useMemo, useRef, useState } from 'react';
import { X, Check } from 'lucide-react';
import type { ActivePlugin } from './backend';
import './PluginTargetDialog.css';

export interface TargetRequest {
  title: string;
  description?: string;
  confirmLabel?: string;
  defaultTarget?: string;
  /** A second value the caller needs, e.g. the EditorID for a duplicated record. */
  extraField?: { label: string; defaultValue: string; placeholder?: string };
}

export interface TargetResult { target: string; extra: string }

/** The two synthetic rows, mirroring xEdit's <new file> entries. */
type NewKind = 'esp' | 'esl';
const NEW_ROWS: { kind: NewKind; label: string; hint: string }[] = [
  { kind: 'esp', label: '<new file.esp>', hint: 'a full plugin' },
  { kind: 'esl', label: '<new file.esl>', hint: 'light: FormIDs limited to 0x800-0xFFF' },
];

function tagFor(p: ActivePlugin): string {
  if (p.Kind === 'light') return 'ESL';
  if (p.Kind === 'master') return 'ESM';
  return 'ESP';
}

export default function PluginTargetDialog(
  { request, onResolve }: { request: TargetRequest; onResolve: (r: TargetResult | null) => void }
) {
  const [plugins, setPlugins] = useState<ActivePlugin[]>([]);
  const [loading, setLoading] = useState(true);
  const [note, setNote] = useState('');
  const [filter, setFilter] = useState('');
  const [selected, setSelected] = useState<string>(request.defaultTarget ?? '');
  const [newKind, setNewKind] = useState<NewKind | null>(null);
  const [newName, setNewName] = useState('ConflictPatch');
  const [extra, setExtra] = useState(request.extraField?.defaultValue ?? '');
  const filterRef = useRef<HTMLInputElement>(null);
  const newRef = useRef<HTMLInputElement>(null);

  useEffect(() => { filterRef.current?.focus(); }, []);
  useEffect(() => { if (newKind) newRef.current?.focus(); }, [newKind]);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      const b = window.chrome?.webview?.hostObjects?.backend;
      try {
        const list = JSON.parse(await b?.GetActivePlugins() ?? '[]') as ActivePlugin[];
        if (cancelled) return;
        // Trust the backend's LoadOrder rather than array position.
        setPlugins([...list].sort((a, b2) => a.LoadOrder - b2.LoadOrder));
        if (list.length === 0) setNote('No plugins are loaded. Load a modlist with Open MO2, or create a new file below.');
      } catch (e) {
        if (!cancelled) setNote('Could not read the load order: ' + (e instanceof Error ? e.message : String(e)));
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, []);

  const shown = useMemo(() => {
    const q = filter.trim().toLowerCase();
    return q ? plugins.filter(p => p.Name.toLowerCase().includes(q)) : plugins;
  }, [plugins, filter]);

  const chosen = newKind ? `${newName.trim()}.${newKind}` : selected.trim();
  const nameValid = /^[^\\/:*?"<>|]+\.(esp|esm|esl)$/i.test(chosen);
  const extraOk = !request.extraField || extra.trim().length > 0;
  const canConfirm = chosen.length > 0 && nameValid && extraOk;
  const collides = newKind !== null && plugins.some(p => p.Name.toLowerCase() === chosen.toLowerCase());

  const confirm = () => { if (canConfirm) onResolve({ target: chosen, extra: extra.trim() }); };

  return (
    <div className="ptd-overlay" onClick={() => onResolve(null)}>
      <div
        className="ptd-modal glass-panel"
        onClick={e => e.stopPropagation()}
        onKeyDown={e => {
          // Esc cancels, Enter confirms -- everywhere in the dialog, including the inputs.
          if (e.key === 'Escape') { e.stopPropagation(); onResolve(null); }
          if (e.key === 'Enter' && canConfirm) { e.preventDefault(); confirm(); }
        }}
      >
        <div className="ptd-header">
          <span className="ptd-title">{request.title}</span>
          <button className="ptd-close" onClick={() => onResolve(null)} title="Cancel (Esc)"><X size={15} /></button>
        </div>

        {request.description && <div className="ptd-desc">{request.description}</div>}

        <input
          ref={filterRef}
          className="ptd-filter"
          value={filter}
          onChange={e => setFilter(e.target.value)}
          placeholder="Filter the load order…"
        />

        <div className="ptd-list">
          {/* <new file> first, as in xEdit */}
          {NEW_ROWS.map(r => (
            <button
              key={r.kind}
              className={`ptd-row ptd-row-new ${newKind === r.kind ? 'selected' : ''}`}
              onClick={() => { setNewKind(r.kind); setSelected(''); }}
            >
              <span className="ptd-idx">--</span>
              <span className="ptd-row-name">{r.label}</span>
              <span className="ptd-hint">{r.hint}</span>
              {newKind === r.kind && <Check size={13} className="ptd-row-check" />}
            </button>
          ))}

          {loading ? (
            <div className="ptd-note">Reading the load order…</div>
          ) : shown.length === 0 ? (
            <div className="ptd-note">{filter.trim() ? `No plugin matches "${filter}".` : note}</div>
          ) : shown.map(p => (
            <button
              key={p.Name}
              className={`ptd-row ${!newKind && selected === p.Name ? 'selected' : ''}`}
              onClick={() => { setNewKind(null); setSelected(p.Name); }}
              onDoubleClick={() => {
                setNewKind(null); setSelected(p.Name);
                if (extraOk) onResolve({ target: p.Name, extra: extra.trim() });
              }}
              title={`${p.Name}  (load order ${p.LoadOrder})`}
            >
              <span className="ptd-idx">{p.LoadOrder.toString(16).toUpperCase().padStart(2, '0')}</span>
              <span className="ptd-row-name">{p.Name}</span>
              <span className={`ptd-tag ptd-tag-${tagFor(p).toLowerCase()}`}>{tagFor(p)}</span>
              {p.Editable && <span className="ptd-badge" title="Already open for editing">open</span>}
              {!newKind && selected === p.Name && <Check size={13} className="ptd-row-check" />}
            </button>
          ))}
        </div>

        {newKind && (
          <label className="ptd-extra">
            <span>New plugin name</span>
            <div className="ptd-new-row">
              <input
                ref={newRef}
                value={newName}
                onChange={e => setNewName(e.target.value)}
                placeholder="ConflictPatch"
              />
              <span className="ptd-ext">.{newKind}</span>
            </div>
          </label>
        )}

        {request.extraField && (
          <label className="ptd-extra">
            <span>{request.extraField.label}</span>
            <input value={extra} onChange={e => setExtra(e.target.value)} placeholder={request.extraField.placeholder} />
          </label>
        )}

        {chosen.length > 0 && !nameValid && (
          <div className="ptd-note ptd-note-warn">"{chosen}" is not a usable plugin filename.</div>
        )}
        {collides && (
          <div className="ptd-note ptd-note-warn">
            {chosen} already exists in the load order. It will be written into rather than created.
          </div>
        )}

        <div className="ptd-actions">
          <span className="ptd-keys">Enter to confirm, Esc to cancel</span>
          <button className="ptd-btn" onClick={() => onResolve(null)}>Cancel</button>
          <button className="ptd-btn ptd-btn-primary" onClick={confirm} disabled={!canConfirm}>
            {request.confirmLabel ?? 'OK'}
          </button>
        </div>
      </div>
    </div>
  );
}
