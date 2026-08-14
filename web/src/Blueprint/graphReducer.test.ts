import { describe, expect, it } from 'vitest';
import { emptyDocument, type BpNode, type BpWire } from './graphModel';
import { graphReducer, initialState, type GraphState } from './graphReducer';

const node = (id: string, x = 0, y = 0): BpNode => ({
  id,
  def: 'branch',
  kind: 'Branch',
  x,
  y,
});

const wire = (id: string, from: string, to: string): BpWire => ({
  id,
  from: { node: from, pin: 'then' },
  to: { node: to, pin: 'exec' },
});

function withGraph(nodes: BpNode[], wires: BpWire[], selected: string[] = []): GraphState {
  const doc = emptyDocument('Fixture');
  doc.nodes = nodes;
  doc.wires = wires;
  const state = initialState(doc);
  return { ...state, selection: { nodes: selected, wires: [] } };
}

describe('PASTE', () => {
  it('gives every pasted node a new id and leaves the originals alone', () => {
    const state = withGraph([node('a'), node('b')], []);

    const next = graphReducer(state, {
      type: 'PASTE',
      nodes: [node('a'), node('b')],
      wires: [],
      dx: 20,
      dy: 20,
    });

    expect(next.doc.nodes).toHaveLength(4);

    const ids = next.doc.nodes.map((n) => n.id);
    expect(new Set(ids).size).toBe(4);
    expect(ids).toContain('a');
    expect(ids).toContain('b');
  });

  it('rewires a pasted wire to the copies, not to the originals', () => {

    const state = withGraph([node('a'), node('b')], [wire('w1', 'a', 'b')]);

    const next = graphReducer(state, {
      type: 'PASTE',
      nodes: [node('a'), node('b')],
      wires: [wire('w1', 'a', 'b')],
      dx: 0,
      dy: 0,
    });

    expect(next.doc.wires).toHaveLength(2);

    const pasted = next.doc.wires.find((w) => w.id !== 'w1')!;
    expect(pasted.from.node).not.toBe('a');
    expect(pasted.to.node).not.toBe('b');
    expect(next.doc.nodes.map((n) => n.id)).toContain(pasted.from.node);
    expect(next.doc.nodes.map((n) => n.id)).toContain(pasted.to.node);
    expect(pasted.from.pin).toBe('then');
    expect(pasted.to.pin).toBe('exec');
  });

  it('drops a wire whose other end was not copied', () => {

    const state = withGraph([node('a'), node('b')], [wire('w1', 'a', 'b')]);

    const next = graphReducer(state, {
      type: 'PASTE',
      nodes: [node('a')],
      wires: [wire('w1', 'a', 'b')],
      dx: 0,
      dy: 0,
    });

    expect(next.doc.wires).toHaveLength(1);
    expect(next.doc.wires[0].id).toBe('w1');
  });

  it('offsets the copy so it does not land exactly on the original', () => {
    const state = withGraph([node('a', 100, 200)], []);

    const next = graphReducer(state, {
      type: 'PASTE',
      nodes: [node('a', 100, 200)],
      wires: [],
      dx: 24,
      dy: 24,
    });

    const pasted = next.doc.nodes.find((n) => n.id !== 'a')!;
    expect(pasted.x).toBe(124);
    expect(pasted.y).toBe(224);
  });

  it('selects what it just pasted', () => {
    const state = withGraph([node('a')], [], ['a']);

    const next = graphReducer(state, {
      type: 'PASTE',
      nodes: [node('a')],
      wires: [],
      dx: 10,
      dy: 10,
    });

    expect(next.selection.nodes).toHaveLength(1);
    expect(next.selection.nodes[0]).not.toBe('a');
  });

  it('is undoable in one step', () => {
    const state = withGraph([node('a')], []);

    const pasted = graphReducer(state, {
      type: 'PASTE',
      nodes: [node('a')],
      wires: [],
      dx: 10,
      dy: 10,
    });
    const undone = graphReducer(pasted, { type: 'UNDO' });

    expect(undone.doc.nodes).toHaveLength(1);
    expect(undone.doc.nodes[0].id).toBe('a');
  });
});

describe('DELETE_SELECTION', () => {
  it('removes wires attached to a deleted node', () => {

    const state = withGraph([node('a'), node('b')], [wire('w1', 'a', 'b')], ['a']);

    const next = graphReducer(state, { type: 'DELETE_SELECTION' });

    expect(next.doc.nodes.map((n) => n.id)).toEqual(['b']);
    expect(next.doc.wires).toHaveLength(0);
  });
});

describe('history', () => {
  it('does not record a selection change', () => {
    const state = withGraph([node('a')], []);

    const selected = graphReducer(state, { type: 'SELECT', ids: ['a'] });

    expect(selected.past).toHaveLength(0);
  });

  it('drops the redo stack once a new edit lands', () => {
    const state = withGraph([node('a')], []);

    const moved = graphReducer(state, { type: 'MOVE_NODES', ids: ['a'], dx: 5, dy: 5 });
    const undone = graphReducer(moved, { type: 'UNDO' });
    expect(undone.future).toHaveLength(1);

    const edited = graphReducer(undone, { type: 'MOVE_NODES', ids: ['a'], dx: 1, dy: 1 });
    expect(edited.future).toHaveLength(0);
  });
});
