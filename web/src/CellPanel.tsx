import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useDialogs } from './dialogs';
import { Boxes, X, Play, CheckCircle2, AlertTriangle, XCircle, Search, Eye, EyeOff, Info, ScrollText } from 'lucide-react';
import { getCell, getBackend, type CellPlacedReference, type CellReferencesResult, type CellSearchHit } from './backend';
import CellViewport, { type CellGeoMap, type CellTextureStats } from './CellViewport';
import { cellLayerOf, MARKER_LAYER } from './util/cellLayer';
import './PapyrusPanel.css';
import './NifPanel.css';
import './CellPanel.css';

const LS = (k: string, d: string) => localStorage.getItem('cell.' + k) ?? d;
const setLS = (k: string, v: string) => localStorage.setItem('cell.' + k, v);

export default function CellPanel({ onClose }: { onClose: () => void }) {
  const { pickPlugin: askForTarget } = useDialogs();
  const [cellId, setCellId] = useState(() => LS('cellId', ''));
  const [busy, setBusy] = useState(false);
  const [status, setStatus] = useState('');
  const [result, setResult] = useState<CellReferencesResult | null>(null);
  const [geometry, setGeometry] = useState<CellGeoMap>({});
  const [log, setLog] = useState<string[]>([]);

  // Dedicated Info/Log tabs -- the log used to be squeezed into a fixed 160px strip at the bottom of
  // the same scrolling column as stats/layers/hotkeys, which on a cell with a lot of layers meant
  // scrolling past everything else just to read it (2026-07-20 feedback). hasUnseenError flags the
  // Log tab button when something failed while the user was looking at Info, instead of silently
  // relying on them to think to switch tabs and scroll down.
  const [sidebarTab, setSidebarTab] = useState<'info' | 'log'>('info');
  const [hasUnseenError, setHasUnseenError] = useState(false);

  // Click-to-select + per-type visibility + per-reference hide (CK "1" cycle -> hidden).
  const [selected, setSelected] = useState<CellPlacedReference | null>(null);
  const [hiddenTypes, setHiddenTypes] = useState<Set<string>>(new Set());
  const [hiddenRefs, setHiddenRefs] = useState<Set<string>>(new Set());

  // Undo/redo for visibility changes (CK "Ctrl+Z"/"Ctrl+Y"). A single combined snapshot of BOTH
  // hiddenTypes and hiddenRefs -- so Ctrl+Z sensibly reverses the last visibility action regardless
  // of whether it was a per-mesh hide, a layer toggle, or "unhide all"/"show all". Capped so an
  // extended hide/show session can't grow the stack unbounded.
  type VisSnapshot = { types: Set<string>; refs: Set<string> };
  const HISTORY_CAP = 50;
  const undoStackRef = useRef<VisSnapshot[]>([]);
  const redoStackRef = useRef<VisSnapshot[]>([]);
  // The stacks are plain refs (not reactive); bumping this state is just what forces a re-render so
  // JSX reading undoStackRef.current.length (to enable/disable a button) reflects the latest push/pop.
  const [, setHistoryTick] = useState(0);

  const snapshotVisibility = (): VisSnapshot => ({ types: new Set(hiddenTypes), refs: new Set(hiddenRefs) });
  const pushUndo = () => {
    undoStackRef.current.push(snapshotVisibility());
    if (undoStackRef.current.length > HISTORY_CAP) undoStackRef.current.shift();
    redoStackRef.current = []; // a new action invalidates any pending redo
    setHistoryTick(t => t + 1);
  };
  const applySnapshot = (s: VisSnapshot) => { setHiddenTypes(s.types); setHiddenRefs(s.refs); };
  const undoVisibility = () => {
    const prev = undoStackRef.current.pop();
    if (!prev) return;
    redoStackRef.current.push(snapshotVisibility());
    applySnapshot(prev);
    setHistoryTick(t => t + 1);
  };
  const redoVisibility = () => {
    const next = redoStackRef.current.pop();
    if (!next) return;
    undoStackRef.current.push(snapshotVisibility());
    applySnapshot(next);
    setHistoryTick(t => t + 1);
  };
  const resetVisibilityHistory = () => { undoStackRef.current = []; redoStackRef.current = []; };

  // Env-loaded gate: an empty plugin list means "nothing loaded yet" -- point the user at the
  // Explorer sidebar's Load Env / Open MO2 buttons instead of letting them type a cell id blind
  // into a panel that can't possibly resolve anything yet.
  const [pluginCount, setPluginCount] = useState<number | null>(null);   // null = still checking

  // Persistent dropdown: search bar on top, a list underneath that's ALWAYS rendered once a
  // modlist is loaded (never appears/disappears based on typing). A new search REPLACES the list
  // only when its results actually arrive -- the previous list stays put (just dimmed) while a
  // query is in flight, so typing never blanks-then-repopulates the panel. That flash-on-every-
  // keystroke was the direct complaint that led to this rewrite.
  const [matches, setMatches] = useState<CellSearchHit[] | null>(null);  // null = not loaded yet
  const [matchesLoading, setMatchesLoading] = useState(false);
  const searchSeq = useRef(0);

  // Real N/total progress for the geometry-conversion phase (niftool runs one real child process
  // per unique mesh -- polled from NifService.GeoBatchDone/Total while the batch call is in flight,
  // not an indeterminate spinner).
  const [geoProgress, setGeoProgress] = useState<{ done: number; total: number } | null>(null);

  // A shape whose diffuse never resolves just renders flat grey, which is indistinguishable from a
  // mesh that genuinely has no texture -- so an entire missing texture root used to look like a
  // rendering quirk rather than a setup problem. Reported per load, same summary the Godot cell
  // editor prints.
  const [texStats, setTexStats] = useState<CellTextureStats | null>(null);
  const texStatsLoggedRef = useRef('');

  const cell = getCell();
  const unavailable = !cell;

  const loadTexture = useCallback(async (modelPath: string, relTexPath: string): Promise<string> => {
    const c = getCell();
    if (!c) return '';
    try { return await c.GetTexture(modelPath, relTexPath); } catch { return ''; }
  }, []);

  const appendLog = (line: string) => {
    setLog(prev => [`[${new Date().toLocaleTimeString()}] ${line}`, ...prev].slice(0, 200));
    if (line.includes('✗') && sidebarTab !== 'log') setHasUnseenError(true);
  };

  // Logged once per distinct tally so the coalesced updates during a load don't spam the log; the
  // final tally for a cell is the one that sticks.
  const onTextureStats = useCallback((s: CellTextureStats) => {
    setTexStats(s);
    const failed = s.resolveFail + s.decodeFail;
    if (failed === 0 && s.noPath === 0) return;
    const line = `Textures: ${s.ok} loaded, ${s.resolveFail} not found, ${s.decodeFail} failed to decode, `
      + `${s.noPath} shape${s.noPath === 1 ? '' : 's'} with no texture path`
      + (s.firstFailure ? `. First failure: ${s.firstFailure}` : '');
    if (texStatsLoggedRef.current === line) return;
    texStatsLoggedRef.current = line;
    appendLog(`${failed > 0 ? '✗' : 'ℹ'} ${line}`);
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  const checkPlugins = async () => {
    if (!cell) { setPluginCount(0); return; }
    try {
      const raw = await cell.GetPlugins();
      const list = JSON.parse(raw) as string[];
      setPluginCount(Array.isArray(list) ? list.length : 0);
    } catch { setPluginCount(0); }
  };

  useEffect(() => { checkPlugins(); }, []); // eslint-disable-line react-hooks/exhaustive-deps
  // Re-check when the panel regains focus (covers "opened Cell Viewer, then loaded a modlist,
  // then came back") without needing a manual refresh button.
  useEffect(() => {
    const onFocus = () => checkPlugins();
    window.addEventListener('focus', onFocus);
    return () => window.removeEventListener('focus', onFocus);
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  const noEnv = pluginCount === 0;

  // Debounced search-as-you-type against the loaded load order. Empty query still searches (limit
  // cells in whatever order the load order returns them) so the panel reads as a real, immediately
  // browsable dropdown the moment a modlist is loaded, not an empty box waiting for input.
  useEffect(() => {
    if (!cell || noEnv) { setMatches(null); return; }
    const mySeq = ++searchSeq.current;
    setMatchesLoading(true);
    const t = setTimeout(async () => {
      try {
        const raw = await cell.SearchCells(cellId.trim(), 30);
        if (searchSeq.current !== mySeq) return; // a newer keystroke superseded this search
        setMatches(JSON.parse(raw) as CellSearchHit[]);
      } catch {
        if (searchSeq.current === mySeq) setMatches([]);
      } finally {
        if (searchSeq.current === mySeq) setMatchesLoading(false);
      }
    }, 250);
    return () => clearTimeout(t);
  }, [cellId, cell, noEnv]);

  // Exterior cells (#67): a worldspace's cells are addressed by grid coordinate, not by a name you
  // could type into the cell search above -- the interior picker only ever finds cells that HAVE an
  // EditorID/Name, which most exterior cells do not. Kept as a second mode on the same panel rather
  // than a second panel, since everything downstream (geometry batch, viewport, layers) is identical
  // once a cell is resolved.
  const [pickMode, setPickMode] = useState<'cell' | 'grid'>(() => (LS('pickMode', 'cell') === 'grid' ? 'grid' : 'cell'));
  const [worldspace, setWorldspace] = useState(() => LS('worldspace', ''));
  const [wsMatches, setWsMatches] = useState<CellSearchHit[] | null>(null);
  const [wsLoading, setWsLoading] = useState(false);
  const [gridX, setGridX] = useState(() => LS('gridX', '0'));
  const [gridY, setGridY] = useState(() => LS('gridY', '0'));
  const wsSeq = useRef(0);

  useEffect(() => {
    if (!cell || noEnv || pickMode !== 'grid') { setWsMatches(null); return; }
    const mySeq = ++wsSeq.current;
    setWsLoading(true);
    const t = setTimeout(async () => {
      try {
        const raw = await cell.SearchWorldspaces(worldspace.trim(), 100);
        if (wsSeq.current !== mySeq) return;
        setWsMatches(JSON.parse(raw) as CellSearchHit[]);
      } catch {
        if (wsSeq.current === mySeq) setWsMatches([]);
      } finally {
        if (wsSeq.current === mySeq) setWsLoading(false);
      }
    }, 250);
    return () => clearTimeout(t);
  }, [worldspace, cell, noEnv, pickMode]);

  const load = async (idOverride?: string) => {
    const id = (idOverride ?? cellId).trim();
    if (!cell || !id) return;
    setLS('cellId', id);
    await runLoad(() => cell.GetPlacedReferences(id));
  };

  const loadGrid = async (wsOverride?: string) => {
    const ws = (wsOverride ?? worldspace).trim();
    const x = Number.parseInt(gridX, 10);
    const y = Number.parseInt(gridY, 10);
    if (!cell || !ws || Number.isNaN(x) || Number.isNaN(y)) return;
    setLS('worldspace', ws);
    setLS('gridX', String(x));
    setLS('gridY', String(y));
    await runLoad(() => cell.GetPlacedReferencesAtGrid(ws, x, y));
  };

  const runLoad = async (fetchReferences: () => Promise<string>) => {
    if (!cell) return;
    setBusy(true);
    setResult(null);
    setGeometry({});
    setTexStats(null);
    texStatsLoggedRef.current = '';
    setSelected(null);
    setHiddenTypes(new Set());
    setHiddenRefs(new Set());
    resetVisibilityHistory();
    setStatus('Reading placed references…');
    try {
      const raw = await fetchReferences();
      const parsed = JSON.parse(raw) as CellReferencesResult;
      if (parsed.error) {
        setStatus(parsed.error);
        appendLog('✗ ' + parsed.error);
        return;
      }
      setResult(parsed);
      const refs = parsed.references ?? [];
      appendLog(`• loaded ${parsed.cellEditorId || parsed.cellFormKey} -- ${refs.length} reference(s), ${parsed.withModelCount ?? 0} with a model`);

      // Include SCOL member statics' own model paths -- these need converting too, whether or not the
      // SCOL's own precombined modelPath above resolves (the viewport picks whichever actually worked).
      const scolModels = refs.flatMap(r => (r.scolParts ?? []).map(p => p.modelPath));
      const uniqueModels = Array.from(new Set(
        [...refs.map(r => r.modelPath).filter((p): p is string => !!p), ...scolModels]));
      if (uniqueModels.length === 0) { setStatus('Loaded -- no placeable meshes in this cell.'); return; }

      setStatus(`Resolving + converting ${uniqueModels.length} unique mesh(es)…`);
      setGeoProgress({ done: 0, total: uniqueModels.length });
      const pollTimer = setInterval(async () => {
        try {
          const raw = await cell.GetGeometryBatchProgress();
          setGeoProgress(JSON.parse(raw) as { done: number; total: number });
        } catch { /* a missed poll tick isn't worth surfacing */ }
      }, 200);
      let geoRaw: string;
      try {
        geoRaw = await cell.GetGeometryBatch(JSON.stringify(uniqueModels));
      } finally {
        clearInterval(pollTimer);
        setGeoProgress(null);
      }
      const geoParsed = JSON.parse(geoRaw) as { count?: number; geometry?: CellGeoMap; error?: string };
      if (geoParsed.error) {
        appendLog('✗ geometry batch: ' + geoParsed.error);
        setStatus('Loaded references, but geometry batch failed: ' + geoParsed.error);
        return;
      }
      const geoMap = geoParsed.geometry ?? {};
      setGeometry(geoMap);
      const failed = Object.values(geoMap).filter(g => 'error' in g).length;
      const ok = uniqueModels.length - failed;
      appendLog(`• geometry: ${ok}/${uniqueModels.length} mesh(es) resolved, ${failed} failed`);
      setStatus(failed > 0
        ? `Loaded ${refs.length} reference(s). ${ok}/${uniqueModels.length} unique meshes resolved -- ${failed} unavailable (see markers).`
        : `Loaded ${refs.length} reference(s), all ${ok} unique mesh(es) resolved.`);
    } catch (e) {
      const msg = 'Error: ' + (e instanceof Error ? e.message : String(e));
      setStatus(msg);
      appendLog('✗ ' + msg);
    } finally {
      setBusy(false);
    }
  };

  const pickMatch = (hit: CellSearchHit) => {
    setCellId(hit.FormKey);
    load(hit.FormKey);
  };

  const references: CellPlacedReference[] = result?.references ?? [];
  const failedModelCount = Object.values(geometry).filter(g => 'error' in g).length;
  const banner = statusBanner(status, busy);

  // Group references the SAME way the viewport tags its meshes: a ref with a resolved mesh groups
  // under its record type; anything drawn as a marker (no model, or its mesh failed to resolve)
  // groups under "(markers)". Sorted by count so the biggest clutter sources are on top.
  const typeGroups = useMemo(() => {
    const m = new Map<string, number>();
    for (const r of references) {
      const kind = cellLayerOf(r, r.modelPath ? geometry[r.modelPath] : undefined);
      m.set(kind, (m.get(kind) ?? 0) + 1);
    }
    return [...m.entries()].sort((a, b) => b[1] - a[1]);
  }, [references, geometry]);

  const toggleType = (t: string) => {
    pushUndo();
    setHiddenTypes(prev => {
      const next = new Set(prev);
      if (next.has(t)) next.delete(t); else next.add(t);
      return next;
    });
  };
  const showAllTypes = () => { pushUndo(); setHiddenTypes(new Set()); };

  // CK-hotkey actions (the viewport calls these; the sidebar buttons reuse them).
  const hideSelected = () => {
    if (!selected) return;
    pushUndo();
    setHiddenRefs(prev => new Set(prev).add(selected.formKey));
  };
  const unhideAll = () => { pushUndo(); setHiddenRefs(new Set()); };
  const toggleMarkers = () => toggleType(MARKER_LAYER);

  // Gizmo drag-end save (the "full gyro" move/rotate feature). Design confirmed with the user:
  // prompt for a patch plugin name the first time a reference is moved this session, then auto-save
  // every subsequent move into the same patch -- no repeated prompts per drag.
  const gizmoPatchRef = useRef<string | null>(null);
  const moveEnd = useCallback(async (ref: CellPlacedReference) => {
    const c = getCell();
    const backend = getBackend();
    if (!c || !backend) return;
    let patch = gizmoPatchRef.current;
    if (!patch) {
      const picked = await askForTarget({
        title: 'Save cell edits into',
        description: 'This move, and any further moves this session, are written here.',
        confirmLabel: 'Save into', defaultTarget: LS('gizmoPatch', 'CellEdits.esp'),
      });
      if (!picked) { appendLog('x move not saved -- no patch plugin chosen.'); return; }
      patch = picked.target;
      gizmoPatchRef.current = patch;
      setLS('gizmoPatch', patch);
    }
    try {
      const msg = await c.SetPlacedReferenceTransform(
        ref.formKey, patch,
        ref.position.x, ref.position.y, ref.position.z,
        ref.rotation.x, ref.rotation.y, ref.rotation.z);
      if (/error|fail|invalid|could not|choose/i.test(msg)) { appendLog(`✗ move: ${msg}`); return; }
      const saveMsg = await backend.SavePlugin(patch, '');
      appendLog(`• moved ${ref.editorId || ref.formKey} -> ${patch}`);
      if (/error|fail/i.test(saveMsg)) appendLog('✗ save: ' + saveMsg);
    } catch (e) {
      appendLog('✗ move failed: ' + (e instanceof Error ? e.message : String(e)));
      return;
    }
    // Refresh the sidebar's live Pos readout for the moved reference (the underlying ref object was
    // already mutated in place by the viewport, so this just copies the current values into a fresh
    // object to trigger a re-render -- see CellViewport's onMoveEnd doc comment).
    setSelected(prev => prev && prev.formKey === ref.formKey
      ? { ...prev, position: { ...ref.position }, rotation: { ...ref.rotation } }
      : prev);
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  // No click-outside-to-close: a Cell Viewer session (selection, hidden meshes/layers, undo
  // history) is easy to lose to a stray click on a huge cell like Switchboard. Only the explicit
  // X button closes it now.
  return (
    <div className="papyrus-overlay">
      <div className="papyrus-modal glass-panel nif-modal-wide">
        <div className="papyrus-header">
          <span className="papyrus-title"><Boxes size={16} /> Cell Viewer</span>
          <button className="papyrus-close" onClick={onClose} title="Close"><X size={16} /></button>
        </div>

        {unavailable && <div className="papyrus-warn">Cell bridge not available -- run the desktop app (not the browser dev server).</div>}
        {!unavailable && noEnv && (
          <div className="papyrus-warn">
            No modlist loaded yet -- click <strong>Load Env</strong> or <strong>Open MO2</strong> in the Explorer sidebar first, then come back here.
          </div>
        )}

        <div className="papyrus-body">
          <div className="papyrus-form cell-sidebar">
            <div className="cell-sidebar-tabs">
              <button
                className={`cell-sidebar-tab${sidebarTab === 'info' ? ' active' : ''}`}
                onClick={() => setSidebarTab('info')}
              >
                <Info size={13} /> Info
              </button>
              <button
                className={`cell-sidebar-tab${sidebarTab === 'log' ? ' active' : ''}`}
                onClick={() => { setSidebarTab('log'); setHasUnseenError(false); }}
              >
                <ScrollText size={13} /> Log ({log.length})
                {hasUnseenError && <span className="cell-sidebar-tab-dot" title="Something failed" />}
              </button>
            </div>

            {sidebarTab === 'info' ? (
              <div className="cell-sidebar-pane">
                <div className="cell-mode-tabs">
                  <button
                    className={`cell-mode-tab${pickMode === 'cell' ? ' active' : ''}`}
                    onClick={() => { setPickMode('cell'); setLS('pickMode', 'cell'); }}
                  >By name</button>
                  <button
                    className={`cell-mode-tab${pickMode === 'grid' ? ' active' : ''}`}
                    onClick={() => { setPickMode('grid'); setLS('pickMode', 'grid'); }}
                    title="Exterior cells are addressed by worldspace + grid coordinate"
                  >Exterior grid</button>
                </div>

                {pickMode === 'grid' && (
                  <>
                    <div className="cell-dropdown">
                      <div className="cell-dropdown-search">
                        <Search size={13} className="cell-search-icon" />
                        <input
                          value={worldspace}
                          onChange={e => setWorldspace(e.target.value)}
                          placeholder={noEnv ? 'Load a modlist first…' : 'Search worldspaces…'}
                          disabled={unavailable || noEnv}
                          onKeyDown={e => { if (e.key === 'Enter') loadGrid(); }}
                        />
                        {wsLoading && <span className="cell-dropdown-spinner" title="Updating…" />}
                      </div>
                      <div className="cell-dropdown-list cell-dropdown-list-short">
                        {noEnv || unavailable ? (
                          <div className="cell-suggest-empty">{unavailable ? 'Cell bridge unavailable.' : 'Load a modlist to browse worldspaces.'}</div>
                        ) : wsMatches === null ? (
                          <div className="cell-suggest-empty">Loading…</div>
                        ) : wsMatches.length === 0 ? (
                          <div className="cell-suggest-empty">No matching worldspaces.</div>
                        ) : (
                          wsMatches.map((h, i) => (
                            <div
                              key={`${h.FormKey}:${i}`}
                              className={`cell-suggest-row${worldspace.trim() === (h.EditorID || h.FormKey) ? ' selected' : ''}`}
                              onClick={() => setWorldspace(h.EditorID || h.FormKey)}
                            >
                              <span className="cell-suggest-id">{h.EditorID || h.FormKey}</span>
                              {h.Name && <span className="cell-suggest-name">"{h.Name}"</span>}
                              <span className="cell-suggest-plugin">{h.Plugin}</span>
                            </div>
                          ))
                        )}
                      </div>
                    </div>
                    <div className="cell-grid-inputs">
                      <label>X<input
                        type="number"
                        value={gridX}
                        onChange={e => setGridX(e.target.value)}
                        disabled={unavailable || noEnv}
                        onKeyDown={e => { if (e.key === 'Enter') loadGrid(); }}
                      /></label>
                      <label>Y<input
                        type="number"
                        value={gridY}
                        onChange={e => setGridY(e.target.value)}
                        disabled={unavailable || noEnv}
                        onKeyDown={e => { if (e.key === 'Enter') loadGrid(); }}
                      /></label>
                    </div>
                    <button
                      className="papyrus-run cell-load-btn"
                      onClick={() => loadGrid()}
                      disabled={busy || unavailable || noEnv || !worldspace.trim()
                        || Number.isNaN(Number.parseInt(gridX, 10)) || Number.isNaN(Number.parseInt(gridY, 10))}
                    >
                      <Play size={14} /> {busy ? 'Loading…' : 'Load Cell'}
                    </button>
                  </>
                )}

                {pickMode === 'cell' && (
                <div className="cell-dropdown">
                  <div className="cell-dropdown-search">
                    <Search size={13} className="cell-search-icon" />
                    <input
                      value={cellId}
                      onChange={e => setCellId(e.target.value)}
                      placeholder={noEnv ? 'Load a modlist first…' : 'Search cells by name, EditorID, or FormKey…'}
                      disabled={unavailable || noEnv}
                      onKeyDown={e => { if (e.key === 'Enter') load(); }}
                    />
                    {matchesLoading && <span className="cell-dropdown-spinner" title="Updating…" />}
                  </div>
                  <div className="cell-dropdown-list">
                    {noEnv || unavailable ? (
                      <div className="cell-suggest-empty">{unavailable ? 'Cell bridge unavailable.' : 'Load a modlist to browse cells.'}</div>
                    ) : matches === null ? (
                      <div className="cell-suggest-empty">Loading…</div>
                    ) : matches.length === 0 ? (
                      <div className="cell-suggest-empty">No matching cells.</div>
                    ) : (
                      matches.map((h, i) => (
                        <div key={`${h.FormKey}:${i}`} className="cell-suggest-row" onClick={() => pickMatch(h)}>
                          <span className="cell-suggest-id">{h.EditorID || h.FormKey}</span>
                          {h.Name && <span className="cell-suggest-name">"{h.Name}"</span>}
                          <span className="cell-suggest-plugin">{h.Plugin}</span>
                        </div>
                      ))
                    )}
                  </div>
                </div>
                )}
                {pickMode === 'cell' && (
                <button className="papyrus-run cell-load-btn" onClick={() => load()} disabled={busy || unavailable || noEnv || !cellId.trim()}>
                  <Play size={14} /> {busy ? 'Loading…' : 'Load Cell'}
                </button>
                )}

                {result && (
                  <div className="cell-stats">
                    <div className="cell-stat"><span>Cell</span><strong>{result.cellEditorId || result.cellFormKey}</strong></div>
                    <div className="cell-stat"><span>Interior</span><strong>{result.interior ? 'Yes' : 'No'}</strong></div>
                    <div className="cell-stat"><span>References</span><strong>{result.referenceCount ?? references.length}</strong></div>
                    <div className="cell-stat"><span>With model</span><strong>{result.withModelCount ?? 0}</strong></div>
                    {failedModelCount > 0 && (
                      <div className="cell-stat cell-stat-warn"><span>Meshes unavailable</span><strong>{failedModelCount}</strong></div>
                    )}
                  </div>
                )}

                {selected && (
                  <div className="cell-selected">
                    <div className="cell-selected-head">
                      <span>Selected</span>
                      <button className="cell-selected-clear" onClick={() => setSelected(null)} title="Deselect">
                        <X size={12} />
                      </button>
                    </div>
                    <div className="cell-selected-row"><span>Ref</span><strong>{selected.editorId || selected.formKey}</strong></div>
                    <div className="cell-selected-row"><span>Type</span><strong>{selected.baseType || selected.recordType}</strong></div>
                    <div className="cell-selected-row"><span>Base</span><strong>{selected.baseEditorId || selected.baseFormKey}</strong></div>
                    <div className="cell-selected-row"><span>Pos</span><strong>{selected.position.x.toFixed(0)}, {selected.position.y.toFixed(0)}, {selected.position.z.toFixed(0)}</strong></div>
                    <button className="cell-hide-btn" onClick={hideSelected}>
                      <EyeOff size={12} /> Hide this mesh <kbd>1</kbd>
                    </button>
                  </div>
                )}

                {texStats && (texStats.resolveFail > 0 || texStats.decodeFail > 0) && (
                  <div className="cell-hidden-bar" title={texStats.firstFailure ?? undefined}>
                    <span>
                      <AlertTriangle size={12} /> {texStats.resolveFail + texStats.decodeFail} texture
                      {texStats.resolveFail + texStats.decodeFail === 1 ? '' : 's'} missing
                      {texStats.ok > 0 ? ` (${texStats.ok} loaded)` : ''}
                    </span>
                    <div className="cell-hidden-actions">
                      <button className="papyrus-copy" onClick={() => setSidebarTab('log')}>Details</button>
                    </div>
                  </div>
                )}

                {(hiddenRefs.size > 0 || undoStackRef.current.length > 0 || redoStackRef.current.length > 0) && (
                  <div className="cell-hidden-bar">
                    <span>{hiddenRefs.size > 0 ? `${hiddenRefs.size} mesh${hiddenRefs.size === 1 ? '' : 'es'} hidden` : ''}</span>
                    <div className="cell-hidden-actions">
                      <button className="papyrus-copy" onClick={undoVisibility} disabled={undoStackRef.current.length === 0} title="Undo last hide/show">
                        Undo <kbd>Ctrl+Z</kbd>
                      </button>
                      <button className="papyrus-copy" onClick={redoVisibility} disabled={redoStackRef.current.length === 0} title="Redo">
                        Redo <kbd>Ctrl+Y</kbd>
                      </button>
                      {hiddenRefs.size > 0 && <button className="papyrus-copy" onClick={unhideAll}>Unhide all <kbd>Alt+1</kbd></button>}
                    </div>
                  </div>
                )}

                {result && typeGroups.length > 0 && (
                  <div className="cell-layers">
                    <div className="cell-layers-head">
                      <span>Layers ({typeGroups.length})</span>
                      {hiddenTypes.size > 0 && (
                        <button className="papyrus-copy" onClick={showAllTypes}>Show all</button>
                      )}
                    </div>
                    <div className="cell-layers-list">
                      {typeGroups.map(([type, count]) => {
                        const hidden = hiddenTypes.has(type);
                        return (
                          <div
                            key={type}
                            className={`cell-layer-row${hidden ? ' hidden' : ''}`}
                            onClick={() => toggleType(type)}
                            title={hidden ? 'Show' : 'Hide'}
                          >
                            {hidden ? <EyeOff size={13} /> : <Eye size={13} />}
                            <span className="cell-layer-name">{type}</span>
                            <span className="cell-layer-count">{count}</span>
                          </div>
                        );
                      })}
                    </div>
                  </div>
                )}

                <div className="cell-legend">
                  <div><span className="cell-swatch cell-swatch-mesh" /> resolved mesh</div>
                  <div><span className="cell-swatch cell-swatch-nomodel" /> no model (actor/trap)</div>
                  <div><span className="cell-swatch cell-swatch-failed" /> mesh unavailable</div>
                </div>
                <div className="cell-hotkeys">
                  <div><kbd>Click</kbd> select · <kbd>1</kbd> hide · <kbd>Alt+1</kbd> unhide all</div>
                  <div><kbd>Shift+F</kbd> focus · <kbd>T</kbd> top-down · <kbd>M</kbd> markers · <kbd>D</kbd> deselect</div>
                  <div><kbd>Ctrl+Z</kbd> undo · <kbd>Ctrl+Y</kbd> redo</div>
                  <div><kbd>G</kbd> move gizmo · <kbd>R</kbd> rotate gizmo (drag a selected reference)</div>
                </div>
                <div className="nif-view-hint">Drag to orbit · scroll to zoom · right-drag to pan</div>
              </div>
            ) : (
              <div className="cell-sidebar-pane cell-log-pane">
                <div className="papyrus-log-head">
                  <span>LOG ({log.length})</span>
                  {log.length > 0 && <button className="papyrus-copy" onClick={() => setLog([])}>Clear</button>}
                </div>
                <div className="papyrus-log-body cell-log-body">
                  {log.length === 0 ? <div className="papyrus-log-empty">No runs yet.</div>
                    : log.map((l, i) => <div key={i} className={`papyrus-log-row ${l.includes('✗') ? 'err' : 'ok'}`}>{l}</div>)}
                </div>
              </div>
            )}
          </div>

          <div className="papyrus-output nif-view-output">
            {geoProgress ? (
              <div className="cell-progress">
                <div className="cell-progress-label">
                  <span>Converting meshes…</span>
                  <span>{geoProgress.done}/{geoProgress.total}</span>
                </div>
                <div className="cell-progress-track">
                  <div className="cell-progress-fill" style={{ width: `${geoProgress.total > 0 ? (geoProgress.done / geoProgress.total) * 100 : 0}%` }} />
                </div>
              </div>
            ) : banner && (
              <div className={`papyrus-banner ${banner.kind}`}>
                {banner.kind === 'ok' ? <CheckCircle2 size={15} /> : banner.kind === 'warn' ? <AlertTriangle size={15} /> : <XCircle size={15} />}
                <span className="papyrus-banner-text">{status}</span>
              </div>
            )}
            {references.length > 0
              ? <CellViewport
                  references={references}
                  geometry={geometry}
                  loadTexture={loadTexture}
                  hiddenTypes={hiddenTypes}
                  hiddenRefs={hiddenRefs}
                  selectedKey={selected?.formKey ?? null}
                  onSelect={setSelected}
                  onHideSelected={hideSelected}
                  onUnhideAll={unhideAll}
                  onToggleMarkers={toggleMarkers}
                  onUndo={undoVisibility}
                  onRedo={redoVisibility}
                  onMoveEnd={moveEnd}
                  onTextureStats={onTextureStats}
                />
              : (
                <div className="nif-view-empty">
                  {unavailable ? 'Cell bridge unavailable.'
                    : noEnv ? 'Load a modlist first, then pick a cell from the list.'
                    : pickMode === 'grid' ? 'Pick a worldspace, enter its grid X/Y, and press Load Cell.'
                    : 'Pick a cell from the list, or paste a FormKey/EditorID and press Load Cell.'}
                </div>
              )}
          </div>
        </div>
      </div>
    </div>
  );
}

function statusBanner(status: string, busy: boolean): { kind: 'ok' | 'warn' | 'error' } | null {
  if (!status || busy) return null;
  if (/error|could not|not available/i.test(status)) return { kind: 'error' };
  if (/unavailable/i.test(status)) return { kind: 'warn' };
  return { kind: 'ok' };
}
