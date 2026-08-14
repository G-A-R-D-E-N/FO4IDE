import { describe, expect, it } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { emptyDocument, type BpDocument } from './graphModel';
import { useSavedDoc } from './useSavedDoc';




const edit = (doc: BpDocument): BpDocument => ({ ...doc, nodes: [...doc.nodes] });

describe('useSavedDoc', () => {
  it('starts clean', () => {
    const doc = emptyDocument('Fixture');
    const { result } = renderHook(() => useSavedDoc(doc));

    expect(result.current.dirty).toBe(false);
  });

  it('goes dirty when the document changes', () => {
    const doc = emptyDocument('Fixture');
    const { result, rerender } = renderHook(({ d }) => useSavedDoc(d), {
      initialProps: { d: doc },
    });

    rerender({ d: edit(doc) });

    expect(result.current.dirty).toBe(true);
  });

  it('goes clean again once that document is marked saved', () => {
    const doc = emptyDocument('Fixture');
    const edited = edit(doc);
    const { result, rerender } = renderHook(({ d }) => useSavedDoc(d), {
      initialProps: { d: doc },
    });

    rerender({ d: edited });
    expect(result.current.dirty).toBe(true);

    act(() => result.current.markSaved(edited));
    rerender({ d: edited });

    expect(result.current.dirty).toBe(false);
  });

  it('stays dirty when what was written is not what is on screen', () => {


    const doc = emptyDocument('Fixture');
    const written = edit(doc);
    const editedSince = edit(written);

    const { result, rerender } = renderHook(({ d }) => useSavedDoc(d), {
      initialProps: { d: written },
    });

    act(() => result.current.markSaved(written));
    rerender({ d: editedSince });

    expect(result.current.dirty).toBe(true);
  });

  it('goes dirty again on the next edit after a save', () => {
    const doc = emptyDocument('Fixture');
    const { result, rerender } = renderHook(({ d }) => useSavedDoc(d), {
      initialProps: { d: doc },
    });

    act(() => result.current.markSaved(doc));
    rerender({ d: doc });
    expect(result.current.dirty).toBe(false);

    rerender({ d: edit(doc) });
    expect(result.current.dirty).toBe(true);
  });

  it('compares by identity, not by value', () => {


    const doc = emptyDocument('Fixture');
    const clone = structuredClone(doc);

    const { result, rerender } = renderHook(({ d }) => useSavedDoc(d), {
      initialProps: { d: doc },
    });

    rerender({ d: clone });

    expect(result.current.dirty).toBe(true);
  });

  it('keeps the same markSaved across renders', () => {


    const doc = emptyDocument('Fixture');
    const { result, rerender } = renderHook(({ d }) => useSavedDoc(d), {
      initialProps: { d: doc },
    });

    const first = result.current.markSaved;
    rerender({ d: edit(doc) });

    expect(result.current.markSaved).toBe(first);
  });
});
