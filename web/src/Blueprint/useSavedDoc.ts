import { useCallback, useState } from 'react';
import type { BpDocument } from './graphModel';

export function useSavedDoc(doc: BpDocument) {
  const [saved, setSaved] = useState<BpDocument>(doc);

  return {
    dirty: doc !== saved,
    markSaved: useCallback((written: BpDocument) => setSaved(written), []),
  };
}
