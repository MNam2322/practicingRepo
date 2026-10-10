// Typed client for the .NET API in ../backend. In development Vite proxies /api to http://localhost:5080
// (vite.config.ts), so the browser sees one origin and the customer cookie works without CORS.
// The types come from the API's OpenAPI document: never write them by hand (library 9.3 "Sharing types with the
// backend"). After a backend contract change run `npm run gen:api` (API running) or `npm run gen:api:file`.
// They describe what should arrive, not what does; the API is ours and tested against the same contract.

import type { components } from './api.generated';
import { messageFor } from './errors';

type Schemas = components['schemas'];
export type SlotState = Schemas['SlotState'];
export type BookingStatus = Schemas['BookingStatus'];
export type ResourceKind = Schemas['ResourceKind'];
export type Policy = Schemas['PolicyResponse'];
export type Venue = Schemas['VenueResponse'];
export type OpeningHours = Schemas['OpeningHoursResponse'];
export type Resource = Schemas['ResourceResponse'];
/** start/end are UTC instants; localStart/localEnd are venue wall-clock "HH:mm", for display. */
export type Slot = Schemas['SlotResponse'];
/** A time the resource (or, with resourceId null, the whole venue) is closed. local* are venue wall-clock "yyyy-MM-ddTHH:mm". */
export type Closure = Schemas['ClosureResponse'];
export type Availability = Schemas['AvailabilityResponse'];
/** localStart/localEnd are venue wall-clock "yyyy-MM-ddTHH:mm". */
export type Booking = Schemas['BookingResponse'];
/** A booking's deposit: due on a hold, then pending, paid, failed or refunded. amount is in major units of currency. */
export type Deposit = Schemas['DepositResponse'];
export type Money = Schemas['MoneyResponse'];
type ConfirmBookingRequest = Schemas['ConfirmBookingRequest'];
/** A weekly series request's outcome: every date held or failed (with a code). */
export type Series = Schemas['SeriesResponse'];
export type SeriesDate = Schemas['SeriesDateResponse'];
/** What confirming or cancelling a series did, date by date. */
export type SeriesChanges = Schemas['SeriesChangesResponse'];
type CreateSeriesRequest = Schemas['CreateSeriesRequest'];
/** A place on a waitlist; position 1 is next, 0 once no longer waiting. */
export type WaitlistEntry = Schemas['WaitlistEntryResponse'];
type JoinWaitlistRequest = Schemas['JoinWaitlistRequest'];
/** The signed-in account. roles: Customer, and Staff or Admin where granted. */
export type Account = Schemas['AccountResponse'];
/** movedBookings: anonymous bookings from this browser that now belong to the account. */
export type SignedIn = Schemas['SignedInResponse'];
export type Schedule = Schemas['ScheduleResponse'];
export type ScheduleColumn = Schemas['ScheduleColumnResponse'];
/** A booking as staff see it; actions say what the API will allow right now. */
export type StaffBooking = Schemas['StaffBookingResponse'];
type CreateBookingRequest = Schemas['CreateBookingRequest'];
type StaffCancelRequest = Schemas['StaffCancelRequest'];
export type ResourceInput = Schemas['CreateResourceRequest'];
export type ResourceChanges = Schemas['UpdateResourceRequest'];
export type HoursInput = Schemas['OpeningHoursRequest'];
export type PolicyInput = Schemas['PolicyRequest'];
export type AdminClosureInput = Schemas['AdminClosureRequest'];
export type AuditEntry = Schemas['AuditEntryResponse'];
type BlockTimeRequest = Schemas['BlockTimeRequest'];
type SignUpRequest = Schemas['SignUpRequest'];
type SignInRequest = Schemas['SignInRequest'];
type Problem = Schemas['ValidationProblemDetails'] & { traceId?: string };

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

// The API's clock, estimated from each response's Date header. The browser's own clock can be minutes off, and a hold
// expires on the server's clock (library 0.2 "Time: three clocks, none of them trustworthy"). The header has whole
// seconds and Kestrel refreshes it once a second, so it lags real time by 0.3 to 1.2 s (measured); 750 ms centres it.
let clockOffset = 0;

/** Milliseconds since the epoch on the API's clock (as near as the Date header tells). Use it for countdowns. */
export function serverNow(): number {
  return Date.now() + clockOffset;
}

function learnClock(res: Response) {
  const date = Date.parse(res.headers.get('Date') ?? '');
  if (!Number.isNaN(date)) clockOffset = date + 750 - Date.now();
}

