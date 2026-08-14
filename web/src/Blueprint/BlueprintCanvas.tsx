import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { BpDiagnostic, BpDocument, BpNode, BpNodeDef, BpPinDef, BpViewport } from './graphModel';
import { NODE_WIDTH, canConnect, nodeBounds, pinAnchor, pinKey, wirePath } from './graphModel';
import Minimap from './Minimap';
import type { GraphAction, BpSelection } from './graphReducer';
import BlueprintNode from './BlueprintNode';

// The viewport. Pan and node drag cost zero React renders: the gesture writes transforms and path
// data straight to the DOM and commits one dispatch on release. That is the reason this canvas is
// hand rolled rather than delegated to a library store.

interface Props {
  doc: BpDocument;
  defs: Record<string, BpNodeDef>;
  selection: BpSelection;
  diagByNode: Map<string, BpDiagnostic[]>;
  diagByPin: Map<string, BpDiagnostic[]>;
  dispatch: (action: GraphAction) => void;
  onRequestNode: (worldX: number, worldY: number, from?: { node: string; pin: string }) => void;
  apiRef?: React.MutableRefObject<CanvasApi | null>;
  /// Right click anywhere on the canvas. The menu itself is the panel's, so the canvas stays
  /// concerned only with the graph surface.
  onContextMenu?: (event: React.MouseEvent) => void;
}

export interface CanvasApi {
  focusNode: (nodeId: string) => void;
  centerWorld: () => { x: number; y: number };
  toWorld: (clientX: number, clientY: number) => { x: number; y: number };
}

const MIN_SCALE = 0.15;
const MAX_SCALE = 2.5;
const CLICK_SLOP = 4;

/** Where the canvas sits before anyone pans or zooms. */
const INITIAL_VIEW: BpViewport = { x: 60, y: 60, k: 1 };

