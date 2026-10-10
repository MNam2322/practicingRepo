// Tiny hash router. Routes:
//   #/                                   resources
//   #/r/court-1                          book a resource (today)
//   #/r/court-1/2026-10-20               book a resource on a day
//   #/r/court-1/2026-10-20?at=<start>    ...with a slot already chosen (start is the slot's UTC start)
//   #/r/court-1/2026-10-20/week          the week starting on that day
//   #/mine                               my bookings
//   #/b/<id>                             one booking
//   #/staff  #/staff/2026-10-20             staff day schedule (staff only; the API checks)
//   #/admin  #/admin/<resources|closures|rules|audit>   venue set-up (admins only; the API checks)
//   #/signin?next=<route>  #/signup?next=<route>   then back to <route> (an in-app route only)
// View and selection live in the URL, so a link reproduces what the user sees (library 9.4 "Kinds of state").
import { useEffect, useState } from 'react';

export type ResourceView = 'day' | 'week';
export const ADMIN_SECTIONS = ['resources', 'closures', 'rules', 'audit'] as const;
export type AdminSection = (typeof ADMIN_SECTIONS)[number];

export type Route =
  | { name: 'home' }
  | { name: 'resource'; id: string; date?: string; view: ResourceView; at?: string }
  | { name: 'mine' }
  | { name: 'booking'; id: string }
  | { name: 'signin' | 'signup'; next: string }
  | { name: 'staff'; date?: string }
  | { name: 'admin'; section: AdminSection }
  | { name: 'notfound' };

export function parseHash(hash: string): Route {
  const [rawPath, rawQuery = ''] = hash.replace(/^#/, '').split('?');
  const path = decodeURIComponent(rawPath) || '/';
  if (path === '/') return { name: 'home' };
  if (path === '/mine') return { name: 'mine' };
  if (path === '/signin' || path === '/signup')
    return { name: path === '/signin' ? 'signin' : 'signup', next: safeNext(new URLSearchParams(rawQuery).get('next')) };
  const staff = path.match(/^\/staff(?:\/(\d{4}-\d{2}-\d{2}))?$/);
  if (staff) return { name: 'staff', date: staff[1] };
  const admin = path.match(/^\/admin(?:\/(resources|closures|rules|audit))?$/);
  if (admin) return { name: 'admin', section: (admin[1] ?? 'resources') as AdminSection };
  const b = path.match(/^\/b\/([0-9a-f-]{36})$/i);
  if (b) return { name: 'booking', id: b[1] };
  const m = path.match(/^\/r\/([\w-]+)(?:\/(\d{4}-\d{2}-\d{2}))?(\/week)?$/);
  if (m) {
    const at = new URLSearchParams(rawQuery).get('at') ?? undefined;
    return { name: 'resource', id: m[1], date: m[2], view: m[3] ? 'week' : 'day', at };
  }
  return { name: 'notfound' };
}

/**
 * Where to go after signing in. It comes from the URL, so it is untrusted input: only an in-app hash route is
 * accepted, anything else means the home page (library 10.4 "Example 2: An open redirect after login").
 */
export function safeNext(next: string | null | undefined): string {
  return next && /^#\/[\w\-./?=&%:]*$/.test(next) && !next.startsWith('#//') ? next : '#/';
}

/** The sign-in page, coming back to `next` (default: where the user is now). */
export function signInHref(next: string = location.hash || '#/', page: 'signin' | 'signup' = 'signin'): string {
  return `#/${page}?next=${encodeURIComponent(safeNext(next))}`;
}

export function resourceHref(id: string, date?: string, view: ResourceView = 'day', at?: string): string {
  return `#/r/${id}${date ? `/${date}` : ''}${view === 'week' ? '/week' : ''}${at ? `?at=${encodeURIComponent(at)}` : ''}`;
}

export function useRoute(): Route {
  const [route, setRoute] = useState(() => parseHash(location.hash));
  useEffect(() => {
    let shown = parseHash(location.hash).name;
    const on = () => {
      const next = parseHash(location.hash);
      // A different page starts at the top, as a new page would; another day or selection on the same page keeps
      // its place.
      if (next.name !== shown) window.scrollTo(0, 0);
      shown = next.name;
      setRoute(next);
    };
    window.addEventListener('hashchange', on);
    return () => window.removeEventListener('hashchange', on);
  }, []);
  return route;
}
