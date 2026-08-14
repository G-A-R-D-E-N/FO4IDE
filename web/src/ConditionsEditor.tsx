import { useEffect, useMemo, useState } from 'react';
import { X, Plus, Trash2, ArrowUp, ArrowDown, Search } from 'lucide-react';
import type { ConditionDto } from './backend';
import RecordPicker from './RecordPicker';
import './ConditionsEditor.css';

const back = () => window.chrome?.webview?.hostObjects?.backend;

const OPERATORS = ['==', '!=', '>', '>=', '<', '<='];
const BLANK: ConditionDto = { function: 'GetItemCount', operator: '==', value: 1, runOn: 'Subject' };

interface ParamSlot { label: string; kind: 'record' | 'number' | 'text'; types: string; }
type ParamTable = Record<string, ParamSlot[]>;

type PickTarget = { index: number; key: 'param1' | 'param2' | 'reference' | 'compareGlobal'; types: string };

export default function ConditionsEditor(
  { plugin, record, path, label, onClose, onSaved }:
  { plugin: string; record: string; path: string; label: string;
    onClose: () => void; onSaved: (msg: string) => void }
) {
  const [rows, setRows] = useState<ConditionDto[] | null>(null);
  const [functions, setFunctions] = useState<string[]>([]);
  const [runOns, setRunOns] = useState<string[]>([]);
  const [paramTable, setParamTable] = useState<ParamTable>({});
  const [labels, setLabels] = useState<Record<string, string>>({});
  const [error, setError] = useState('');
  const [saving, setSaving] = useState(false);
  const [pick, setPick] = useState<PickTarget | null>(null);
  const [fnFilter, setFnFilter] = useState('');

  useEffect(() => {
    const b = back();
    if (!b) { setError('No backend bridge.'); setRows([]); return; }
    (async () => {
      try {
        const raw = await b.GetConditionsAt(plugin, record, path);
        const parsed = JSON.parse(raw);
        if (Array.isArray(parsed)) setRows(parsed);
        else { setError(String(raw)); setRows([]); }
      } catch (e: any) { setError('Could not read conditions: ' + (e?.message || e)); setRows([]); }
      try { setFunctions(JSON.parse(await b.GetConditionFunctions())); } catch { setFunctions([]); }
      try { setRunOns(JSON.parse(await b.GetConditionRunOnTypes())); } catch { setRunOns(['Subject']); }
      try { setParamTable(JSON.parse(await b.GetConditionFunctionParams())); } catch { setParamTable({}); }
    })();
  }, [plugin, record, path]);

  useEffect(() => {
    const b = back();
    if (!b || !rows) return;
    const keys = new Set<string>();
    for (const r of rows) {
      for (const v of [r.param1, r.param2, r.reference, r.compareGlobal])
        if (typeof v === 'string' && /^[0-9A-Fa-f]{6}:/.test(v)) keys.add(v);
    }
    const missing = [...keys].filter(k => !(k in labels));
    if (missing.length === 0) return;
    (async () => {
      try {
        const got = JSON.parse(await b.ResolveFormKeyLabels(missing.join(',')));
        setLabels(prev => ({ ...prev, ...got }));
      } catch {  }
    })();
  }, [rows]);

  const slotsFor = (fn: string): ParamSlot[] => paramTable[fn] ?? [];
  const shownFunctions = useMemo(() => {
    const q = fnFilter.trim().toLowerCase();
    return q ? functions.filter(f => f.toLowerCase().includes(q)) : functions;
  }, [functions, fnFilter]);

  const patch = (i: number, change: Partial<ConditionDto>) =>
    setRows(rs => (rs ?? []).map((r, ri) => (ri === i ? { ...r, ...change } : r)));

  const move = (i: number, delta: number) =>
    setRows(rs => {
      if (!rs) return rs;
      const j = i + delta;
      if (j < 0 || j >= rs.length) return rs;
      const next = [...rs];
      [next[i], next[j]] = [next[j], next[i]];
      return next;
    });

  const save = async () => {
    const b = back();
    if (!b || !rows) return;

    const unset = rows.findIndex(r => r.compareGlobal !== undefined && !String(r.compareGlobal).trim());
    if (unset >= 0) {
      setError(`Condition ${unset + 1} compares against a global but none is picked. `
        + 'Choose one, or switch it back to comparing against a number -- saving as-is would quietly '
        + 'turn it into a numeric comparison against 1.');
      return;
    }

    setError('');
    setSaving(true);
    try {
      onSaved(await b.SetConditionsAt(plugin, record, path, JSON.stringify(rows)));
      onClose();
    } catch (e: any) {
      setError('Save failed: ' + (e?.message || e));
      setSaving(false);
    }
  };

  const shown = (v: unknown) => {
    const s = String(v ?? '');
    return labels[s] ?? s;
  };

  const renderParam = (r: ConditionDto, i: number, slot: ParamSlot, key: 'param1' | 'param2') => (
    <label className="ce-field" key={key}>
      <span>{slot.label}</span>
      {slot.kind === 'record' ? (
        <button className="ce-ref" onClick={() => setPick({ index: i, key, types: slot.types })}
          title={String(r[key] ?? '')}>
          {r[key] ? shown(r[key]) : 'pick a record…'}
        </button>
      ) : (
        <input
          type={slot.kind === 'number' ? 'number' : 'text'}
          value={String(r[key] ?? '')}
          onChange={e => patch(i, { [key]: slot.kind === 'number' ? Number(e.target.value) : e.target.value } as Partial<ConditionDto>)}
        />
      )}
    </label>
  );

  return (
    <div className="ce-overlay" onClick={onClose}>
      <div className="ce-modal" onClick={e => e.stopPropagation()}>
        <div className="ce-header">
          <div className="ce-title">
            <span className="ce-title-main">Conditions</span>
            <span className="ce-title-sub">{label} · {plugin}{path === 'Conditions' ? '' : ` · ${path}`}</span>
          </div>
          <button className="ce-icon" onClick={onClose} title="Close"><X size={16} /></button>
        </div>

        {error && <div className="ce-error">{error}</div>}

        <div className="ce-search">
          <Search size={13} />
          <input value={fnFilter} onChange={e => setFnFilter(e.target.value)}
            placeholder="Filter the function list…" />
        </div>

        <div className="ce-rows">
          {rows === null ? (
            <div className="ce-note">Reading…</div>
          ) : rows.length === 0 ? (
            <div className="ce-note">No conditions. An empty list means the record always applies.</div>
          ) : rows.map((r, i) => {
            const slots = slotsFor(r.function);
            const usesGlobal = r.compareGlobal !== undefined;
            return (
              <div className="ce-row" key={i}>
                <div className="ce-line">
                  <span className="ce-index">{i + 1}</span>

                  <select className="ce-fn" value={r.function} onChange={e => patch(i, { function: e.target.value })}>
                    {(shownFunctions.includes(r.function) ? shownFunctions : [r.function, ...shownFunctions])
                      .map(f => <option key={f} value={f}>{f}</option>)}
                  </select>

                  <select className="ce-op" value={r.operator} onChange={e => patch(i, { operator: e.target.value })}>
                    {OPERATORS.map(o => <option key={o} value={o}>{o}</option>)}
                  </select>

                  {usesGlobal ? (
                    <button className="ce-ref ce-cmp" onClick={() => setPick({ index: i, key: 'compareGlobal', types: 'Global' })}>
                      {r.compareGlobal ? shown(r.compareGlobal) : 'pick a global…'}
                    </button>
                  ) : (
                    <input className="ce-cmp" type="number" step="any" value={r.value ?? 0}
                      onChange={e => patch(i, { value: Number(e.target.value) })} />
                  )}

                  <button className="ce-toggle"
                    title={usesGlobal ? 'Compare against a number instead' : 'Compare against a global variable instead'}
                    onClick={() => patch(i, usesGlobal
                      ? { compareGlobal: undefined, value: r.value ?? 1 }
                      : { compareGlobal: '', value: undefined })}>
                    {usesGlobal ? 'global' : 'number'}
                  </button>

                  <span className="ce-grow" />
                  <button className="ce-icon" title="Move up" disabled={i === 0}
                    onClick={() => move(i, -1)}><ArrowUp size={13} /></button>
                  <button className="ce-icon" title="Move down" disabled={i === rows.length - 1}
                    onClick={() => move(i, 1)}><ArrowDown size={13} /></button>
                  <button className="ce-icon ce-danger" title="Remove"
                    onClick={() => setRows(rs => (rs ?? []).filter((_, ri) => ri !== i))}><Trash2 size={13} /></button>
                </div>

                {(slots.length > 0 || r.runOn !== 'Subject') && (
                  <div className="ce-args">
                    {slots[0] && renderParam(r, i, slots[0], 'param1')}
                    {slots[1] && renderParam(r, i, slots[1], 'param2')}

                    <label className="ce-field">
                      <span>Run on</span>
                      <select value={r.runOn ?? 'Subject'} onChange={e => patch(i, { runOn: e.target.value })}>
                        {(runOns.length ? runOns : ['Subject']).map(o => <option key={o} value={o}>{o}</option>)}
                      </select>
                    </label>

                    {r.runOn === 'Reference' && (
                      <label className="ce-field">
                        <span>Reference</span>
                        <button className="ce-ref"
                          onClick={() => setPick({ index: i, key: 'reference', types: 'PlacedObject,PlacedNpc' })}>
                          {r.reference ? shown(r.reference) : 'pick a reference…'}
                        </button>
                      </label>
                    )}
                  </div>
                )}

                {slots.length === 0 && (r.runOn ?? 'Subject') === 'Subject' && (
                  <div className="ce-args ce-args-empty">
                    <span>Takes no parameters.</span>
                    <button className="ce-linkish" onClick={() => patch(i, { runOn: 'Reference' })}>
                      run it on a reference instead
                    </button>
                  </div>
                )}
              </div>
            );
          })}
        </div>

        <div className="ce-footer">
          <button onClick={() => setRows(rs => [...(rs ?? []), { ...BLANK }])}>
            <Plus size={13} /> Add condition
          </button>
          <span className="ce-grow" />
          <button onClick={onClose}>Cancel</button>
          <button className="ce-primary" disabled={rows === null || saving} onClick={save}>
            {saving ? 'Saving…' : 'Save'}
          </button>
        </div>
      </div>

      {pick && (
        <RecordPicker
          title={pick.key}
          refType={pick.types.split(',')[0] || null}
          refTypes={pick.types || null}
          onPick={fk => { patch(pick.index, { [pick.key]: fk } as Partial<ConditionDto>); setPick(null); }}
          onClose={() => setPick(null)}
        />
      )}
    </div>
  );
}
