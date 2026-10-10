import type { ReactNode } from 'react';
import type { Route } from '../lib/router';
import { AccountMenu } from './AccountMenu';

export function Layout({ venueName, route, children }: { venueName: string; route: Route; children: ReactNode }) {
  return (
    <>
      <header className="topbar">
        <a className="brand" href="#/"><span className="logo" aria-hidden="true" />{venueName || 'Bookings'}</a>
        <nav className="nav">
          <a href="#/" aria-current={route.name === 'home' || route.name === 'resource' ? 'page' : undefined}>Book</a>
          <a href="#/mine" aria-current={route.name === 'mine' || route.name === 'booking' ? 'page' : undefined}>My bookings</a>
          <AccountMenu />
        </nav>
      </header>
      <main className="page">{children}</main>
    </>
  );
}
