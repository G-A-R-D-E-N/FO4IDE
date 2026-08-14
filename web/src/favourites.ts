










const KEY = 'favouriteRecords';

export interface Favourite {
  formKey: string;
  label: string;
  plugin: string;
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


  window.dispatchEvent(new CustomEvent(FAVOURITES_CHANGED));
}

export const FAVOURITES_CHANGED = 'fo4re:favourites-changed';

export function isFavourite(formKey: string): boolean {
  return readFavourites().some(f => f.formKey === formKey);
}


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
