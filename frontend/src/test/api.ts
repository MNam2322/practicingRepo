// A stand-in for the .NET API in component tests (MSW). Shapes come from the generated contract types, so a contract
// change that breaks these fixtures breaks the build. Writes must carry the antiforgery header, as the real API
// insists; tests set the XSRF-TOKEN cookie (setup.ts).
import { http, HttpResponse, type JsonBodyType } from 'msw';
import { setupServer } from 'msw/node';
import type { Availability, Booking, Resource, Slot, Venue } from '../lib/api';

export const XSRF = 'test-xsrf-token';

export const venue: Venue = {
  name: 'Test club',
  timeZone: 'Asia/Ho_Chi_Minh',
  policy: { maxDaysAhead: 30, minLeadMinutes: 15, cancellationCutoffHours: 2, maxActiveBookingsPerCustomer: 5 },
  deposit: { amount: 100000, currency: 'VND' },
};

export const court: Resource = {
  id: 'court-1', name: 'Tennis court 1', kind: 'court', capacity: 4, slotMinutes: 60, maxSlotsPerBooking: 2,
  hours: [{ day: 'tuesday', open: '09:00', close: '13:00' }],
};

export const DAY = '2026-06-02';

/** 09:00–13:00 venue time (UTC+7): four slots. */
export function slots(states: Partial<Record<string, Slot['state']>> = {}): Slot[] {
  return [9, 10, 11, 12].map((h) => {
    const local = `${String(h).padStart(2, '0')}:00`;
    return {
      start: `2026-06-02T${String(h - 7).padStart(2, '0')}:00:00+00:00`,
      end: `2026-06-02T${String(h - 6).padStart(2, '0')}:00:00+00:00`,
      localStart: local,
      localEnd: `${String(h + 1).padStart(2, '0')}:00`,
      state: states[local] ?? 'free',
    };
  });
}

export function availability(states: Partial<Record<string, Slot['state']>> = {}): Availability {
  return { resourceId: 'court-1', date: DAY, timeZone: venue.timeZone, slots: slots(states), closures: [] };
}

export function booking(overrides: Partial<Booking> = {}): Booking {
  return {
    id: '01a12000-0000-7000-8000-000000000001', resourceId: 'court-1', resourceName: 'Tennis court 1',
    start: '2026-06-02T03:00:00+00:00', end: '2026-06-02T04:00:00+00:00', localStart: '2026-06-02T10:00', localEnd: '2026-06-02T11:00',
    status: 'confirmed', customerName: 'Nam', createdAt: '2026-06-01T00:00:00+00:00', cancelledAt: null, canCancel: true,
    cancellationReason: null, holdExpiresAt: null, canConfirm: false, deposit: null, email: null, seriesId: null,
    ...overrides,
  };
}

export function problem(status: number, code: string, title: string) {
  return HttpResponse.json({ type: 'about:blank', title, status, code, traceId: 'trace-1' }, { status, headers: { 'Content-Type': 'application/problem+json' } });
}

/** A write handler that answers 400 antiforgery_failed without the header, like the API. */
export function write(handler: (request: Request, params: Record<string, string>) => Promise<Response> | Response) {
  return async ({ request, params }: { request: Request; params: Record<string, string | readonly string[] | undefined> }) =>
    request.headers.get('X-XSRF-TOKEN') === XSRF
      ? handler(request, params as Record<string, string>)
      : problem(400, 'antiforgery_failed', 'This request did not come with a valid antiforgery token.');
}

export const json = (body: JsonBodyType, status = 200) => HttpResponse.json(body, { status });

export const server = setupServer(
  http.get('/api/venue', () => json(venue)),
  http.get('/api/resources', () => json([court])),
  http.get('/api/me', () => problem(401, 'unauthenticated', 'Sign in to do that.')),
  http.get('/api/antiforgery', () => new HttpResponse(null, { status: 204 })),
  http.get('/api/bookings/mine', () => json([])),
  http.get('/api/resources/court-1/availability', () => json(availability())),
);
