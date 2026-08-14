import { describe, expect, it } from 'vitest';
import {
  COLUMN_GAP,
  LAYOUT_ORIGIN,
  autoLayout,
  graphBounds,
  minimapFit,
  minimapToWorld,
} from './layout';
import {
  NODE_WIDTH,
  emptyDocument,
  nodeHeight,
  type BpDocument,
  type BpNode,
  type BpNodeDef,
  type BpPinDef,
  type BpWire,
} from './graphModel';

// Layout is pure, so the arrangement can be checked as arithmetic rather than looked at.

const pin = (id: string, kind: 'exec' | 'data', dir: 'in' | 'out'): BpPinDef => ({
  id,
  name: id,
  kind,
  dir,
  dataType: kind === 'exec' ? 'none' : 'int',
  optional: false,
});

const DEFS: Record<string, BpNodeDef> = {
  // A statement: exec in and out.
  step: {
    type: 'step', label: 'Step', category: 'Flow', kind: 'Call', isPure: false, isGlobal: false,
    pins: [pin('exec', 'exec', 'in'), pin('then', 'exec', 'out'), pin('ret', 'data', 'out')],
  },
  // A branch: one exec in, two exec out.
  branch: {
    type: 'branch', label: 'Branch', category: 'Flow', kind: 'Branch', isPure: false, isGlobal: false,
    pins: [
      pin('exec', 'exec', 'in'), pin('then', 'exec', 'out'), pin('else', 'exec', 'out'),
      pin('cond', 'data', 'in'),
    ],
  },
  // An expression: no exec pins at all.
  literal: {
    type: 'literal', label: 'Literal', category: 'Values', kind: 'Literal', isPure: true, isGlobal: false,
    pins: [pin('value', 'data', 'out'), pin('a', 'data', 'in')],
  },
};

const node = (id: string, def: keyof typeof DEFS, x = 0, y = 0): BpNode =>
  ({ id, def, kind: DEFS[def].kind, x, y });

const wire = (id: string, from: string, fromPin: string, to: string, toPin: string): BpWire =>
  ({ id, from: { node: from, pin: fromPin }, to: { node: to, pin: toPin } });

function doc(nodes: BpNode[], wires: BpWire[] = []): BpDocument {
  const d = emptyDocument('Fixture');
  d.nodes = nodes;
  d.wires = wires;
  return d;
}

const COLUMN = NODE_WIDTH + COLUMN_GAP;

describe('autoLayout', () => {
  it('returns nothing for an empty graph', () => {
    expect(autoLayout(doc([]), DEFS)).toEqual({});
  });

  it('places every node', () => {
    const d = doc([node('a', 'step'), node('b', 'step'), node('c', 'literal')]);

    const at = autoLayout(d, DEFS);

    expect(Object.keys(at).sort()).toEqual(['a', 'b', 'c']);
  });

  it('puts an exec successor in the next column', () => {
    const d = doc(
      [node('a', 'step'), node('b', 'step')],
      [wire('w1', 'a', 'then', 'b', 'exec')],
    );

    const at = autoLayout(d, DEFS);

    expect(at.a.x).toBe(LAYOUT_ORIGIN.x);
    expect(at.b.x).toBe(LAYOUT_ORIGIN.x + COLUMN);
  });

  it('uses the longest path, so a node never sits left of something that must run first', () => {
    // a -> b -> c and also a -> c. c must land in column 2, not column 1.
    const d = doc(
      [node('a', 'step'), node('b', 'step'), node('c', 'step')],
      [
        wire('w1', 'a', 'then', 'b', 'exec'),
        wire('w2', 'b', 'then', 'c', 'exec'),
        wire('w3', 'a', 'then', 'c', 'exec'),
      ],
    );

    const at = autoLayout(d, DEFS);

    expect(at.c.x).toBe(LAYOUT_ORIGIN.x + COLUMN * 2);
  });

  it('stacks the arms of a branch in one column without overlapping', () => {
    const d = doc(
      [node('br', 'branch'), node('t', 'step'), node('e', 'step')],
      [wire('w1', 'br', 'then', 't', 'exec'), wire('w2', 'br', 'else', 'e', 'exec')],
    );

    const at = autoLayout(d, DEFS);

    expect(at.t.x).toBe(at.e.x);
    const gap = Math.abs(at.t.y - at.e.y);
    expect(gap).toBeGreaterThanOrEqual(nodeHeight(DEFS.step));
  });

  it('puts an expression just left of what reads it, not at the far left', () => {
    // The literal feeds a node three columns in. Left at column 0 it would drag a wire across the
    // whole canvas.
    const d = doc(
      [node('a', 'step'), node('b', 'step'), node('c', 'branch'), node('lit', 'literal')],
      [
        wire('w1', 'a', 'then', 'b', 'exec'),
        wire('w2', 'b', 'then', 'c', 'exec'),
        wire('w3', 'lit', 'value', 'c', 'cond'),
      ],
    );

    const at = autoLayout(d, DEFS);

    expect(at.c.x).toBe(LAYOUT_ORIGIN.x + COLUMN * 2);
    expect(at.lit.x).toBe(LAYOUT_ORIGIN.x + COLUMN);
  });

  it('settles a chain of expressions', () => {
    const d = doc(
      [node('a', 'step'), node('b', 'step'), node('outer', 'literal'), node('inner', 'literal')],
      [
        wire('w1', 'a', 'then', 'b', 'exec'),
        wire('w2', 'outer', 'value', 'b', 'exec'),
        wire('w3', 'inner', 'value', 'outer', 'a'),
      ],
    );

    const at = autoLayout(d, DEFS);

    expect(at.outer.x).toBeLessThan(at.b.x);
    expect(at.inner.x).toBeLessThan(at.outer.x);
  });

  it('never overlaps two nodes', () => {
    const nodes = Array.from({ length: 12 }, (_, i) => node('n' + i, i % 3 === 0 ? 'branch' : 'step'));
    const wires = nodes.slice(1).map((n, i) => wire('w' + i, nodes[i].id, 'then', n.id, 'exec'));

    const at = autoLayout(doc(nodes, wires), DEFS);

    const boxes = nodes.map((n) => ({
      x: at[n.id].x, y: at[n.id].y, w: NODE_WIDTH, h: nodeHeight(DEFS[n.def]),
    }));

    for (let i = 0; i < boxes.length; i++) {
      for (let j = i + 1; j < boxes.length; j++) {
        const a = boxes[i];
        const b = boxes[j];
        const overlaps =
          a.x < b.x + b.w && b.x < a.x + a.w && a.y < b.y + b.h && b.y < a.y + a.h;
        expect(overlaps).toBe(false);
      }
    }
  });

  it('terminates on a loop instead of pushing the target rightwards for ever', () => {
    // The back edge would otherwise raise its own target's rank on every pass.
    const d = doc(
      [node('a', 'step'), node('b', 'step'), node('c', 'step')],
      [
        wire('w1', 'a', 'then', 'b', 'exec'),
        wire('w2', 'b', 'then', 'c', 'exec'),
        wire('w3', 'c', 'then', 'b', 'exec'),
      ],
    );

    const at = autoLayout(d, DEFS);

    expect(Object.keys(at)).toHaveLength(3);
    for (const id of ['a', 'b', 'c']) {
      expect(Number.isFinite(at[id].x)).toBe(true);
      expect(at[id].x).toBeLessThan(LAYOUT_ORIGIN.x + COLUMN * 4);
    }
  });

  it('is stable, so laying out twice changes nothing', () => {
    const d = doc(
      [node('a', 'step', 500, 900), node('b', 'step', 12, 3), node('lit', 'literal', 77, 4)],
      [wire('w1', 'a', 'then', 'b', 'exec'), wire('w2', 'lit', 'value', 'b', 'exec')],
    );

    const first = autoLayout(d, DEFS);
    const applied = doc(
      d.nodes.map((n) => ({ ...n, ...first[n.id] })),
      d.wires,
    );

    expect(autoLayout(applied, DEFS)).toEqual(first);
  });

  it('ignores data wires when deciding what runs after what', () => {
    // Only exec wires order statements. A data wire between two statements must not move either.
    const d = doc(
      [node('a', 'step'), node('b', 'step')],
      [wire('w1', 'a', 'ret', 'b', 'cond')],
    );

    const at = autoLayout(d, DEFS);

    expect(at.a.x).toBe(at.b.x);
  });

  it('survives a node whose definition is not on the palette', () => {
    const d = doc([node('a', 'step'), { id: 'x', def: 'missing', kind: 'Unknown', x: 0, y: 0 }]);

    const at = autoLayout(d, DEFS);

    expect(Object.keys(at).sort()).toEqual(['a', 'x']);
  });
});

