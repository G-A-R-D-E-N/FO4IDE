interface Slice { label: string; value: number; color: string; }

interface ConflictDonutProps {
  values: number;
  flags: number;
  formIds: number;
}

const SIZE = 108;
const STROKE = 14;
const R = (SIZE - STROKE) / 2;
const C = 2 * Math.PI * R;

/**
 * Conflict breakdown as an SVG donut. Deliberately hand-drawn rather than pulling in a chart
 * library for one figure: three arcs on a circle is less code than the dependency would be.
 */
export default function ConflictDonut({ values, flags, formIds }: ConflictDonutProps) {
  const slices: Slice[] = [
    { label: 'Values', value: values, color: 'var(--text-secondary)' },
    { label: 'Flags', value: flags, color: 'var(--status-warning)' },
    { label: 'FormIDs', value: formIds, color: 'var(--chip-3)' },
  ];
  const total = values + flags + formIds;

  let offset = 0;
  return (
    <div className="donut-wrap">
      <svg width={SIZE} height={SIZE} viewBox={`0 0 ${SIZE} ${SIZE}`} className="donut">
        <circle
          cx={SIZE / 2} cy={SIZE / 2} r={R}
          fill="none" stroke="var(--bg-tertiary)" strokeWidth={STROKE}
        />
        {total > 0 && slices.map(s => {
          if (s.value === 0) return null;
          const len = (s.value / total) * C;
          // Each arc starts where the previous one ended; rotating -90deg puts 0 at the top.
          const dash = `${len} ${C - len}`;
          const el = (
            <circle
              key={s.label}
              cx={SIZE / 2} cy={SIZE / 2} r={R}
              fill="none" stroke={s.color} strokeWidth={STROKE}
              strokeDasharray={dash} strokeDashoffset={-offset}
              transform={`rotate(-90 ${SIZE / 2} ${SIZE / 2})`}
            />
          );
          offset += len;
          return el;
        })}
        <text x="50%" y="47%" className="donut-total" textAnchor="middle">{total}</text>
        <text x="50%" y="62%" className="donut-caption" textAnchor="middle">
          {total === 1 ? 'conflict' : 'conflicts'}
        </text>
      </svg>

      <ul className="donut-legend">
        {slices.map(s => (
          <li key={s.label}>
            <span className="donut-swatch" style={{ background: s.color }} />
            <span className="donut-label">{s.label}</span>
            <span className="donut-value">{s.value}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}
