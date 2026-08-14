// Favourite records: the shared store behind the star in the detail rail and the Favourites section
// in the Explorer.
//
// This exists because favourites used to be write-only. The rail wrote a list of bare FormKeys to
// localStorage and nothing anywhere read it back, so a starred record could never be found again. A
// bare FormKey is also not enough to build a list a human can use -- "000801:XDI.esm" says nothing --
// so an entry now carries the label and plugin captured at the moment it was starred.
//
// Entries written by the old version are plain strings; they are migrated on read rather than
// discarded, so nobody loses what they had starred.

const KEY = 'favouriteRecords';

export interface Favourite {
  formKey: string;
  label: string;    // EditorID where known, else the FormKey
  plugin: string;   // the winning plugin at the time it was starred, for display only
}

type StoredFavourite = string | Partial<Favourite>;

function normalise(raw: StoredFavourite): Favourite | null {
  if (typeof raw === 'string') return raw ? { formKey: raw, label: raw, plugin: '' } : null;
  if (raw && typeof raw.formKey === 'string' && raw.formKey.length > 0) {
    return { formKey: raw.formKey, label: raw.label || raw.formKey, plugin: raw.plugin || '' };
  }
  return null;
}

export function readFavourites(): Favourite[] {
  try {
    const parsed = JSON.parse(localStorage.getItem(KEY) || '[]');
    if (!Array.isArray(parsed)) return [];
    return parsed.map(normalise).filter((f): f is Favourite => f !== null);
  } catch {
    return [];
  }
}

function write(list: Favourite[]) {
  localStorage.setItem(KEY, JSON.stringify(list));
  // Same-document storage events do not fire, so the Explorer would not notice a star toggled in the
  // rail. Announce it explicitly; both sides listen.
  window.dispatchEvent(new CustomEvent(FAVOURITES_CHANGED));
}

export const FAVOURITES_CHANGED = 'fo4re:favourites-changed';

export function isFavourite(formKey: string): boolean {
  return readFavourites().some(f => f.formKey === formKey);
}

/** Adds or removes, and returns true when the record is a favourite afterwards. */
export function toggleFavourite(entry: Favourite): boolean {
  const list = readFavourites();
  const at = list.findIndex(f => f.formKey === entry.formKey);
  if (at === -1) {
    write([...list, entry]);
    return true;
  }
  list.splice(at, 1);
  write(list);
  return false;
}

export function removeFavourite(formKey: string) {
  write(readFavourites().filter(f => f.formKey !== formKey));
}
