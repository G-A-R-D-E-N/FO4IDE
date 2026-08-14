import { useMemo } from 'react';
import type { BpDocument, BpNodeDef, BpViewport } from './graphModel';
import { NODE_WIDTH, nodeHeight } from './graphModel';
import { graphBounds, minimapFit, minimapToWorld } from './layout';

const WIDTH = 180;
const HEIGHT = 120;

interface Props {
  doc: BpDocument;
  defs: Record<string, BpNodeDef>;
  selection: string[];
  view: BpViewport;

  stage: { width: number; height: number };

  onGoTo: (x: number, y: number) => void;
}

export default function Minimap({ doc, defs, selection, view, stage, onGoTo }: Props) {
  const bounds = useMemo(() => graphBounds(doc, defs), [doc, defs]);
  const fit = useMemo(() => minimapFit(bounds, WIDTH, HEIGHT), [bounds]);
  const selected = useMemo(() => new Set(selection), [selection]);

  if (!bounds) return null;

  const visible = {
    x: -view.x / view.k,
    y: -view.y / view.k,
    w: stage.width / view.k,
    h: stage.height / view.k,
  };

  const goTo = (event: React.MouseEvent<SVGSVGElement>) => {
    const rect = event.currentTarget.getBoundingClientRect();
    const point = minimapToWorld(
      { x: event.clientX - rect.left, y: event.clientY - rect.top },
      fit,
    );
    onGoTo(point.x, point.y);
  };

  return (
    <svg
      className="bp-minimap"
      width={WIDTH}
      height={HEIGHT}
      onPointerDown={(e) => e.stopPropagation()}
      onClick={goTo}
    >
      <rect className="bp-minimap-bg" x={0} y={0} width={WIDTH} height={HEIGHT} />

      <rect
        className="bp-minimap-view"
        x={fit.offsetX + visible.x * fit.scale}
        y={fit.offsetY + visible.y * fit.scale}
        width={Math.max(2, visible.w * fit.scale)}
        height={Math.max(2, visible.h * fit.scale)}
      />

      {doc.nodes.map((node) => (
        <rect
          key={node.id}
          className={selected.has(node.id) ? 'bp-minimap-node selected' : 'bp-minimap-node'}
          x={fit.offsetX + node.x * fit.scale}
          y={fit.offsetY + node.y * fit.scale}
          width={Math.max(1, NODE_WIDTH * fit.scale)}
          height={Math.max(1, nodeHeight(defs[node.def]) * fit.scale)}
        />
      ))}
    </svg>
  );
}
