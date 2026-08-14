import { describe, expect, it, vi } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { useReducer } from 'react';
import { emptyDocument, type BpDocument, type BpNode, type BpWire } from './graphModel';
import { copySelection, useGraphClipboard, PASTE_OFFSET } from './clipboard';
import { graphReducer, initialState } from './graphReducer';

const node = (id: string, x = 0, y = 0): BpNode => ({ id, def: 'branch', kind: 'Branch', x, y });

const wire = (id: string, from: string, to: string): BpWire => ({
  id,
  from: { node: from, pin: 'then' },
  to: { node: to, pin: 'exec' },
});

function doc(nodes: BpNode[], wires: BpWire[]): BpDocument {
  const d = emptyDocument('Fixture');
  d.nodes = nodes;
  d.wires = wires;
  return d;
}

/** The hook driven by the real reducer, which is how the panel wires it. */
function mount(nodes: BpNode[], wires: BpWire[], selected: string[]) {
  return renderHook(() => {
    const [state, dispatch] = useReducer(graphReducer, {
      ...initialState(doc(nodes, wires)),
      selection: { nodes: selected, wires: [] },
    });
    return { state, clipboard: useGraphClipboard(state, dispatch) };
  });
}

const press = (key: string, init: KeyboardEventInit = {}) =>
  act(() => {
    window.dispatchEvent(new KeyboardEvent('keydown', { key, ctrlKey: true, bubbles: true, ...init }));
  });

describe('copySelection', () => {
  it('keeps only wires with both ends selected', () => {
    const clip = copySelection(
      doc([node('a'), node('b'), node('c')], [wire('w1', 'a', 'b'), wire('w2', 'b', 'c')]),
      { nodes: ['a', 'b'], wires: [] },
    );

    expect(clip.nodes.map((n) => n.id)).toEqual(['a', 'b']);
    expect(clip.wires.map((w) => w.id)).toEqual(['w1']);
  });

  it('is a deep copy, so editing the source afterwards cannot change it', () => {
    const source = doc([node('a', 10, 10)], []);
    const clip = copySelection(source, { nodes: ['a'], wires: [] });

    source.nodes[0].x = 999;

    expect(clip.nodes[0].x).toBe(10);
  });

  it('copies nothing when nothing is selected', () => {
    const clip = copySelection(doc([node('a')], []), { nodes: [], wires: [] });
    expect(clip.nodes).toHaveLength(0);
  });
});

describe('keyboard wiring', () => {
  // The defect this closes is that the reducer supported paste and nothing ever called it, so the
  // point of these is that the KEYS reach the reducer, not that the reducer works.

  it('Ctrl+C then Ctrl+V adds a copy', () => {
    const { result } = mount([node('a')], [], ['a']);

    press('c');
    press('v');

    expect(result.current.state.doc.nodes).toHaveLength(2);
  });

  it('Ctrl+X removes the original and Ctrl+V brings it back', () => {
    const { result } = mount([node('a'), node('b')], [], ['a']);

    press('x');
    expect(result.current.state.doc.nodes.map((n) => n.id)).toEqual(['b']);

    press('v');
    expect(result.current.state.doc.nodes).toHaveLength(2);
    expect(result.current.state.doc.nodes.map((n) => n.id)).not.toContain('a');
  });

  it('Ctrl+D duplicates without touching the clipboard', () => {
    const { result } = mount([node('a')], [], ['a']);

    press('c');
    press('d');
    expect(result.current.state.doc.nodes).toHaveLength(2);

    // The clipboard still holds the original copy, so paste is unaffected by the duplicate.
    press('v');
    expect(result.current.state.doc.nodes).toHaveLength(3);
  });

  it('repeated pastes cascade instead of stacking', () => {
    const { result } = mount([node('a', 0, 0)], [], ['a']);

    press('c');
    press('v');
    press('v');

    const pasted = result.current.state.doc.nodes.filter((n) => n.id !== 'a');
    expect(pasted).toHaveLength(2);
    expect(pasted[0].x).toBe(PASTE_OFFSET);
    expect(pasted[1].x).toBe(PASTE_OFFSET * 2);
  });

  it('ignores the shortcut while typing in a field', () => {
    // Otherwise copying text out of the script name box would silently copy nodes as well.
    const { result } = mount([node('a')], [], ['a']);

    const input = document.createElement('input');
    document.body.appendChild(input);
    act(() => {
      input.dispatchEvent(new KeyboardEvent('keydown', { key: 'c', ctrlKey: true, bubbles: true }));
    });
    press('v');

    expect(result.current.state.doc.nodes).toHaveLength(1);
    document.body.removeChild(input);
  });

  it('does nothing on paste with an empty clipboard', () => {
    const { result } = mount([node('a')], [], ['a']);

    press('v');

    expect(result.current.state.doc.nodes).toHaveLength(1);
  });

  it('does nothing on copy with an empty selection', () => {
    const { result } = mount([node('a')], [], []);

    press('c');
    press('v');

    expect(result.current.state.doc.nodes).toHaveLength(1);
  });

  it('leaves a plain keypress alone', () => {
    const { result } = mount([node('a')], [], ['a']);

    press('c', { ctrlKey: false });
    press('v', { ctrlKey: false });

    expect(result.current.state.doc.nodes).toHaveLength(1);
  });

  it('carries internal wires through a copy and paste', () => {
    const { result } = mount([node('a'), node('b')], [wire('w1', 'a', 'b')], ['a', 'b']);

    press('c');
    press('v');

    expect(result.current.state.doc.wires).toHaveLength(2);
    const pasted = result.current.state.doc.wires.find((w) => w.id !== 'w1')!;
    expect(pasted.from.node).not.toBe('a');
    expect(pasted.to.node).not.toBe('b');
  });

  it('reports whether there is anything to paste', () => {
    const { result } = mount([node('a')], [], ['a']);

    expect(result.current.clipboard.canPaste).toBe(false);
    press('c');
    expect(result.current.clipboard.canPaste).toBe(true);
  });

  it('unbinds the listener when the canvas goes away', () => {
    const remove = vi.spyOn(window, 'removeEventListener');
    const { unmount } = mount([node('a')], [], ['a']);

    unmount();

    expect(remove).toHaveBeenCalledWith('keydown', expect.any(Function));
    remove.mockRestore();
  });
});
