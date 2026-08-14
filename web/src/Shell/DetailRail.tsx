import { useEffect, useMemo, useState } from 'react';
import { Copy, CornerUpRight, Crosshair, Filter, Info, Layers, Star } from 'lucide-react';
import type { BreadcrumbNode, ConflictMatrix, PluginMatrixRow, RecordDetails } from '../backend';
import { pluginColorVar } from '../util/pluginColor';
import ConflictDonut from './ConflictDonut';
import './DetailRail.css';

interface DetailRailProps {
  matrix: ConflictMatrix | null;
  onOpenConflictsTab?: () => void;
  onOpenRecord?: (formKey: string, plugin: string) => void;

  onAddToFilter?: (needle: string) => void;
}

import { readFavourites, toggleFavourite as toggleFav, FAVOURITES_CHANGED } from '../favourites';

export default function DetailRail({
  matrix, onOpenConflictsTab, onOpenRecord, onAddToFilter,
}: DetailRailProps) {
  const [details, setDetails] = useState<RecordDetails | null>(null);
  const [pluginRows, setPluginRows] = useState<PluginMatrixRow[]>([]);
  const [path, setPath] = useState<BreadcrumbNode[]>([]);
  const [favourites, setFavourites] = useState(readFavourites);
  const [toast, setToast] = useState('');

  const formKey = matrix?.FormKey ?? '';

  useEffect(() => {
    let cancelled = false;
    const b = window.chrome?.webview?.hostObjects?.backend;
    if (!formKey || !b) return;
    (async () => {
      try {
        const [d, m, p] = await Promise.all([
          b.GetRecordDetails(formKey), b.GetRecordPluginMatrix(formKey), b.GetContainmentPath(formKey),
        ]);
        if (cancelled) return;
        setDetails(JSON.parse(d));
        setPluginRows(JSON.parse(m));
        setPath(JSON.parse(p));
      } catch {
        if (!cancelled) { setDetails(null); setPluginRows([]); setPath([]); }
      }
    })();
    return () => { cancelled = true; };
  }, [formKey]);

  const counts = useMemo(() => {
    const c = { Value: 0, Flag: 0, FormID: 0 };
    for (const r of matrix?.Rows ?? []) {
      if (!r.Differs) continue;
      c[(r.Kind ?? 'Value') as keyof typeof c]++;
    }
    return c;
  }, [matrix]);

  const isFavourite = favourites.some(f => f.formKey === formKey);
  const flash = (msg: string) => { setToast(msg); setTimeout(() => setToast(''), 1600); };

  useEffect(() => {
    const sync = () => setFavourites(readFavourites());
    window.addEventListener(FAVOURITES_CHANGED, sync);
    return () => window.removeEventListener(FAVOURITES_CHANGED, sync);
  }, []);

  const toggleFavourite = () => {
    if (!formKey) return;

    const nowFavourite = toggleFav({
      formKey,
      label: matrix?.EditorID || details?.EditorId || formKey,
      plugin: matrix?.Winner || '',
    });
    setFavourites(readFavourites());
    flash(nowFavourite ? 'Added to favourites' : 'Removed from favourites');
  };

  if (!matrix) {
    return (
      <aside className="detail-rail">
        <section className="rail-section">
          <div className="rail-head">RECORD DETAILS</div>
          <div className="rail-empty">
            <Info size={20} />
            <span>Open a record to see its details.</span>
          </div>
        </section>
      </aside>
    );
  }

  return (
    <aside className="detail-rail">
      <section className="rail-section">
        <div className="rail-head">RECORD DETAILS</div>
        <div className="rail-badges">
          <span className="rail-class">{details?.ClassName || matrix.Type}</span>
          {details?.Signature && <span className="rail-sig">{details.Signature}</span>}
        </div>
        <div className="rail-fields">
          <Field label="FormID" value={details?.FormId || matrix.FormKey} mono />
          <Field label="Editor ID" value={details?.EditorId || matrix.EditorID || '-'} />
          <Field label="Form Key" value={matrix.FormKey} mono />
          <Field label="File" value={details?.File || matrix.Winner || '-'} />
          {details?.BaseForm && (
            <div className="rail-field">
              <span className="rf-label">Base Form</span>
              <button
                className="rf-value rf-link mono"
                title={details.BaseFormKey}
                onClick={() => onOpenRecord?.(details.BaseFormKey, details.File)}
              >
                {details.BaseForm}
              </button>
            </div>
          )}
        </div>
      </section>

      <section className="rail-section">
        <div className="rail-head">CONFLICT SUMMARY</div>
        <ConflictDonut values={counts.Value} flags={counts.Flag} formIds={counts.FormID} />
      </section>

      <section className="rail-section">
        <div className="rail-head">QUICK ACTIONS</div>
        <div className="rail-actions">
          <RailAction
            icon={<Copy size={13} />} label="Copy FormID"
            onClick={() => {
              void navigator.clipboard.writeText(details?.FormId || matrix.FormKey);
              flash('FormID copied');
            }}
          />
          <RailAction
            icon={<CornerUpRight size={13} />} label="Jump to Base Form"
            disabled={!details?.BaseFormKey}
            onClick={() => details && onOpenRecord?.(details.BaseFormKey, details.File)}
          />
          <RailAction
            icon={<Filter size={13} />} label="Add to Filter"
            onClick={() => {
              onAddToFilter?.(details?.EditorId || details?.FormId || matrix.FormKey);
              flash('Added to the navigator filter');
            }}
          />
          <RailAction
            icon={<Star size={13} className={isFavourite ? 'rail-star-on' : ''} />}
            label={isFavourite ? 'Remove Favourite' : 'Add to Favourites'}
            onClick={toggleFavourite}
          />
        </div>
        {toast && <div className="rail-toast">{toast}</div>}
      </section>

      {path.length > 1 && (
        <section className="rail-section">
          <div className="rail-head">CONTAINED IN</div>
          <ol className="rail-contained">
            {path.slice(0, -1).map((n, i) => (
              <li key={`${n.FormKey}:${i}`}>
                <Crosshair size={11} />
                <button
                  className="rail-contained-btn"
                  title={`${n.Kind} ${n.FormKey}`}
                  onClick={() => onOpenRecord?.(n.FormKey, matrix.Winner)}
                >
                  {n.Label}
                </button>
              </li>
            ))}
          </ol>
        </section>
      )}

      {pluginRows.length > 0 && (
        <section className="rail-section">
          <div className="rail-head">
            <Layers size={12} /> PLUGINS AFFECTING THIS RECORD
          </div>
          <table className="rail-plugin-table">
            <thead>
              <tr>
                <th>Plugin</th>
                <th title="Fields this plugin sets">Sets</th>
                <th title="Fields where this plugin loses to a later one">Lost</th>
              </tr>
            </thead>
            <tbody>
              {pluginRows.map(r => (
                <tr
                  key={r.Plugin}
                  className={r.IsWinner ? 'winner' : ''}
                  title={[
                    `load order ${r.LoadOrder}`,
                    r.Kind,
                    r.IsOverride ? 'override' : 'origin',
                    r.LastModified ? `modified ${r.LastModified}` : '',
                  ].filter(Boolean).join(' - ')}
                  onClick={onOpenConflictsTab}
                >
                  <td>
                    <span className="rail-dot" style={{ background: pluginColorVar(r.Plugin) }} />
                    {r.Plugin}
                  </td>
                  <td className="rail-num">{r.Changes}</td>
                  <td className={`rail-num ${r.Conflicts > 0 ? 'rail-num-bad' : ''}`}>{r.Conflicts}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      )}
    </aside>
  );
}

function Field({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="rail-field">
      <span className="rf-label">{label}</span>
      <span className={`rf-value ${mono ? 'mono' : ''}`} title={value}>{value}</span>
    </div>
  );
}

function RailAction({ icon, label, onClick, disabled }: {
  icon: React.ReactNode; label: string; onClick: () => void; disabled?: boolean;
}) {
  return (
    <button className="rail-action" onClick={onClick} disabled={disabled}>
      {icon}<span>{label}</span>
    </button>
  );
}
