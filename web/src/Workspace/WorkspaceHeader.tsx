import { useEffect, useRef, useState } from 'react';
import { ChevronDown, ChevronRight, Home, Plus, X } from 'lucide-react';
import type { BreadcrumbNode, ConflictMatrix } from '../backend';
import { pluginColorVar } from '../util/pluginColor';
import type { WorkspaceAction } from './actions';
import './WorkspaceHeader.css';

interface WorkspaceHeaderProps {
  matrix: ConflictMatrix;

  anchors: [string, string] | null;
  onAnchorsChange: (next: [string, string] | null) => void;

  compareOnly: boolean;
  onCompareOnlyChange: (next: boolean) => void;

  showCompare: boolean;
  actions: WorkspaceAction[];
  onOpenRecord: (formKey: string, plugin: string) => void;
}


export default function WorkspaceHeader({
  matrix, anchors, onAnchorsChange, compareOnly, onCompareOnlyChange, showCompare, actions,
  onOpenRecord,
}: WorkspaceHeaderProps) {
  const [path, setPath] = useState<BreadcrumbNode[]>([]);
  const [menuOpen, setMenuOpen] = useState(false);
  const [picking, setPicking] = useState<0 | 1 | null>(null);
  const menuRef = useRef<HTMLDivElement>(null);



  useEffect(() => {
    let cancelled = false;
    const b = window.chrome?.webview?.hostObjects?.backend;
    if (!b) return;
    b.GetContainmentPath(matrix.FormKey)
      .then(json => { if (!cancelled) setPath(JSON.parse(json)); })
      .catch(() => { if (!cancelled) setPath([]); });
    return () => { cancelled = true; };
  }, [matrix.FormKey]);

  useEffect(() => {
    const onDown = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) setMenuOpen(false);
    };
    window.addEventListener('mousedown', onDown);
    return () => window.removeEventListener('mousedown', onDown);
  }, []);

  const pick = (slot: 0 | 1, plugin: string) => {
    const base: [string, string] = anchors
      ?? [matrix.Plugins[0] ?? '', matrix.Winner || matrix.Plugins[matrix.Plugins.length - 1] || ''];
    const next: [string, string] = slot === 0 ? [plugin, base[1]] : [base[0], plugin];
    onAnchorsChange(next[0] === next[1] ? null : next);
    setPicking(null);
  };

  const left = anchors?.[0] ?? matrix.Plugins[0] ?? '';
  const right = anchors?.[1] ?? matrix.Winner ?? '';
  const others = matrix.Plugins.filter(p => p !== left && p !== right);

  return (
    <div className="ws-header">
      <div className="ws-row ws-compare">
        {showCompare && (
          <>
            <PluginPill name={left} label="Base" onClick={() => setPicking(0)} />
            <span className="ws-vs">vs</span>
            <PluginPill name={right} label="Compare" onClick={() => setPicking(1)} />
            {others.length > 0 && (
              <button
                className="ws-add"
                title={`Also touched by: ${others.join(', ')}`}
                onClick={() => setPicking(1)}
              >
                <Plus size={12} /> {others.length} more
              </button>
            )}
            <button
              className={`ws-btn ${compareOnly ? 'on' : ''}`}
              onClick={() => onCompareOnlyChange(!compareOnly)}
              title="Show only the two anchored plugins, collapsing the rest"
            >
              Compare
            </button>
            {anchors && (
              <button className="ws-btn" onClick={() => onAnchorsChange(null)} title="Reset the compare anchors">
                <X size={12} />
              </button>
            )}
          </>
        )}

        <Breadcrumb path={path} matrix={matrix} onOpenRecord={onOpenRecord} />

        <div className="ws-spacer" />

        <div className="ws-actions" ref={menuRef}>
          <button className="ws-btn ws-btn-primary" onClick={() => setMenuOpen(o => !o)}>
            Actions <ChevronDown size={12} />
          </button>
          {menuOpen && (
            <div className="ws-menu">
              {actions.map(a => (
                <button
                  key={a.id}
                  className={`ws-menu-item ${a.danger ? 'danger' : ''}`}
                  onClick={() => { setMenuOpen(false); a.run(); }}
                >
                  {a.icon}{a.label}
                </button>
              ))}
            </div>
          )}
        </div>
      </div>

      {picking !== null && (
        <div className="ws-picker">
          <span className="ws-picker-label">
            {picking === 0 ? 'Base plugin' : 'Compare against'}
          </span>
          {matrix.Plugins.map(p => (
            <button key={p} className="ws-picker-item" onClick={() => pick(picking, p)}>
              <span className="ws-dot" style={{ background: pluginColorVar(p) }} />
              {p}
            </button>
          ))}
          <button className="ws-picker-cancel" onClick={() => setPicking(null)}>Cancel</button>
        </div>
      )}

    </div>
  );
}


function Breadcrumb({ path, matrix, onOpenRecord }: {
  path: BreadcrumbNode[];
  matrix: ConflictMatrix;
  onOpenRecord: (formKey: string, plugin: string) => void;
}) {
  return (
    <div className="ws-breadcrumb">
      <Home size={12} />
      {path.length === 0 ? (
        <>
          <ChevronRight size={11} />
          <span className="ws-bc-leaf">{matrix.EditorID || matrix.FormKey}</span>
        </>
      ) : (
        path.map((n, i) => (
          <span key={`${n.FormKey}:${i}`} className="ws-bc-seg">
            <ChevronRight size={11} />
            <button
              className={`ws-bc-btn ${i === path.length - 1 ? 'leaf' : ''}`}
              title={`${n.Kind} ${n.FormKey}`}
              onClick={() => i < path.length - 1 && onOpenRecord(n.FormKey, matrix.Winner)}
              disabled={i === path.length - 1}
            >
              {n.Label}
            </button>
          </span>
        ))
      )}
    </div>
  );
}

function PluginPill({ name, label, onClick }: { name: string; label: string; onClick: () => void }) {
  return (
    <button className="ws-pill" onClick={onClick} title={`${label}: ${name}`}>
      <span className="ws-dot" style={{ background: pluginColorVar(name) }} />
      <span className="ws-pill-name">{name || '(none)'}</span>
      <ChevronDown size={11} />
    </button>
  );
}