/** An amount for display in the browser's language: 100000 VND reads "₫100,000" or "100.000 ₫". */
export function formatMoney(m: { amount: number; currency: string }): string {
  return new Intl.NumberFormat(undefined, { style: 'currency', currency: m.currency }).format(m.amount);
}

// Antiforgery (library 10.4 "Cross-site request forgery"): every write carries the XSRF-TOKEN cookie's value in a
// header. A page on another site can make the browser send our cookies, but cannot read this one to copy it.
const XSRF_COOKIE = 'XSRF-TOKEN';
const XSRF_HEADER = 'X-XSRF-TOKEN';
const SAFE = new Set(['GET', 'HEAD', 'OPTIONS']);

function xsrfToken(): string | null {
  const pair = document.cookie.split('; ').find((c) => c.startsWith(`${XSRF_COOKIE}=`));
  return pair ? decodeURIComponent(pair.slice(XSRF_COOKIE.length + 1)) : null;
}

/** Gets a fresh token (the API sets the cookie). Also after signing in or out, when the old one stops counting. */
async function refreshXsrf(): Promise<void> {
  await fetch('/api/antiforgery', { credentials: 'include' }).catch(() => undefined);
}

async function request<T>(path: string, init?: RequestInit, retried = false): Promise<T> {
  const write = !SAFE.has((init?.method ?? 'GET').toUpperCase());
  if (write && !xsrfToken()) await refreshXsrf();
  let res: Response;
  try {
    res = await fetch(path, {
      credentials: 'include',
      ...init,
      headers: {
        Accept: 'application/json',
        ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
        ...(write ? { [XSRF_HEADER]: xsrfToken() ?? '' } : {}),
        ...init?.headers,
      },
    });
  } catch {
    throw new ApiError(0, messageFor('network', 'Network error'), 'network');
  }
  learnClock(res);
  if (!res.ok) {
    const p = await res.json().catch(() => null) as Problem | null;
    const code = res.status === 429 ? 'rate_limited' : p?.code ?? null;
    // A stale token (signed in or out in another tab): get a new one and try once more.
    if (code === 'antiforgery_failed' && !retried) {
      await refreshXsrf();
      return request<T>(path, init, true);
    }
    throw new ApiError(res.status, messageFor(code, p?.title ?? `${res.status} ${res.statusText}`), code, p?.traceId ?? null, p?.errors ?? {});
  }
  if (res.status === 204) return undefined as T;
  return res.json() as Promise<T>;
}

