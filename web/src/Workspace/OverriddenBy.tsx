import { ChevronRight } from 'lucide-react';
import PluginChip from './PluginChip';
import './OverriddenBy.css';

interface OverriddenByProps {
  plugins: string[];
  onExpand?: () => void;
}


export default function OverriddenBy({ plugins, onExpand }: OverriddenByProps) {
  if (plugins.length === 0) {
    return null;
  }

  return (
    <div className="overridden-by">
      <div className="overridden-by-main">
        <div className="overridden-by-head">Overridden By ({plugins.length})</div>
        <div className="overridden-by-list">
          {plugins.map((p) => (
            <PluginChip key={p} name={p} />
          ))}
        </div>
      </div>
      {onExpand ? (
        <button type="button" className="overridden-by-chevron" onClick={onExpand} aria-label="Expand">
          <ChevronRight size={16} />
        </button>
      ) : (
        <span className="overridden-by-chevron">
          <ChevronRight size={16} />
        </span>
      )}
    </div>
  );
}
