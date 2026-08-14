import { useEffect, useRef, useState } from 'react';
import {
  Home, Database, X, Search, Settings, PanelRight, HelpCircle, MessageSquare, Sun, Moon,
} from 'lucide-react';
import type { SearchHit } from '../backend';
import './TopBar.css';

export type ShellTab = 'home' | 'record';

interface TopBarProps {
  activeTab: ShellTab;
  hasRecord: boolean;
  recordTitle: string;
  onSelectTab: (tab: ShellTab) => void;
  onCloseRecord: () => void;
  onOpenSettings: () => void;
  onOpenHelp: () => void;
  onToggleRail: () => void;
  railVisible: boolean;
  onToggleChat: () => void;
  chatVisible: boolean;
  isDark: boolean;
  onToggleTheme: () => void;
  // Command bar wiring (reuses the existing record search bridge).
  onSearch: (query: string) => Promise<SearchHit[]>;
  onOpenHit: (hit: SearchHit) => void;
}

export default function TopBar({
  activeTab, hasRecord, recordTitle, onSelectTab, onCloseRecord,
  onOpenSettings, onOpenHelp, onToggleRail, railVisible, onToggleChat, chatVisible,
  isDark, onToggleTheme,
  onSearch, onOpenHit,
}: TopBarProps) {
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<SearchHit[] | null>(null);
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);
  const wrapRef = useRef<HTMLDivElement>(null);

  // Ctrl+K focuses the command bar from anywhere.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        inputRef.current?.focus();
        inputRef.current?.select();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  // Debounced search (>= 2 chars) against the existing SearchRecords bridge.
  useEffect(() => {
    const q = query.trim();
    if (q.length < 2) { setResults(null); setOpen(false); return; }
    let cancelled = false;
    const t = setTimeout(async () => {
      const hits = await onSearch(q);
      if (cancelled) return;
      setResults(hits);
      setOpen(true);
      setActive(0);
    }, 300);
    return () => { cancelled = true; clearTimeout(t); };
  }, [query, onSearch]);

  // Close the dropdown on outside click.
  useEffect(() => {
    const onClick = (e: MouseEvent) => {
      if (wrapRef.current && !wrapRef.current.contains(e.target as Node)) setOpen(false);
    };
    window.addEventListener('mousedown', onClick);
    return () => window.removeEventListener('mousedown', onClick);
  }, []);

  const choose = (hit: SearchHit) => {
    onOpenHit(hit);
    setOpen(false);
    setQuery('');
    inputRef.current?.blur();
  };

  const onInputKey = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (!open || !results || results.length === 0) return;
    if (e.key === 'ArrowDown') { e.preventDefault(); setActive(a => Math.min(a + 1, results.length - 1)); }
    else if (e.key === 'ArrowUp') { e.preventDefault(); setActive(a => Math.max(a - 1, 0)); }
    else if (e.key === 'Enter') { e.preventDefault(); choose(results[active]); }
    else if (e.key === 'Escape') { setOpen(false); }
  };

  return (
    <div className="topbar">
      {/* Brand */}
      <div className="topbar-brand">
        <span className="brand-x">NexusEdit</span>
      </div>

      {/* Tab strip */}
      <div className="topbar-tabs">
        <button
          className={`top-tab ${activeTab === 'home' ? 'active' : ''}`}
          onClick={() => onSelectTab('home')}
        >
          <Home size={14} /> Home
        </button>
        {hasRecord && (
          <button
            className={`top-tab ${activeTab === 'record' ? 'active' : ''}`}
            onClick={() => onSelectTab('record')}
          >
            <Database size={14} color="var(--accent-info)" />
            <span className="top-tab-label">{recordTitle || 'Record Viewer'}</span>
            <span
              className="top-tab-close"
              role="button"
              title="Close"
              onClick={(e) => { e.stopPropagation(); onCloseRecord(); }}
            >
              <X size={13} />
            </span>
          </button>
        )}
      </div>

      {/* Command bar */}
      <div className="topbar-command" ref={wrapRef}>
        <div className="command-field">
          <Search size={14} className="command-icon" />
          <input
            ref={inputRef}
            value={query}
            placeholder="Search records, fields, or formIDs..."
            onChange={e => setQuery(e.target.value)}
            onKeyDown={onInputKey}
            onFocus={() => { if (results && results.length) setOpen(true); }}
          />
          <span className="command-kbd">Ctrl K</span>
        </div>
        {open && results !== null && (
          <div className="command-results">
            {results.length === 0 ? (
              <div className="command-empty">No matching records.</div>
            ) : (
              results.map((h, i) => (
                <div
                  key={`${h.Plugin}:${h.FormKey}:${i}`}
                  className={`command-result ${i === active ? 'active' : ''}`}
                  onMouseEnter={() => setActive(i)}
                  onMouseDown={(e) => { e.preventDefault(); choose(h); }}
                  title={`${h.FormKey} - ${h.Plugin}`}
                >
                  <span className="cr-id">{h.EditorID || h.FormKey}</span>
                  <span className="cr-type">{h.Type}</span>
                  <span className="cr-plugin">{h.Plugin}</span>
                </div>
              ))
            )}
          </div>
        )}
      </div>

      {/* Tool cluster */}
      <div className="topbar-tools">
        <button className={`tool-btn`} onClick={onToggleTheme} title={isDark ? 'Switch to light mode' : 'Switch to dark mode'}>
          {isDark ? <Sun size={17} strokeWidth={1.6} /> : <Moon size={17} strokeWidth={1.6} />}
        </button>
        <button className="tool-btn" onClick={onOpenSettings} title="Settings">
          <Settings size={17} strokeWidth={1.6} />
        </button>
        <button
          className={`tool-btn ${railVisible ? 'on' : ''}`}
          onClick={onToggleRail}
          title="Toggle detail rail"
        >
          <PanelRight size={17} strokeWidth={1.6} />
        </button>
        <button className="tool-btn" onClick={onOpenHelp} title="Help">
          <HelpCircle size={17} strokeWidth={1.6} />
        </button>
        <button
          className={`tool-btn ${chatVisible ? 'on' : ''}`}
          onClick={onToggleChat}
          title="Toggle assistant"
        >
          <MessageSquare size={17} strokeWidth={1.6} />
        </button>
        <div className="tool-avatar" title="John Doe">JD</div>
      </div>
    </div>
  );
}
