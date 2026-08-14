import { pluginColorVar, pluginBadge } from '../util/pluginColor';
import './PluginChip.css';

interface PluginChipProps {
  name: string;
  onClick?: () => void;
}

// Reusable plugin chip: a stable colored badge (first letter) plus the plugin file name.
export default function PluginChip({ name, onClick }: PluginChipProps) {
  return (
    <div
      className={`plugin-chip ${onClick ? 'clickable' : ''}`}
      onClick={onClick}
      title={name}
    >
      <span className="plugin-chip-badge" style={{ background: pluginColorVar(name) }}>
        {pluginBadge(name)}
      </span>
      <span className="plugin-chip-name">{name}</span>
    </div>
  );
}
