import { useCallback, useEffect, useState } from 'react';

export type Loadable<T> = { status: 'loading' } | { status: 'error'; error: string } | { status: 'ready'; data: T };

/**
 * Runs an API call whenever `key` changes; `reload()` runs it again (after a booking, say).
 * Results are stored with their key so a stale response is never shown for a new key.
 * TASKS.md replaces this with TanStack Query once caching and mutations grow.
 */
export function useApi<T>(key: string, load: () => Promise<T>): [Loadable<T>, () => void] {
  const [state, setState] = useState<{ key: string; n: number; value: Loadable<T> } | null>(null);
  const [n, setN] = useState(0);
  useEffect(() => {
    let live = true;
    load()
      .then((data) => { if (live) setState({ key, n, value: { status: 'ready', data } }); })
      .catch((e: unknown) => { if (live) setState({ key, n, value: { status: 'error', error: e instanceof Error ? e.message : String(e) } }); });
    return () => { live = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps -- `key` and `n` identify the request
  }, [key, n]);
  const reload = useCallback(() => setN((x) => x + 1), []);
  // Keep showing the previous data for the same key while reloading, so the page doesn't flash.
  const value: Loadable<T> = state?.key === key ? state.value : { status: 'loading' };
  return [value, reload];
}
