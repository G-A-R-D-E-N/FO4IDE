import './StatusBar.css';

interface StatusBarProps {
  status: string;
  progress: number | null;
  recordCount: number;
  visibleCount: number;
  selectedCount: number;
  loadOrder: string[];
}

export default function StatusBar({
  status, progress, recordCount, visibleCount, selectedCount, loadOrder,
}: StatusBarProps) {
  const loadOrderText = loadOrder.length ? loadOrder.join(', ') : 'none';
  return (
    <div className="status-bar">
      {progress !== null && (
        <div className="status-progress">
          <div
            className={`status-progress-fill ${progress === 0 ? 'indeterminate' : ''}`}
            style={{ width: `${Math.max(0, Math.min(100, progress))}%` }}
          />
        </div>
      )}
      <span className="status-text">{status || (progress !== null ? 'Working...' : 'Ready')}</span>
      <span className="status-sep" />
      <span className="status-stat">Records: {recordCount}</span>
      <span className="status-stat">Visible: {visibleCount}</span>
      <span className="status-stat">Selected: {selectedCount}</span>
      <span className="status-stat status-loadorder" title={loadOrderText}>
        Load Order: {loadOrderText}
      </span>
    </div>
  );
}