describe('graphBounds', () => {
  it('is null for an empty graph', () => {
    expect(graphBounds(doc([]), DEFS)).toBeNull();
  });

  it('covers every node including its height', () => {
    const d = doc([node('a', 'step', 0, 0), node('b', 'step', 300, 200)]);

    const bounds = graphBounds(d, DEFS)!;

    expect(bounds.x).toBe(0);
    expect(bounds.y).toBe(0);
    expect(bounds.w).toBe(300 + NODE_WIDTH);
    expect(bounds.h).toBe(200 + nodeHeight(DEFS.step));
  });

  it('handles negative coordinates', () => {
    const d = doc([node('a', 'step', -400, -50)]);

    const bounds = graphBounds(d, DEFS)!;

    expect(bounds.x).toBe(-400);
    expect(bounds.y).toBe(-50);
  });
});

describe('minimapFit', () => {
  it('uses one scale for both axes so the overview is not distorted', () => {
    const fit = minimapFit({ x: 0, y: 0, w: 1000, h: 100 }, 200, 200, 0);

    expect(fit.scale).toBe(0.2);
  });

  it('centres what it fits', () => {
    const fit = minimapFit({ x: 0, y: 0, w: 100, h: 100 }, 200, 100, 0);

    // Square graph in a wide box: scaled to the height, centred horizontally.
    expect(fit.scale).toBe(1);
    expect(fit.offsetX).toBe(50);
    expect(fit.offsetY).toBe(0);
  });

  it('accounts for a graph that does not start at the origin', () => {
    const fit = minimapFit({ x: 500, y: 500, w: 100, h: 100 }, 100, 100, 0);

    expect(fit.offsetX).toBe(-500);
    expect(fit.offsetY).toBe(-500);
  });

  it('is harmless on an empty graph', () => {
    const fit = minimapFit(null, 200, 200);

    expect(fit.scale).toBe(1);
    expect(Number.isFinite(fit.offsetX)).toBe(true);
  });

  it('round trips a point back to graph coordinates', () => {
    const bounds = { x: 120, y: -40, w: 800, h: 600 };
    const fit = minimapFit(bounds, 180, 120);

    const centre = {
      x: fit.offsetX + (bounds.x + bounds.w / 2) * fit.scale,
      y: fit.offsetY + (bounds.y + bounds.h / 2) * fit.scale,
    };

    const world = minimapToWorld(centre, fit);

    expect(world.x).toBeCloseTo(bounds.x + bounds.w / 2, 6);
    expect(world.y).toBeCloseTo(bounds.y + bounds.h / 2, 6);
  });
});
