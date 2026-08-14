
const CHIP_COUNT = 8;

function hashString(s: string): number {
  let h = 0;
  for (let i = 0; i < s.length; i++) {
    h = (h * 31 + s.charCodeAt(i)) | 0;
  }
  return Math.abs(h);
}

export function pluginColorVar(name: string): string {

  let h = hashString(name.toLowerCase());
  h = (h ^ (h >>> 16)) >>> 0;
  return `var(--chip-${(h % CHIP_COUNT) + 1})`;
}

export function pluginBadge(name: string): string {
  const m = name.match(/[a-z0-9]/i);
  return (m ? m[0] : '?').toUpperCase();
}
