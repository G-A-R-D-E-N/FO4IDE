import { useState, useEffect, useMemo } from 'react';
import { Link2, X, RefreshCw, ArrowUp, ArrowDown, Save, CheckCircle2, AlertTriangle, XCircle } from 'lucide-react';
import { getMasters, type MasterRow } from './backend';
import './PapyrusPanel.css';
import './MastersPanel.css';

const LS = (k: string, d: string) => localStorage.getItem('masters.' + k) ?? d;
const setLS = (k: string, v: string) => localStorage.setItem('masters.' + k, v);

/**
 * Inspect and repair a plugin's master table + ESL flag by hand -- the GUI counterpart of the
 * list_masters/reorder_masters/set_light_flag MCP tools. Reordering writes to disk immediately
 * (matches reorder_masters's own contract: it bypasses save_plugin's automatic load-order-derived
 * ordering on purpose, see WriteService.ReorderMasters's doc comment); the ESL flag stays in memory
 * until Save Plugin, matching every other in-app edit.
 */
export default function MastersPanel({ onClose }: { onClose: () => void }) {
  const [plugins, setPlugins] = useState<string[]>([]);
  const [plugin, setPlugin] = useState(() => LS('plugin', ''));
  const [rows, setRows] = useState<MasterRow[] | null>(null);
  const [order, setOrder] = useState<string[]>([]);   // working (possibly-reordered) name list
  const [light, setLight] = useState(false);
  const [origLight, setOrigLight] = useState(false);
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState('');
  const [log, setLog] = useState<string[]>([]);
  const [error, setError] = useState('');

  const masters = getMasters();
  const unavailable = !masters;

  useEffect(() => setLS('plugin', plugin), [plugin]);

  useEffect(() => {
    if (!masters) return;
    masters.GetPlugins().then(raw => {
      try { setPlugins(JSON.parse(raw) as string[]); } catch { setPlugins([]); }
    });
  }, [masters]);

  const appendLog = (line: string) =>
    setLog(prev => [`[${new Date().toLocaleTimeString()}] ${line}`, ...prev].slice(0, 200));

  const load = async (p?: string) => {
    const name = (p ?? plugin).trim();
    if (!masters || !name) return;
    setBusy(true); setError(''); setResult('');
    try {
      const raw = await masters.List(name);
      const parsed = JSON.parse(raw) as { pluginName?: string; masters?: MasterRow[]; light?: boolean; error?: string };
      if (parsed.error) throw new Error(parsed.error);
      if (!parsed.masters) throw new Error('no masters');
      setRows(parsed.masters);
      setOrder(parsed.masters.map(m => m.name));
      setLight(!!parsed.light);
      setOrigLight(!!parsed.light);
      appendLog(`• loaded masters of ${name} (${parsed.masters.length})`);
    } catch (e) {
      setRows(null);
      setError('Could not load masters: ' + (e instanceof Error ? e.message : String(e)));
    } finally { setBusy(false); }
  };

  const orderDirty = useMemo(() => rows != null && order.some((n, i) => n !== rows[i]?.name), [order, rows]);
  const lightDirty = light !== origLight;

  const move = (i: number, dir: -1 | 1) => {
    const j = i + dir;
    if (j < 0 || j >= order.length) return;
    const next = order.slice();
    [next[i], next[j]] = [next[j], next[i]];
    setOrder(next);
  };

  const applyOrder = async () => {
    if (!masters || !plugin.trim() || !orderDirty) return;
    setBusy(true);
    try {
      const res = await masters.Reorder(plugin, JSON.stringify(order));
      const ok = /^Wrote /.test(res);
      setResult(res);
      appendLog(`${ok ? '✓' : '✗'} ${ok ? 'reordered masters' : 'reorder failed'}`);
      if (ok) await load(plugin);
    } catch (e) {
      appendLog('✗ reorder failed -- ' + (e instanceof Error ? e.message : String(e)));
    } finally { setBusy(false); }
  };

  const toggleLight = async (checked: boolean) => {
    if (!masters || !plugin.trim()) return;
    setLight(checked);   // optimistic; SetLight is in-memory anyway
    setBusy(true);
    try {
      const res = await masters.SetLight(plugin, checked);
      const ok = /^Set the ESL|^Cleared the ESL/.test(res);
      appendLog(`${ok ? '✓' : '✗'} ${res}`);
      if (!ok) setLight(!checked);   // revert the checkbox if the call itself failed
    } catch (e) {
      setLight(!checked);
      appendLog('✗ set_light_flag failed -- ' + (e instanceof Error ? e.message : String(e)));
    } finally { setBusy(false); }
  };

  const savePlugin = async () => {
    if (!masters || !plugin.trim()) return;
    setBusy(true);
    try {
      const res = await masters.SavePlugin(plugin);
      const ok = /^Saved /.test(res);
      setResult(res);
      appendLog(`${ok ? '✓' : '✗'} ${ok ? 'saved ' + plugin : 'save failed'}`);
      if (ok) { setOrigLight(light); await load(plugin); }
    } catch (e) {
      appendLog('✗ save failed -- ' + (e instanceof Error ? e.message : String(e)));
    } finally { setBusy(false); }
  };

  return (
    <div className="papyrus-overlay" onClick={onClose}>
      <div className="papyrus-modal glass-panel masters-modal" onClick={e => e.stopPropagation()}>
        <div className="papyrus-header">
          <span className="papyrus-title"><Link2 size={16} /> Masters</span>
          <button className="papyrus-close" onClick={onClose} title="Close"><X size={16} /></button>
        </div>

        {unavailable && <div className="papyrus-warn">Masters bridge not available -- run the desktop app (not the browser dev server).</div>}

        <div className="masters-toolbar">
          <select className="masters-plugin-select" value={plugin} onChange={e => setPlugin(e.target.value)} disabled={unavailable}>
            <option value="">Choose a plugin…</option>
            {plugins.map(p => <option key={p} value={p}>{p}</option>)}
          </select>
          <button className="papyrus-run masters-load-btn" onClick={() => load()} disabled={busy || unavailable || !plugin.trim()}>
            <RefreshCw size={13} /> {busy ? 'Loading…' : 'Load'}
          </button>
        </div>

        <div className="masters-body">
          {!rows ? (
            <div className="nif-view-empty">{unavailable ? 'Masters bridge unavailable.' : error || 'Pick a plugin, then Load.'}</div>
          ) : (
            <>
              <table className="masters-table">
                <thead>
                  <tr><th></th><th>#</th><th>Master</th><th>Size</th><th>Used</th></tr>
                </thead>
                <tbody>
                  {order.map((name, i) => {
                    const row = rows.find(r => r.name === name);
                    return (
                      <tr key={name} className={row && !row.used ? 'unused' : ''}>
                        <td className="masters-reorder-cell">
                          <button className="masters-mini-btn" disabled={i === 0} onClick={() => move(i, -1)} title="Move up"><ArrowUp size={12} /></button>
                          <button className="masters-mini-btn" disabled={i === order.length - 1} onClick={() => move(i, 1)} title="Move down"><ArrowDown size={12} /></button>
                        </td>
                        <td>{i}</td>
                        <td className="masters-name">{name}</td>
                        <td>{row?.size != null ? `${row.size.toLocaleString()} B` : '--'}</td>
                        <td>{row?.used ? 'used' : <span className="masters-unused-flag">UNUSED</span>}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>

              <div className="masters-flag-row">
                <label className="masters-flag-label">
                  <input type="checkbox" checked={light} disabled={busy || unavailable}
                         onChange={e => toggleLight(e.target.checked)} />
                  Light plugin (ESL)
                </label>
                {lightDirty && <span className="masters-dirty-badge">unsaved -- click Save Plugin</span>}
              </div>

              <div className="masters-actions">
                <button className="sidebar-action-btn" disabled={!orderDirty || busy} onClick={applyOrder}>
                  Apply New Order (writes immediately)
                </button>
                <button className="papyrus-run" disabled={!lightDirty || busy} onClick={savePlugin}>
                  <Save size={14} /> Save Plugin
                </button>
              </div>
            </>
          )}

          {result && (
            <div className={`papyrus-banner ${/^(Wrote|Saved)/.test(result) ? 'ok' : /WARNING/.test(result) ? 'warn' : 'error'}`}>
              {/^(Wrote|Saved)/.test(result) ? <CheckCircle2 size={15} /> : /WARNING/.test(result) ? <AlertTriangle size={15} /> : <XCircle size={15} />}
              <span className="papyrus-banner-text">{result}</span>
            </div>
          )}

          <div className="papyrus-log-head">
            <span>LOG ({log.length})</span>
            {log.length > 0 && <button className="papyrus-copy" onClick={() => setLog([])}>Clear</button>}
          </div>
          <div className="papyrus-log-body">
            {log.length === 0 ? <div className="papyrus-log-empty">No actions yet.</div>
              : log.map((l, i) => <div key={i} className={`papyrus-log-row ${l.includes('✗') ? 'err' : 'ok'}`}>{l}</div>)}
          </div>
        </div>
      </div>
    </div>
  );
}
