import type { ReactNode } from 'react';
import type { Route } from '../lib/router';

export function Layout({ venueName, route, children }: { venueName: string; route: Route; children: ReactNode }) {
  return (
    <>
      <header className="topbar">
        <a className="brand" href="#/"><span className="logo" aria-hidden="true" />{venueName || 'Bookings'}</a>
        <nav className="nav">
          <a href="#/" aria-current={route.name === 'home' || route.name === 'resource' ? 'page' : undefined}>Book</a>
          <a href="#/mine" aria-current={route.name === 'mine' ? 'page' : undefined}>My bookings</a>
        </nav>
      </header>
      <main className="page">{children}</main>
    </>
  );
}

export function Loading() {
  return <p className="muted">Loading…</p>;
}

export function ErrorBox({ message, retry }: { message: string; retry?: () => void }) {
  return (
    <div className="notice error" role="alert">
      <p>{message}</p>
      {retry && <button type="button" className="btn ghost" onClick={retry}>Try again</button>}
    </div>
  );
}
