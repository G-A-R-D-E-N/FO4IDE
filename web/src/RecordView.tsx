import { Fragment, useEffect, useMemo, useRef, useState } from 'react';
import {
  ChevronRight, ChevronDown, EyeOff, Pencil, Copy, Save, Trash2,
  Link2, AlertTriangle, Hash, Minimize2, Eraser, Brain, Crown, Clock, Network, HelpCircle,
  Plus, Minus, ListFilter, ClipboardCopy, FilePlus2, ArrowUp, ArrowDown, Eraser as EraserIcon,
  GitCompare as GitCompareIcon,
} from 'lucide-react';
import type {
  ConflictMatrix, ConflictFieldRow, RefByEntry, RecordProblem, Dependency, HistoryEntry, ElementActions,
} from './backend';
import RecordPicker from './RecordPicker';
import ConditionsEditor from './ConditionsEditor';
import { useDialogs } from './dialogs';
import WorkspaceHeader from './Workspace/WorkspaceHeader';
import { buildActions } from './Workspace/actions';
import ConflictsView, { type ConflictLayout } from './Workspace/ConflictsView';
import './RecordView.css';

const back = () => window.chrome?.webview?.hostObjects?.backend;

export type RecordTab = 'grid' | 'fields' | 'conflicts' | 'references' | 'history' | 'dependencies';

const defaultExpanded = (r: ConflictFieldRow) => !(r.IsSummary || r.DisplayLabel.startsWith('['));

type Status = 'notdefined' | 'none' | 'identical' | 'master' | 'override' | 'win' | 'lose' | 'only';
function cellStatus(vals: string[], idx: number): Status {
  const me = vals[idx] ?? '';
  if (me === '') return 'notdefined';
  const present = vals.filter(v => v !== '');
  let winner = -1;
  for (let i = vals.length - 1; i >= 0; i--) { if (vals[i] !== '') { winner = i; break; } }
  if (present.length <= 1) return 'only';
  const allSame = present.every(v => v === present[0]);
  if (allSame) return idx === 0 ? 'master' : 'identical';
  if (idx === winner) return 'win';
  if (idx === 0) return 'master';
  if (me === vals[winner]) return 'override';
  return 'lose';
}

function resolveStatus(r: ConflictFieldRow, idx: number): Status {
  const s = r.Statuses?.[idx];
  if (s) return s as Status;
  return cellStatus(r.Values, idx);
}

type SeverityFilter = 'all' | 'override' | 'conflict' | 'critical';
const SEVERITY_SETS: Record<Exclude<SeverityFilter, 'all'>, Set<string>> = {
  override: new Set(['override', 'conflict', 'critical']),
  conflict: new Set(['conflict', 'critical']),
  critical: new Set(['critical']),
};

const LEVEL_BADGES: Record<string, { label: string; cls: string }> = {
  noconflict: { label: 'No Conflict', cls: 'rv-lvl-noconflict' },
  override:   { label: 'Override', cls: 'rv-lvl-override' },
  conflict:   { label: 'Conflict', cls: 'rv-lvl-conflict' },
  critical:   { label: 'Critical', cls: 'rv-lvl-critical' },
  onlyone:    { label: 'Single Record', cls: 'rv-lvl-onlyone' },
};

const LEGEND_ROWS: { cls: string; label: string }[] = [
  { cls: 'rv-s-none', label: 'No conflict' },
  { cls: 'rv-s-benign', label: 'Benign conflict' },
  { cls: 'rv-s-override', label: 'Override without conflict' },
  { cls: 'rv-s-conflict', label: 'Conflict' },
  { cls: 'rv-s-critical', label: 'Critical conflict' },
];

const LEGEND_CELLS: { cls: string; label: string }[] = [
  { cls: 'rv-c-notdefined', label: 'Not defined' },
  { cls: 'rv-c-identical', label: 'Identical to master' },
  { cls: 'rv-c-only', label: 'Single record' },
  { cls: 'rv-c-master', label: 'Master' },
  { cls: 'rv-c-override', label: 'Override without conflict' },
  { cls: 'rv-c-win', label: 'Conflict winner (what the game uses)' },
  { cls: 'rv-c-lose', label: 'Conflict loser' },
];

const SEV_ORDER: Record<string, number> = { none: 0, benign: 1, override: 2, conflict: 3, critical: 4 };

interface Menu { x: number; y: number; row: ConflictFieldRow; col: number; }
interface Edit { field: string; col: number; }

function TabBtn({ id, current, set, children, count }: {
  id: RecordTab; current: RecordTab; set: (t: RecordTab) => void;
  children: React.ReactNode; count?: number;
}) {
  return (
    <button className={`rv-tab ${current === id ? 'active' : ''}`} onClick={() => set(id)}>
      {children}
      {count !== undefined && count > 0 && <span className="rv-tab-count">{count}</span>}
    </button>
  );
}

