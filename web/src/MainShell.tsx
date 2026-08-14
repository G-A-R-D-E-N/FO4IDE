import { useState, useEffect, useRef, useCallback } from 'react';
import {
  Files, Search, GitCompare, AlertCircle, ScrollText, Box,
  ChevronRight, ChevronDown, Database, FileBox, X, Zap, Rocket,
  Link2, Archive as ArchiveIcon, Music, Boxes, Table2, Star, Workflow,
} from 'lucide-react';
import PapyrusPanel from './PapyrusPanel';
import NifPanel from './NifPanel';
import MastersPanel from './MastersPanel';
import ArchivePanel from './ArchivePanel';
import AudioPanel from './AudioPanel';
import CellPanel from './CellPanel';
import SpreadsheetPanel from './SpreadsheetPanel';
import BlueprintPanel from './Blueprint/BlueprintPanel';
import { readFavourites, removeFavourite, FAVOURITES_CHANGED } from './favourites';
import { useDialogs } from './dialogs';

interface McpLiveMsg {
  Tool: string;
  Plugin: string;
  Record: string;
  Field: string;
  Summary: string;
  IsWrite: boolean;
  ts: number;
  isNew?: boolean;
}
import './MainShell.css';
import SettingsModal from './SettingsModal';
import RecordView from './RecordView';
import ChatPanel from './ChatPanel';
import TopBar, { type ShellTab } from './Shell/TopBar';
import DetailRail from './Shell/DetailRail';
import StatusBar from './Shell/StatusBar';
import type {
  ConflictMatrix, SearchHit, ConflictEntry, RecordTypeEntry, LoadOrderSummary,
} from './backend';
import type { RecordTab } from './RecordView';

interface RecordNode {
  Key: string;
  ConflictStatus: number;
  IsRecordNode: boolean;
  HasChildren: boolean;
  FilePath: string;
}

const ExplorerTree = ({ path, node, backend, onOpenRecord }: { path: string, node: RecordNode, backend: any, onOpenRecord: (path: string, node: RecordNode) => void }) => {
  const [expanded, setExpanded] = useState(false);
  const [children, setChildren] = useState<RecordNode[]>([]);
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState('');

  const toggle = async () => {
    if (!expanded) {
      if (node.HasChildren && children.length === 0) {
        setLoading(true);


        try {
          const json = await backend.GetChildren(path);
          setChildren(JSON.parse(json));
        } catch (err: any) {
          setLoadError(err?.message || String(err));
        } finally {
          setLoading(false);
        }
      }
    }
    setExpanded(!expanded);
  };

  return (
    <div className="tree-node">
      <div
        className={`tree-row ${node.IsRecordNode ? 'is-record' : ''}`}
        onClick={node.IsRecordNode ? () => onOpenRecord(path, node) : toggle}
      >
        <div className="tree-icon-container">
          {node.HasChildren ? (
            expanded ? <ChevronDown size={14} /> : <ChevronRight size={14} />
          ) : <div style={{width: 14}}/>}
        </div>
        <div className="tree-icon">
          {node.IsRecordNode ? <FileBox size={14} color="#569CD6"/> : <Database size={14} color="#DCDCAA" />}
        </div>
        <span
          className="tree-label"
          style={node.ConflictStatus === 1 ? { color: '#c79a5e' }
            : node.ConflictStatus === 2 ? { color: '#b07a7a' }
            : undefined}
        >{node.Key}</span>
        {loading && <span className="tree-loading">...</span>}
        {!loading && loadError && (
          <span className="tree-loading" style={{ color: '#f44747' }} title={loadError}>failed</span>
        )}
      </div>
      {expanded && (
        <div className="tree-children">
          {children.map((c, i) => (
            <ExplorerTree key={`${c.Key}:${i}`} path={`${path}\\${c.Key}`} node={c} backend={backend} onOpenRecord={onOpenRecord} />
          ))}
        </div>
      )}
    </div>
  );
};

