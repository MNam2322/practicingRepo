import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { ToastContext, type Toast, type ToastInput } from '../lib/toast';

const SHOW_MS = 5000;

/** Owns the toast list and renders it in a polite live region, so screen readers announce each message. */
export function Toaster({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);
  const nextId = useRef(1);

  const dismiss = useCallback((id: number) => setToasts((list) => list.filter((t) => t.id !== id)), []);
  const show = useCallback((input: ToastInput) => {
    const id = nextId.current++;
    setToasts((list) => [...list.slice(-2), { kind: 'ok', ...input, id }]);
  }, []);

  return (
    <ToastContext.Provider value={show}>
      {children}
      <div className="toasts" role="status" aria-live="polite">
        {toasts.map((t) => <ToastItem key={t.id} toast={t} onDone={dismiss} />)}
      </div>
    </ToastContext.Provider>
  );
}

function ToastItem({ toast, onDone }: { toast: Toast; onDone: (id: number) => void }) {
  // A timer is a real side effect (it talks to the clock), so an effect is the right tool here.
  useEffect(() => {
    const timer = setTimeout(() => onDone(toast.id), SHOW_MS);
    return () => clearTimeout(timer);
  }, [toast.id, onDone]);

  return (
    <div className={`toast ${toast.kind}`}>
      <span>{toast.text}</span>
      {toast.link && <a href={toast.link.href} onClick={() => onDone(toast.id)}>{toast.link.label}</a>}
      <button type="button" className="toast-close" aria-label="Dismiss" onClick={() => onDone(toast.id)}>×</button>
    </div>
  );
}
