// Tiny hash router. Routes:
//   #/                       resources
//   #/r/court-1              book a resource (today)
//   #/r/court-1/2026-10-20   book a resource on a day
//   #/mine                   my bookings
import { useEffect, useState } from 'react';

export type Route =
  | { name: 'home' }
  | { name: 'resource'; id: string; date?: string }
  | { name: 'mine' }
  | { name: 'notfound' };

export function parseHash(hash: string): Route {
  const path = decodeURIComponent(hash.replace(/^#/, '')) || '/';
  if (path === '/') return { name: 'home' };
  if (path === '/mine') return { name: 'mine' };
  const m = path.match(/^\/r\/([\w-]+)(?:\/(\d{4}-\d{2}-\d{2}))?$/);
  if (m) return { name: 'resource', id: m[1], date: m[2] };
  return { name: 'notfound' };
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