export default function BlueprintCanvas({
  doc, defs, selection, diagByNode, diagByPin, dispatch, onRequestNode, apiRef, onContextMenu,
}: Props) {
  const canvasRef = useRef<HTMLDivElement | null>(null);
  const nodeLayerRef = useRef<HTMLDivElement | null>(null);
  const wireGroupRef = useRef<SVGGElement | null>(null);
  const marqueeRef = useRef<HTMLDivElement | null>(null);
  const previewRef = useRef<SVGPathElement | null>(null);

  const nodeElements = useRef(new Map<string, HTMLDivElement>());
  const wireElements = useRef(new Map<string, SVGPathElement>());

  // Both seeded from the same constant rather than the state reading the ref. Reading a ref during
  // render is a react-hooks/refs error, and it said "these two start equal" only by side effect.
  const viewRef = useRef<BpViewport>({ ...INITIAL_VIEW });
  const [view, setView] = useState<BpViewport>({ ...INITIAL_VIEW });
  const [connect, setConnect] = useState<{ node: string; pin: string; compatible: Set<string> } | null>(null);

  const nodeById = useMemo(
    () => new Map(doc.nodes.map((n) => [n.id, n])), [doc.nodes],
  );

  const applyTransform = useCallback(() => {
    const { x, y, k } = viewRef.current;
    const transform = `translate(${x}px, ${y}px) scale(${k})`;
    if (nodeLayerRef.current) nodeLayerRef.current.style.transform = transform;
    if (wireGroupRef.current) {
      wireGroupRef.current.setAttribute('transform', `translate(${x} ${y}) scale(${k})`);
    }
    if (canvasRef.current) {
      const size = 24 * k;
      canvasRef.current.style.backgroundSize = `${size}px ${size}px`;
      canvasRef.current.style.backgroundPosition = `${x}px ${y}px`;
    }
  }, []);

  useEffect(applyTransform, [applyTransform]);

  const toWorld = useCallback((clientX: number, clientY: number) => {
    const rect = canvasRef.current?.getBoundingClientRect();
    const { x, y, k } = viewRef.current;
    return {
      x: ((clientX - (rect?.left ?? 0)) - x) / k,
      y: ((clientY - (rect?.top ?? 0)) - y) / k,
    };
  }, []);

  // Wheel has to be a real listener with passive false. React's onWheel is passive in Chromium, so
  // preventDefault there silently does nothing and the page scrolls under the panel.
  useEffect(() => {
    const element = canvasRef.current;
    if (!element) return undefined;

    let frame = 0;
    const onWheel = (event: WheelEvent) => {
      event.preventDefault();
      const rect = element.getBoundingClientRect();
      const px = event.clientX - rect.left;
      const py = event.clientY - rect.top;
      const current = viewRef.current;

      const worldX = (px - current.x) / current.k;
      const worldY = (py - current.y) / current.k;
      const k = Math.min(MAX_SCALE, Math.max(MIN_SCALE, current.k * (event.deltaY < 0 ? 1.1 : 1 / 1.1)));

      viewRef.current = { k, x: px - worldX * k, y: py - worldY * k };
      applyTransform();

      if (!frame) {
        frame = requestAnimationFrame(() => {
          frame = 0;
          setView({ ...viewRef.current });
        });
      }
    };

    element.addEventListener('wheel', onWheel, { passive: false });
    return () => {
      element.removeEventListener('wheel', onWheel);
      if (frame) cancelAnimationFrame(frame);
    };
  }, [applyTransform]);

  const redrawWires = useCallback((movedIds?: Set<string>) => {
    for (const wire of doc.wires) {
      if (movedIds && !movedIds.has(wire.from.node) && !movedIds.has(wire.to.node)) continue;
      const path = wireElements.current.get(wire.id);
      const fromNode = nodeById.get(wire.from.node);
      const toNode = nodeById.get(wire.to.node);
      if (!path || !fromNode || !toNode) continue;

      const offsets = draggedOffsets.current;
      const a = pinAnchor(shift(fromNode, offsets), defs[fromNode.def], wire.from.pin, 'out');
      const b = pinAnchor(shift(toNode, offsets), defs[toNode.def], wire.to.pin, 'in');
      path.setAttribute('d', wirePath(a.x, a.y, b.x, b.y));
    }
  }, [doc.wires, nodeById, defs]);

  const draggedOffsets = useRef<Map<string, { dx: number; dy: number }> | null>(null);

  // ---- pan and marquee --------------------------------------------------------------------

  const onCanvasPointerDown = (event: React.PointerEvent) => {
    const target = event.target as HTMLElement;
    const onEmpty = target === canvasRef.current || target.classList.contains('bp-wires');
    if (!onEmpty) return;

    const element = canvasRef.current!;
    element.setPointerCapture(event.pointerId);

    const panning = event.button === 1 || event.shiftKey;
    const start = { x: event.clientX, y: event.clientY };
    const origin = { ...viewRef.current };
    const startWorld = toWorld(event.clientX, event.clientY);
    let moved = false;

    const onMove = (move: PointerEvent) => {
      const dx = move.clientX - start.x;
      const dy = move.clientY - start.y;
      if (!moved && Math.hypot(dx, dy) < CLICK_SLOP) return;
      moved = true;

      if (panning) {
        viewRef.current = { ...origin, x: origin.x + dx, y: origin.y + dy };
        applyTransform();
        return;
      }

      const now = toWorld(move.clientX, move.clientY);
      const box = marqueeRef.current;
      if (!box) return;
      box.style.display = 'block';
      box.style.left = `${Math.min(start.x, move.clientX) - element.getBoundingClientRect().left}px`;
      box.style.top = `${Math.min(start.y, move.clientY) - element.getBoundingClientRect().top}px`;
      box.style.width = `${Math.abs(move.clientX - start.x)}px`;
      box.style.height = `${Math.abs(move.clientY - start.y)}px`;
      box.dataset.x1 = String(Math.min(startWorld.x, now.x));
      box.dataset.y1 = String(Math.min(startWorld.y, now.y));
      box.dataset.x2 = String(Math.max(startWorld.x, now.x));
      box.dataset.y2 = String(Math.max(startWorld.y, now.y));
    };

    const onUp = () => {
      element.removeEventListener('pointermove', onMove);
      element.removeEventListener('pointerup', onUp);
      element.removeEventListener('pointercancel', onCancel);

      const box = marqueeRef.current;
      if (box) box.style.display = 'none';

      if (!moved) {
        dispatch({ type: 'SELECT_NONE' });
      } else if (panning) {
        setView({ ...viewRef.current });
      } else if (box) {
        const x1 = Number(box.dataset.x1 ?? 0);
        const y1 = Number(box.dataset.y1 ?? 0);
        const x2 = Number(box.dataset.x2 ?? 0);
        const y2 = Number(box.dataset.y2 ?? 0);
        const hit = doc.nodes
          .filter((n) => {
            const b = nodeBounds(n, defs[n.def]);
            return b.x < x2 && b.x + b.w > x1 && b.y < y2 && b.y + b.h > y1;
          })
          .map((n) => n.id);
        dispatch({ type: 'SELECT', ids: hit });
      }
    };

    const onCancel = () => {
      // WebKitGTK cancels pointers in situations Chromium does not, so every capture path needs
      // this or a lost gesture leaves the canvas mid-drag.
      if (marqueeRef.current) marqueeRef.current.style.display = 'none';
      viewRef.current = { ...origin };
      applyTransform();
      onUp();
    };

    element.addEventListener('pointermove', onMove);
    element.addEventListener('pointerup', onUp);
    element.addEventListener('pointercancel', onCancel);
  };

  // ---- node drag --------------------------------------------------------------------------

  const onNodePointerDown = (event: React.PointerEvent, nodeId: string) => {
    const target = event.target as HTMLElement;
    if (target.closest('.bp-nodrag')) return;
    event.stopPropagation();

    const element = event.currentTarget as HTMLElement;
    element.setPointerCapture(event.pointerId);

    const ids = selection.nodes.includes(nodeId) ? selection.nodes : [nodeId];
    const start = { x: event.clientX, y: event.clientY };
    const origins = new Map(
      ids.map((id) => {
        const node = nodeById.get(id)!;
        return [id, { x: node.x, y: node.y }];
      }),
    );
    let moved = false;

    const onMove = (move: PointerEvent) => {
      const dx = (move.clientX - start.x) / viewRef.current.k;
      const dy = (move.clientY - start.y) / viewRef.current.k;
      if (!moved && Math.hypot(move.clientX - start.x, move.clientY - start.y) < CLICK_SLOP) return;
      moved = true;

      const offsets = new Map<string, { dx: number; dy: number }>();
      for (const id of ids) {
        offsets.set(id, { dx, dy });
        const origin = origins.get(id)!;
        const el = nodeElements.current.get(id);
        if (el) el.style.transform = `translate(${origin.x + dx}px, ${origin.y + dy}px)`;
      }
      draggedOffsets.current = offsets;
      redrawWires(new Set(ids));
    };

    const finish = (commit: boolean) => {
      element.removeEventListener('pointermove', onMove);
      element.removeEventListener('pointerup', onUp);
      element.removeEventListener('pointercancel', onCancel);

      const offsets = draggedOffsets.current;
      draggedOffsets.current = null;

      if (!moved) {
        dispatch({ type: 'SELECT', ids: [nodeId], additive: event.shiftKey || event.ctrlKey });
        return;
      }
      if (commit && offsets) {
        const { dx, dy } = offsets.get(nodeId)!;
        dispatch({ type: 'MOVE_NODES', ids, dx, dy });
      } else {
        for (const id of ids) {
          const origin = origins.get(id)!;
          const el = nodeElements.current.get(id);
          if (el) el.style.transform = `translate(${origin.x}px, ${origin.y}px)`;
        }
        redrawWires(new Set(ids));
      }
    };

    const onUp = () => finish(true);
    const onCancel = () => finish(false);

    element.addEventListener('pointermove', onMove);
    element.addEventListener('pointerup', onUp);
    element.addEventListener('pointercancel', onCancel);
  };

  // ---- wire drag --------------------------------------------------------------------------

  const onPinPointerDown = (event: React.PointerEvent, nodeId: string, pin: BpPinDef) => {
    event.stopPropagation();
    const element = event.currentTarget as HTMLElement;
    element.setPointerCapture(event.pointerId);

    // The compatible set is published once, so every pin can paint itself without another render
    // for the rest of the gesture.
    const compatible = new Set<string>();
    for (const other of doc.nodes) {
      const def = defs[other.def];
      if (!def) continue;
      for (const candidate of def.pins) {
        if (other.id === nodeId && candidate.id === pin.id) continue;
        if (canConnect(pin, candidate)) compatible.add(pinKey(other.id, candidate.id));
      }
    }
    setConnect({ node: nodeId, pin: pin.id, compatible });

    const node = nodeById.get(nodeId)!;
    const anchor = pinAnchor(node, defs[node.def], pin.id, pin.dir);

    const onMove = (move: PointerEvent) => {
      const world = toWorld(move.clientX, move.clientY);
      previewRef.current?.setAttribute('d', wirePath(anchor.x, anchor.y, world.x, world.y));
    };

    const finish = (event2: PointerEvent | null) => {
      element.removeEventListener('pointermove', onMove);
      element.removeEventListener('pointerup', onUp);
      element.removeEventListener('pointercancel', onCancel);
      previewRef.current?.setAttribute('d', '');
      setConnect(null);

      if (!event2) return;

      // Pointer capture redirects events, not hit testing, so the drop target is found this way.
      const dropped = document.elementFromPoint(event2.clientX, event2.clientY) as HTMLElement | null;
      const pinElement = dropped?.closest('[data-pin]') as HTMLElement | null;

      if (!pinElement) {
        const world = toWorld(event2.clientX, event2.clientY);
        onRequestNode(world.x, world.y, { node: nodeId, pin: pin.id });
        return;
      }

      const targetNode = pinElement.dataset.node!;
      const targetPin = pinElement.dataset.pin!;
      if (!compatible.has(pinKey(targetNode, targetPin))) return;

      const wire = pin.dir === 'out'
        ? { from: { node: nodeId, pin: pin.id }, to: { node: targetNode, pin: targetPin } }
        : { from: { node: targetNode, pin: targetPin }, to: { node: nodeId, pin: pin.id } };
      dispatch({ type: 'ADD_WIRE', wire, defs: defsByNode(doc, defs) });
    };

    const onUp = (up: PointerEvent) => finish(up);
    const onCancel = () => finish(null);

    element.addEventListener('pointermove', onMove);
    element.addEventListener('pointerup', onUp);
    element.addEventListener('pointercancel', onCancel);
  };

  // Measured into state rather than read off the ref at render time. The minimap needs the canvas
  // size to draw the viewport rectangle to scale, and a ref read during render is both a
  // react-hooks/refs error and wrong on a resize, since nothing would re-render to update it.
  const [stage, setStage] = useState({ width: 0, height: 0 });

  useEffect(() => {
    const element = canvasRef.current;
    if (!element) return;

    const observer = new ResizeObserver((entries) => {
      const box = entries[0]?.contentRect;
      if (box) setStage({ width: box.width, height: box.height });
    });
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  /** Puts a point in graph coordinates at the middle of the visible canvas. */
  const centreOn = useCallback((worldX: number, worldY: number, minScale = 0) => {
    const rect = canvasRef.current?.getBoundingClientRect();
    if (!rect) return;

    const k = Math.max(viewRef.current.k, minScale);
    viewRef.current = { k, x: rect.width / 2 - worldX * k, y: rect.height / 2 - worldY * k };
    applyTransform();
    setView({ ...viewRef.current });
  }, [applyTransform]);

  // ---- imperative api ---------------------------------------------------------------------

  useEffect(() => {
    if (!apiRef) return;
    apiRef.current = {
      focusNode: (nodeId: string) => {
        const node = nodeById.get(nodeId);
        const rect = canvasRef.current?.getBoundingClientRect();
        if (!node || !rect) return;

        centreOn(node.x + NODE_WIDTH / 2, node.y + 40, 0.6);
        dispatch({ type: 'SELECT', ids: [nodeId] });
      },
      centerWorld: () => {
        const rect = canvasRef.current?.getBoundingClientRect();
        const { x, y, k } = viewRef.current;
        return {
          x: ((rect?.width ?? 800) / 2 - x) / k,
          y: ((rect?.height ?? 600) / 2 - y) / k,
        };
      },
      toWorld,
    };
  }, [apiRef, nodeById, centreOn, dispatch, toWorld]);

  const selectedNodes = useMemo(() => new Set(selection.nodes), [selection.nodes]);

  return (
    <div
      className="bp-canvas"
      ref={canvasRef}
      onPointerDown={onCanvasPointerDown}
      onDoubleClick={(e) => {
        const world = toWorld(e.clientX, e.clientY);
        onRequestNode(world.x, world.y);
      }}
      onContextMenu={(e) => {
        if (!onContextMenu) return;
        e.preventDefault();
        onContextMenu(e);
      }}
    >
      <svg className="bp-wires">
        <g ref={wireGroupRef}>
          {doc.wires.map((wire) => {
            const fromNode = nodeById.get(wire.from.node);
            const toNode = nodeById.get(wire.to.node);
            if (!fromNode || !toNode) return null;

            const a = pinAnchor(fromNode, defs[fromNode.def], wire.from.pin, 'out');
            const b = pinAnchor(toNode, defs[toNode.def], wire.to.pin, 'in');
            const isExec = defs[fromNode.def]?.pins
              .find((p) => p.id === wire.from.pin)?.kind === 'exec';

            return (
              <path
                key={wire.id}
                ref={(el) => {
                  if (el) wireElements.current.set(wire.id, el);
                  else wireElements.current.delete(wire.id);
                }}
                className={`bp-wire ${isExec ? 'bp-wire-exec' : ''} ${
                  selection.wires.includes(wire.id) ? 'bp-wire-selected' : ''}`}
                d={wirePath(a.x, a.y, b.x, b.y)}
              />
            );
          })}
          <path ref={previewRef} className="bp-wire bp-wire-preview" d="" />
        </g>
      </svg>

      <div className="bp-nodes" ref={nodeLayerRef}>
        {doc.nodes.map((node) => (
          <BlueprintNode
            key={node.id}
            node={node}
            def={defs[node.def]}
            selected={selectedNodes.has(node.id)}
            diagnostics={diagByNode.get(node.id)}
            diagByPin={diagByPin}
            connect={connect}
            registerElement={(el) => {
              if (el) nodeElements.current.set(node.id, el);
              else nodeElements.current.delete(node.id);
            }}
            onPointerDown={onNodePointerDown}
            onPinPointerDown={onPinPointerDown}
            dispatch={dispatch}
          />
        ))}
      </div>

      <div className="bp-marquee" ref={marqueeRef} />
      <div className="bp-zoom">{Math.round(view.k * 100)}%</div>

      <Minimap
        doc={doc}
        defs={defs}
        selection={selection.nodes}
        view={view}
        stage={stage}
        onGoTo={(x, y) => centreOn(x, y)}
      />
    </div>
  );
}

// Applies a live drag offset without committing it, so wires follow the node during the gesture
// while the document still holds the pre-drag position.
const shift = (
  node: BpNode,
  offsets: Map<string, { dx: number; dy: number }> | null,
): BpNode => {
  const offset = offsets?.get(node.id);
  return offset ? { ...node, x: node.x + offset.dx, y: node.y + offset.dy } : node;
};

const defsByNode = (doc: BpDocument, defs: Record<string, BpNodeDef>) => {
  const map: Record<string, BpNodeDef> = {};
  for (const node of doc.nodes) {
    const def = defs[node.def];
    if (def) map[node.id] = def;
  }
  return map;
};
