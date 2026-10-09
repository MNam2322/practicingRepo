// Typed client for the .NET API in ../backend. In development Vite proxies /api to http://localhost:5080
// (vite.config.ts), so the browser sees one origin and the customer cookie works without CORS.
// These types mirror backend/src/BookingApp.Api/Contracts.cs. Change both together (TASKS.md: generate from OpenAPI).

import { messageFor } from './errors';

export type SlotState = 'free' | 'booked' | 'past';
export type BookingStatus = 'confirmed' | 'cancelled' | 'completed' | 'noShow';
export type ResourceKind = 'court' | 'room' | 'table' | 'other';

export interface Policy { maxDaysAhead: number; minLeadMinutes: number; cancellationCutoffHours: number; maxActiveBookingsPerCustomer: number }
export interface Venue { name: string; timeZone: string; policy: Policy }
export interface OpeningHours { day: string; open: string; close: string }
export interface Resource { id: string; name: string; kind: ResourceKind; capacity: number; slotMinutes: number; maxSlotsPerBooking: number; hours: OpeningHours[] }
/** start/end are UTC instants; localStart/localEnd are venue wall-clock "HH:mm", for display. */
export interface Slot { start: string; end: string; localStart: string; localEnd: string; state: SlotState }
export interface Availability { resourceId: string; date: string; timeZone: string; slots: Slot[] }
export interface Booking {
  id: string; resourceId: string; resourceName: string; start: string; end: string;
  /** venue wall-clock "yyyy-MM-ddTHH:mm" */ localStart: string; localEnd: string;
  status: BookingStatus; customerName: string; createdAt: string; cancelledAt: string | null; canCancel: boolean;
}

/**
 * An RFC 9457 problem response. `code` is stable (e.g. "slot_taken") and is what the UI switches on; `message` is
 * the friendly text from lib/errors.ts; `traceId` lets support find the request in the server logs;
 * `fieldErrors` holds a 400's per-field messages, keyed by the JSON field name.
 */
export class ApiError extends Error {
  readonly status: number;
  readonly code: string | null;
  readonly traceId: string | null;
  readonly fieldErrors: Record<string, string[]>;
  constructor(status: number, message: string, code: string | null, traceId: string | null = null, fieldErrors: Record<string, string[]> = {}) {
    super(message);
    this.status = status;
    this.code = code;
    this.traceId = traceId;
    this.fieldErrors = fieldErrors;
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let res: Response;
  try {
    res = await fetch(path, {
      credentials: 'include',
      ...init,
      headers: { Accept: 'application/json', ...(init?.body ? { 'Content-Type': 'application/json' } : {}), ...init?.headers },
    });
  } catch {
    throw new ApiError(0, messageFor('network', 'Network error'), 'network');
  }
  if (!res.ok) {
    const p = await res.json().catch(() => null) as { title?: string; code?: string; traceId?: string; errors?: Record<string, string[]> } | null;
    const code = res.status === 429 ? 'rate_limited' : p?.code ?? null;
    throw new ApiError(res.status, messageFor(code, p?.title ?? `${res.status} ${res.statusText}`), code, p?.traceId ?? null, p?.errors ?? {});
  }
  return res.json() as Promise<T>;
}

export const api = {
  venue: () => request<Venue>('/api/venue'),
  resources: () => request<Resource[]>('/api/resources'),
  availability: (resourceId: string, date: string) =>
    request<Availability>(`/api/resources/${encodeURIComponent(resourceId)}/availability?date=${date}`),
  book: (resourceId: string, start: string, slots: number, name: string) =>
    request<Booking>('/api/bookings', { method: 'POST', body: JSON.stringify({ resourceId, start, slots, name }) }),
  mine: () => request<Booking[]>('/api/bookings/mine'),
  booking: (id: string) => request<Booking>(`/api/bookings/${encodeURIComponent(id)}`),
  cancel: (id: string) => request<Booking>(`/api/bookings/${id}/cancel`, { method: 'POST' }),
};

/** Today's date (yyyy-MM-dd) in the venue's time zone, not the browser's. */
export function venueToday(timeZone: string): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date());
}

/** yyyy-MM-dd plus n days (calendar arithmetic, no time zone involved). */
export function addDays(date: string, n: number): string {
  const d = new Date(`${date}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + n);
  return d.toISOString().slice(0, 10);
}

export function formatDay(date: string, style: 'short' | 'long' = 'short'): string {
  const d = new Date(`${date}T00:00:00Z`);
  return new Intl.DateTimeFormat(undefined, style === 'short'
    ? { weekday: 'short', day: 'numeric', timeZone: 'UTC' }
    : { weekday: 'long', day: 'numeric', month: 'long', timeZone: 'UTC' }).format(d);
}
