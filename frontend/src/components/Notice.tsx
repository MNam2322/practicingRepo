import type { ReactNode } from 'react';
import { Button } from './Button';

type Kind = 'ok' | 'error' | 'info';

/** An inline message box. Errors are announced at once (role="alert"); the rest politely (role="status"). */
export function Notice({ kind, children }: { kind: Kind; children: ReactNode }) {
  return <div className={`notice ${kind}`} role={kind === 'error' ? 'alert' : 'status'}>{children}</div>;
}

/**
 * An error with an optional "Try again". Shows the friendly message for the error code, never a raw status code,
 * and the trace id as a reference support can look up in the server logs (library 2.8 "Errors in web APIs").
 */
export function ErrorNotice({ message, traceId, retry }: { message: string; traceId?: string | null; retry?: () => void }) {
  return (
    <Notice kind="error">
      <p>{message}</p>
      {traceId && <p className="small trace">Reference: <code>{traceId}</code></p>}
      {retry && <Button variant="ghost" onClick={retry}>Try again</Button>}
    </Notice>
  );
}
