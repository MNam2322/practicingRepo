import type { RefreshOptions } from '../../lib/useApi';

/** Slots change when other people book, so availability refreshes every minute and when the tab regains focus. */
export const AVAILABILITY_REFRESH: RefreshOptions = { refreshEveryMs: 60_000, refreshOnFocus: true };
