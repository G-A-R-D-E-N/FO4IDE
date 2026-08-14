import { describe, expect, it } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { emptyDocument, type BpDocument } from './graphModel';
import { useSavedDoc } from './useSavedDoc';

// The unsaved marker on the Save button. Held as state rather than read out of a ref during render,
// so that it is a function of the render and not of whether some other call happened to re-render.

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
    // A save is asynchronous. If the graph is edited while one is in flight, the file holds the
    // older document, so the marker has to stay on.
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
    // Deliberate. The reducer never mutates, so a new object means a real edit, and deep comparing
    // a large graph every render would buy nothing. A structural clone counts as a change.
    const doc = emptyDocument('Fixture');
    const clone = structuredClone(doc);

    const { result, rerender } = renderHook(({ d }) => useSavedDoc(d), {
      initialProps: { d: doc },
    });

    rerender({ d: clone });

    expect(result.current.dirty).toBe(true);
  });

  it('keeps the same markSaved across renders', () => {
    // It is a dependency of the save callback, so a new identity each render would re-create that
    // callback for no reason.
    const doc = emptyDocument('Fixture');
    const { result, rerender } = renderHook(({ d }) => useSavedDoc(d), {
      initialProps: { d: doc },
    });

    const first = result.current.markSaved;
    rerender({ d: edit(doc) });

    expect(result.current.markSaved).toBe(first);
  });
});
