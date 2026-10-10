import { useEffect, useId, useRef, useState } from 'react';
import { useMe, useSignOut } from '../lib/queries';
import { signInHref } from '../lib/router';
import { useToast } from '../lib/toast';
import { Button } from './Button';

/**
 * Top-bar account control: "Sign in" when signed out; otherwise a button that opens a small menu with the account and
 * "Sign out". Keyboard: Enter/Space opens it, Escape closes it and returns focus to the button.
 */
export function AccountMenu() {
  const me = useMe();
  const signOut = useSignOut();
  const toast = useToast();
  const [open, setOpen] = useState(false);
  const button = useRef<HTMLButtonElement>(null);
  const root = useRef<HTMLDivElement>(null);
  const menuId = useId();

  // Closing on a click elsewhere or on Escape talks to the document: an effect, only while open.
  useEffect(() => {
    if (!open) return;
    const onClick = (e: MouseEvent) => { if (!root.current?.contains(e.target as Node)) setOpen(false); };
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') { setOpen(false); button.current?.focus(); } };
    document.addEventListener('mousedown', onClick);
    document.addEventListener('keydown', onKey);
    return () => { document.removeEventListener('mousedown', onClick); document.removeEventListener('keydown', onKey); };
  }, [open]);

  if (me.isPending) return <span className="account-placeholder" aria-hidden="true" />;
  if (!me.data) return <a className="signin-link" href={signInHref()}>Sign in</a>;

  const account = me.data;
  return (
    <div className="account" ref={root}>
      <button ref={button} type="button" className="account-button" aria-haspopup="true" aria-expanded={open} aria-controls={menuId}
        onClick={() => setOpen((o) => !o)}>
        <span className="avatar" aria-hidden="true">{account.name.slice(0, 1).toUpperCase()}</span>
        <span className="account-name">{account.name}</span>
      </button>
      {open && (
        <div id={menuId} className="account-menu card">
          <p className="small"><strong>{account.name}</strong><br /><span className="muted">{account.email}</span></p>
          {account.roles.some((r) => r !== 'Customer') && <p className="small muted">{account.roles.filter((r) => r !== 'Customer').join(', ')}</p>}
          <a href="#/mine" onClick={() => setOpen(false)}>My bookings</a>
          <Button variant="ghost" disabled={signOut.isPending}
            onClick={() => signOut.mutate(undefined, { onSuccess: () => { setOpen(false); toast({ text: 'Signed out.' }); location.hash = '#/'; } })}>
            Sign out
          </Button>
        </div>
      )}
    </div>
  );
}
