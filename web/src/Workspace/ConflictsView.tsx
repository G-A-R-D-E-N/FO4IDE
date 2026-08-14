import { useMemo, useState } from 'react';
import {
  AlertTriangle, ChevronDown, ChevronRight, CheckCircle2, Crown, LayoutGrid, Rows3,
} from 'lucide-react';
import type { ConflictFieldRow, ConflictMatrix } from '../backend';
import { pluginColorVar } from '../util/pluginColor';
import PluginChip from './PluginChip';
import './ConflictsView.css';

export type ConflictKind = 'All' | 'Value' | 'Flag' | 'FormID';
export type ConflictLayout = 'cards' | 'matrix';

interface ConflictsViewProps {
  matrix: ConflictMatrix;
  /** Only rows that actually differ; the caller already has this list for its own counts. */
  rows: ConflictFieldRow[];
  layout: ConflictLayout;
  onLayoutChange: (next: ConflictLayout) => void;
  /** Rendered when layout is 'matrix' -- the existing xEdit-style grid. */
  matrixView: React.ReactNode;
  anchors: [string, string] | null;
}

const KINDS: ConflictKind[] = ['All', 'Value', 'Flag', 'FormID'];
const KIND_LABEL: Record<ConflictKind, string> = {
  All: 'All Conflicts', Value: 'Values', Flag: 'Flags', FormID: 'FormIDs',
};

/**
 * Variant B: one card per conflicting field, base value beside the winning value, with the
 * plugins that also touch it listed alongside.
 *
 * The matrix answers "what does every plugin say"; the cards answer "what changed and who did it",
 * which is the question you have when you are deciding whether an override is wanted. The matrix is
 * still one click away for the times the full column set matters.
 */
export default function ConflictsView({
  matrix, rows, layout, onLayoutChange, matrixView, anchors,
}: ConflictsViewProps) {
  const [kind, setKind] = useState<ConflictKind>('All');
  const [collapsed, setCollapsed] = useState<Set<string>>(new Set());

  const counts = useMemo(() => {
    const c: Record<ConflictKind, number> = { All: rows.length, Value: 0, Flag: 0, FormID: 0 };
    for (const r of rows) c[(r.Kind ?? 'Value') as Exclude<ConflictKind, 'All'>]++;
    return c;
  }, [rows]);

  const shown = useMemo(
    () => (kind === 'All' ? rows : rows.filter(r => (r.Kind ?? 'Value') === kind)),
    [rows, kind]);

  // Group by the subrecord each row hangs off, preserving the order rows arrive in so the cards
  // read in the same sequence as the grid.
  const groups = useMemo(() => {
    const out: { key: string; label: string; rows: ConflictFieldRow[] }[] = [];
    const index = new Map<string, number>();
    for (const r of shown) {
      const key = r.Group || '';
      const label = r.GroupLabel || (key ? key : 'Record');
      let at = index.get(key);
      if (at === undefined) { at = out.length; index.set(key, at); out.push({ key, label, rows: [] }); }
      out[at].rows.push(r);
    }
    return out;
  }, [shown]);

  const baseCol = anchors ? matrix.Plugins.indexOf(anchors[0]) : 0;
  const winnerCol = anchors
    ? matrix.Plugins.indexOf(anchors[1])
    : matrix.Plugins.lastIndexOf(matrix.Winner);

  const toggle = (key: string) => setCollapsed(prev => {
    const next = new Set(prev);
    if (next.has(key)) next.delete(key); else next.add(key);
    return next;
  });

  return (
    <div className="cv-root">
      <div className="cv-toolbar">
        <div className="cv-kinds">
          {KINDS.map(k => (
            <button
              key={k}
              className={`cv-kind ${kind === k ? 'active' : ''}`}
              onClick={() => setKind(k)}
            >
              {KIND_LABEL[k]} <span className="cv-kind-count">{counts[k]}</span>
            </button>
          ))}
        </div>

        <div className="cv-spacer" />

        <button
          className="cv-plain"
          onClick={() => setCollapsed(c => (c.size ? new Set() : new Set(groups.map(g => g.key))))}
        >
          {collapsed.size ? 'Expand All' : 'Collapse All'}
        </button>

        <div className="cv-layout">
          <button
            className={`cv-layout-btn ${layout === 'cards' ? 'active' : ''}`}
            onClick={() => onLayoutChange('cards')}
            title="Card view: base against the winner"
          >
            <LayoutGrid size={13} />
          </button>
          <button
            className={`cv-layout-btn ${layout === 'matrix' ? 'active' : ''}`}
            onClick={() => onLayoutChange('matrix')}
            title="Matrix view: one column per plugin"
          >
            <Rows3 size={13} />
          </button>
        </div>
      </div>

      <div className="cv-stats">
        <span className="cv-stat-total">
          {rows.length} conflict{rows.length === 1 ? '' : 's'} in this record
        </span>
        <Stat label="Modified Values" value={counts.Value} tone="value" />
        <Stat label="Modified Flags" value={counts.Flag} tone="flag" />
        <Stat label="Changed FormIDs" value={counts.FormID} tone="formid" />
      </div>

      {layout === 'matrix' ? (
        <div className="cv-matrix-host">{matrixView}</div>
      ) : rows.length === 0 ? (
        <div className="cv-empty">
          <CheckCircle2 size={22} />
          <span>No conflicting fields. Every plugin agrees on this record.</span>
        </div>
      ) : shown.length === 0 ? (
        <div className="cv-empty">
          <CheckCircle2 size={22} />
          <span>No {KIND_LABEL[kind].toLowerCase()} conflicts in this record.</span>
        </div>
      ) : (
        <div className="cv-groups">
          {groups.map(g => {
            const isCollapsed = collapsed.has(g.key);
            return (
              <section key={g.key || '_root'} className="cv-group">
                <header className="cv-group-head" onClick={() => toggle(g.key)}>
                  {isCollapsed ? <ChevronRight size={13} /> : <ChevronDown size={13} />}
                  <span className="cv-group-name">{g.label}</span>
                  <span className="cv-group-count">
                    {g.rows.length} conflict{g.rows.length === 1 ? '' : 's'}
                  </span>
                </header>

                {!isCollapsed && (
                  <div className="cv-cards">
                    {g.rows.map(r => (
                      <ConflictCard
                        key={r.Field}
                        row={r}
                        matrix={matrix}
                        baseCol={baseCol}
                        winnerCol={winnerCol}
                      />
                    ))}
                  </div>
                )}
              </section>
            );
          })}
        </div>
      )}
    </div>
  );
}

