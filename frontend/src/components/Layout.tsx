import { useEffect, useRef, type ReactNode } from 'react';
import type { Route } from '../lib/router';
import { AccountMenu } from './AccountMenu';
import { useIsAdmin, useIsStaff } from '../lib/queries';

export function Layout({ venueName, route, children }: { venueName: string; route: Route; children: ReactNode }) {
  // Only a way there for staff; the API refuses everyone else (library 10.2: hiding a link is not a control).
  const isStaff = useIsStaff();
  const isAdmin = useIsAdmin();
  // Moving to another page puts keyboard focus at the start of its content, as a new page load would, so the next Tab
  // and a screen reader start there, not at an element that no longer exists. (DOM focus is the outside world: an
  // effect.) Not on first load, and not when only the day or the selection changes.
  const main = useRef<HTMLElement>(null);
  const page = `${route.name}:${'id' in route ? route.id : ''}`;
  const shown = useRef(page);
  useEffect(() => {
    if (shown.current === page) return;
    shown.current = page;
    main.current?.focus({ preventScroll: true });
  }, [page]);
  return (
    <>
      <header className="topbar">
        <a className="brand" href="#/"><span className="logo" aria-hidden="true" />{venueName || 'Bookings'}</a>
        <nav className="nav">
          <a href="#/" aria-current={route.name === 'home' || route.name === 'resource' ? 'page' : undefined}>Book</a>
          <a href="#/mine" aria-current={route.name === 'mine' || route.name === 'booking' ? 'page' : undefined}>My bookings</a>
          {isStaff && <a href="#/staff" aria-current={route.name === 'staff' ? 'page' : undefined}>Schedule</a>}
          {isAdmin && <a href="#/admin" aria-current={route.name === 'admin' ? 'page' : undefined}>Admin</a>}
          <AccountMenu />
        </nav>
      </header>
      <main className="page" tabIndex={-1} ref={main}>{children}</main>
    </>
  );
}
