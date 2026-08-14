


const BY_TYPE: Record<string, string> = {
  int: '--chip-5',
  float: '--chip-8',
  bool: '--chip-2',
  string: '--chip-6',
  var: '--chip-4',
  none: '--text-muted',
};

const OBJECT_COLOR = '--chip-3';
const FALLBACK = '--chip-7';

export function pinColorVar(dataType: string): string {
  const base = (dataType.endsWith('[]') ? dataType.slice(0, -2) : dataType).toLowerCase();
  if (!base) return `var(${FALLBACK})`;

  const known = BY_TYPE[base];
  if (known) return `var(${known})`;



  return `var(${OBJECT_COLOR})`;
}

export const pinColorFor = pinColorVar;
export const objectPinColor = `var(${OBJECT_COLOR})`;
export const fallbackPinColor = `var(${FALLBACK})`;
