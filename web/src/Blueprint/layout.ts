import type { BpDocument, BpNodeDef } from './graphModel';
import { NODE_WIDTH, nodeHeight } from './graphModel';

export interface Point { x: number; y: number }

/** Gap between one column of nodes and the next. */
export const COLUMN_GAP = 90;

/** Gap between two nodes stacked in the same column. */
export const ROW_GAP = 28;

/** Where the top left node lands. */
export const LAYOUT_ORIGIN: Point = { x: 40, y: 40 };

/**
 * Arranges a graph left to right along its execution flow.
 *
 * Layered, because that is what the graph already means: exec wires run from a statement to the one
 * after it, so column position is depth along the flow and the arrangement reads in the same
 * direction as the Papyrus it will become. A force directed layout would produce something prettier
 * in the abstract and less readable here, because it has no reason to keep the flow pointing one way.
 *
 * Pure, so it can be tested without a canvas. Returns positions rather than a document, so the
 * caller decides whether that is one undoable edit.
 */
export function autoLayout(doc: BpDocument, defs: Record<string, BpNodeDef>): Record<string, Point> {
  if (doc.nodes.length === 0) return {};

  const execEdges = executionEdges(doc, defs);
  const back = backEdges(doc, execEdges);
  const forward = execEdges.filter((_, i) => !back.has(i));

  const rank = rankNodes(doc, forward);
  placePureProducers(doc, defs, rank);
  normalise(rank);

  return position(doc, defs, rank, forward);
}

/** Exec wires only. A data wire says nothing about what runs after what. */
function executionEdges(
  doc: BpDocument,
  defs: Record<string, BpNodeDef>,
): Array<{ from: string; to: string }> {
  const isExec = (nodeDef: string, pinId: string) =>
    defs[nodeDef]?.pins.find((p) => p.id === pinId)?.kind === 'exec';

  const byId = new Map(doc.nodes.map((n) => [n.id, n]));

  return doc.wires
    .filter((w) => {
      const from = byId.get(w.from.node);
      const to = byId.get(w.to.node);
      return !!from && !!to && isExec(from.def, w.from.pin) && isExec(to.def, w.to.pin);
    })
    .map((w) => ({ from: w.from.node, to: w.to.node }));
}

/**
 * The exec edges that close a loop, by index.
 *
 * A back edge is the loop repeating, not a step further along the flow, so ranking must not relax
 * across one. Without this a three node cycle marched out to column nine instead of column two: each
 * pass pushed the loop header one further right, bounded only by the pass limit. This is the same
 * rule `GraphExecFlow` applies on the compiler side, for the same reason.
 */
function backEdges(doc: BpDocument, edges: Array<{ from: string; to: string }>): Set<number> {
  const outgoing = new Map<string, Array<{ index: number; to: string }>>();
  edges.forEach((edge, index) => {
    if (!outgoing.has(edge.from)) outgoing.set(edge.from, []);
    outgoing.get(edge.from)!.push({ index, to: edge.to });
  });

  const WHITE = 0;
  const GREY = 1;
  const BLACK = 2;
  const colour = new Map<string, number>(doc.nodes.map((n) => [n.id, WHITE]));
  const back = new Set<number>();

  for (const root of doc.nodes) {
    if (colour.get(root.id) !== WHITE) continue;

    // Iterative, because a long straight chain of statements is deep and a recursive walk would
    // risk the stack on a real script.
    const stack: Array<{ id: string; next: number }> = [{ id: root.id, next: 0 }];
    colour.set(root.id, GREY);

    while (stack.length > 0) {
      const frame = stack[stack.length - 1];
      const successors = outgoing.get(frame.id) ?? [];

      if (frame.next >= successors.length) {
        colour.set(frame.id, BLACK);
        stack.pop();
        continue;
      }

      const edge = successors[frame.next++];
      const seen = colour.get(edge.to) ?? WHITE;

      if (seen === GREY) back.add(edge.index);
      else if (seen === WHITE) {
        colour.set(edge.to, GREY);
        stack.push({ id: edge.to, next: 0 });
      }
    }
  }

  return back;
}

/** Shifts every column so the leftmost is zero, after expressions have moved left of the flow. */
function normalise(rank: Map<string, number>): void {
  let least = 0;
  for (const value of rank.values()) least = Math.min(least, value);
  if (least === 0) return;

  for (const [id, value] of rank) rank.set(id, value - least);
}

/**
 * Column index per node: the longest execution path reaching it.
 *
 * Longest rather than shortest, so a node never sits to the left of something that must run before
 * it. Back edges are excluded by the caller, so this runs over an acyclic graph and the relaxation
 * reaches its fixpoint; the pass limit is a backstop, not the mechanism.
 */
function rankNodes(doc: BpDocument, edges: Array<{ from: string; to: string }>): Map<string, number> {
  const rank = new Map(doc.nodes.map((n) => [n.id, 0]));

  for (let pass = 0; pass < doc.nodes.length; pass++) {
    let changed = false;
    for (const edge of edges) {
      const next = (rank.get(edge.from) ?? 0) + 1;
      if (next > (rank.get(edge.to) ?? 0)) {
        rank.set(edge.to, next);
        changed = true;
      }
    }
    if (!changed) break;
  }

  return rank;
}

/**
 * A node with no exec pins at all is an expression, so it belongs just left of whatever reads it.
 *
 * Without this every pure node stays at column 0 and a literal feeding something deep in the graph
 * is dragged to the far left, with a wire across the whole canvas.
 */
