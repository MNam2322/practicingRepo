import { useEffect, useState } from 'react';
import { serverNow } from './api';

/**
 * The API's "now", ticking twice a second while `active`, so a seconds display is never more than half a second late.
 * A timer is the outside world, so it lives in an effect that cleans up after itself (library 9.5 "Effects, and when
 * not to use them"); everything shown from it (time left, whether a hold is overdue) is computed while rendering,
 * never stored.
 */
export function useServerNow(active: boolean): number {
  const [now, setNow] = useState(serverNow);
  useEffect(() => {
    if (!active) return;
    const timer = setInterval(() => setNow(serverNow()), 500);
    return () => clearInterval(timer);
  }, [active]);
  return now;
}
