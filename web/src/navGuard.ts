
function isInSpaOrHarmless(a: HTMLAnchorElement): boolean {
  if (a.hasAttribute('download')) return true;
  if (a.target === '_blank') return true;
  const href = a.getAttribute('href') ?? '';
  return href === '' || href.startsWith('#');
}

if (typeof document !== 'undefined') {

  document.addEventListener('click', (e) => {
    const el = e.target as HTMLElement | null;
    const a = el?.closest?.('a[href]') as HTMLAnchorElement | null;
    if (!a || isInSpaOrHarmless(a)) return;
    e.preventDefault();
    console.warn('[nav-guard] blocked top-level navigation to', a.getAttribute('href'));
  }, true);
}
