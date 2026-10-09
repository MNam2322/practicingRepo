import { useCallback, useEffect, useState } from 'react';
import { ApiError } from './api';

export type Loadable<T> = { status: 'loading' } | { status: 'error'; error: string; traceId: string | null } | { status: 'ready'; data: T };

export interface RefreshOptions {
  /** Load again in the background this often while the tab is visible. */
  refreshEveryMs?: number;
  /** Load again when the window regains focus or the tab becomes visible. */
  refreshOnFocus?: boolean;
}

/**
 * Runs an API call whenever `key` changes; `reload()` runs it again (after a booking, say).
 * Results are stored with their key so a stale response is never shown for a new key.
 * Background refreshes keep showing the last good data, even if a refresh fails.
 * TASKS.md replaces this with TanStack Query once caching and mutations grow.
 */
export function useApi<T>(key: string, load: () => Promise<T>, options: RefreshOptions = {}): [Loadable<T>, () => void] {
  const [state, setState] = useState<{ key: string; n: number; value: Loadable<T> } | null>(null);
  const [n, setN] = useState(0);
  useEffect(() => {
    let live = true;
    load()
      .then((data) => { if (live) setState({ key, n, value: { status: 'ready', data } }); })
      .catch((e: unknown) => {
        if (!live) return;
        const error = e instanceof Error ? e.message : String(e);
        const traceId = e instanceof ApiError ? e.traceId : null;
        // Keep the data we already have for this key; only show the error when there is nothing to show.
        setState((prev) => prev?.key === key && prev.value.status === 'ready' ? prev : { key, n, value: { status: 'error', error, traceId } });
      });
    return () => { live = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps -- `key` and `n` identify the request
  }, [key, n]);
  const reload = useCallback(() => setN((x) => x + 1), []);

  // Timers and focus events are the outside world, so they belong in an effect (library 9.5).
  const { refreshEveryMs, refreshOnFocus } = options;
  useEffect(() => {
    if (!refreshEveryMs && !refreshOnFocus) return;
    const visible = () => document.visibilityState === 'visible';
    const timer = refreshEveryMs ? setInterval(() => { if (visible()) reload(); }, refreshEveryMs) : undefined;
    const onFocus = () => { if (visible()) reload(); };
    if (refreshOnFocus) {
      window.addEventListener('focus', onFocus);
      document.addEventListener('visibilitychange', onFocus);
    }
    return () => {
      clearInterval(timer);
      window.removeEventListener('focus', onFocus);
      document.removeEventListener('visibilitychange', onFocus);
    };
  }, [refreshEveryMs, refreshOnFocus, reload]);

  // Keep showing the previous data for the same key while reloading, so the page doesn't flash.
  const value: Loadable<T> = state?.key === key ? state.value : { status: 'loading' };
  return [value, reload];
}
