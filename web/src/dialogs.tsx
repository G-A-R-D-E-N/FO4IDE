// App-wide replacement for window.confirm / window.prompt / window.alert.
//
// Native dialogs were wrong here for three reasons: in the Photino/WebKitGTK window they render as a
// bare OS box that does not look like the app, they block the whole webview thread, and prompt()
// gives a single unvalidated text field for questions that are really a choice from a list (which is
// why "into which plugin?" was a filename you had to know by heart).
//
// The API is promise-shaped so a call site reads almost the same as the native one it replaces:
//     if (!(await confirm({ ... }))) return;
//     const name = await prompt({ ... });
//     const picked = await pickPlugin({ ... });
// Every one returns null/false on cancel, so existing early-return logic survives unchanged.

import { createContext, useCallback, useContext, useRef, useState, type ReactNode } from 'react';
import PluginTargetDialog, { type TargetRequest, type TargetResult } from './PluginTargetDialog';
import './PluginTargetDialog.css';

export interface ConfirmRequest {
  title: string;
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
  /** Red confirm button, for anything that destroys or rewrites data. */
  danger?: boolean;
}

export interface PromptRequest {
  title: string;
  label: string;
  defaultValue?: string;
  placeholder?: string;
  description?: string;
  confirmLabel?: string;
  /** Reject the value with this message when it returns one. */
  validate?: (value: string) => string | null;
}

interface DialogApi {
  confirm: (req: ConfirmRequest) => Promise<boolean>;
  prompt: (req: PromptRequest) => Promise<string | null>;
  pickPlugin: (req: TargetRequest) => Promise<TargetResult | null>;
}

const Ctx = createContext<DialogApi | null>(null);

export function useDialogs(): DialogApi {
  const api = useContext(Ctx);
  if (!api) throw new Error('useDialogs must be used inside <DialogProvider>');
  return api;
}

type Pending =
  | { kind: 'confirm'; req: ConfirmRequest }
  | { kind: 'prompt'; req: PromptRequest }
  | { kind: 'plugin'; req: TargetRequest };

export function DialogProvider({ children }: { children: ReactNode }) {
  const [pending, setPending] = useState<Pending | null>(null);
  const resolver = useRef<((v: unknown) => void) | null>(null);

  const open = useCallback(<T,>(p: Pending) => new Promise<T>(resolve => {
    resolver.current = resolve as (v: unknown) => void;
    setPending(p);
  }), []);

  const settle = useCallback((value: unknown) => {
    setPending(null);
    resolver.current?.(value);
    resolver.current = null;
  }, []);

  const api: DialogApi = {
    confirm: req => open<boolean>({ kind: 'confirm', req }),
    prompt: req => open<string | null>({ kind: 'prompt', req }),
    pickPlugin: req => open<TargetResult | null>({ kind: 'plugin', req }),
  };

  return (
    <Ctx.Provider value={api}>
      {children}
      {pending?.kind === 'confirm' && (
        <ConfirmDialog req={pending.req} onResolve={v => settle(v)} />
      )}
      {pending?.kind === 'prompt' && (
        <PromptDialog req={pending.req} onResolve={v => settle(v)} />
      )}
      {pending?.kind === 'plugin' && (
        <PluginTargetDialog request={pending.req} onResolve={v => settle(v)} />
      )}
    </Ctx.Provider>
  );
}

function ConfirmDialog({ req, onResolve }: { req: ConfirmRequest; onResolve: (v: boolean) => void }) {
  return (
    <div className="ptd-overlay" onClick={() => onResolve(false)}>
      <div
        className="ptd-modal glass-panel ptd-modal-sm"
        onClick={e => e.stopPropagation()}
        onKeyDown={e => {
          if (e.key === 'Escape') { e.stopPropagation(); onResolve(false); }
          if (e.key === 'Enter') { e.preventDefault(); onResolve(true); }
        }}
        tabIndex={-1}
        ref={el => el?.focus()}
      >
        <div className="ptd-header"><span className="ptd-title">{req.title}</span></div>
        {/* pre-wrap: several callers pass a multi-line dry-run report straight through. */}
        <div className="ptd-desc ptd-desc-block">{req.message}</div>
        <div className="ptd-actions">
          <span className="ptd-keys">Enter to confirm, Esc to cancel</span>
          <button className="ptd-btn" onClick={() => onResolve(false)}>{req.cancelLabel ?? 'Cancel'}</button>
          <button
            className={`ptd-btn ${req.danger ? 'ptd-btn-danger' : 'ptd-btn-primary'}`}
            onClick={() => onResolve(true)}
          >
            {req.confirmLabel ?? 'OK'}
          </button>
        </div>
      </div>
    </div>
  );
}

function PromptDialog({ req, onResolve }: { req: PromptRequest; onResolve: (v: string | null) => void }) {
  const [value, setValue] = useState(req.defaultValue ?? '');
  const error = req.validate ? req.validate(value) : null;
  const ok = () => { if (!error) onResolve(value); };
  return (
    <div className="ptd-overlay" onClick={() => onResolve(null)}>
      <div
        className="ptd-modal glass-panel ptd-modal-sm"
        onClick={e => e.stopPropagation()}
        onKeyDown={e => {
          if (e.key === 'Escape') { e.stopPropagation(); onResolve(null); }
          if (e.key === 'Enter' && !error) { e.preventDefault(); ok(); }
        }}
      >
        <div className="ptd-header"><span className="ptd-title">{req.title}</span></div>
        {req.description && <div className="ptd-desc">{req.description}</div>}
        <label className="ptd-extra">
          <span>{req.label}</span>
          <input
            autoFocus
            value={value}
            placeholder={req.placeholder}
            onChange={e => setValue(e.target.value)}
            onFocus={e => e.currentTarget.select()}
          />
        </label>
        {error && <div className="ptd-note ptd-note-warn">{error}</div>}
        <div className="ptd-actions">
          <span className="ptd-keys">Enter to confirm, Esc to cancel</span>
          <button className="ptd-btn" onClick={() => onResolve(null)}>Cancel</button>
          <button className="ptd-btn ptd-btn-primary" onClick={ok} disabled={!!error}>
            {req.confirmLabel ?? 'OK'}
          </button>
        </div>
      </div>
    </div>
  );
}
