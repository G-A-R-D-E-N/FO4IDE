// Keep the WebView pinned to the SPA.
//
// This app is a HashRouter SPA (/#/main) served over a loopback port and driven entirely through
// fetch('/rpc'); it should never perform a browser top-level navigation. Photino/WebKitGTK cannot
// veto a same-window navigation (it exposes no navigation-starting event), so if an <a href> that
// leaves the SPA is ever introduced, a plain click would break the window onto a blank/secondary
// page. This capture-phase guard neutralises exactly that click. Today the app has no such anchor,
// so this changes nothing visible -- it is a backstop against a future regression, and the server's
// /#/main redirect is the belt to this suspenders.
//
// Left alone: in-SPA hash links (href="#..."), download anchors (the crash-risk report export), and
// anchors that explicitly opt into a new window (target="_blank").

function isInSpaOrHarmless(a: HTMLAnchorElement): boolean {
  if (a.hasAttribute('download')) return true;
  if (a.target === '_blank') return true;
  const href = a.getAttribute('href') ?? '';
  return href === '' || href.startsWith('#');
}

if (typeof document !== 'undefined') {
  // Capture phase, so this runs before any component handler that might forget to preventDefault.
  document.addEventListener('click', (e) => {
    const el = e.target as HTMLElement | null;
    const a = el?.closest?.('a[href]') as HTMLAnchorElement | null;
    if (!a || isInSpaOrHarmless(a)) return;
    e.preventDefault();
    console.warn('[nav-guard] blocked top-level navigation to', a.getAttribute('href'));
  }, true);
}