function placePureProducers(
  doc: BpDocument,
  defs: Record<string, BpNodeDef>,
  rank: Map<string, number>,
): void {
  const pure = new Set(
    doc.nodes
      .filter((n) => (defs[n.def]?.pins ?? []).every((p) => p.kind !== 'exec'))
      .map((n) => n.id),
  );
  if (pure.size === 0) return;

  // Repeated, so a chain of pure nodes settles: an operator feeding an operator feeding a call.
  for (let pass = 0; pass < doc.nodes.length; pass++) {
    let changed = false;

    for (const id of pure) {
      const consumers = doc.wires.filter((w) => w.from.node === id).map((w) => w.to.node);
      if (consumers.length === 0) continue;

      // Not clamped at zero. A chain of expressions has to be able to extend left of the flow,
      // and clamping collapsed the whole chain into one column on top of itself. The columns are
      // shifted back to zero afterwards.
      const target = Math.min(...consumers.map((c) => rank.get(c) ?? 0)) - 1;
      if (target !== rank.get(id)) {
        rank.set(id, target);
        changed = true;
      }
    }

    if (!changed) break;
  }
}

/**
 * Final coordinates, stacking each column top to bottom.
 *
 * Within a column, nodes are ordered by the average row of what feeds them, which is the standard
 * cheap way to keep wires from crossing without a full layered ordering pass. Ties fall back to the
 * position the node already had, then to its id, so the result is stable and repeatable: laying out
 * twice does not shuffle anything.
 */
function position(
  doc: BpDocument,
  defs: Record<string, BpNodeDef>,
  rank: Map<string, number>,
  edges: Array<{ from: string; to: string }>,
): Record<string, Point> {
  const columns = new Map<number, string[]>();
  for (const node of doc.nodes) {
    const column = rank.get(node.id) ?? 0;
    if (!columns.has(column)) columns.set(column, []);
    columns.get(column)!.push(node.id);
  }

  const original = new Map(doc.nodes.map((n) => [n.id, n]));
  const inputs = new Map<string, string[]>();
  for (const edge of edges) {
    if (!inputs.has(edge.to)) inputs.set(edge.to, []);
    inputs.get(edge.to)!.push(edge.from);
  }

  const row = new Map<string, number>();
  const out: Record<string, Point> = {};

  for (const column of [...columns.keys()].sort((a, b) => a - b)) {
    const ids = columns.get(column)!;

    ids.sort((a, b) => {
      const ba = barycentre(a);
      const bb = barycentre(b);
      if (ba !== bb) return ba - bb;

      const ya = original.get(a)?.y ?? 0;
      const yb = original.get(b)?.y ?? 0;
      if (ya !== yb) return ya - yb;
      return a < b ? -1 : a > b ? 1 : 0;
    });

    let y = LAYOUT_ORIGIN.y;
    ids.forEach((id, index) => {
      row.set(id, index);
      out[id] = { x: LAYOUT_ORIGIN.x + column * (NODE_WIDTH + COLUMN_GAP), y };
      y += nodeHeight(defs[original.get(id)!.def]) + ROW_GAP;
    });
  }

  return out;

  function barycentre(id: string): number {
    const feeders = (inputs.get(id) ?? []).map((f) => row.get(f)).filter((r): r is number => r != null);
    if (feeders.length === 0) return Number.MAX_SAFE_INTEGER;
    return feeders.reduce((a, b) => a + b, 0) / feeders.length;
  }
}

/** The rectangle every node fits inside, or null for an empty graph. */
export function graphBounds(
  doc: BpDocument,
  defs: Record<string, BpNodeDef>,
): { x: number; y: number; w: number; h: number } | null {
  if (doc.nodes.length === 0) return null;

  let minX = Infinity;
  let minY = Infinity;
  let maxX = -Infinity;
  let maxY = -Infinity;

  for (const node of doc.nodes) {
    minX = Math.min(minX, node.x);
    minY = Math.min(minY, node.y);
    maxX = Math.max(maxX, node.x + NODE_WIDTH);
    maxY = Math.max(maxY, node.y + nodeHeight(defs[node.def]));
  }

  return { x: minX, y: minY, w: maxX - minX, h: maxY - minY };
}

export interface MinimapFit {
  scale: number;
  offsetX: number;
  offsetY: number;
}

/**
 * How to squeeze the whole graph into a small box, centred, without distorting it.
 *
 * One scale for both axes on purpose. Fitting each axis separately would stretch the overview so it
 * no longer resembles the canvas, which is the only thing a minimap is for.
 */
export function minimapFit(
  bounds: { x: number; y: number; w: number; h: number } | null,
  width: number,
  height: number,
  padding = 6,
): MinimapFit {
  if (!bounds || bounds.w <= 0 || bounds.h <= 0) {
    return { scale: 1, offsetX: 0, offsetY: 0 };
  }

  const usableWidth = Math.max(1, width - padding * 2);
  const usableHeight = Math.max(1, height - padding * 2);
  const scale = Math.min(usableWidth / bounds.w, usableHeight / bounds.h);

  return {
    scale,
    offsetX: padding + (usableWidth - bounds.w * scale) / 2 - bounds.x * scale,
    offsetY: padding + (usableHeight - bounds.h * scale) / 2 - bounds.y * scale,
  };
}

/** A point on the minimap, back in graph coordinates. */
export function minimapToWorld(point: Point, fit: MinimapFit): Point {
  return {
    x: (point.x - fit.offsetX) / fit.scale,
    y: (point.y - fit.offsetY) / fit.scale,
  };
}
