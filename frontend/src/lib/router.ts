// Tiny hash router. Routes:
//   #/                                   resources
//   #/r/court-1                          book a resource (today)
//   #/r/court-1/2026-10-20               book a resource on a day
//   #/r/court-1/2026-10-20?at=<start>    ...with a slot already chosen (start is the slot's UTC start)
//   #/r/court-1/2026-10-20/week          the week starting on that day
//   #/mine                               my bookings
// View and selection live in the URL, so a link reproduces what the user sees (library 9.4 "Kinds of state").
import { useEffect, useState } from 'react';

export type ResourceView = 'day' | 'week';

export type Route =
  | { name: 'home' }
  | { name: 'resource'; id: string; date?: string; view: ResourceView; at?: string }
  | { name: 'mine' }
  | { name: 'notfound' };

export function parseHash(hash: string): Route {
  const [rawPath, rawQuery = ''] = hash.replace(/^#/, '').split('?');
  const path = decodeURIComponent(rawPath) || '/';
  if (path === '/') return { name: 'home' };
  if (path === '/mine') return { name: 'mine' };
  const m = path.match(/^\/r\/([\w-]+)(?:\/(\d{4}-\d{2}-\d{2}))?(\/week)?$/);
  if (m) {
    const at = new URLSearchParams(rawQuery).get('at') ?? undefined;
    return { name: 'resource', id: m[1], date: m[2], view: m[3] ? 'week' : 'day', at };
  }
  return { name: 'notfound' };
}

export function resourceHref(id: string, date?: string, view: ResourceView = 'day', at?: string): string {
  return `#/r/${id}${date ? `/${date}` : ''}${view === 'week' ? '/week' : ''}${at ? `?at=${encodeURIComponent(at)}` : ''}`;
}

export function useRoute(): Route {
  const [route, setRoute] = useState(() => parseHash(location.hash));
  useEffect(() => {
    const on = () => setRoute(parseHash(location.hash));
    window.addEventListener('hashchange', on);
    return () => window.removeEventListener('hashchange', on);
  }, []);
  return route;
}
