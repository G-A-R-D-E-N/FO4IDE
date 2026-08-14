
type Listener = (e: { data: unknown }) => void;

const HOST_OBJECTS = [
  'backend', 'appInterop', 'chat', 'settings', 'papyrus',
  'nif', 'material', 'masters', 'archive', 'audio', 'cell', 'graph',
] as const;

async function call(target: string, method: string, args: unknown[]): Promise<unknown> {
  const res = await fetch('/rpc', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ target, method, args }),
  });
  if (!res.ok) throw new Error(`${target}.${method}: HTTP ${res.status} ${res.statusText}`);
  const body = await res.json();
  if (!body.ok) throw new Error(`${target}.${method}: ${body.error ?? 'unknown error'}`);
  return body.value;
}

function hostObject(name: string) {
  return new Proxy(function () {} as unknown as Record<string, unknown>, {
    get(_t, prop) {
      if (typeof prop !== 'string') return undefined;

      if (prop === 'then' || prop === Symbol.toStringTag as unknown as string) return undefined;
      return (...args: unknown[]) => call(name, prop, args);
    },
  });
}

function install() {
  const listeners = new Set<Listener>();

  const webview = {
    hostObjects: Object.fromEntries(HOST_OBJECTS.map(n => [n, hostObject(n)])),
    addEventListener: (type: string, fn: Listener) => { if (type === 'message') listeners.add(fn); },
    removeEventListener: (type: string, fn: Listener) => { if (type === 'message') listeners.delete(fn); },
    postMessage: () => {  },
  };

  (window as unknown as { chrome: unknown }).chrome = { ...(window as any).chrome, webview };

  const source = new EventSource('/events');
  source.onmessage = ev => {
    let data: unknown;
    try { data = JSON.parse(ev.data); } catch { data = ev.data; }
    for (const fn of listeners) {
      try { fn({ data }); } catch (err) { console.error('host message listener failed', err); }
    }
  };
}

if (!(window as any).chrome?.webview) install();