export default function MainShell() {
  const [activeTab, setActiveTab] = useState('explorer');


  const [treeMode, setTreeMode] = useState<'files' | 'conflicts'>(
    () => (localStorage.getItem('treeMode') as 'files' | 'conflicts') || 'files');
  useEffect(() => { localStorage.setItem('treeMode', treeMode); }, [treeMode]);
  const [showSettings, setShowSettings] = useState(false);
  const [showPapyrus, setShowPapyrus] = useState(false);
  const [showNif, setShowNif] = useState(false);
  const [showMasters, setShowMasters] = useState(false);
  const [showArchive, setShowArchive] = useState(false);
  const [showAudio, setShowAudio] = useState(false);
  const [showCell, setShowCell] = useState(false);
  const [showSpreadsheet, setShowSpreadsheet] = useState(false);
  const [showBlueprint, setShowBlueprint] = useState(false);
  const [showHelp, setShowHelp] = useState(false);

  const { pickPlugin: askForTarget, confirm: askConfirm } = useDialogs();


  const [shellTab, setShellTab] = useState<ShellTab>('home');
  const [railVisible, setRailVisible] = useState(true);
  const [chatVisible, setChatVisible] = useState(true);
  const [chatWidth, setChatWidth] = useState(() => Number(localStorage.getItem('chatWidth') || 320));

  const onChatResizeStart = useCallback((e: React.MouseEvent) => {
    e.preventDefault();
    const startX = e.clientX;
    const startW = chatWidth;
    const onMove = (mv: MouseEvent) => {
      const w = Math.max(240, Math.min(800, startW + (startX - mv.clientX)));
      setChatWidth(w);
      localStorage.setItem('chatWidth', String(w));
    };
    const onUp = () => {
      document.removeEventListener('mousemove', onMove);
      document.removeEventListener('mouseup', onUp);
      document.body.style.cursor = '';
      document.body.style.userSelect = '';
    };
    document.addEventListener('mousemove', onMove);
    document.addEventListener('mouseup', onUp);
    document.body.style.cursor = 'col-resize';
    document.body.style.userSelect = 'none';
  }, [chatWidth]);



  const [sidebarWidth, setSidebarWidth] = useState(() => Number(localStorage.getItem('sidebarWidth') || 280));

  const onSidebarResizeStart = useCallback((e: React.MouseEvent) => {
    e.preventDefault();
    const startX = e.clientX;
    const startW = sidebarWidth;
    const onMove = (mv: MouseEvent) => {
      const w = Math.max(200, Math.min(620, startW + (mv.clientX - startX)));
      setSidebarWidth(w);
      localStorage.setItem('sidebarWidth', String(w));
    };
    const onUp = () => {
      document.removeEventListener('mousemove', onMove);
      document.removeEventListener('mouseup', onUp);
      document.body.style.cursor = '';
      document.body.style.userSelect = '';
    };
    document.addEventListener('mousemove', onMove);
    document.addEventListener('mouseup', onUp);
    document.body.style.cursor = 'col-resize';
    document.body.style.userSelect = 'none';
  }, [sidebarWidth]);


  const [isDark, setIsDark] = useState(() => localStorage.getItem('theme') !== 'light');
  useEffect(() => {
    document.documentElement.setAttribute('data-theme', isDark ? 'dark' : 'light');
    localStorage.setItem('theme', isDark ? 'dark' : 'light');
  }, [isDark]);


  const [recordTab, setRecordTab] = useState<RecordTab>('grid');
  const [plugins, setPlugins] = useState<RecordNode[]>([]);
  const [backend, setBackend] = useState<any>(null);
  const [errorStr, setErrorStr] = useState<string>("");
  const [status, setStatus] = useState<string>("");
  const [progress, setProgress] = useState<number | null>(null);



  const [envLoading, setEnvLoading] = useState(false);



  const [favourites, setFavourites] = useState(readFavourites);
  const [favouritesOpen, setFavouritesOpen] = useState(
    () => localStorage.getItem('favouritesOpen') !== 'false');
  useEffect(() => { localStorage.setItem('favouritesOpen', String(favouritesOpen)); }, [favouritesOpen]);
  useEffect(() => {
    const sync = () => setFavourites(readFavourites());
    window.addEventListener(FAVOURITES_CHANGED, sync);
    return () => window.removeEventListener(FAVOURITES_CHANGED, sync);
  }, []);




  interface OpenRec { title: string; plugin: string; matrix: ConflictMatrix }
  const [openRecords, setOpenRecords] = useState<OpenRec[]>([]);
  const [activeKey, setActiveKey] = useState<string>('');
  const [recordLoading, setRecordLoading] = useState(false);





  const openRecord = openRecords.find(r => r.matrix?.FormKey === activeKey) ?? null;


  const putRecord = useCallback((rec: OpenRec) => {
    if (!rec.matrix?.FormKey) return;
    setOpenRecords(prev => {
      const at = prev.findIndex(r => r.matrix?.FormKey === rec.matrix.FormKey);
      if (at === -1) return [...prev, rec];
      const next = [...prev];
      next[at] = rec;
      return next;
    });
    setActiveKey(rec.matrix.FormKey);
  }, []);

  const closeRecordTab = useCallback((formKey: string) => {
    setOpenRecords(prev => {
      const at = prev.findIndex(r => r.matrix?.FormKey === formKey);
      if (at === -1) return prev;
      const next = prev.filter(r => r.matrix?.FormKey !== formKey);

      setActiveKey(cur => (cur !== formKey ? cur : (next[at] ?? next[at - 1])?.matrix?.FormKey ?? ''));
      return next;
    });
  }, []);


  const [mcpFeed, setMcpFeed] = useState<McpLiveMsg[]>([]);
  const [mcpActive, setMcpActive] = useState(false);
  const mcpActiveTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const [aiHighlightField, setAiHighlightField] = useState('');
  const highlightTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);



  const openRecordRef = useRef(openRecord);
  openRecordRef.current = openRecord;


  const [search, setSearch] = useState('');
  const [searchResults, setSearchResults] = useState<SearchHit[] | null>(null);



  const [recordTypes, setRecordTypes] = useState<RecordTypeEntry[]>([]);
  const [loadOrderSummary, setLoadOrderSummary] = useState<LoadOrderSummary | null>(null);


  const refreshNavigator = useCallback(async () => {
    const b = window.chrome?.webview?.hostObjects?.backend;
    if (!b) return;
    try {
      const [typesJson, summaryJson] = await Promise.all([
        b.GetRecordTypeIndex(), b.GetLoadOrderSummary(),
      ]);
      setRecordTypes(JSON.parse(typesJson));
      setLoadOrderSummary(JSON.parse(summaryJson));
    } catch (e) {
      setErrorStr(`Load-order summary failed: ${e}`);
    }
  }, []);

  useEffect(() => {
    const handleRejection = (event: PromiseRejectionEvent) => {
      let msg = "Unhandled Promise: ";
      if (event.reason && typeof event.reason === 'object') {
        try {
          msg += JSON.stringify(event.reason, Object.getOwnPropertyNames(event.reason));
        } catch {
          msg += String(event.reason);
        }
      } else {
        msg += String(event.reason);
      }
      setErrorStr(msg);
    };
    window.addEventListener('unhandledrejection', handleRejection);



    const onMessage = (e: any) => {
      const data = typeof e.data === 'string' ? JSON.parse(e.data) : e.data;
      if (!data || !data.Type) return;
      if (data.Type === 'SetStatus') setStatus(data.Text || "");
      if (data.Type === 'SetProgress') setProgress(data.Value < 0 ? null : data.Value);
      if (data.Type === 'McpLive') {
        const ev: McpLiveMsg = { ...data, ts: Date.now(), isNew: true };
        setMcpFeed(prev => [ev, ...prev].slice(0, 12));
        setMcpActive(true);
        if (mcpActiveTimerRef.current) clearTimeout(mcpActiveTimerRef.current);
        mcpActiveTimerRef.current = setTimeout(() => setMcpActive(false), 2000);
        if (ev.IsWrite && ev.Record) {
          const cur = openRecordRef.current;
          if (cur && cur.matrix.FormKey === ev.Record) {
            reloadMatrixRef.current();
          } else {

            openByFormKeyRef.current(ev.Record, ev.Plugin);
          }

          if (ev.Field) {
            if (highlightTimerRef.current) clearTimeout(highlightTimerRef.current);
            setAiHighlightField(ev.Field);
            highlightTimerRef.current = setTimeout(() => setAiHighlightField(''), 3000);
          }
        } else if (ev.IsWrite && openRecordRef.current) {
          reloadMatrixRef.current();
        }
      }

    };

    window.chrome?.webview?.addEventListener('message', onMessage);

    const init = async () => {
      try {

        if (window.chrome?.webview?.hostObjects?.appInterop) {

          const b = window.chrome.webview.hostObjects.appInterop;




          setBackend(() => b);
          await refreshPlugins();
        }
      } catch (err: any) {
        console.error("Init Error:", err);
        setErrorStr(err?.message || JSON.stringify(err) || String(err));
      }
    };
    init();

    return () => {
      window.removeEventListener('unhandledrejection', handleRejection);

      window.chrome?.webview?.removeEventListener('message', onMessage);
    };
  }, []);


  const refreshPlugins = async () => {
    const b = window.chrome?.webview?.hostObjects?.appInterop;
    if (!b) return;
    const json = await b.GetPlugins();
    setPlugins(JSON.parse(json));
    setErrorStr("");

    await refreshNavigator();
  };

  const handleLoadEnv = async () => {
    const b = window.chrome?.webview?.hostObjects?.appInterop;
    if (!b) return;
    setEnvLoading(true);
    try {
      await b.LoadEnvironment();
      await refreshPlugins();
    } catch (err: any) {
      setErrorStr(err?.message || JSON.stringify(err));
    } finally {
      setProgress(null);
      setEnvLoading(false);
    }
  };

  const handleLoadMo2 = async () => {
    const b = window.chrome?.webview?.hostObjects?.appInterop;
    if (!b) return;
    try {
      const path = await b.BrowseForMo2Folder();
      if (!path) return;
      setEnvLoading(true);
      await b.OpenMo2Profile(path);
      await refreshPlugins();
    } catch (err: any) {
      setErrorStr(err?.message || JSON.stringify(err));
    } finally {
      setProgress(null);
      setEnvLoading(false);
    }
  };



  const handleRefresh = async () => {
    const b = window.chrome?.webview?.hostObjects?.appInterop;
    if (!b) return;
    try {
      const json = await b.RefreshTree();
      setPlugins(JSON.parse(json));
      await refreshNavigator();
      if (openRecord) await reloadMatrix();
      setStatus('Refreshed.');
    } catch (err: any) {
      setErrorStr(err?.message || JSON.stringify(err));
    }
  };

  const handleScanConflicts = async () => {
    const b = window.chrome?.webview?.hostObjects?.appInterop;
    if (!b) return;
    try {
      const summary = await b.ScanConflicts();
      setStatus(summary);
      await refreshPlugins();
    } catch (err: any) {
      setErrorStr(err?.message || JSON.stringify(err));
    } finally {
      setProgress(null);
    }
  };

  const [brokenRefsReport, setBrokenRefsReport] = useState<string | null>(null);
  const [brokenRefsScanning, setBrokenRefsScanning] = useState(false);

  const handleScanBrokenRefs = async () => {
    const b = window.chrome?.webview?.hostObjects?.appInterop;
    if (!b) return;
    setBrokenRefsScanning(true);
    setStatus('Scanning for broken references...');
    try {
      const report = await b.ScanBrokenRefs();
      setBrokenRefsReport(report);
      setStatus('Broken-ref scan complete.');
    } catch (err: any) {
      setErrorStr(err?.message || JSON.stringify(err));
    } finally {
      setBrokenRefsScanning(false);
      setProgress(null);
    }
  };


  const openRecordInView = async (path: string, node: RecordNode) => {
    const app = window.chrome?.webview?.hostObjects?.appInterop;
    const back = window.chrome?.webview?.hostObjects?.backend;
    if (!app || !back) { setErrorStr("Record bridge unavailable."); return; }
    setRecordLoading(true);
    try {
      const formKey = await app.OpenRecord(path);
      if (!formKey) { setErrorStr(`Couldn't resolve a FormKey for ${node.Key}.`); return; }
      const plugin = path.split(/[\\/]/)[0];





      const raw = await back.GetConflictMatrix(formKey);
      const matrix: ConflictMatrix | null = raw ? JSON.parse(raw) : null;
      if (!matrix) {




        setErrorStr(`Couldn't build a view of ${node.Key} (${formKey}). The environment may still be `
          + `loading or was just reloaded -- try again, or reload it with Open MO2.`);
        return;
      }
      putRecord({ title: node.Key, plugin, matrix });
    } catch (err: any) {
      setErrorStr(err?.message || String(err));
    } finally {
      setRecordLoading(false);
    }
  };


  useEffect(() => {
    const q = search.trim();
    if (q.length < 2) { setSearchResults(null); return; }
    const t = setTimeout(async () => {
      const back = window.chrome?.webview?.hostObjects?.backend;
      if (!back) return;
      try { setSearchResults(JSON.parse(await back.SearchRecords(q, ''))); }
      catch { setSearchResults([]); }
    }, 300);
    return () => clearTimeout(t);
  }, [search]);


  const [conflicts, setConflicts] = useState<ConflictEntry[] | null>(null);
  const [conflictSel, setConflictSel] = useState<Record<string, boolean>>({});
  const [conflictBusy, setConflictBusy] = useState(false);

  const loadConflicts = async () => {
    const b = window.chrome?.webview?.hostObjects?.backend;
    if (!b) return;
    setConflicts(null);
    try { setConflicts(JSON.parse(await b.GetConflicts())); }
    catch (e: any) { setErrorStr(e?.message || String(e)); setConflicts([]); }
  };


  useEffect(() => {
    if (activeTab === 'explorer' && treeMode === 'conflicts' && conflicts === null) loadConflicts();

  }, [activeTab, treeMode]);

  const batchCopyAsOverride = async () => {
    const b = window.chrome?.webview?.hostObjects?.backend;
    if (!b || !conflicts) return;
    const picked = conflicts.filter(c => conflictSel[c.FormKey]);
    if (picked.length === 0) { setStatus('Select at least one conflict first.'); return; }
    const chosen = await askForTarget({
      title: `Copy ${picked.length} record(s) as override`,
      description: 'Every selected conflict is copied into this plugin as an override. It still has to be saved and enabled last in the load order.',
      confirmLabel: 'Copy',
      defaultTarget: 'ConflictPatch.esp',
    });
    if (!chosen) return;
    const patch = chosen.target;
    setConflictBusy(true);
    try {
      const items = picked.map(c => ({ formKey: c.FormKey, source: c.Winner }));
      let res = JSON.parse(await b.CopyAsOverrideMany(JSON.stringify(items), patch, false));
      if (res.requiresOverwrite) {
        const existing = Array.isArray(res.existing) ? res.existing : [];
        const sample = existing.slice(0, 5)
          .map((x: any) => `${x.editorId || x.formKey} (${x.formKey})`).join('\n');
        const more = existing.length > 5 ? `\n...and ${existing.length - 5} more.` : '';
        const proceed = await askConfirm({
          title: 'Overwrite existing overrides?',
          message: `${existing.length} of the ${res.total} selected records already exist in ${patch}. ` +
            `Nothing has been copied yet.\n\n${sample}${more}`,
          confirmLabel: 'Overwrite and copy',
          danger: true,
        });
        if (!proceed) {
          setStatus(`Batch copy cancelled -- ${patch} was left unchanged.`);
          return;
        }
        res = JSON.parse(await b.CopyAsOverrideMany(JSON.stringify(items), patch, true));
      }
      const firstFailure = Array.isArray(res.failures) && res.failures.length ? res.failures[0]?.reason : '';
      const failureNote = res.failed
        ? ` ${res.failed} failed${firstFailure ? `: ${firstFailure}` : ''}.`
        : '';
      setStatus(`Copied ${res.ok}/${res.total} into ${patch}.${failureNote} Save ${patch} from a record's context menu, then enable it last.`);
      if (!res.failed) setConflictSel({});
    } catch (e: any) {
      setStatus('Batch copy failed: ' + (e?.message || e));
    } finally { setConflictBusy(false); }
  };


  const openHit = async (hit: SearchHit) => {
    const back = window.chrome?.webview?.hostObjects?.backend;
    if (!back) return;
    setRecordLoading(true);
    try {
      const json = await back.GetConflictMatrix(hit.FormKey);
      const matrix: ConflictMatrix | null = json ? JSON.parse(json) : null;
      if (!matrix) { setErrorStr(`No record for ${hit.FormKey}.`); return; }
      putRecord({ title: hit.EditorID || hit.FormKey, plugin: hit.Plugin, matrix });
    } catch (err: any) {
      setErrorStr(err?.message || String(err));
    } finally {
      setRecordLoading(false);
    }
  };


  const openByFormKey = async (formKey: string, pluginName: string) => {
    const b = window.chrome?.webview?.hostObjects?.backend;
    if (!b) return;
    setRecordLoading(true);
    try {
      const json = await b.GetConflictMatrix(formKey);
      const matrix: ConflictMatrix | null = json ? JSON.parse(json) : null;
      if (!matrix) { setErrorStr(`No record for ${formKey}.`); return; }
      putRecord({ title: matrix.EditorID || formKey, plugin: pluginName || matrix.Winner, matrix });
    } catch (err: any) {
      setErrorStr(err?.message || String(err));
    } finally {
      setRecordLoading(false);
    }
  };


  const reloadMatrix = async () => {
    const b = window.chrome?.webview?.hostObjects?.backend;
    if (!b || !openRecord) return;
    try {
      const json = await b.GetConflictMatrix(openRecord.matrix.FormKey);
      const matrix: ConflictMatrix | null = json ? JSON.parse(json) : null;

      if (!matrix || !matrix.Plugins || matrix.Plugins.length === 0) closeRecordTab(openRecord.matrix.FormKey);
      else setOpenRecords(prev => prev.map(r => (r.matrix.FormKey === matrix.FormKey ? { ...r, matrix } : r)));
    } catch (err: any) {
      setErrorStr(err?.message || String(err));
    }
  };



  const reloadMatrixRef = useRef(reloadMatrix);
  reloadMatrixRef.current = reloadMatrix;
  const openByFormKeyRef = useRef(openByFormKey);
  openByFormKeyRef.current = openByFormKey;


  useEffect(() => {
    if (openRecord) { setShellTab('record'); setRecordTab('grid'); }
  }, [openRecord?.matrix?.FormKey]);


  const commandSearch = useCallback(async (q: string): Promise<SearchHit[]> => {
    const back = window.chrome?.webview?.hostObjects?.backend;
    if (!back) return [];
    try { return JSON.parse(await back.SearchRecords(q, '')); }
    catch { return []; }
  }, []);


  const closeRecord = () => {
    if (!openRecord) { setShellTab('home'); return; }
    const remaining = openRecords.length - 1;
    closeRecordTab(openRecord.matrix.FormKey);
    if (remaining === 0) setShellTab('home');
  };

  return (
    <div
      className={`shell-container animate-fade-in ${railVisible ? 'rail-on' : 'rail-off'} ${chatVisible ? 'chat-on' : 'chat-off'}`}
    >
      {




}
      {envLoading && (
        <div className="env-loading-overlay" role="alertdialog" aria-busy="true" aria-live="polite">
          <div className="env-loading-box">
            <div className="env-loading-title">Loading environment…</div>
            <div className="env-loading-msg">{status || 'Reading the load order. This can take a few seconds on a large modlist.'}</div>
            <div className="env-loading-track">
              <div
                className={`env-loading-fill ${progress === null ? 'indeterminate' : ''}`}
                style={progress === null ? undefined : { width: `${Math.max(2, Math.min(100, progress))}%` }}
              />
            </div>
            <div className="env-loading-hint">The tool is locked until this finishes, so nothing is asked of a half-built environment.</div>
          </div>
        </div>
      )}

      <TopBar
        activeTab={shellTab}
        hasRecord={!!openRecord}
        recordTitle={openRecord?.title ?? ''}
        onSelectTab={setShellTab}
        onCloseRecord={closeRecord}
        onOpenSettings={() => setShowSettings(true)}
        onOpenHelp={() => setShowHelp(true)}
        onToggleRail={() => setRailVisible(v => !v)}
        railVisible={railVisible}
        onToggleChat={() => setChatVisible(v => !v)}
        chatVisible={chatVisible}
        isDark={isDark}
        onToggleTheme={() => setIsDark(v => !v)}
        onSearch={commandSearch}
        onOpenHit={openHit}
      />

      {}
      <div className="shell-left">
      <div className="activity-bar">
        <div className="activity-top">
          <button className={`activity-btn ${activeTab === 'explorer' ? 'active' : ''}`} onClick={() => setActiveTab('explorer')} title="Explorer: records, files, search and errors">
            <Files size={22} strokeWidth={1.5} />
          </button>
          <button className={`activity-btn ${showPapyrus ? 'active' : ''}`} onClick={() => setShowPapyrus(true)} title="Papyrus (compile / decompile)">
            <ScrollText size={22} strokeWidth={1.5} />
          </button>
          <button className={`activity-btn ${showNif ? 'active' : ''}`} onClick={() => setShowNif(true)} title="NIF (author / inspect / verify / fix)">
            <Box size={22} strokeWidth={1.5} />
          </button>
          <button className={`activity-btn ${showMasters ? 'active' : ''}`} onClick={() => setShowMasters(true)} title="Masters (inspect / reorder / ESL flag)">
            <Link2 size={22} strokeWidth={1.5} />
          </button>
          <button className={`activity-btn ${showArchive ? 'active' : ''}`} onClick={() => setShowArchive(true)} title="Archive (BA2/BSA browse / extract)">
            <ArchiveIcon size={22} strokeWidth={1.5} />
          </button>
          <button className={`activity-btn ${showAudio ? 'active' : ''}`} onClick={() => setShowAudio(true)} title="Audio (convert to/from xWMA, merge/split fuz)">
            <Music size={22} strokeWidth={1.5} />
          </button>
          <button className={`activity-btn ${showCell ? 'active' : ''}`} onClick={() => setShowCell(true)} title="Cell Viewer (read-only 3D, like Creation Kit's render window)">
            <Boxes size={22} strokeWidth={1.5} />
          </button>
          <button className={`activity-btn ${showSpreadsheet ? 'active' : ''}`} onClick={() => setShowSpreadsheet(true)} title="Spreadsheet (bulk-edit every record of one type)">
            <Table2 size={22} strokeWidth={1.5} />
          </button>
          <button className={`activity-btn ${showBlueprint ? 'active' : ''}`} onClick={() => setShowBlueprint(true)} title="Blueprint (node graph that compiles to Papyrus)">
            <Workflow size={22} strokeWidth={1.5} />
          </button>
        </div>
      </div>

      {}
      <div className="sidebar" style={{ width: sidebarWidth }}>
        <div className="sidebar-header">
          <h2>EXPLORER</h2>
          {activeTab === 'explorer' && (
            <div className="tree-mode">
              <button
                className={treeMode === 'files' ? 'on' : ''}
                onClick={() => setTreeMode('files')}
                title="The load order, plugin by plugin"
              >
                <Files size={12} /> Files
              </button>
              <button
                className={treeMode === 'conflicts' ? 'on' : ''}
                onClick={() => setTreeMode('conflicts')}
                title="Records more than one plugin touches"
              >
                <GitCompare size={12} /> Conflicts
              </button>
            </div>
          )}
        </div>
        <div className="sidebar-actions">
          <button className="sidebar-action-btn" onClick={handleLoadEnv}>Load Env</button>
          <button className="sidebar-action-btn" onClick={handleLoadMo2}>Open MO2</button>
          <button className="sidebar-action-btn" onClick={handleScanConflicts}>Scan Conflicts</button>
          <button className="sidebar-action-btn sidebar-action-btn--warn" onClick={handleScanBrokenRefs} disabled={brokenRefsScanning} title="Scan all plugins for dangling FormLinks (crash risks)">
            {brokenRefsScanning ? 'Scanning...' : 'Scan Crash Risks'}
          </button>
          <button className="sidebar-action-btn" onClick={handleRefresh}>Refresh</button>
        </div>
        {treeMode === 'files' && (
        <div className="sidebar-search">
          <Search size={13} />
          <input
            value={search}
            onChange={e => setSearch(e.target.value)}
            placeholder="Search plugins & records…"
          />
          {search && <button className="sidebar-search-clear" onClick={() => setSearch('')} title="Clear"><X size={13} /></button>}
        </div>
        )}

        {activeTab === 'explorer' && errorStr && (
          <div className="sidebar-error">
            <AlertCircle size={13} />
            <span className="sidebar-error-text">{errorStr}</span>
            <button className="sidebar-error-clear" onClick={() => setErrorStr('')} title="Dismiss">
              <X size={12} />
            </button>
          </div>
        )}

        {


}
        {activeTab === 'explorer' && treeMode === 'files' && favourites.length > 0 && (
          <div className="sidebar-favourites">
            <button
              className="sidebar-fav-header"
              onClick={() => setFavouritesOpen(o => !o)}
              title={favouritesOpen ? 'Collapse favourites' : 'Expand favourites'}
            >
              {favouritesOpen ? <ChevronDown size={13} /> : <ChevronRight size={13} />}
              <Star size={12} className="sidebar-fav-star" />
              <span>FAVOURITES</span>
              <span className="sidebar-fav-count">{favourites.length}</span>
            </button>
            {favouritesOpen && favourites.map(f => (
              <div
                key={f.formKey}
                className="tree-row is-record sidebar-fav-row"
                onClick={() => openByFormKey(f.formKey, f.plugin)}
                title={`${f.formKey}${f.plugin ? ' - ' + f.plugin : ''}`}
              >
                <div className="tree-icon-container"><div style={{ width: 14 }} /></div>
                <div className="tree-icon"><FileBox size={14} color="#569CD6" /></div>
                <span className="tree-label">{f.label}</span>
                <button
                  className="sidebar-fav-remove"
                  title="Remove from favourites"
                  onClick={e => { e.stopPropagation(); removeFavourite(f.formKey); setFavourites(readFavourites()); }}
                >
                  <X size={11} />
                </button>
              </div>
            ))}
          </div>
        )}

        {activeTab === 'explorer' && treeMode === 'files' && (
        <div className="sidebar-tree">
          {plugins.length === 0 && !errorStr ? (
            <div className="sidebar-empty">
              No plugins loaded. Click "Load Env" or "Open MO2".
            </div>
          ) : (
            <>
              {}
              {plugins
                .filter(p => !search.trim() || p.Key.toLowerCase().includes(search.trim().toLowerCase()))
                .map((p, i) => (
                  <ExplorerTree key={`${p.Key}:${i}`} path={p.Key} node={p} backend={backend} onOpenRecord={openRecordInView} />
                ))}

              {}
              {search.trim().length >= 2 && (
                <div className="sidebar-results">
                  <div className="sidebar-results-head">
                    RECORDS {searchResults === null ? '…' : `(${searchResults.length})`}
                  </div>
                  {searchResults === null ? (
                    <div className="sidebar-empty">Searching…</div>
                  ) : searchResults.length === 0 ? (
                    <div className="sidebar-empty">No matching records.</div>
                  ) : (
                    searchResults.map((h, i) => (
                      <div key={`${h.Plugin}:${h.FormKey}:${i}`} className="sidebar-result" onClick={() => openHit(h)} title={`${h.FormKey} · ${h.Plugin}`}>
                        <FileBox size={13} color="#569CD6" />
                        <span className="sr-id">{h.EditorID || h.FormKey}</span>
                        <span className="sr-type">{h.Type}</span>
                      </div>
                    ))
                  )}
                </div>
              )}
            </>
          )}
        </div>
        )}
        {activeTab === 'explorer' && treeMode === 'conflicts' && (
          <div className="conflicts-panel">
            <div className="conflicts-actions">
              <button className="sidebar-action-btn" onClick={loadConflicts}>Rescan</button>
              <button className="sidebar-action-btn" disabled={conflictBusy} onClick={batchCopyAsOverride}>
                Copy selected → patch
              </button>
            </div>
            {conflicts === null ? (
              <div className="sidebar-empty">Scanning conflicts…</div>
            ) : conflicts.length === 0 ? (
              <div className="sidebar-empty">No conflicts. Load a modlist and Scan Conflicts.</div>
            ) : (
              <div className="conflicts-list">
                {conflicts.map((c, i) => (
                  <div key={`${c.FormKey}:${i}`} className="conflict-row">
                    <input
                      type="checkbox"
                      checked={!!conflictSel[c.FormKey]}
                      onChange={e => setConflictSel(s => ({ ...s, [c.FormKey]: e.target.checked }))}
                    />
                    <span className="conflict-id" onClick={() => openByFormKey(c.FormKey, c.Winner)} title={`${c.FormKey} · ${c.Plugins.length} plugins`}>
                      {c.EditorID || c.FormKey}
                    </span>
                    <span className="conflict-type">{c.Type}</span>
                    <span className="conflict-count">{c.Plugins.length}</span>
                  </div>
                ))}
              </div>
            )}
          </div>
        )}
      </div>
      <div className="sidebar-resizer" onMouseDown={onSidebarResizeStart} />
      </div>{}

      {}
      <div className="main-area">
        {shellTab === 'home' ? (
          <div className="home-view">
            <div className="home-card glass-panel">
              <Rocket size={56} color="var(--accent-color)" />
              <h1>FO4 Record Editor</h1>
              <p>The next-generation modding IDE for Fallout 4 plugins.</p>
              <div className="home-hints">
                <p>Load your environment from the Explorer on the left.</p>
                <p>Click a record to open it in the Record Viewer.</p>
                <p>Press <kbd>Ctrl K</kbd> to search records, fields, or formIDs.</p>
              </div>
            </div>
          </div>
        ) : (
          <>
            <div className="editor-tabs">
              {openRecords.length === 0 ? (
                <div className="editor-tab active">
                  <Zap size={14} color="#F44747"/> Welcome
                </div>
              ) : openRecords.map(r => (
                <div
                  key={r.matrix.FormKey}
                  className={`editor-tab ${r.matrix.FormKey === activeKey ? 'active' : ''}`}
                  onClick={() => setActiveKey(r.matrix.FormKey)}
                  title={`${r.matrix.FormKey} - ${r.plugin}`}
                >
                  <Database size={14} color="#569CD6"/>
                  <span className="editor-tab-label">{r.title}</span>
                  <button
                    className="tab-close"
                    title="Close"
                    onClick={e => { e.stopPropagation(); closeRecordTab(r.matrix.FormKey); }}
                  >
                    <X size={14} />
                  </button>
                </div>
              ))}
            </div>

            <div className={`editor-content ${openRecord && !recordLoading ? 'has-record' : ''}`}>
              {recordLoading ? (
                <div className="welcome-screen glass-panel">
                  <Database size={48} color="var(--border-color)" />
                  <p>Loading record...</p>
                </div>
              ) : openRecord ? (
                <RecordView
                  matrix={openRecord.matrix}
                  plugin={openRecord.plugin}
                  onReload={reloadMatrix}
                  onOpenRecord={openByFormKey}
                  activeTab={recordTab}
                  onTabChange={setRecordTab}
                  highlightField={aiHighlightField}
                  onPluginsChanged={() => { void refreshPlugins(); }}
                />
              ) : (
                <div className="welcome-screen glass-panel">
                  <Database size={64} color="var(--border-color)" />
                  <h1>No record open</h1>
                  <p>Pick a record from the Explorer or search with Ctrl K.</p>
                </div>
              )}
            </div>

            {mcpFeed.length > 0 && (
              <div className="mcp-feed">
                <div className="mcp-feed-header">
                  <span className={`mcp-dot ${mcpActive ? 'mcp-dot-active' : ''}`} />
                  <span>MCP TOOL CALLS</span>
                  <button className="mcp-feed-clear" onClick={() => setMcpFeed([])}>✕</button>
                </div>
                <div className="mcp-feed-rows">
                  {mcpFeed.map((ev, i) => (
                    <div key={i} className={`mcp-row ${ev.IsWrite ? 'mcp-row-write' : 'mcp-row-read'} ${i === 0 ? 'mcp-row-new' : ''}`}>
                      <span className="mcp-badge">{ev.Tool}</span>
                      <span className="mcp-summary">{ev.Summary}</span>
                      <span className="mcp-time">{new Date(ev.ts).toLocaleTimeString()}</span>
                    </div>
                  ))}
                </div>
              </div>
            )}
          </>
        )}
      </div>

      {}
      {railVisible && (
        <DetailRail
          matrix={openRecord?.matrix ?? null}
          onOpenConflictsTab={() => { setRecordTab('conflicts'); setShellTab('record'); }}
          onOpenRecord={openByFormKey}
          onAddToFilter={needle => {
            setSearch(needle);
            setActiveTab('explorer');
            setTreeMode('files');
          }}
        />
      )}

      {}
      <div className="shell-chat" style={{ display: chatVisible ? undefined : 'none', width: chatVisible ? chatWidth : undefined }}>
        <div className="chat-resizer" onMouseDown={onChatResizeStart} />
        <ChatPanel />
      </div>

      {}
      <StatusBar
        status={status}
        progress={progress}
        recordCount={loadOrderSummary?.TotalRecords ?? 0}
        visibleCount={recordTypes.reduce((n, t) => n + t.Count, 0)}
        selectedCount={openRecord ? 1 : 0}
        loadOrder={loadOrderSummary?.Plugins ?? plugins.map(p => p.Key)}
      />

      {brokenRefsReport !== null && (
        <div className="broken-refs-overlay" onClick={() => setBrokenRefsReport(null)}>
          <div className="broken-refs-modal" onClick={e => e.stopPropagation()}>
            <div className="broken-refs-header">
              <span>Crash Risk Report</span>
              <div className="broken-refs-header-actions">
                <button className="sidebar-action-btn" onClick={() => navigator.clipboard.writeText(brokenRefsReport)}>Copy</button>
                <button className="sidebar-action-btn" onClick={() => {
                  const blob = new Blob([brokenRefsReport], { type: 'text/plain' });
                  const url = URL.createObjectURL(blob);
                  const a = document.createElement('a');
                  a.href = url;
                  a.download = 'crash_risk_report.txt';
                  a.click();
                  URL.revokeObjectURL(url);
                }}>Export</button>
                <button className="broken-refs-close" onClick={() => setBrokenRefsReport(null)}>✕</button>
              </div>
            </div>
            <pre className="broken-refs-body">{brokenRefsReport}</pre>
          </div>
        </div>
      )}

      {



}


      {showHelp && (

        <div className="papyrus-overlay" onClick={() => setShowHelp(false)}>

          <div className="papyrus-modal glass-panel" style={{ maxWidth: 620 }} onClick={e => e.stopPropagation()}>

            <div className="papyrus-header">

              <span className="papyrus-title">Help</span>

              <button className="papyrus-close" onClick={() => setShowHelp(false)} title="Close"><X size={16} /></button>

            </div>

            <div style={{ padding: '14px 18px', fontSize: 13, lineHeight: 1.65 }}>

              <p><strong>Loading a modlist.</strong> Use <em>Open MO2</em> and pick the instance folder

                (the one holding <code>mods/</code> and <code>profiles/</code>). On Linux <em>Load Env</em>

                cannot auto-detect a load order, because there is no game-managed <code>Plugins.txt</code>

                outside a Proton prefix; it will say so rather than fail silently.</p>

              <p><strong>Edits are in memory until saved.</strong> Changing a field marks the plugin dirty;

                use Save on that plugin (or the Spreadsheet's Save Changes) to write it to disk.</p>

              <p><strong>Favourites.</strong> Star a record in the right-hand rail and it appears under

                FAVOURITES at the top of the Explorer.</p>

              <p><strong>Search</strong> is <code>Ctrl</code>+<code>K</code>, or the box at the top.</p>

              <p><strong>When something looks wrong,</strong> the log is at

                <code>~/.config/FO4RecordEditor/debug.log</code>. A failed scan now says it failed --

                an empty References tab means nothing references the record, not that the scan broke.</p>

            </div>

          </div>

        </div>

      )}

      {showSettings && <SettingsModal onClose={() => setShowSettings(false)} />}
      {showPapyrus && <PapyrusPanel onClose={() => setShowPapyrus(false)} />}
      {showNif && <NifPanel onClose={() => setShowNif(false)} />}
      {showMasters && <MastersPanel onClose={() => setShowMasters(false)} />}
      {showArchive && <ArchivePanel onClose={() => setShowArchive(false)} />}
      {showAudio && <AudioPanel onClose={() => setShowAudio(false)} />}
      {showCell && <CellPanel onClose={() => setShowCell(false)} />}
      {showSpreadsheet && <SpreadsheetPanel onClose={() => setShowSpreadsheet(false)} />}
      {showBlueprint && <BlueprintPanel onClose={() => setShowBlueprint(false)} />}
    </div>
  );
}
