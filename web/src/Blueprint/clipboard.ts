import { useCallback, useEffect, useRef, useState } from 'react';
import type { BpDocument, BpNode, BpWire } from './graphModel';
import type { BpSelection, GraphAction, GraphState } from './graphReducer';

export interface GraphClip {
  nodes: BpNode[];
  wires: BpWire[];
}

/** How far a paste lands from what it came from, and from the paste before it. */
export const PASTE_OFFSET = 24;

/**
 * The selected nodes, plus only the wires whose BOTH ends are selected.
 *
 * A wire with one end outside the selection is not a wire that can be pasted: the copy would either
 * dangle or, worse, silently attach to the original. Dropping it here rather than at paste time
 * means what is on the clipboard is already exactly what will appear.
 *
 * The result is deep copied, so editing or deleting the source after a copy cannot reach back and
 * change what is pending on the clipboard.
 */
export function copySelection(doc: BpDocument, selection: BpSelection): GraphClip {
  const ids = new Set(selection.nodes);

  return structuredClone({
    nodes: doc.nodes.filter((n) => ids.has(n.id)),
    wires: doc.wires.filter((w) => ids.has(w.from.node) && ids.has(w.to.node)),
  });
}

/**
 * Copy, cut and paste for the canvas, including the keyboard bindings.
 *
 * The listener lives here rather than in the panel on purpose. The reducer has supported PASTE since
 * it was written and nothing ever called it, which is the defect this closes; keeping the binding
 * next to the operation is what stops the two drifting apart again, and it lets the wiring be tested
 * without standing up the whole panel and its host bridge.
 */
export function useGraphClipboard(state: GraphState, dispatch: (action: GraphAction) => void) {
  const clip = useRef<GraphClip | null>(null);
  const pastes = useRef(0);
  const [canPaste, setCanPaste] = useState(false);

  const copy = useCallback(() => {
    if (state.selection.nodes.length === 0) return false;
    clip.current = copySelection(state.doc, state.selection);
    pastes.current = 0;
    setCanPaste(true);
    return true;
  }, [state.doc, state.selection]);

  const cut = useCallback(() => {
    if (!copy()) return false;
    dispatch({ type: 'DELETE_SELECTION' });
    return true;
  }, [copy, dispatch]);

  const paste = useCallback(() => {
    const pending = clip.current;
    if (!pending || pending.nodes.length === 0) return false;

    // Cascade, so pasting three times gives three visible copies rather than one stack.
    pastes.current += 1;
    const shift = PASTE_OFFSET * pastes.current;

    dispatch({ type: 'PASTE', nodes: pending.nodes, wires: pending.wires, dx: shift, dy: shift });
    return true;
  }, [dispatch]);

  const duplicate = useCallback(() => {
    if (state.selection.nodes.length === 0) return false;

    const picked = copySelection(state.doc, state.selection);
    dispatch({
      type: 'PASTE',
      nodes: picked.nodes,
      wires: picked.wires,
      dx: PASTE_OFFSET,
      dy: PASTE_OFFSET,
    });
    return true;
  }, [state.doc, state.selection, dispatch]);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      // A field has its own clipboard. Copying nodes because someone copied text out of the script
      // name box would be the kind of surprise that makes people stop trusting the shortcut.
      const tag = (event.target as HTMLElement | null)?.tagName;
      if (tag === 'INPUT' || tag === 'TEXTAREA') return;
      if (!event.ctrlKey && !event.metaKey) return;

      const handled =
        event.key.toLowerCase() === 'c' ? copy()
        : event.key.toLowerCase() === 'x' ? cut()
        : event.key.toLowerCase() === 'v' ? paste()
        : event.key.toLowerCase() === 'd' ? duplicate()
        : false;

      if (handled) event.preventDefault();
    };

    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [copy, cut, paste, duplicate]);

  return { copy, cut, paste, duplicate, canPaste };
}
