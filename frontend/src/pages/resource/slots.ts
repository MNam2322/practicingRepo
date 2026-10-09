import type { Closure, SlotState } from '../../lib/api';

/** What each slot state is called on screen. One place, so the day grid, week grid and legend agree. */
export const SLOT_LABEL: Record<SlotState, string> = { free: 'Free', booked: 'Booked', past: 'Past', closed: 'Closed' };

/** "09:00–12:00", or "all day" when the closure covers the whole day being shown. Times come from the API (venue time). */
export function closureTimes(c: Closure, day: string): string {
  const from = c.localStart.slice(0, 10) < day ? '00:00' : c.localStart.slice(11);
  const to = c.localEnd.slice(0, 10) > day ? '24:00' : c.localEnd.slice(11);
  return from === '00:00' && to === '24:00' ? 'all day' : `${from}–${to}`;
}
