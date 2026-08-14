import { useCallback, useEffect, useMemo, useReducer, useRef, useState } from 'react';
import { AlertTriangle, CheckCircle2, Search, X, XCircle } from 'lucide-react';
import { getGraph } from '../backend';
import type { BpDiagnostic, BpDocument, BpNodeDef, BpPaletteEntry } from './graphModel';
import { emptyDocument, newId, pinKey } from './graphModel';
import { useGraphClipboard } from './clipboard';
import { autoLayout } from './layout';
import { useSavedDoc } from './useSavedDoc';
import { graphReducer, initialState } from './graphReducer';
import type { CanvasApi } from './BlueprintCanvas';
import BlueprintCanvas from './BlueprintCanvas';
import '../PapyrusPanel.css';
import '../NifPanel.css';
import './BlueprintPanel.css';

type Mode = 'graph' | 'source';

const LS = (key: string, fallback = '') =>
  localStorage.getItem(`blueprint.${key}`) ?? fallback;
const setLS = (key: string, value: string) =>
  localStorage.setItem(`blueprint.${key}`, value);

export default function BlueprintPanel({ onClose }: { onClose: () => void }) {
  const graph = getGraph();
  const unavailable = !graph;

  const [state, dispatch] = useReducer(graphReducer, undefined, () =>
    initialState(emptyDocument(LS('scriptName', 'MyScript'))));

  const [mode, setMode] = useState<Mode>('graph');
  const [defs, setDefs] = useState<Record<string, BpNodeDef>>({});
  const [validation, setValidation] = useState<{ diagnostics: BpDiagnostic[] } | null>(null);
  const [validatedDoc, setValidatedDoc] = useState<BpDocument | null>(null);
  const [source, setSource] = useState('');
  const [busy, setBusy] = useState(false);
  const [status, setStatus] = useState('');
  const [search, setSearch] = useState('');
  const [entries, setEntries] = useState<BpPaletteEntry[]>([]);
  const [total, setTotal] = useState(0);
  const [picker, setPicker] = useState<
    { x: number; y: number; from?: { node: string; pin: string } } | null>(null);

  const canvasApi = useRef<CanvasApi | null>(null);
  const { dirty, markSaved } = useSavedDoc(state.doc);

  const signature = useCallback(async (type: string): Promise<BpNodeDef | null> => {
    if (defs[type]) return defs[type];
    if (!graph) return null;
    const raw = await graph.GetNodeSignature(type);
    if (raw.startsWith('Error:')) return null;
    const parsed = JSON.parse(raw) as BpNodeDef;
    setDefs((current) => ({ ...current, [parsed.type]: parsed }));
    return parsed;
  }, [defs, graph]);

  const searchSeq = useRef(0);
  useEffect(() => {
    if (!graph) return undefined;
    const mine = ++searchSeq.current;
    const timer = setTimeout(async () => {
      try {
        const raw = await graph.SearchPalette('any', search, '', 60);
        if (mine !== searchSeq.current || raw.startsWith('Error:')) return;
        const parsed = JSON.parse(raw) as { entries: BpPaletteEntry[]; total: number };
        setEntries(parsed.entries);
        setTotal(parsed.total);
      } catch {

      }
    }, 200);
    return () => clearTimeout(timer);
  }, [search, graph]);

  const validateSeq = useRef(0);
  useEffect(() => {
    if (!graph) return undefined;
    const mine = ++validateSeq.current;
    const doc = state.doc;
    const timer = setTimeout(async () => {
      try {
        const raw = await graph.ValidateGraph(JSON.stringify(doc));
        if (mine !== validateSeq.current || raw.startsWith('Error:')) return;
        setValidation(JSON.parse(raw));
        setValidatedDoc(doc);
      } catch {

      }
    }, 600);
    return () => clearTimeout(timer);
  }, [state.doc, graph]);

  const { diagByNode, diagByPin, errorCount, warningCount } = useMemo(() => {
    const byNode = new Map<string, BpDiagnostic[]>();
    const byPin = new Map<string, BpDiagnostic[]>();
    let errors = 0;
    let warnings = 0;

    for (const diagnostic of validation?.diagnostics ?? []) {
      if (diagnostic.severity === 'error') errors += 1;
      else warnings += 1;
      if (!diagnostic.nodeId) continue;

      const list = byNode.get(diagnostic.nodeId) ?? [];
      list.push(diagnostic);
      byNode.set(diagnostic.nodeId, list);

      if (!diagnostic.pinId) continue;
      const key = pinKey(diagnostic.nodeId, diagnostic.pinId);
      const pinList = byPin.get(key) ?? [];
      pinList.push(diagnostic);
      byPin.set(key, pinList);
    }
    return { diagByNode: byNode, diagByPin: byPin, errorCount: errors, warningCount: warnings };
  }, [validation]);

  const gate: 'stale' | 'ok' | 'warn' | 'error' =
    state.doc !== validatedDoc ? 'stale'
      : errorCount > 0 ? 'error'
        : warningCount > 0 ? 'warn' : 'ok';

  const createNode = useCallback(async (
    type: string, x: number, y: number, from?: { node: string; pin: string },
  ) => {
    const def = await signature(type);
    if (!def) { setStatus(`No node type '${type}'.`); return; }

    const node = { id: newId('n'), def: type, kind: def.kind, x, y };
    let wire;
    if (from) {
      const target = def.pins.find((p) => p.dir === 'in');
      if (target) wire = { from, to: { node: node.id, pin: target.id } };
    }
    dispatch({ type: 'ADD_NODE', node, wire });
    setPicker(null);
  }, [signature]);

  const run = useCallback(async (work: () => Promise<void>) => {
    setBusy(true);
    try {
      await work();
    } catch (e) {
      setStatus('Error: ' + (e instanceof Error ? e.message : String(e)));
    } finally {
      setBusy(false);
    }
  }, []);

  const showSource = () => run(async () => {
    if (!graph) return;
    const raw = await graph.CompileToSource(JSON.stringify(state.doc));
    if (raw.startsWith('Error:')) { setStatus(raw); return; }
    const parsed = JSON.parse(raw) as { source: string | null; diagnostics: BpDiagnostic[] };
    setSource(parsed.source ?? '');
    setValidation({ diagnostics: parsed.diagnostics });
    setValidatedDoc(state.doc);
    setMode('source');
  });

  const compile = () => run(async () => {
    if (!graph) return;
    const raw = await graph.CompileToPex(JSON.stringify(state.doc), '');
    if (raw.startsWith('Error:')) { setStatus(raw); return; }
    const parsed = JSON.parse(raw) as
      { source: string | null; diagnostics: BpDiagnostic[]; ok: boolean };
    setSource(parsed.source ?? '');
    setValidation({ diagnostics: parsed.diagnostics });
    setValidatedDoc(state.doc);
    setStatus(parsed.ok ? 'Compiled.' : 'Did not compile. See the problems list.');
  });

  const save = () => run(async () => {
    if (!graph) return;
    const path = await graph.BrowseForGraph(true);
    if (!path) return;
    const result = await graph.SaveGraph(path, JSON.stringify(state.doc));
    if (result.startsWith('Error:')) { setStatus(result); return; }
    markSaved(state.doc);
    setStatus('Saved.');
  });

  const open = () => run(async () => {
    if (!graph) return;
    const path = await graph.BrowseForScript();
    if (!path) return;

    const isScript = /\.(psc|pex)$/i.test(path);
    let doc: BpDocument;

    if (isScript) {
      const raw = await graph.LoadScript(path);
      if (raw.startsWith('Error:')) { setStatus(raw); return; }
      const parsed = JSON.parse(raw) as
        { ok: boolean; document: BpDocument | null; diagnostics: BpDiagnostic[] };

      setValidation({ diagnostics: parsed.diagnostics });
      if (!parsed.ok || !parsed.document) {
        setStatus('Could not read that script into a graph. See the problems list.');
        return;
      }
      doc = parsed.document;
    } else {
      const raw = await graph.LoadGraph(path);
      if (raw.startsWith('Error:')) { setStatus(raw); return; }
      doc = JSON.parse(raw) as BpDocument;
    }

    const loaded: Record<string, BpNodeDef> = { ...defs };
    for (const node of doc.nodes) {
      const def = await signature(node.def);
      if (def) loaded[def.type] = def;
    }

    dispatch({ type: 'LOAD', doc });

    if (isScript) {

      const positions = autoLayout(doc, loaded);
      if (Object.keys(positions).length > 0) dispatch({ type: 'SET_POSITIONS', positions });

      setStatus(`Opened ${path.split(/[\\/]/).pop()} as a graph.`);
    } else {
      markSaved(doc);
      setStatus('Opened.');
    }
  });

  const clipboard = useGraphClipboard(state, dispatch);
  const [menu, setMenu] = useState<{ x: number; y: number } | null>(null);

  const tidy = useCallback(() => {
    const positions = autoLayout(state.doc, defs);
    if (Object.keys(positions).length > 0) dispatch({ type: 'SET_POSITIONS', positions });
  }, [state.doc, defs]);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      const tag = (event.target as HTMLElement)?.tagName;
      if (tag === 'INPUT' || tag === 'TEXTAREA') return;

      if (event.key === 'Delete' || event.key === 'Backspace') {
        event.preventDefault();
        dispatch({ type: 'DELETE_SELECTION' });
      } else if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'z') {
        event.preventDefault();
        dispatch({ type: event.shiftKey ? 'REDO' : 'UNDO' });
      } else if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'y') {
        event.preventDefault();
        dispatch({ type: 'REDO' });
      } else if (event.key === 'Escape') {
        if (menu) setMenu(null);
        else if (picker) setPicker(null);
        else dispatch({ type: 'SELECT_NONE' });
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [picker, menu]);

  useEffect(() => setLS('scriptName', state.doc.header.scriptName), [state.doc.header.scriptName]);

  return (
    <div className="papyrus-overlay">
      {}
      <div className="papyrus-modal glass-panel nif-modal-wide" onClick={(e) => e.stopPropagation()}>
        <div className="papyrus-header">
          <div className="papyrus-title">Blueprint</div>
          <div className="papyrus-modes">
            <button className={`papyrus-mode ${mode === 'graph' ? 'active' : ''}`}
                    onClick={() => setMode('graph')}>Graph</button>
            <button className={`papyrus-mode ${mode === 'source' ? 'active' : ''}`}
                    onClick={showSource} disabled={unavailable || busy}>Source</button>
          </div>
          <BuildGate gate={gate} errors={errorCount} warnings={warningCount} />
          <div className="bp-actions">
            <button className="sidebar-action-btn" onClick={tidy}
                    disabled={state.doc.nodes.length === 0}
                    title="Arrange the graph left to right along its execution flow">
              Tidy
            </button>
            <button className="sidebar-action-btn" onClick={open} disabled={unavailable || busy}>Open</button>
            <button className="sidebar-action-btn" onClick={save} disabled={unavailable || busy}>
              Save{dirty ? ' *' : ''}
            </button>
            <button className="sidebar-action-btn" onClick={compile}
                    disabled={unavailable || busy || gate === 'error'}
                    title={gate === 'error' ? 'Fix the problems first' : 'Compile to .pex'}>
              Compile
            </button>
          </div>
          <button className="papyrus-close" onClick={onClose}><X size={18} /></button>
        </div>

        {unavailable && (
          <div className="papyrus-warn">
            Graph bridge not available -- run the desktop app rather than the dev server.
          </div>
        )}

        <div className="bp-body">
          <div className="bp-palette">
            <div className="bp-palette-search">
              <Search size={14} />
              <input
                value={search}
                placeholder="Search nodes"
                onChange={(e) => setSearch(e.target.value)}
                disabled={unavailable}
              />
            </div>
            <div className="bp-palette-list">
              {entries.map((entry) => (
                <button
                  key={entry.id}
                  className="bp-palette-row"
                  title={entry.signature}
                  onClick={() => {
                    const center = canvasApi.current?.centerWorld() ?? { x: 100, y: 100 };
                    void createNode(entry.id, center.x, center.y);
                  }}
                >
                  <span className="bp-palette-name">{entry.title}</span>
                  <span className="bp-palette-cat">{entry.category}</span>
                </button>
              ))}
            </div>
            {total > entries.length && (
              <div className="bp-palette-more">
                {entries.length} of {total}, keep typing
              </div>
            )}
          </div>

          <div className={`bp-stage ${gate === 'error' ? 'bp-gate-error' : ''}`}>
            {mode === 'graph' ? (
              <BlueprintCanvas
                doc={state.doc}
                defs={defs}
                selection={state.selection}
                diagByNode={diagByNode}
                diagByPin={diagByPin}
                dispatch={dispatch}
                onRequestNode={(x, y, from) => setPicker({ x, y, from })}
                apiRef={canvasApi}
                onContextMenu={(e) => setMenu({ x: e.clientX, y: e.clientY })}
              />
            ) : (
              <pre className="papyrus-output-body bp-source">{source || 'No source yet.'}</pre>
            )}

            {menu && (
              <>
                {
}
                <div className="bp-menu-backdrop" onPointerDown={() => setMenu(null)} />
                <div className="bp-menu" style={{ left: menu.x, top: menu.y }}>
                  <button disabled={state.selection.nodes.length === 0}
                          onClick={() => { clipboard.copy(); setMenu(null); }}>
                    Copy<span>Ctrl+C</span>
                  </button>
                  <button disabled={state.selection.nodes.length === 0}
                          onClick={() => { clipboard.cut(); setMenu(null); }}>
                    Cut<span>Ctrl+X</span>
                  </button>
                  <button disabled={!clipboard.canPaste}
                          onClick={() => { clipboard.paste(); setMenu(null); }}>
                    Paste<span>Ctrl+V</span>
                  </button>
                  <button disabled={state.selection.nodes.length === 0}
                          onClick={() => { clipboard.duplicate(); setMenu(null); }}>
                    Duplicate<span>Ctrl+D</span>
                  </button>
                  <div className="bp-menu-rule" />
                  <button disabled={state.doc.nodes.length === 0}
                          onClick={() => { tidy(); setMenu(null); }}>
                    Tidy layout<span></span>
                  </button>
                  <div className="bp-menu-rule" />
                  <button disabled={state.selection.nodes.length === 0 && state.selection.wires.length === 0}
                          onClick={() => { dispatch({ type: 'DELETE_SELECTION' }); setMenu(null); }}>
                    Delete<span>Del</span>
                  </button>
                </div>
              </>
            )}

            {picker && (
              <div className="bp-popover" style={{ left: 16, top: 16 }}>
                <div className="bp-popover-head">
                  Add node
                  <button onClick={() => setPicker(null)}><X size={14} /></button>
                </div>
                <input
                  autoFocus
                  placeholder="Search"
                  onChange={(e) => setSearch(e.target.value)}
                />
                <div className="bp-popover-list">
                  {entries.slice(0, 20).map((entry) => (
                    <button key={entry.id} onClick={() =>
                      void createNode(entry.id, picker.x, picker.y, picker.from)}>
                      {entry.title}
                      <span>{entry.category}</span>
                    </button>
                  ))}
                </div>
              </div>
            )}
          </div>

          <div className="bp-side">
            <div className="bp-side-section">
              <label>Script name</label>
              <input
                value={state.doc.header.scriptName}
                onChange={(e) =>
                  dispatch({ type: 'SET_HEADER', header: { scriptName: e.target.value } })}
              />
              <label>Extends</label>
              <input
                value={state.doc.header.extends ?? ''}
                placeholder="ObjectReference"
                onChange={(e) =>
                  dispatch({ type: 'SET_HEADER', header: { extends: e.target.value || null } })}
              />
            </div>

            <div className="bp-side-section bp-diagnostics">
              <label>Problems</label>
              {(validation?.diagnostics ?? []).length === 0 && (
                <div className="bp-diag-empty">Nothing to report.</div>
              )}
              {(validation?.diagnostics ?? []).map((diagnostic, index) => (
                <button
                  key={`${diagnostic.code}-${index}`}
                  className={`bp-diag-row bp-diag-${diagnostic.severity}`}
                  onClick={() => diagnostic.nodeId && canvasApi.current?.focusNode(diagnostic.nodeId)}
                  title={diagnostic.code}
                >
                  <span className="bp-diag-code">{diagnostic.code}</span>
                  <span className="bp-diag-message">{diagnostic.message}</span>
                </button>
              ))}
            </div>

            {status && <div className="bp-status">{status}</div>}
          </div>
        </div>
      </div>
    </div>
  );
}

function BuildGate({ gate, errors, warnings }: {
  gate: 'stale' | 'ok' | 'warn' | 'error';
  errors: number;
  warnings: number;
}) {
  if (gate === 'stale') return <div className="bp-gate bp-gate-stale">checking</div>;
  if (gate === 'error') {
    return (
      <div className="bp-gate bp-gate-bad">
        <XCircle size={14} /> {errors} error{errors === 1 ? '' : 's'}
      </div>
    );
  }
  if (gate === 'warn') {
    return (
      <div className="bp-gate bp-gate-warn">
        <AlertTriangle size={14} /> {warnings} warning{warnings === 1 ? '' : 's'}
      </div>
    );
  }
  return <div className="bp-gate bp-gate-ok"><CheckCircle2 size={14} /> Compiles</div>;
}
