import { useState, useEffect } from 'react';
import { Search, ShieldAlert, Check, Copy } from 'lucide-react';
import './ConflictResolver.css';

interface ConflictEntry {
  FormKey: string;
  EditorID: string;
  Type: string;
  Winner: string;
  PluginsText: string;
  Count: number;
}

interface ConflictFieldRow {
  Field: string;
  DisplayLabel: string;
  Level: number;
  Values: string[];
  Differs: boolean;
}

interface ConflictMatrix {
  FormKey: string;
  EditorID: string;
  Type: string;
  Winner: string;
  Plugins: string[];
  Rows: ConflictFieldRow[];
}

export default function ConflictResolver() {
  const [conflicts, setConflicts] = useState<ConflictEntry[]>([]);
  const [selectedConflict, setSelectedConflict] = useState<ConflictEntry | null>(null);
  const [matrix, setMatrix] = useState<ConflictMatrix | null>(null);
  const [search, setSearch] = useState('');
  const [diffOnly, setDiffOnly] = useState(true);
  const [patchTarget, setPatchTarget] = useState('ConflictPatch.esp');
  const [winnerTarget, setWinnerTarget] = useState('');
  const [editablePlugins, setEditablePlugins] = useState<string[]>([]);
  const [copied, setCopied] = useState(false);

  const copyReport = async () => {
    if (!matrix) return;
    const lines: string[] = [
      `${matrix.EditorID || matrix.FormKey}  [${matrix.FormKey}]`,
      `Type: ${matrix.Type}   Winner: ${matrix.Winner}`,
      `Plugins: ${matrix.Plugins.join(', ')}`,
      '',
    ];
    for (const r of matrix.Rows ?? []) {
      if (!r.Differs) continue;
      lines.push(`${r.DisplayLabel || r.Field}:`);
      matrix.Plugins.forEach((p, i) => lines.push(`    ${p}: ${r.Values[i] ?? ''}`));
    }
    if (lines.length === 4) lines.push('(no differing fields)');
    try {
      await navigator.clipboard.writeText(lines.join('\n'));
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    } catch {
      setStatus('Could not copy to the clipboard.');
    }
  };

  const [status, setStatus] = useState('');

  useEffect(() => {
    const init = async () => {

      if (window.chrome?.webview?.hostObjects?.backend) {

        const backend = window.chrome.webview.hostObjects.backend;
        const json = await backend.GetConflicts();
        setConflicts(JSON.parse(json));

        const plugins: string[] = JSON.parse(await backend.GetEditablePlugins());
        setEditablePlugins([...plugins, 'ConflictPatch.esp']);
      }
    };
    init();
  }, []);

  useEffect(() => {
    const loadMatrix = async () => {
      if (!selectedConflict) return;

      if (window.chrome?.webview?.hostObjects?.backend) {

        const backend = window.chrome.webview.hostObjects.backend;
        const json = await backend.GetConflictMatrix(selectedConflict.FormKey);
        const data = JSON.parse(json);
        setMatrix(data);
        setWinnerTarget(data.Winner);
      }
    };
    loadMatrix();
  }, [selectedConflict]);

  const handleResolve = async () => {
    if (!matrix) return;
    const backend = window.chrome?.webview?.hostObjects?.backend;
    if (!backend) return;
    const msg = await backend.ResolveConflict(matrix.FormKey, winnerTarget, patchTarget);
    setStatus(msg);
  };

  const handleSave = async () => {
    const backend = window.chrome?.webview?.hostObjects?.backend;
    if (!backend) return;
    const msg = await backend.SavePatch(patchTarget);
    setStatus(msg);
  };

  const filteredConflicts = conflicts.filter(c =>
    c.EditorID.toLowerCase().includes(search.toLowerCase()) ||
    c.FormKey.toLowerCase().includes(search.toLowerCase()) ||
    c.PluginsText.toLowerCase().includes(search.toLowerCase())
  );

  const getCellColor = (vals: string[], idx: number, differs: boolean) => {
    const present = vals.filter(v => v.length > 0);
    const me = vals[idx];
    if (present.length === 0) return 'var(--conflict-empty)';
    if (!differs) return 'var(--conflict-green)';
    if (me.length === 0) return 'var(--conflict-empty)';
    if (present.length === 1) return 'var(--conflict-yellow)';

    let winner = -1;
    for (let i = vals.length - 1; i >= 0; i--) {
      if (vals[i].length > 0) { winner = i; break; }
    }

    if (idx === winner) return 'var(--conflict-orange)';
    if (idx === 0) return 'var(--conflict-purple)';
    return 'var(--conflict-red)';
  };

  return (
    <div className="cr-container animate-fade-in">
      {}
      <div className="cr-sidebar">
        <div className="cr-sidebar-header">
          <h2><ShieldAlert size={18} /> Conflicts</h2>
          <div className="cr-search-box">
            <Search size={14} className="cr-search-icon" />
            <input
              type="text"
              placeholder="Search ID, FormKey, plugins..."
              value={search}
              onChange={e => setSearch(e.target.value)}
            />
          </div>
        </div>
        <div className="cr-list">
          {filteredConflicts.map(c => (
            <div
              key={c.FormKey}
              className={`cr-list-item ${selectedConflict?.FormKey === c.FormKey ? 'selected' : ''}`}
              onClick={() => setSelectedConflict(c)}
            >
              <div className="cr-li-title">{c.EditorID || c.FormKey}</div>
              <div className="cr-li-sub">{c.Type} • {c.Count} plugins</div>
            </div>
          ))}
        </div>
        <div className="cr-sidebar-footer">
          {filteredConflicts.length} / {conflicts.length} conflicts
        </div>
      </div>

      {}
      <div className="cr-main">
        {matrix ? (
          <>
            <div className="cr-header glass-panel">
              <div className="cr-header-title">
                <h1>{matrix.EditorID || matrix.FormKey}</h1>
                <span className="cr-badge">{matrix.Type}</span>
                <span className="cr-badge-sub">{matrix.FormKey}</span>
              </div>
              <div className="cr-toolbar">
                <label className="cr-checkbox">
                  <input type="checkbox" checked={diffOnly} onChange={e => setDiffOnly(e.target.checked)} />
                  Only Differing Fields
                </label>
                <div className="cr-legend">
                  <span className="cr-legend-item differs">differs</span>
                  <span className="cr-legend-item identical">identical</span>
                </div>
              </div>
            </div>

            <div className="cr-grid-container">
              <table className="cr-grid">
                <thead>
                  <tr>
                    <th className="cr-field-col">Field</th>
                    {matrix.Plugins.map((p, i) => (
                      <th key={p}>
                        {p}
                        {i === matrix.Plugins.length - 1 && matrix.Plugins.length > 1 &&
                          <span className="cr-winner-badge">◀ WINNER</span>}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {matrix.Rows.filter(r => !diffOnly || r.Differs).map((r, rowIdx) => (
                    <tr key={rowIdx}>
                      <td className="cr-field-col" style={{ color: r.Differs ? 'var(--text-highlight)' : 'var(--text-primary)' }}>
                        <div style={{ marginLeft: `${r.Level * 16}px` }}>{r.DisplayLabel}</div>
                      </td>
                      {r.Values.map((v, i) => (
                        <td key={i} style={{ backgroundColor: getCellColor(r.Values, i, r.Differs) }}>
                          {v}
                        </td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div className="cr-footer glass-panel">
              <div className="cr-resolve-row">
                <div className="cr-resolve-group">
                  <span>Make winner:</span>
                  <select value={winnerTarget} onChange={e => setWinnerTarget(e.target.value)}>
                    {matrix.Plugins.map(p => <option key={p} value={p}>{p}</option>)}
                  </select>
                </div>
                <div className="cr-resolve-group">
                  <span>Patch into:</span>
                  <select value={patchTarget} onChange={e => setPatchTarget(e.target.value)}>
                    {editablePlugins.map(p => <option key={p} value={p}>{p}</option>)}
                  </select>
                </div>
                <button className="button button-success" onClick={handleResolve}>
                  <Check size={14} /> Resolve
                </button>
                <button className="button" onClick={handleSave}>
                  Save Patch
                </button>
              </div>
              <div className="cr-actions-row">
                <span className="cr-status">{status}</span>
                <div className="cr-actions-right">
                  {

}
                  <button className="button" onClick={copyReport} title="Copy this conflict as text">
                    <Copy size={14} /> {copied ? 'Copied' : 'Copy Report'}
                  </button>
                </div>
              </div>
            </div>
          </>
        ) : (
          <div className="cr-empty">
            <ShieldAlert size={48} color="var(--border-color)" />
            <h2>Select a conflict to see what differs</h2>
            <p>Conflicts will be displayed in an interactive matrix.</p>
          </div>
        )}
      </div>
    </div>
  );
}
