import type { ReactNode } from 'react';
import type { Route } from '../lib/router';
import { AccountMenu } from './AccountMenu';
import { useIsAdmin, useIsStaff } from '../lib/queries';

export function Layout({ venueName, route, children }: { venueName: string; route: Route; children: ReactNode }) {
  // Only a way there for staff; the API refuses everyone else (library 10.2: hiding a link is not a control).
  const isStaff = useIsStaff();
  const isAdmin = useIsAdmin();
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
      <main className="page">{children}</main>
    </>
  );
}
