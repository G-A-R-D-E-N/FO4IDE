import { useCallback, useState } from 'react';
import type { BpDocument } from './graphModel';

/**
 * Whether the document has changed since it was last written to disk.
 *
 * The comparison is by identity, not by value, which is exactly right here: the reducer never
 * mutates and always returns a new document object for a real edit, so a changed reference means a
 * changed graph and an unchanged one means nothing happened. Deep comparing a three hundred node
 * graph on every render to learn the same thing would be pure waste.
 *
 * This was a `useRef` read during render. That is a `react-hooks/refs` error, and it worked only
 * because every assignment happened inside a helper whose `finally` toggled an unrelated `busy`
 * flag, and that re-render recomputed the flag. Nothing stated the dependency, so an assignment
 * added outside that helper, or a helper that stopped toggling, would have silently frozen the
 * unsaved marker with no test able to see it. Holding it as state makes the marker a function of
 * the render rather than of a coincidence elsewhere.
 */
export function useSavedDoc(doc: BpDocument) {
  const [saved, setSaved] = useState<BpDocument>(doc);

  return {
    dirty: doc !== saved,
    markSaved: useCallback((written: BpDocument) => setSaved(written), []),
  };
}