function Stat({ label, value, tone }: { label: string; value: number; tone: string }) {
  return (
    <span className={`cv-stat cv-stat-${tone}`}>
      <span className="cv-stat-value">{value}</span>
      <span className="cv-stat-label">{label}</span>
    </span>
  );
}

function ConflictCard({ row, matrix, baseCol, winnerCol }: {
  row: ConflictFieldRow; matrix: ConflictMatrix; baseCol: number; winnerCol: number;
}) {
  const basePlugin = matrix.Plugins[baseCol] ?? '';
  const winPlugin = matrix.Plugins[winnerCol] ?? matrix.Winner;
  const baseValue = row.Values[baseCol] ?? '';
  const winValue = row.Values[winnerCol] ?? '';

  // Everyone else that defines this field, which is what makes a two-way card honest about a
  // three-plugin disagreement instead of hiding it.
  const others = matrix.Plugins.filter(
    (_, i) => i !== baseCol && i !== winnerCol && (row.Values[i] ?? '') !== '');

  const disagree = others.some(p => {
    const i = matrix.Plugins.indexOf(p);
    return (row.Values[i] ?? '') !== winValue;
  });

  return (
    <article className={`cv-card ${disagree ? 'contested' : ''}`}>
      <div className="cv-card-head">
        <span className="cv-card-field" title={row.Field}>{row.DisplayLabel || row.Field}</span>
        <span className={`cv-kind-tag cv-kind-${(row.Kind ?? 'Value').toLowerCase()}`}>
          {row.Kind ?? 'Value'}
        </span>
        <span title={disagree ? 'Plugins disagree on this field' : 'The final value is unambiguous'}>
          {disagree
            ? <AlertTriangle size={13} className="cv-icon-warn" />
            : <CheckCircle2 size={13} className="cv-icon-ok" />}
        </span>
      </div>

      <div className="cv-card-values">
        <div className="cv-side">
          <div className="cv-side-head">
            <span className="cv-dot" style={{ background: pluginColorVar(basePlugin) }} />
            <span className="cv-side-plugin" title={basePlugin}>{basePlugin}</span>
          </div>
          <div className="cv-value cv-value-base">{baseValue || <em>not set</em>}</div>
        </div>

        <div className="cv-side">
          <div className="cv-side-head">
            <span className="cv-dot" style={{ background: pluginColorVar(winPlugin) }} />
            <span className="cv-side-plugin" title={winPlugin}>{winPlugin}</span>
            <Crown size={11} className="cv-icon-win" />
          </div>
          <div className={`cv-value ${winValue === baseValue ? 'cv-value-same' : 'cv-value-changed'}`}>
            {winValue || <em>not set</em>}
          </div>
        </div>
      </div>

      {others.length > 0 && (
        <div className="cv-card-others">
          <span className="cv-others-head">Overridden By ({others.length})</span>
          <div className="cv-others-list">
            {others.map(p => <PluginChip key={p} name={p} />)}
          </div>
        </div>
      )}
    </article>
  );
}
