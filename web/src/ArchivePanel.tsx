import { useState, useEffect } from 'react';
import { Archive as ArchiveIcon, X, FolderOpen, Download, DownloadCloud, CheckCircle2, XCircle, GitCompare, ChevronDown, ChevronRight, Package as PackageIcon } from 'lucide-react';
import { getArchive, type ArchiveEntry, type ArchiveFilterMode, type ArchiveCompareResult } from './backend';
import './PapyrusPanel.css';
import './ArchivePanel.css';

const LS = (k: string, d: string) => localStorage.getItem('archive.' + k) ?? d;
const setLS = (k: string, v: string) => localStorage.setItem('archive.' + k, v);

/** Browse and pull files out of a FO4 BA2/BSA archive by hand -- the GUI counterpart of the
 * archive_list/archive_extract/archive_extract_all MCP tools. Read-only against the archive itself;
 * extraction writes only to the destination the user picks. Wildcard/regex filtering and the
 * archive-compare tool were ported from AlexxEG/BSA_Browser (GPL-3.0, reviewed directly) -- see
 * ArchiveService.BuildMatcher/CompareArchivesJson for the exact algorithms. */
export default function ArchivePanel({ onClose }: { onClose: () => void }) {
  const [archivePath, setArchivePath] = useState(() => LS('archivePath', ''));
  const [filter, setFilter] = useState('');
  const [filterMode, setFilterMode] = useState<ArchiveFilterMode>(() => (LS('filterMode', 'simple') as ArchiveFilterMode));
  const [entries, setEntries] = useState<ArchiveEntry[] | null>(null);
  const [meta, setMeta] = useState<{ totalCount: number; shownCount: number; truncated: boolean } | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState('');
  const [error, setError] = useState('');
  const [lastOutDir, setLastOutDir] = useState('');

  // Compare sub-panel
  const [compareOpen, setCompareOpen] = useState(false);
  const [compareB, setCompareB] = useState('');
  const [compareBusy, setCompareBusy] = useState(false);
  const [compareResult, setCompareResult] = useState<ArchiveCompareResult | null>(null);
  const [compareError, setCompareError] = useState('');

  // Pack sub-panel
  const [packOpen, setPackOpen] = useState(false);
  const [packSources, setPackSources] = useState<string[]>(() => {
    try { return JSON.parse(LS('packSources', '[]')); } catch { return []; }
  });
  const [packOutput, setPackOutput] = useState(() => LS('packOutput', ''));
  const [packFormat, setPackFormat] = useState(() => LS('packFormat', 'General'));
  const [packRoot, setPackRoot] = useState(() => LS('packRoot', ''));
  const [packCompress, setPackCompress] = useState(true);
  const [packBusy, setPackBusy] = useState(false);
  const [packResult, setPackResult] = useState('');

  const archive = getArchive();
  const unavailable = !archive;

  useEffect(() => setLS('archivePath', archivePath), [archivePath]);
  useEffect(() => setLS('filterMode', filterMode), [filterMode]);
  useEffect(() => setLS('packSources', JSON.stringify(packSources)), [packSources]);
  useEffect(() => setLS('packOutput', packOutput), [packOutput]);
  useEffect(() => setLS('packFormat', packFormat), [packFormat]);
  useEffect(() => setLS('packRoot', packRoot), [packRoot]);

  const browseArchive = async () => {
    if (!archive) return;
    const p = await archive.BrowseForFile('Select a BA2/BSA archive', 'Bethesda archive (*.ba2;*.bsa)|*.ba2;*.bsa|All files|*.*');
    if (p) { setArchivePath(p); load(p); }
  };

  const load = async (path?: string, f?: string) => {
    const p = (path ?? archivePath).trim();
    if (!archive || !p) return;
    setBusy(true); setError(''); setResult('');
    try {
      const raw = await archive.List(p, f ?? filter, 2000, filterMode);
      const parsed = JSON.parse(raw) as { archiveName?: string; totalCount?: number; shownCount?: number; truncated?: boolean; entries?: ArchiveEntry[]; error?: string };
      if (parsed.error) throw new Error(parsed.error);
      if (!parsed.entries) throw new Error('no entries');
      setEntries(parsed.entries);
      setMeta({ totalCount: parsed.totalCount ?? parsed.entries.length, shownCount: parsed.shownCount ?? parsed.entries.length, truncated: !!parsed.truncated });
      setSelected(new Set());
    } catch (e) {
      setEntries(null);
      setError('Could not read archive: ' + (e instanceof Error ? e.message : String(e)));
    } finally { setBusy(false); }
  };

  const toggle = (path: string) => {
    setSelected(prev => {
      const next = new Set(prev);
      if (next.has(path)) next.delete(path); else next.add(path);
      return next;
    });
  };
  const toggleAll = () => {
    if (!entries) return;
    setSelected(prev => (prev.size === entries.length ? new Set() : new Set(entries.map(e => e.path))));
  };

  const extractSelected = async () => {
    if (!archive || !archivePath.trim() || selected.size === 0) return;
    const outDir = await archive.BrowseForFolder('Extract selected files to');
    if (!outDir) return;
    setBusy(true);
    try {
      const res = await archive.ExtractSelected(archivePath, JSON.stringify(Array.from(selected)), outDir);
      setResult(res);
      if (/^Extracted/.test(res)) setLastOutDir(outDir);
    } catch (e) {
      setResult('Error: ' + (e instanceof Error ? e.message : String(e)));
    } finally { setBusy(false); }
  };

  const extractAll = async () => {
    if (!archive || !archivePath.trim()) return;
    const outDir = await archive.BrowseForFolder('Extract all (matching) files to');
    if (!outDir) return;
    setBusy(true);
    try {
      const res = await archive.ExtractAll(archivePath, outDir, filter, 5000, filterMode);
      setResult(res);
      if (/^Extracted/.test(res)) setLastOutDir(outDir);
    } catch (e) {
      setResult('Error: ' + (e instanceof Error ? e.message : String(e)));
    } finally { setBusy(false); }
  };

  const openOut = async () => { if (archive && lastOutDir) await archive.OpenFolder(lastOutDir); };

  const browseCompareB = async () => {
    if (!archive) return;
    const p = await archive.BrowseForFile('Select the archive to compare against', 'Bethesda archive (*.ba2;*.bsa)|*.ba2;*.bsa|All files|*.*');
    if (p) setCompareB(p);
  };

  const runCompare = async () => {
    if (!archive || !archivePath.trim() || !compareB.trim()) return;
    setCompareBusy(true); setCompareError(''); setCompareResult(null);
    try {
      const raw = await archive.Compare(archivePath, compareB);
      const parsed = JSON.parse(raw) as ArchiveCompareResult;
      if (parsed.error) throw new Error(parsed.error);
      setCompareResult(parsed);
    } catch (e) {
      setCompareError('Could not compare: ' + (e instanceof Error ? e.message : String(e)));
    } finally { setCompareBusy(false); }
  };

  const addPackSource = async () => {
    if (!archive) return;
    const p = await archive.BrowseForFolder('Add a source folder to pack');
    if (p && !packSources.includes(p)) setPackSources(prev => [...prev, p]);
  };
  const removePackSource = (p: string) => setPackSources(prev => prev.filter(x => x !== p));
  const browsePackOutput = async () => {
    if (!archive) return;
    const p = await archive.BrowseForSave('Save the new .ba2 as', 'Bethesda archive (*.ba2)|*.ba2|All files|*.*');
    if (p) setPackOutput(p);
  };
  const browsePackRoot = async () => {
    if (!archive) return;
    const p = await archive.BrowseForFolder('Select the root folder (in-archive paths are computed relative to this)');
    if (p) setPackRoot(p);
  };
  const runPack = async () => {
    if (!archive || packSources.length === 0 || !packOutput.trim() || !packRoot.trim()) return;
    setPackBusy(true); setPackResult('');
    try {
      const res = await archive.Pack(JSON.stringify(packSources), packOutput, packFormat, packRoot, packCompress);
      setPackResult(res);
    } catch (e) {
      setPackResult('Error: ' + (e instanceof Error ? e.message : String(e)));
    } finally { setPackBusy(false); }
  };

  return (
    <div className="papyrus-overlay" onClick={onClose}>
      <div className="papyrus-modal glass-panel archive-modal" onClick={e => e.stopPropagation()}>
        <div className="papyrus-header">
          <span className="papyrus-title"><ArchiveIcon size={16} /> Archive</span>
          <button className="papyrus-close" onClick={onClose} title="Close"><X size={16} /></button>
        </div>

        {unavailable && <div className="papyrus-warn">Archive bridge not available -- run the desktop app (not the browser dev server).</div>}

        <div className="archive-toolbar">
          <input className="archive-path-input" value={archivePath} onChange={e => setArchivePath(e.target.value)}
                 placeholder="Path to a .ba2/.bsa…" onKeyDown={e => { if (e.key === 'Enter') load(); }} />
          <button className="sidebar-action-btn" onClick={browseArchive} disabled={unavailable}>Browse…</button>
          <input className="archive-filter" value={filter} onChange={e => setFilter(e.target.value)}
                 placeholder={filterMode === 'regex' ? 'Filter (regex)…' : filterMode === 'wildcard' ? 'Filter (e.g. *.nif)…' : 'Filter (contains)…'}
                 onKeyDown={e => { if (e.key === 'Enter') load(); }} />
          <select className="archive-filter-mode" value={filterMode} onChange={e => setFilterMode(e.target.value as ArchiveFilterMode)} title="Filter mode">
            <option value="simple">Contains</option>
            <option value="wildcard">Wildcard</option>
            <option value="regex">Regex</option>
          </select>
          <button className="papyrus-run" onClick={() => load()} disabled={busy || unavailable || !archivePath.trim()}>
            {busy ? 'Loading…' : 'List'}
          </button>
        </div>

        <div className="archive-body">
          {!entries ? (
            <div className="nif-view-empty">{unavailable ? 'Archive bridge unavailable.' : error || 'Pick a .ba2/.bsa, then List.'}</div>
          ) : (
            <>
              {meta && (
                <div className="archive-meta">
                  {meta.shownCount.toLocaleString()} of {meta.totalCount.toLocaleString()} entr{meta.totalCount === 1 ? 'y' : 'ies'} shown
                  {meta.truncated && <span className="archive-truncated"> -- narrow with a filter to see the rest</span>}
                </div>
              )}
              <table className="archive-table">
                <thead>
                  <tr>
                    <th><input type="checkbox" checked={entries.length > 0 && selected.size === entries.length} onChange={toggleAll} /></th>
                    <th>Path</th>
                    <th>Size</th>
                  </tr>
                </thead>
                <tbody>
                  {entries.map(e => (
                    <tr key={e.path}>
                      <td><input type="checkbox" checked={selected.has(e.path)} onChange={() => toggle(e.path)} /></td>
                      <td className="archive-path">{e.path}</td>
                      <td className="archive-size">{e.size.toLocaleString()} B</td>
                    </tr>
                  ))}
                </tbody>
              </table>

              <div className="archive-actions">
                <button className="sidebar-action-btn" disabled={selected.size === 0 || busy} onClick={extractSelected}>
                  <Download size={13} /> Extract Selected ({selected.size})
                </button>
                <button className="sidebar-action-btn" disabled={busy} onClick={extractAll}>
                  <DownloadCloud size={13} /> Extract All{filter ? ' (matching filter)' : ''}
                </button>
              </div>
            </>
          )}

          {result && (
            <div className={`papyrus-banner ${/^Extracted/.test(result) ? 'ok' : 'error'}`}>
              {/^Extracted/.test(result) ? <CheckCircle2 size={15} /> : <XCircle size={15} />}
              <span className="papyrus-banner-text">{result}</span>
              {lastOutDir && <button className="papyrus-openfolder" onClick={openOut} title={lastOutDir}><FolderOpen size={13} /> Open folder</button>}
            </div>
          )}

          <div className="archive-compare-section">
            <button className="archive-compare-toggle" onClick={() => setCompareOpen(o => !o)}>
              {compareOpen ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
              <GitCompare size={13} /> Compare with another archive
            </button>
            {compareOpen && (
              <div className="archive-compare-body">
                <div className="archive-compare-row">
                  <span className="archive-compare-label">A:</span>
                  <span className="archive-compare-path">{archivePath || '(pick an archive above first)'}</span>
                </div>
                <div className="archive-compare-row">
                  <span className="archive-compare-label">B:</span>
                  <input className="archive-path-input" value={compareB} onChange={e => setCompareB(e.target.value)} placeholder="Path to the archive to compare against…" />
                  <button className="sidebar-action-btn" onClick={browseCompareB} disabled={unavailable}>Browse…</button>
                  <button className="papyrus-run" onClick={runCompare} disabled={compareBusy || unavailable || !archivePath.trim() || !compareB.trim()}>
                    {compareBusy ? 'Comparing…' : 'Compare'}
                  </button>
                </div>

                {compareError && <div className="nif-view-empty">{compareError}</div>}

                {compareResult && (
                  <div className="archive-compare-results">
                    <div className="archive-compare-summary">
                      <span className="archive-compare-added">+{compareResult.added?.length ?? 0} added</span>
                      <span className="archive-compare-removed">−{compareResult.removed?.length ?? 0} removed</span>
                      <span className="archive-compare-changed">~{compareResult.changed?.length ?? 0} changed</span>
                      <span className="archive-compare-identical">{compareResult.identicalCount ?? 0} identical</span>
                    </div>
                    {(['added', 'removed', 'changed'] as const).map(kind => {
                      const list = compareResult[kind];
                      if (!list || list.length === 0) return null;
                      return (
                        <div key={kind} className={`archive-compare-list archive-compare-list-${kind}`}>
                          <div className="archive-compare-list-head">{kind.toUpperCase()} ({list.length})</div>
                          {list.slice(0, 200).map(p => <div key={p} className="archive-compare-list-row">{p}</div>)}
                          {list.length > 200 && <div className="archive-compare-list-row archive-compare-more">…and {list.length - 200} more</div>}
                        </div>
                      );
                    })}
                  </div>
                )}
              </div>
            )}
          </div>

          <div className="archive-compare-section">
            <button className="archive-compare-toggle" onClick={() => setPackOpen(o => !o)}>
              {packOpen ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
              <PackageIcon size={13} /> Pack folder(s) into a new BA2
            </button>
            {packOpen && (
              <div className="archive-compare-body">
                <div className="archive-compare-row">
                  <span className="archive-compare-label">Src:</span>
                  <button className="sidebar-action-btn" onClick={addPackSource} disabled={unavailable}>Add folder…</button>
                  <span className="archive-compare-path">{packSources.length === 0 ? '(none added yet)' : `${packSources.length} folder(s)`}</span>
                </div>
                {packSources.map(p => (
                  <div key={p} className="archive-compare-row">
                    <span className="archive-compare-label" />
                    <span className="archive-compare-path">{p}</span>
                    <button className="sidebar-action-btn" onClick={() => removePackSource(p)}>Remove</button>
                  </div>
                ))}
                <div className="archive-compare-row">
                  <span className="archive-compare-label">Out:</span>
                  <input className="archive-path-input" value={packOutput} onChange={e => setPackOutput(e.target.value)} placeholder="Output .ba2 path…" />
                  <button className="sidebar-action-btn" onClick={browsePackOutput} disabled={unavailable}>Save…</button>
                </div>
                <div className="archive-compare-row">
                  <span className="archive-compare-label">Root:</span>
                  <input className="archive-path-input" value={packRoot} onChange={e => setPackRoot(e.target.value)} placeholder="Folder each source's in-archive path is relative to (e.g. the mod's Data\ folder)…" />
                  <button className="sidebar-action-btn" onClick={browsePackRoot} disabled={unavailable}>Browse…</button>
                </div>
                <div className="archive-compare-row">
                  <span className="archive-compare-label">Fmt:</span>
                  <select className="archive-filter-mode" value={packFormat} onChange={e => setPackFormat(e.target.value)}>
                    <option value="General">General (sounds/meshes/scripts/...)</option>
                    <option value="DDS">DDS (textures only)</option>
                  </select>
                  <label style={{ display: 'inline-flex', alignItems: 'center', gap: 6, fontSize: 12.5 }}>
                    <input type="checkbox" checked={packCompress} onChange={e => setPackCompress(e.target.checked)} /> Compress
                  </label>
                  <button className="papyrus-run" onClick={runPack}
                          disabled={packBusy || unavailable || packSources.length === 0 || !packOutput.trim() || !packRoot.trim()}>
                    {packBusy ? 'Packing…' : 'Pack'}
                  </button>
                </div>

                {packResult && (
                  <div className={`papyrus-banner ${/^RESULT: success/.test(packResult) ? 'ok' : 'error'}`}>
                    {/^RESULT: success/.test(packResult) ? <CheckCircle2 size={15} /> : <XCircle size={15} />}
                    <span className="papyrus-banner-text">{packResult.split('\n')[0]}</span>
                  </div>
                )}
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
