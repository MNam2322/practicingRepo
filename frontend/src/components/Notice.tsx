import type { ReactNode } from 'react';
import { Button } from './Button';

type Kind = 'ok' | 'error' | 'info';

/** An inline message box. Errors are announced at once (role="alert"); the rest politely (role="status"). */
export function Notice({ kind, children }: { kind: Kind; children: ReactNode }) {
  return <div className={`notice ${kind}`} role={kind === 'error' ? 'alert' : 'status'}>{children}</div>;
}

/** An error with an optional "Try again". Shows the API's problem title, never a raw status code. */
export function ErrorNotice({ message, retry }: { message: string; retry?: () => void }) {
  return (
    <Notice kind="error">
      <p>{message}</p>
      {retry && <Button variant="ghost" onClick={retry}>Try again</Button>}
    </Notice>
  );
}