export default function RecordView(
  { matrix, plugin, onReload, onOpenRecord, activeTab: externalTab, onTabChange, highlightField,
    onPluginsChanged }: {
    matrix: ConflictMatrix; plugin: string;
    onReload: () => Promise<void>;
    onOpenRecord?: (formKey: string, plugin: string) => void;
    activeTab?: RecordTab;
    onTabChange?: (t: RecordTab) => void;
    highlightField?: string;

    onPluginsChanged?: () => void;
  }
) {
  const [internalTab, setInternalTab] = useState<RecordTab>('grid');
  const recordTab = externalTab ?? internalTab;
  const setRecordTab = (t: RecordTab) => { setInternalTab(t); onTabChange?.(t); };

  const [filter, setFilter] = useState('');
  const [valueFilter, setValueFilter] = useState('');
  const [legendOpen, setLegendOpen] = useState(false);
  const [onlyDiffing, setOnlyDiffing] = useState(false);
  const [severityFilter, setSeverityFilter] = useState<SeverityFilter>('all');
  const [ov, setOv] = useState<Record<string, boolean>>({});
  const [menu, setMenu] = useState<Menu | null>(null);
  const [edit, setEdit] = useState<Edit | null>(null);
  const [editVal, setEditVal] = useState('');
  const [picker, setPicker] = useState<{ row: ConflictFieldRow; col: number } | null>(null);

  const { confirm: askConfirm, prompt: askPrompt, pickPlugin: askForTarget } = useDialogs();
  const [conditions, setConditions] = useState<{ plugin: string; path: string; label: string } | null>(null);
  const [status, setStatus] = useState('');
  const [editablePlugins, setEditablePlugins] = useState<string[]>([]);
  const [elementActions, setElementActions] = useState<ElementActions | null>(null);

  const [collapsedGroups, setCollapsedGroups] = useState<Record<number, boolean>>({});

  type ColumnWidthMode = 'standard' | 'fitAll' | 'fitText' | 'fitSmart';
  const [columnWidthMode, setColumnWidthMode] = useState<ColumnWidthMode>(
    () => (localStorage.getItem('rvColumnWidthMode') as ColumnWidthMode) || 'fitAll');
  const changeColumnWidthMode = (m: ColumnWidthMode) => {
    setColumnWidthMode(m);
    localStorage.setItem('rvColumnWidthMode', m);
  };

  const [flashedField, setFlashedField] = useState('');
  const flashTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const [drawerOpen, setDrawerOpen] = useState(true);
  const [refBy, setRefBy] = useState<RefByEntry[] | null>(null);
  const [problems, setProblems] = useState<RecordProblem[] | null>(null);
  const [deps, setDeps] = useState<Dependency[] | null>(null);
  const [history, setHistory] = useState<HistoryEntry[] | null>(null);

  const [scanErrors, setScanErrors] = useState<Record<string, string>>({});

  const [anchors, setAnchors] = useState<[string, string] | null>(null);
  const [compareOnly, setCompareOnly] = useState(false);

  const visibleCols = useMemo(() => {
    const all = matrix.Plugins.map((_, i) => i);
    if (!compareOnly || !anchors) return all;
    const picked = [matrix.Plugins.indexOf(anchors[0]), matrix.Plugins.indexOf(anchors[1])]
      .filter(i => i >= 0);
    return picked.length ? [...new Set(picked)].sort((a, b) => a - b) : all;
  }, [matrix.Plugins, compareOnly, anchors]);
  const visiblePlugins = useMemo(
    () => visibleCols.map(i => [matrix.Plugins[i], i] as const), [visibleCols, matrix.Plugins]);
  const [conflictLayout, setConflictLayout] = useState<ConflictLayout>(
    () => (localStorage.getItem('conflictLayout') as ConflictLayout) || 'cards');
  const changeLayout = (next: ConflictLayout) => {
    setConflictLayout(next);
    localStorage.setItem('conflictLayout', next);
  };

  useEffect(() => {
    const close = () => setMenu(null);
    window.addEventListener('click', close);
    window.addEventListener('scroll', close, true);
    return () => { window.removeEventListener('click', close); window.removeEventListener('scroll', close, true); };
  }, []);

  useEffect(() => {
    const b = back();
    if (!b) return;
    setRefBy(null); setProblems(null); setCollapsedGroups({});
    setDeps(null); setHistory(null); setAnchors(null);
    setScanErrors({});
    (async () => {

      const fail = (k: string, e: unknown) =>
        setScanErrors(prev => ({ ...prev, [k]: e instanceof Error ? e.message : String(e) }));
      try { setProblems(JSON.parse(await b.GetProblems(matrix.FormKey))); }
      catch (e) { setProblems([]); fail('problems', e); }
      try { setRefBy(JSON.parse(await b.GetReferencedBy(matrix.FormKey))); }
      catch (e) { setRefBy([]); fail('refBy', e); }
      try { setDeps(JSON.parse(await b.GetDependencies(matrix.FormKey))); }
      catch (e) { setDeps([]); fail('deps', e); }
      try { setHistory(JSON.parse(await b.GetHistory(matrix.FormKey))); }
      catch (e) { setHistory([]); fail('history', e); }
    })();
  }, [matrix.FormKey]);

  useEffect(() => {
    if (!highlightField) return;
    if (flashTimerRef.current) clearTimeout(flashTimerRef.current);
    setFlashedField(highlightField);
    setRecordTab('grid');
    flashTimerRef.current = setTimeout(() => setFlashedField(''), 1800);

  }, [highlightField]);

  const winnerIdx = useMemo(() => {
    const i = matrix.Plugins.lastIndexOf(matrix.Winner);
    return i >= 0 ? i : matrix.Plugins.length - 1;
  }, [matrix]);

  const isCollapsed = (r: ConflictFieldRow) => !(r.Field in ov ? ov[r.Field] : defaultExpanded(r));
  const toggle = (r: ConflictFieldRow) =>
    setOv(prev => ({ ...prev, [r.Field]: r.Field in prev ? !prev[r.Field] : !defaultExpanded(r) }));

  const f = filter.trim().toLowerCase();
  const vf = valueFilter.trim().toLowerCase();

  const filtering = f.length > 0 || vf.length > 0;

  const matched = useMemo(() => matrix.Rows.filter(r => {
    if (onlyDiffing && !r.Differs) return false;
    if (f && !(r.DisplayLabel || r.Field).toLowerCase().includes(f)) return false;
    if (vf && !r.Values.some(v => v.toLowerCase().includes(vf))) return false;
    if (severityFilter !== 'all' && !SEVERITY_SETS[severityFilter].has(r.Severity ?? 'none')) return false;
    return true;
  }), [matrix, f, vf, onlyDiffing, severityFilter]);

  const rows = useMemo(() => {
    if (filtering) return matched;
    const out: ConflictFieldRow[] = [];
    const collapsedLevels: number[] = [];
    for (const r of matched) {
      while (collapsedLevels.length && collapsedLevels[collapsedLevels.length - 1] >= r.Level) collapsedLevels.pop();
      if (collapsedLevels.length > 0) continue;
      out.push(r);
      if (r.HasChildren && isCollapsed(r)) collapsedLevels.push(r.Level);
    }
    return out;

  }, [matched, ov, filtering]);

  const isGenuineConflict = (r: ConflictFieldRow) => {
    if (!r.Differs || r.HasChildren) return false;
    const nonEmpty = r.Values.filter(v => v !== '' && v != null);
    if (nonEmpty.length < 2) return false;
    return !nonEmpty.every(v => v === nonEmpty[0]);
  };

  const conflictLeafRows = useMemo(
    () => matrix.Rows.filter(isGenuineConflict),

    [matrix.Rows]
  );

  const conflictGroups = useMemo(() => {
    type CGroup = { label: string; rows: ConflictFieldRow[]; severity: string };
    const groups: CGroup[] = [];
    let cur: CGroup | null = null;

    for (const r of matrix.Rows) {
      if (r.HasChildren && r.Level <= 1) {
        if (cur && cur.rows.length > 0) groups.push(cur);
        cur = { label: r.DisplayLabel || r.Field, rows: [], severity: 'none' };
      } else if (isGenuineConflict(r)) {
        if (!cur) cur = { label: 'General', rows: [], severity: 'none' };
        cur.rows.push(r);
        const sev = r.Severity ?? 'none';
        if ((SEV_ORDER[sev] ?? 0) > (SEV_ORDER[cur.severity] ?? 0)) cur.severity = sev;
      }
    }
    if (cur && cur.rows.length > 0) groups.push(cur);
    return groups;
  }, [matrix.Rows]);

  const startEdit = (r: ConflictFieldRow, col: number) => {
    setMenu(null);
    if (r.HasChildren) return;
    if (r.EditKind === 'Ref') { setPicker({ row: r, col }); return; }
    setEdit({ field: r.Field, col });
    setEditVal(r.Values[col] ?? '');
  };

  const commitEdit = async (r: ConflictFieldRow, col: number, value = editVal) => {
    setEdit(null);
    const b = back();
    if (!b) return;
    if (value === (r.Values[col] ?? '')) return;
    setStatus('Saving…');
    try {
      setStatus(await b.SetField(matrix.Plugins[col], matrix.FormKey, r.Field, value));
      await onReload();
    } catch (e: any) { setStatus('Edit failed: ' + (e?.message || e)); }
  };

  useEffect(() => {
    if (!menu) return;
    const b = back();
    if (!b) return;
    setElementActions(null);
    (async () => {
      try { setEditablePlugins(JSON.parse(await b.GetEditablePlugins())); } catch { setEditablePlugins([]); }
      try {
        setElementActions(JSON.parse(
          await b.DescribeElement(matrix.Plugins[menu.col], matrix.FormKey, menu.row.Field)));
      } catch { setElementActions(null); }
    })();

  }, [menu]);

  const copyAsOverrideInto = async (col: number) => {
    setMenu(null);
    const picked = await askForTarget({
      title: 'Copy as override into',
      description: `Copies ${matrix.EditorID || matrix.FormKey} from ${matrix.Plugins[col]} into the plugin you choose, as an override.`,
      confirmLabel: 'Copy', defaultTarget: editablePlugins[0] ?? '',
    });
    if (picked) await copyInto(col, picked.target);
  };

  const copyInto = async (col: number, target: string) => {
    setMenu(null);
    const b = back();
    if (!b) return;
    setStatus('Copying…');
    try {
      let msg = await b.CopyAsOverride(matrix.Plugins[col], matrix.FormKey, target, false);

      if (msg.startsWith('EXISTS:')) {
        const proceed = await askConfirm({
          title: 'Record already exists in target plugin',
          message: msg.slice('EXISTS:'.length).trim(),
          confirmLabel: 'Overwrite',
          danger: true,
        });
        if (!proceed) { setStatus('Copy cancelled -- the existing override was left alone.'); return; }
        msg = await b.CopyAsOverride(matrix.Plugins[col], matrix.FormKey, target, true);
      }
      setStatus(msg);

      await onReload();
      onPluginsChanged?.();
    }
    catch (e: any) { setStatus('Copy failed: ' + (e?.message || e)); }
  };

  const savePlugin = async (col: number) => {
    setMenu(null);
    const b = back();
    if (!b) return;
    setStatus('Saving plugin…');
    try { setStatus(await b.SavePlugin(matrix.Plugins[col], '')); }
    catch (e: any) { setStatus('Save failed: ' + (e?.message || e)); }
  };

  const deleteRecord = async (col: number) => {
    setMenu(null);
    const b = back();
    if (!b) return;
    const plug = matrix.Plugins[col];
    if (!await askConfirm({ title: 'Remove record', danger: true, confirmLabel: 'Remove',
      message: `Remove ${matrix.EditorID || matrix.FormKey} from ${plug}?` })) return;
    setStatus('Deleting…');
    try { setStatus(await b.DeleteRecord(plug, matrix.FormKey)); await onReload(); }
    catch (e: any) { setStatus('Delete failed: ' + (e?.message || e)); }
  };

  const renumberFormId = async (col: number) => {
    setMenu(null);
    const b = back();
    if (!b) return;
    const plug = matrix.Plugins[col];
    const cur = matrix.FormKey.split(':')[0];
    const next = await askPrompt({
      title: 'Change FormID',
      description: `Renumbers ${matrix.EditorID || matrix.FormKey} in ${plug}.`,
      label: 'New FormID (6-digit hex)', defaultValue: cur, placeholder: '001000',
      validate: v => /^[0-9a-fA-F]{1,6}$/.test(v.trim()) ? null : 'Six hex digits or fewer, e.g. 001000.',
    });
    if (!next) return;
    setStatus('Renumbering…');
    try { setStatus(await b.RenumberFormId(plug, matrix.FormKey, next)); await onReload(); }
    catch (e: any) { setStatus('Renumber failed: ' + (e?.message || e)); }
  };

  const compactEsl = async (col: number) => {
    setMenu(null);
    const b = back();
    if (!b) return;
    if (!await askConfirm({ title: 'Compact to ESL', danger: true, confirmLabel: 'Compact',
      message: `Compact ${matrix.Plugins[col]} to ESL?\n\nThis renumbers every record's FormID into the 0x800-0xFFF range and can break references from other plugins.` })) return;
    setStatus('Compacting…');
    try { setStatus(await b.CompactToEsl(matrix.Plugins[col])); await onReload(); }
    catch (e: any) { setStatus('Compact failed: ' + (e?.message || e)); }
  };

  const cleanPlugin = async (col: number) => {
    setMenu(null);
    const b = back();
    if (!b) return;
    if (!await askConfirm({ title: 'Clean (UDR)', confirmLabel: 'Clean',
      message: `Clean (UDR) ${matrix.Plugins[col]}?\n\nThis undeletes and disables every deleted record in the plugin.` })) return;
    setStatus('Cleaning…');
    try { setStatus(await b.CleanPlugin(matrix.Plugins[col])); await onReload(); }
    catch (e: any) { setStatus('Clean failed: ' + (e?.message || e)); }
  };

  const copyAsNewRecord = async (col: number) => {
    setMenu(null);
    const b = back();
    if (!b) return;
    const picked = await askForTarget({
      title: 'Copy as new record',
      description: `Duplicates ${matrix.EditorID || matrix.FormKey} under a fresh FormID in the plugin you choose.`,
      confirmLabel: 'Duplicate',
      defaultTarget: editablePlugins[0] ?? '',
      extraField: {
        label: 'EditorID for the new record',
        defaultValue: (matrix.EditorID || 'NewRecord') + 'DUP',
        placeholder: 'MyNewRecord',
      },
    });
    if (!picked) return;
    const { target, extra: edid } = picked;
    setStatus('Duplicating…');
    try { setStatus(await b.CopyAsNewRecord(matrix.Plugins[col], matrix.FormKey, target, edid)); await onReload(); }
    catch (e: any) { setStatus('Copy as new record failed: ' + (e?.message || e)); }
  };

  const deepCopyAsOverride = async (col: number) => {
    setMenu(null);
    const b = back();
    if (!b) return;
    if (matrix.Type === 'Cell' || matrix.Type === 'Worldspace') {
      setStatus(`${matrix.Type} does not support deep copy here -- copy it with Copy as Override and its placed objects individually.`);
      return;
    }
    const picked = await askForTarget({
      title: 'Deep copy as override',
      description: 'Copies the record and the content it references into the plugin you choose. You get a dry run to confirm first.',
      confirmLabel: 'Dry run',
      defaultTarget: editablePlugins[0] ?? 'ConflictPatch.esp',
    });
    if (!picked) return;
    const target = picked.target;
    setStatus('Scanning references…');
    try {
      const preview = await b.DeepCopyAsOverride(matrix.Plugins[col], matrix.FormKey, target, false, false);
      setStatus(preview);
      if (!preview.startsWith('DRY RUN')) return;
      if (!await askConfirm({ title: 'Confirm', message: preview, confirmLabel: 'Proceed' })) return;
      let result = await b.DeepCopyAsOverride(matrix.Plugins[col], matrix.FormKey, target, true, false);
      if (result.startsWith('EXISTS:')) {
        const proceed = await askConfirm({
          title: 'Deep copy would replace existing overrides',
          message: result.slice('EXISTS:'.length).trim(),
          confirmLabel: 'Overwrite all and copy',
          danger: true,
        });
        if (!proceed) {
          setStatus('Deep copy cancelled -- the target plugin was left unchanged.');
          return;
        }
        result = await b.DeepCopyAsOverride(matrix.Plugins[col], matrix.FormKey, target, true, true);
      }
      setStatus(result);
      await onReload();
    } catch (e: any) { setStatus('Deep copy failed: ' + (e?.message || e)); }
  };

  const changeReferencingRecords = async () => {
    setMenu(null);
    const b = back();
    if (!b) return;
    const to = await askPrompt({
      title: 'Change referencing records',
      description: `Every record that references ${matrix.EditorID || matrix.FormKey} will point at this instead.`,
      label: 'Replacement record (FormKey or EditorID)', placeholder: '000800:MyPlugin.esp',
      validate: v => v.trim().length > 0 ? null : 'Enter a FormKey or EditorID.',
    });
    if (!to) return;
    const targetPick = await askForTarget({
      title: 'Write repointed records into',
      description: 'The rewritten records are written here as overrides.',
      confirmLabel: 'Dry run', defaultTarget: editablePlugins[0] ?? 'ConflictPatch.esp',
    });
    const target = targetPick?.target ?? '';
    if (!target) return;
    setStatus('Finding referencing records…');
    try {
      const preview = await b.ChangeReferencingRecords(matrix.FormKey, to, target, false);
      setStatus(preview);
      if (!preview.startsWith('DRY RUN')) return;
      if (!await askConfirm({ title: 'Confirm', message: preview, confirmLabel: 'Proceed' })) return;
      setStatus(await b.ChangeReferencingRecords(matrix.FormKey, to, target, true));
      await onReload();
    } catch (e: any) { setStatus('Change referencing records failed: ' + (e?.message || e)); }
  };

  const removeItm = async (col: number) => {
    setMenu(null);
    const b = back();
    if (!b) return;
    const plug = matrix.Plugins[col];
    setStatus('Scanning for identical-to-master records…');
    try {
      const preview = await b.RemoveIdenticalToMaster(plug, false);
      setStatus(preview);
      if (!preview.startsWith('DRY RUN')) return;
      if (!await askConfirm({ title: 'Remove from selected records', danger: true, confirmLabel: 'Remove',
        message: `${preview}\n\nRemove them from ${plug}?` })) return;
      setStatus(await b.RemoveIdenticalToMaster(plug, true));
      await onReload();
    } catch (e: any) { setStatus('Remove ITM failed: ' + (e?.message || e)); }
  };

  const addMasters = async (col: number) => {
    setMenu(null);
    const b = back();
    if (!b) return;
    const plug = matrix.Plugins[col];
    const list = await askPrompt({
      title: 'Add masters',
      description: `Declares master files on ${plug}.`,
      label: 'Plugin filenames, comma separated', placeholder: 'Fallout4.esm, MyMod.esp',
      validate: v => v.trim().length > 0 ? null : 'Enter at least one plugin filename.',
    });
    if (!list) return;
    try { setStatus(await b.AddMasters(plug, list)); await onReload(); }
    catch (e: any) { setStatus('Add masters failed: ' + (e?.message || e)); }
  };

  const renumberPlugin = async (col: number) => {
    setMenu(null);
    const b = back();
    if (!b) return;
    const plug = matrix.Plugins[col];
    const start = await askPrompt({
      title: 'Renumber FormIDs',
      description: `Renumbers every record in ${plug}. You get a dry run to confirm first.`,
      label: 'Starting object id (hex)', defaultValue: '001000',
      validate: v => /^[0-9a-fA-F]{1,6}$/.test(v.trim()) ? null : 'Six hex digits or fewer, e.g. 001000.',
    });
    if (!start) return;
    try {
      const preview = await b.RenumberPluginFormIds(plug, start, false);
      setStatus(preview);
      if (!preview.startsWith('DRY RUN')) return;
      if (!await askConfirm({ title: 'Confirm', message: preview, confirmLabel: 'Proceed' })) return;
      setStatus(await b.RenumberPluginFormIds(plug, start, true));
      await onReload();
    } catch (e: any) { setStatus('Renumber failed: ' + (e?.message || e)); }
  };

  const createSeq = async (col: number) => {
    setMenu(null);
    const b = back();
    if (!b) return;
    try { setStatus(await b.CreateSeqFile(matrix.Plugins[col], '')); }
    catch (e: any) { setStatus('Create SEQ failed: ' + (e?.message || e)); }
  };

  const checkCircularLeveled = async (col: number) => {
    setMenu(null);
    const b = back();
    if (!b) return;
    setStatus('Checking leveled lists…');
    try { setStatus(await b.CheckCircularLeveledLists(matrix.Plugins[col])); }
    catch (e: any) { setStatus('Check failed: ' + (e?.message || e)); }
  };

  const copyText = (what: string, text: string) => {
    setMenu(null);
    void navigator.clipboard?.writeText(text);
    setStatus(`Copied ${what}: ${text}`);
  };

  const conditionsPathOf = (field: string) => (/(^|\.)Conditions(\[|$)/.test(field) ? field : null);

  const elementAction = async (
    kind: 'add' | 'remove' | 'up' | 'down' | 'clear',
    r: ConflictFieldRow, col: number, template = '',
  ) => {
    setMenu(null);
    const b = back();
    if (!b) return;
    const plug = matrix.Plugins[col];
    try {
      let msg: string;
      switch (kind) {
        case 'add':    msg = await b.AddElement(plug, matrix.FormKey, r.Field, template); break;
        case 'remove': msg = await b.RemoveElement(plug, matrix.FormKey, r.Field); break;
        case 'up':     msg = await b.MoveElement(plug, matrix.FormKey, r.Field, -1); break;
        case 'down':   msg = await b.MoveElement(plug, matrix.FormKey, r.Field, 1); break;
        case 'clear':
          if (!await askConfirm({ title: 'Clear list', danger: true, confirmLabel: 'Clear',
            message: `Clear every entry from ${r.DisplayLabel || r.Field} in ${plug}?` })) return;
          msg = await b.ClearElement(plug, matrix.FormKey, r.Field); break;
      }
      setStatus(msg);
      await onReload();
    } catch (e: any) { setStatus('Failed: ' + (e?.message || e)); }
  };

  const winnerCol = Math.max(0, matrix.Plugins.lastIndexOf(matrix.Winner));
  const headerActions = useMemo(() => buildActions({
    copyAsOverride: () => {
      void (async () => {
        const picked = await askForTarget({
          title: 'Copy as override',
          description: `Copies ${matrix.EditorID || matrix.FormKey} from ${matrix.Plugins[winnerCol]} into the plugin you choose, as an override.`,
          confirmLabel: 'Copy',
          defaultTarget: editablePlugins[0] ?? '',
        });
        if (picked) await copyInto(winnerCol, picked.target);
      })();
    },
    changeFormId: () => void renumberFormId(winnerCol),
    compactToEsl: () => void compactEsl(winnerCol),
    cleanUdr: () => void cleanPlugin(winnerCol),
    deleteRecord: () => void deleteRecord(winnerCol),

  }), [matrix.FormKey, matrix.Winner, editablePlugins]);

  const askImpact = () => {
    setMenu(null);
    const id = matrix.EditorID || matrix.FormKey;
    const prompt =
      `What breaks if I change or renumber the record ${id} [${matrix.FormKey}] (${matrix.Type})? ` +
      `Use get_referenced_by on ${matrix.FormKey} to list every record and plugin that references it, ` +
      `then tell me which would break and how to patch them.`;
    window.dispatchEvent(new CustomEvent('fo4:ask-ai', { detail: prompt }));
    setStatus('Asked Claude about impact. See the chat panel.');
  };

  const renderEditor = (r: ConflictFieldRow, ci: number) => {
    if (r.EditKind === 'Bool') {
      return (
        <select className="rv-edit-input" autoFocus value={editVal}
          onChange={e => commitEdit(r, ci, e.target.value)} onBlur={() => setEdit(null)}>
          <option value="True">True</option>
          <option value="False">False</option>
        </select>
      );
    }
    if (r.EditKind === 'Enum' && r.EnumOptions && r.EnumOptions.length > 0) {
      return (
        <select className="rv-edit-input" autoFocus value={editVal}
          onChange={e => commitEdit(r, ci, e.target.value)} onBlur={() => setEdit(null)}>
          {r.EnumOptions.map(o => <option key={o} value={o}>{o}</option>)}
        </select>
      );
    }
    return (
      <input className="rv-edit-input" autoFocus value={editVal}
        onChange={e => setEditVal(e.target.value)}
        onBlur={() => commitEdit(r, ci)}
        onKeyDown={e => {
          if (e.key === 'Enter') { e.preventDefault(); commitEdit(r, ci); }
          if (e.key === 'Escape') { e.preventDefault(); setEdit(null); }
        }} />
    );
  };

  const renderGrid = (gridRows: ConflictFieldRow[]) => (
    <table
      className={`rv-grid rv-cw-${columnWidthMode}`}
      style={columnWidthMode === 'standard' ? { minWidth: 320 + visibleCols.length * 240 } : undefined}
    >
      <thead>
        <tr>
          <th className="rv-prop-col rv-sticky">Record Property</th>
          {visiblePlugins.map(([p, i]) => (
            <th key={p + i} className={i === winnerIdx ? 'rv-winner-col' : ''}>
              <span className="rv-col-name">{p}</span>
              {i === winnerIdx && matrix.Plugins.length > 1 && <span className="rv-winner-badge">WINNER</span>}
            </th>
          ))}
        </tr>
      </thead>
      <tbody>
        {gridRows.length === 0 ? (
          <tr><td className="rv-empty" colSpan={visibleCols.length + 1}>No fields match.</td></tr>
        ) : gridRows.map((r, ri) => (
          <tr key={ri} className={[`rv-s-${r.Severity ?? 'none'}`, r.Differs ? 'rv-diff' : '', flashedField && r.Field === flashedField ? 'rv-row-flash' : ''].filter(Boolean).join(' ')}>
            <td
              className={`rv-prop-col rv-sticky ${r.HasChildren ? 'rv-expandable' : ''}`}
              onDoubleClick={() => r.HasChildren && toggle(r)}
              onContextMenu={e => { e.preventDefault(); e.stopPropagation(); setMenu({ x: e.clientX, y: e.clientY, row: r, col: winnerCol }); }}
            >
              <span className="rv-prop-inner" style={{ paddingLeft: r.Level * 14 }}>
                {r.HasChildren ? (
                  <span className="rv-expand" onClick={e => { e.stopPropagation(); toggle(r); }}>
                    {isCollapsed(r) ? <ChevronRight size={12} /> : <ChevronDown size={12} />}
                  </span>
                ) : <span className="rv-expand-spacer" />}
                {r.DisplayLabel || r.Field}
              </span>
            </td>
            {visibleCols.map(ci => {
              const editing = edit && edit.field === r.Field && edit.col === ci;
              const st = resolveStatus(r, ci);
              return (
                <td
                  key={ci}
                  className={`rv-cell rv-c-${st}`}
                  onDoubleClick={() => !r.HasChildren && startEdit(r, ci)}
                  onContextMenu={e => { e.preventDefault(); e.stopPropagation(); setMenu({ x: e.clientX, y: e.clientY, row: r, col: ci }); }}
                >
                  {st === 'win' && <Crown size={10} className="rv-win-icon" />}
                  {editing ? renderEditor(r, ci) : r.Values[ci]}
                </td>
              );
            })}
          </tr>
        ))}
      </tbody>
    </table>
  );

  const problemCount = problems?.length ?? 0;
  const refByCount = refBy?.length ?? 0;
  const conflictCount = conflictLeafRows.length;

  return (
    <div className="rv-container">

      {}
      <div className="rv-tab-bar">
        <TabBtn id="grid" current={recordTab} set={setRecordTab}>Record View</TabBtn>
        <TabBtn id="fields" current={recordTab} set={setRecordTab} count={matrix.Rows.length}>Field View</TabBtn>
        <TabBtn id="conflicts" current={recordTab} set={setRecordTab} count={conflictCount}>Conflicts</TabBtn>
        <TabBtn id="references" current={recordTab} set={setRecordTab} count={refByCount}>
          <Link2 size={12} /> References
        </TabBtn>
        <TabBtn id="history" current={recordTab} set={setRecordTab}>
          <Clock size={12} /> History
        </TabBtn>
        <TabBtn id="dependencies" current={recordTab} set={setRecordTab}>
          <Network size={12} /> Dependencies
        </TabBtn>
      </div>

      {}
      <WorkspaceHeader
        matrix={matrix}
        anchors={anchors}
        onAnchorsChange={setAnchors}
        compareOnly={compareOnly}
        onCompareOnlyChange={setCompareOnly}
        showCompare={recordTab === 'conflicts'}
        actions={headerActions}
        onOpenRecord={(fk, pl) => onOpenRecord?.(fk, pl)}
      />

      {}
      {recordTab === 'grid' && (
        <>
          <div className="rv-toolbar">
            <span className="rv-tb-label">Filter by name</span>
            <div className="rv-filter">
              <input value={filter} onChange={e => setFilter(e.target.value)} placeholder="field name…" />
            </div>
            <span className="rv-tb-label">by value</span>
            <div className="rv-filter">
              <input value={valueFilter} onChange={e => setValueFilter(e.target.value)} placeholder="any value…" />
            </div>
            <button className={`rv-toggle-btn ${onlyDiffing ? 'on' : ''}`} onClick={() => setOnlyDiffing(v => !v)}>
              <EyeOff size={12} /> Only conflicts
            </button>
            <select
              className="rv-status-filter"
              value={severityFilter}
              onChange={e => setSeverityFilter(e.target.value as SeverityFilter)}
              title="Filter rows by conflict severity"
            >
              <option value="all">All rows</option>
              <option value="override">Conflicts (override+)</option>
              <option value="conflict">Real conflicts (conflict+)</option>
              <option value="critical">Critical only</option>
            </select>
            <select
              className="rv-status-filter"
              value={columnWidthMode}
              onChange={e => changeColumnWidthMode(e.target.value as ColumnWidthMode)}
              title="How plugin columns share the available width"
            >
              <option value="standard">Column width: Standard</option>
              <option value="fitAll">Column width: Fit All</option>
              <option value="fitText">Column width: Fit Text</option>
              <option value="fitSmart">Column width: Fit Smart</option>
            </select>
            {(() => {
              const badge = LEVEL_BADGES[matrix.Level];
              return badge ? <span className={`rv-lvl-badge ${badge.cls}`}>{badge.label}</span> : null;
            })()}
            <span className="rv-tb-src" title={`Opened from ${plugin}`}>{plugin}</span>
            {status && <span className="rv-status">{status}</span>}
            <button
              className="rv-legend-btn"
              onClick={() => setLegendOpen(o => !o)}
              title="What the colours mean"
            >
              <HelpCircle size={12} /> Legend
            </button>
            {legendOpen && (
              <div className="rv-legend">
                <h4>Row: record state</h4>
                {LEGEND_ROWS.map(l => (
                  <div key={l.cls} className="rv-legend-row">
                    <span className={`rv-legend-swatch ${l.cls}`} />
                    <span>{l.label}</span>
                  </div>
                ))}
                <h4>Value: per plugin</h4>
                {LEGEND_CELLS.map(l => (
                  <div key={l.cls} className="rv-legend-row">
                    <span className="rv-legend-swatch" />
                    <span className={l.cls}>{l.label}</span>
                  </div>
                ))}
              </div>
            )}
          </div>
          <div className="rv-grid-scroll">
            {renderGrid(rows)}
          </div>
        </>
      )}

      {}
      {recordTab === 'conflicts' && (
        <ConflictsView
          matrix={matrix}
          rows={conflictLeafRows}
          layout={conflictLayout}
          onLayoutChange={changeLayout}
          anchors={anchors}
          matrixView={
            <div className="rv-conflicts-tab">
              {}
              <div className="rv-conflict-groups">
                <table className="rv-grid rv-cg-table" style={{ minWidth: 280 + visibleCols.length * 200 }}>
                  <thead>
                    <tr>
                      <th className="rv-prop-col rv-sticky">Field</th>
                      {visiblePlugins.map(([p, i]) => (
                        <th key={p + i} className={i === winnerIdx ? 'rv-winner-col' : ''}>
                          <span className="rv-col-name" title={p}>{p}</span>
                          {i === winnerIdx && matrix.Plugins.length > 1 && <span className="rv-winner-badge">WINNER</span>}
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {conflictGroups.map((g, gi) => {
                      const collapsed = !!collapsedGroups[gi];
                      const colCount = visibleCols.length + 1;
                      return (
                        <Fragment key={gi}>
                          <tr
                            className={`rv-cg-sep rv-cg-sep-sev-${g.severity}`}
                            onClick={() => setCollapsedGroups(prev => ({ ...prev, [gi]: !prev[gi] }))}
                          >
                            <td colSpan={colCount}>
                              <div className="rv-sep-inner">
                                <span className="rv-sep-name">{g.label}</span>
                                <span className={`rv-sep-badge rv-cg-sev-badge-${g.severity}`}>
                                  {g.rows.length} conflict{g.rows.length !== 1 ? 's' : ''}
                                </span>
                                <ChevronRight size={12} className={`rv-sep-chevron${collapsed ? '' : ' rv-sep-chevron-open'}`} />
                              </div>
                            </td>
                          </tr>
                          {!collapsed && g.rows.map((r, ri) => (
                            <tr key={ri} className={[`rv-s-${r.Severity ?? 'none'}`, 'rv-diff', flashedField && r.Field === flashedField ? 'rv-row-flash' : ''].filter(Boolean).join(' ')}>
                              <td
                                className="rv-prop-col rv-sticky"
                                onContextMenu={e => { e.preventDefault(); e.stopPropagation(); setMenu({ x: e.clientX, y: e.clientY, row: r, col: winnerCol }); }}
                              >
                                <span className="rv-prop-inner">{r.DisplayLabel || r.Field}</span>
                              </td>
                              {visibleCols.map(ci => {
                                const st = resolveStatus(r, ci);
                                return (
                                  <td
                                    key={ci}
                                    className={`rv-cell rv-c-${st}`}
                                    onDoubleClick={() => startEdit(r, ci)}
                                    onContextMenu={e => { e.preventDefault(); e.stopPropagation(); setMenu({ x: e.clientX, y: e.clientY, row: r, col: ci }); }}
                                  >
                                    {st === 'win' && <Crown size={10} className="rv-win-icon" />}
                                    {r.Values[ci]}
                                  </td>
                                );
                              })}
                            </tr>
                          ))}
                        </Fragment>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            </div>
          }
        />
      )}

      {}
      {recordTab === 'references' && (
        <div className="rv-references-tab">
          {refBy === null ? (
            <div className="rv-tab-loading">Scanning load order for references…</div>
          ) : scanErrors.refBy ? (
            <div className="rv-tab-empty" style={{ color: '#f44747' }}>
              The reference scan failed, so this is NOT a list of nothing -- it is no answer at all.
              Do not treat this record as unreferenced. <br />{scanErrors.refBy}
            </div>
          ) : refBy.length === 0 ? (
            <div className="rv-tab-empty">Nothing in the load order references this record.</div>
          ) : (
            <>
              <div className="rv-ref-header">{refBy.length} record{refBy.length !== 1 ? 's' : ''} reference this</div>
              <div className="rv-ref-list">
                {refBy.map((e, i) => (
                  <div
                    key={i}
                    className={`rv-ref-row ${onOpenRecord ? 'rv-ref-clickable' : ''}`}
                    onClick={() => onOpenRecord?.(e.FormKey, e.Plugin)}
                    title={onOpenRecord ? `Open ${e.EditorID || e.FormKey}` : undefined}
                  >
                    <span className="rv-ref-type">{e.Type}</span>
                    <span className="rv-ref-id">{e.EditorID || e.FormKey}</span>
                    <span className="rv-ref-plugin">{e.Plugin}</span>
                  </div>
                ))}
              </div>
            </>
          )}
        </div>
      )}

      {}
      {recordTab === 'fields' && (
        <div className="rv-list-tab">
          <div className="rv-tab-note">
            Every field of the winning version as a flat list, without the tree. Useful for reading
            or copying a whole record at once; edit from Record View.
          </div>
          <table className="rv-flat-table">
            <thead>
              <tr><th>Field</th><th>Value</th><th>Kind</th><th>Group</th></tr>
            </thead>
            <tbody>
              {matrix.Rows.map(r => (
                <tr key={r.Field} className={r.Differs ? 'rv-row-diff' : ''}>
                  <td className="rv-mono" title={r.Field}>{r.DisplayLabel || r.Field}</td>
                  <td>{r.Values[winnerIdx] ?? <em>not set</em>}</td>
                  <td className="rv-dim">{r.Kind ?? 'Value'}</td>
                  <td className="rv-dim">{r.GroupLabel || '-'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {}
      {recordTab === 'history' && (
        <div className="rv-list-tab">
          {history === null ? (
            <div className="rv-tab-loading">Reading the override chain…</div>
          ) : history.length === 0 ? (
            <div className="rv-tab-empty">No plugin carries this record.</div>
          ) : (
            <>
              {
}
              <div className="rv-tab-note">
                Override chain in load order. Plugin files carry no edit history, so this is who
                touched the record, not when it was changed.
              </div>
              <table className="rv-flat-table">
                <thead>
                  <tr><th>#</th><th>Plugin</th><th>Action</th><th>Changed fields</th><th>File modified</th></tr>
                </thead>
                <tbody>
                  {history.map(h => (
                    <tr key={`${h.LoadOrder}:${h.Plugin}`}>
                      <td className="rv-num">{h.LoadOrder}</td>
                      <td>{h.Plugin}</td>
                      <td><span className={`rv-tag rv-tag-${h.Action.split(' ')[0]}`}>{h.Action}</span></td>
                      <td className="rv-num">{h.ChangedFields}</td>
                      <td className="rv-dim">{h.LastModified || '-'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </>
          )}
        </div>
      )}

      {}
      {recordTab === 'dependencies' && (
        <div className="rv-list-tab">
          {deps === null ? (
            <div className="rv-tab-loading">Resolving outgoing references…</div>
          ) : scanErrors.deps ? (
            <div className="rv-tab-empty" style={{ color: '#f44747' }}>
              The dependency scan failed, so this is no answer rather than an empty one.<br />{scanErrors.deps}
            </div>
          ) : deps.length === 0 ? (
            <div className="rv-tab-empty">This record does not reference anything.</div>
          ) : (
            <>
              <div className="rv-tab-note">
                What the winning version needs in order to load. A row marked missing is a dangling
                FormLink, which is a crash risk rather than a cosmetic problem.
              </div>
              <table className="rv-flat-table">
                <thead>
                  <tr><th>FormID</th><th>Editor ID</th><th>Type</th><th>Provided by</th></tr>
                </thead>
                <tbody>
                  {deps.map(d => (
                    <tr
                      key={d.FormKey}
                      className={d.Kind === 'missing' ? 'rv-row-bad' : 'rv-row-link'}
                      onClick={() => d.Kind === 'link' && onOpenRecord?.(d.FormKey, d.Plugin)}
                    >
                      <td className="rv-mono">{d.FormKey}</td>
                      <td>{d.EditorId || <em>unnamed</em>}</td>
                      <td>{d.Type || '-'}</td>
                      <td>
                        {d.Kind === 'missing'
                          ? <span className="rv-tag rv-tag-missing">unresolved</span>
                          : d.Plugin}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </>
          )}
        </div>
      )}

      {}
      <div className={`rv-drawer ${drawerOpen ? 'open' : 'closed'}`}>
        <div className="rv-drawer-tabs">
          <button className="on">
            <AlertTriangle size={12} /> Problems
            {problemCount > 0 && <span className="rv-prob-badge">{problemCount}</span>}
          </button>
          <button className="rv-drawer-collapse" onClick={() => setDrawerOpen(o => !o)} title={drawerOpen ? 'Collapse' : 'Expand'}>
            {drawerOpen ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
          </button>
        </div>
        {drawerOpen && (
          <div className="rv-drawer-body">
            {problems === null ? <div className="rv-drawer-note">Checking…</div>
            : scanErrors.problems ? <div className="rv-drawer-note" style={{ color: '#f44747' }}>
                Problem check failed (not "no problems"): {scanErrors.problems}</div>
            : problems.length === 0 ? <div className="rv-drawer-note">No problems found.</div>
            : problems.map((p, i) => (
              <div key={i} className="rv-prob-row">
                <AlertTriangle size={12} className="rv-prob-icon" />
                <span>{p.Description}</span>
              </div>
            ))}
          </div>
        )}
      </div>

      {}
      {menu && (
        <div className="rv-context" style={{ left: menu.x, top: menu.y }} onClick={e => e.stopPropagation()}>
          {!menu.row.HasChildren && (
            <button onClick={() => startEdit(menu.row, menu.col)}><Pencil size={13} /> Edit value <span className="rv-key">F2</span></button>
          )}

          {}
          {conditionsPathOf(menu.row.Field) && (
            <button onClick={() => {
              const plug = matrix.Plugins[menu.col];
              const p = conditionsPathOf(menu.row.Field)!;
              setMenu(null);
              setConditions({ plugin: plug, path: p, label: matrix.EditorID || matrix.FormKey });
            }}><ListFilter size={13} /> Edit conditions…</button>
          )}
          {
}
          {elementActions?.canAdd && elementActions.templates.length === 1 && (
            <button onClick={() => elementAction('add', menu.row, menu.col, elementActions.templates[0])}>
              <Plus size={13} /> Add "{elementActions.templates[0]}"
            </button>
          )}
          {elementActions?.canAdd && elementActions.templates.length > 1 && (
            <>
              <div className="rv-context-label">Add</div>
              {elementActions.templates.map(t => (
                <button key={t} onClick={() => elementAction('add', menu.row, menu.col, t)}>
                  <Plus size={13} /> {t}
                </button>
              ))}
            </>
          )}
          {elementActions?.canRemove && (
            <button className="rv-danger" onClick={() => elementAction('remove', menu.row, menu.col)}>
              <Minus size={13} /> Remove
            </button>
          )}
          {(elementActions?.canMoveUp || elementActions?.canMoveDown) && (
            <>
              <button disabled={!elementActions?.canMoveUp}
                onClick={() => elementAction('up', menu.row, menu.col)}><ArrowUp size={13} /> Move up</button>
              <button disabled={!elementActions?.canMoveDown}
                onClick={() => elementAction('down', menu.row, menu.col)}><ArrowDown size={13} /> Move down</button>
            </>
          )}
          {elementActions?.canClear && (
            <button className="rv-danger" onClick={() => elementAction('clear', menu.row, menu.col)}>
              <EraserIcon size={13} /> Clear ({elementActions.count})
            </button>
          )}

          <div className="rv-context-sep" />
          {}
          <button onClick={() => copyAsOverrideInto(menu.col)}><Copy size={13} /> Copy as override into...</button>
          <button onClick={() => copyAsNewRecord(menu.col)}><FilePlus2 size={13} /> Copy as new record into...</button>
          <button onClick={() => deepCopyAsOverride(menu.col)}><FilePlus2 size={13} /> Deep copy as override into...</button>
          <div className="rv-context-sep" />
          <button onClick={() => { setMenu(null); setRecordTab('conflicts'); }}>
            <GitCompareIcon size={13} /> Compare...
          </button>
          <button onClick={() => { setMenu(null); setRecordTab('references'); }}>
            <Link2 size={13} /> Referenced By
          </button>
          <button onClick={() => changeReferencingRecords()}><Link2 size={13} /> Change referencing records...</button>
          <div className="rv-context-sep" />
          <div className="rv-context-label">Copy to clipboard</div>
          <button onClick={() => copyText('path', menu.row.Field)}><ClipboardCopy size={13} /> Path</button>
          <button onClick={() => copyText('value', menu.row.Values[menu.col] ?? '')}><ClipboardCopy size={13} /> Value</button>
          <button onClick={() => copyText('FormKey', matrix.FormKey)}><ClipboardCopy size={13} /> FormKey</button>
          <button onClick={() => copyText('signature', matrix.Type)}><ClipboardCopy size={13} /> Signature</button>
          <div className="rv-context-sep" />
          <button onClick={() => savePlugin(menu.col)}><Save size={13} /> Save "{matrix.Plugins[menu.col]}"</button>
          <button className="rv-danger" onClick={() => deleteRecord(menu.col)}
            title={`Remove this record from ${matrix.Plugins[menu.col]}`}><Trash2 size={13} /> Remove</button>
          <div className="rv-context-sep" />
          <div className="rv-context-label">Plugin: {matrix.Plugins[menu.col]}</div>
          <button onClick={() => renumberFormId(menu.col)}><Hash size={13} /> Change FormID...</button>
          <button onClick={() => compactEsl(menu.col)}><Minimize2 size={13} /> Compact to ESL</button>
          <button onClick={() => cleanPlugin(menu.col)}><Eraser size={13} /> Clean (UDR)</button>
          <button onClick={() => removeItm(menu.col)}><Eraser size={13} /> Remove identical to master...</button>
          <button onClick={() => addMasters(menu.col)}><Plus size={13} /> Add masters...</button>
          <button onClick={() => renumberPlugin(menu.col)}><Hash size={13} /> Renumber all FormIDs...</button>
          <button onClick={() => createSeq(menu.col)}><FilePlus2 size={13} /> Create SEQ file</button>
          <button onClick={() => checkCircularLeveled(menu.col)}><ListFilter size={13} /> Check circular leveled lists</button>
          <div className="rv-context-sep" />
          <button onClick={askImpact}><Brain size={13} /> Ask AI: what breaks?</button>
        </div>
      )}

      {}
      {picker && (
        <RecordPicker
          title={picker.row.DisplayLabel}
          refType={picker.row.RefType}
          refTypes={picker.row.RefTypes}
          onPick={fk => { const p = picker; setPicker(null); commitEdit(p.row, p.col, fk); }}
          onClose={() => setPicker(null)}
        />
      )}

      {}
      {conditions && (
        <ConditionsEditor
          plugin={conditions.plugin}
          record={matrix.FormKey}
          path={conditions.path}
          label={conditions.label}
          onClose={() => setConditions(null)}
          onSaved={async msg => { setStatus(msg); await onReload(); }}
        />
      )}
    </div>
  );
}