export const api = {
  venue: () => request<Venue>('/api/venue'),
  resources: () => request<Resource[]>('/api/resources'),
  availability: (resourceId: string, date: string) =>
    request<Availability>(`/api/resources/${encodeURIComponent(resourceId)}/availability?date=${date}`),
  /**
   * Books a slot. `idempotencyKey` identifies this booking attempt: send the same key when retrying the same booking
   * and the API returns the first answer instead of booking twice.
   */
  book: (resourceId: string, start: string, slots: number, name: string, email: string | null, idempotencyKey?: string) =>
    request<Booking>('/api/bookings', {
      method: 'POST',
      body: JSON.stringify({ resourceId, start, slots, name, email } satisfies CreateBookingRequest),
      headers: idempotencyKey ? { 'Idempotency-Key': idempotencyKey } : undefined,
    }),
  /**
   * Pays the deposit with a payment token from the provider's card form and confirms the hold before it expires.
   * Safe to retry (a 503 payment_unavailable especially): it reuses the same payment, so it cannot charge twice.
   */
  confirm: (id: string, paymentToken: string) =>
    request<Booking>(`/api/bookings/${encodeURIComponent(id)}/confirm`, {
      method: 'POST',
      body: JSON.stringify({ paymentToken } satisfies ConfirmBookingRequest),
    }),
  mine: () => request<Booking[]>('/api/bookings/mine'),
  /** Holds the same time every week; each date is held or fails on its own. Retry with the same key: booked once. */
  createSeries: (v: { resourceId: string; start: string; slots: number; weeks: number; name: string; email: string | null }, idempotencyKey: string) =>
    request<Series>('/api/series', {
      method: 'POST',
      body: JSON.stringify(v satisfies CreateSeriesRequest),
      headers: { 'Idempotency-Key': idempotencyKey },
    }),
  series: (id: string) => request<Booking[]>(`/api/series/${encodeURIComponent(id)}`),
  confirmSeries: (id: string, paymentToken: string) =>
    request<SeriesChanges>(`/api/series/${encodeURIComponent(id)}/confirm`, { method: 'POST', body: JSON.stringify({ paymentToken } satisfies ConfirmBookingRequest) }),
  cancelSeries: (id: string) => request<SeriesChanges>(`/api/series/${encodeURIComponent(id)}/cancel`, { method: 'POST' }),
  joinWaitlist: (v: { resourceId: string; start: string; slots: number; name: string; email: string | null }) =>
    request<WaitlistEntry>('/api/waitlist', { method: 'POST', body: JSON.stringify(v satisfies JoinWaitlistRequest) }),
  myWaitlist: () => request<WaitlistEntry[]>('/api/waitlist/mine'),
  leaveWaitlist: (id: string) => request<WaitlistEntry>(`/api/waitlist/${encodeURIComponent(id)}/leave`, { method: 'POST' }),
  booking: (id: string) => request<Booking>(`/api/bookings/${encodeURIComponent(id)}`),
  /** The signed-in account, or null when nobody is signed in (the API answers 401). */
  me: () => request<Account>('/api/me').catch((e: unknown) => {
    if (e instanceof ApiError && e.status === 401) return null;
    throw e;
  }),
  signUp: (email: string, password: string, name: string) =>
    request<SignedIn>('/api/account/signup', { method: 'POST', body: JSON.stringify({ email, password, name } satisfies SignUpRequest) }),
  signIn: (email: string, password: string) =>
    request<SignedIn>('/api/account/signin', { method: 'POST', body: JSON.stringify({ email, password } satisfies SignInRequest) }),
  signOut: () => request<void>('/api/account/signout', { method: 'POST' }),
  // staff (Staff or Admin; the API refuses everyone else)
  schedule: (date: string) => request<Schedule>(`/api/staff/schedule?date=${date}`),
  staffComplete: (id: string) => request<StaffBooking>(`/api/staff/bookings/${id}/complete`, { method: 'POST' }),
  staffNoShow: (id: string) => request<StaffBooking>(`/api/staff/bookings/${id}/no-show`, { method: 'POST' }),
  staffCancel: (id: string, reason: string) =>
    request<StaffBooking>(`/api/staff/bookings/${id}/cancel`, { method: 'POST', body: JSON.stringify({ reason } satisfies StaffCancelRequest) }),
  // admin (Admin only; the API refuses everyone else)
  createResource: (input: ResourceInput) => request<Resource>('/api/admin/resources', { method: 'POST', body: JSON.stringify(input) }),
  updateResource: (id: string, changes: ResourceChanges) =>
    request<Resource>(`/api/admin/resources/${encodeURIComponent(id)}`, { method: 'PUT', body: JSON.stringify(changes) }),
  setHours: (id: string, hours: HoursInput[]) =>
    request<Resource>(`/api/admin/resources/${encodeURIComponent(id)}/hours`, { method: 'PUT', body: JSON.stringify({ hours }) }),
  deleteResource: (id: string) => request<void>(`/api/admin/resources/${encodeURIComponent(id)}`, { method: 'DELETE' }),
  adminClosures: (from: string, to: string) => request<Closure[]>(`/api/admin/closures?from=${from}&to=${to}`),
  addClosure: (input: AdminClosureInput) => request<Closure>('/api/admin/closures', { method: 'POST', body: JSON.stringify(input) }),
  deleteClosure: (id: string) => request<void>(`/api/admin/closures/${id}`, { method: 'DELETE' }),
  updatePolicy: (input: PolicyInput) => request<Policy>('/api/admin/policy', { method: 'PUT', body: JSON.stringify(input) }),
  audit: (take = 50) => request<AuditEntry[]>(`/api/admin/audit?take=${take}`),
  blockTime: (resourceId: string, start: string, end: string, reason: string) =>
    request<Closure>('/api/staff/closures', { method: 'POST', body: JSON.stringify({ resourceId, start, end, reason } satisfies BlockTimeRequest) }),
  cancel: (id: string) => request<Booking>(`/api/bookings/${id}/cancel`, { method: 'POST' }),
};

/** "Wednesday, October 14, 11:00–12:00" from the API's venue-local times. */
export function bookingWhen(b: Booking): string {
  return `${formatDay(b.localStart.slice(0, 10), 'long')}, ${b.localStart.slice(11)}–${b.localEnd.slice(11)}`;
}

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
