// Server state through TanStack Query: every read has a key, every write invalidates exactly the keys it affects.
// Library: 9.7 "Server state libraries", 8.6 "Invalidation" and "Optimistic updates".
import { focusManager, MutationCache, QueryCache, QueryClient, useMutation, useQueries, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, ApiError, bookingWhen, serverNow, type Account, type Availability, type Booking, type StaffBooking } from './api';
import { signInHref } from './router';
import { useToast } from './toast';

export const keys = {
  basics: ['basics'] as const,
  availabilityAll: ['availability'] as const,
  availability: (resourceId: string, date: string) => ['availability', resourceId, date] as const,
  bookings: ['bookings'] as const,
  mine: ['bookings', 'mine'] as const,
  booking: (id: string) => ['bookings', 'one', id] as const,
  me: ['me'] as const,
  schedule: (date: string) => ['schedule', date] as const,
  adminClosures: (from: string, to: string) => ['admin', 'closures', from, to] as const,
  audit: ['admin', 'audit'] as const,
};

/**
 * Any request answered "401 unauthenticated" (for example a staff page after the session expired) sends the person to
 * sign in, and back to where they were afterwards. /api/me is not an error: it means "signed out" (api.me).
 */
function onUnauthenticated(error: unknown) {
  if (error instanceof ApiError && error.status === 401 && error.code === 'unauthenticated' && !location.hash.startsWith('#/sign'))
    location.hash = signInHref();
}

/**
 * TanStack Query refetches "on focus" when the tab becomes visible. Also count the window itself gaining focus, so two
 * windows side by side refresh when you click between them.
 */
focusManager.setEventListener((setFocused) => {
  const visible = () => setFocused(document.visibilityState === 'visible');
  const focus = () => setFocused(true);
  const blur = () => setFocused(false);
  document.addEventListener('visibilitychange', visible);
  window.addEventListener('focus', focus);
  window.addEventListener('blur', blur);
  return () => {
    document.removeEventListener('visibilitychange', visible);
    window.removeEventListener('focus', focus);
    window.removeEventListener('blur', blur);
  };
});

export function createQueryClient() {
  return new QueryClient({
    queryCache: new QueryCache({ onError: onUnauthenticated }),
    mutationCache: new MutationCache({ onError: onUnauthenticated }),
    defaultOptions: {
      queries: {
        // A 4xx will not change on retry (not found, validation, rules); only network errors and 5xx are retried.
        retry: (count, error) => count < 2 && !(error instanceof ApiError && error.status >= 400 && error.status < 500),
      },
    },
  });
}

/** The venue and resources: they change rarely (admin edits), so they stay fresh for five minutes. */
export function useBasics() {
  return useQuery({ queryKey: keys.basics, queryFn: () => Promise.all([api.venue(), api.resources()]), staleTime: 5 * 60_000 });
}

/**
 * Slots change when other people book. Fresh for 30 s; refetched every minute while visible and whenever the tab
 * regains focus, so a slot someone else took shows up without a reload.
 */
const availabilityOptions = (resourceId: string, date: string) => ({
  queryKey: keys.availability(resourceId, date),
  queryFn: () => api.availability(resourceId, date),
  staleTime: 30_000,
  refetchInterval: 60_000,
  refetchOnWindowFocus: 'always' as const,
});

export function useAvailability(resourceId: string, date: string) {
  return useQuery(availabilityOptions(resourceId, date));
}

/** Several days at once (the week view). Shares the cache with the day view, so switching views is instant. */
export function useAvailabilities(resourceId: string, dates: string[]) {
  return useQueries({
    queries: dates.map((d) => availabilityOptions(resourceId, d)),
    combine: (results) => ({
      data: results.every((r) => r.data) ? results.map((r) => r.data as Availability) : undefined,
      error: results.find((r) => r.error)?.error ?? null,
      isPending: results.some((r) => r.isPending),
      refetch: () => results.forEach((r) => void r.refetch()),
    }),
  });
}

export function useMyBookings() {
  return useQuery({ queryKey: keys.mine, queryFn: api.mine });
}

/** A hold past its time on the API's clock, which the API has not reported as expired yet. */
const holdOverdue = (b: Booking) => b.status === 'pending' && b.holdExpiresAt != null && Date.parse(b.holdExpiresAt) <= serverNow();

export function useBooking(id: string) {
  return useQuery({
    queryKey: keys.booking(id),
    queryFn: () => api.booking(id),
    // When a hold runs out on our clock, ask again every two seconds until the API says what happened: the API
    // decides that a hold has expired, the browser only counts down.
    refetchInterval: (query) => (query.state.data && holdOverdue(query.state.data) ? 2000 : false),
  });
}

/** The venue-local day a booking is on, as the availability key uses it. */
const dayOf = (b: Booking) => b.localStart.slice(0, 10);

export function useBook() {
  const qc = useQueryClient();
  return useMutation({
    // Holds the slot: the answer is a "pending" booking with holdExpiresAt. The booking page then takes the payment.
    mutationFn: (v: { resourceId: string; start: string; slots: number; name: string; email: string | null; day: string; idempotencyKey: string }) =>
      api.book(v.resourceId, v.start, v.slots, v.name, v.email, v.idempotencyKey),
    onSuccess: (booking) => {
      qc.setQueryData(keys.booking(booking.id), booking);
      void qc.invalidateQueries({ queryKey: keys.bookings });
    },
    // Whatever happened (booked, or slot_taken), that day's slots are worth reloading.
    onSettled: (_booking, _error, v) => qc.invalidateQueries({ queryKey: keys.availability(v.resourceId, v.day) }),
  });
}

/**
 * Pays the deposit and confirms a hold. Safe to press again after an error: the API reuses the same payment, so a retry
 * cannot charge twice. Whatever the answer (paid, declined, expired), the booking and that day's slots are reloaded.
 */
export function useConfirm() {
  const qc = useQueryClient();
  const toast = useToast();
  return useMutation({
    mutationFn: (v: { booking: Booking; paymentToken: string }) => api.confirm(v.booking.id, v.paymentToken),
    onSuccess: (b) => {
      qc.setQueryData(keys.booking(b.id), b);
      toast({ text: `Booked ${b.resourceName}, ${bookingWhen(b)}.`, link: { href: `#/b/${b.id}`, label: 'Details' } });
    },
    onSettled: (_b, _error, v) => {
      void qc.invalidateQueries({ queryKey: keys.bookings });
      void qc.invalidateQueries({ queryKey: keys.availability(v.booking.resourceId, dayOf(v.booking)) });
    },
  });
}

/**
 * Cancel with an optimistic update: the booking shows as cancelled at once, and is put back if the API says no.
 * cancelQueries first, so an in-flight refetch cannot land after the optimistic write and undo it (library 8.6).
 * The outcome is a toast from the mutation itself: the row that started it moves lists (and unmounts) at once,
 * and callbacks given to mutate() do not run for an unmounted component.
 */
export function useCancelBooking() {
  const qc = useQueryClient();
  const toast = useToast();
  return useMutation({
    mutationFn: (b: Booking) => api.cancel(b.id),
    onMutate: async (b) => {
      await qc.cancelQueries({ queryKey: keys.bookings });
      const previousMine = qc.getQueryData<Booking[]>(keys.mine);
      const previousOne = qc.getQueryData<Booking>(keys.booking(b.id));
      const cancelled = (x: Booking): Booking => x.id === b.id ? { ...x, status: 'cancelled', canCancel: false, cancelledAt: new Date().toISOString() } : x;
      qc.setQueryData<Booking[]>(keys.mine, (list) => list?.map(cancelled));
      qc.setQueryData<Booking>(keys.booking(b.id), (one) => one && cancelled(one));
      return { previousMine, previousOne };
    },
    onSuccess: (_cancelled, b) => toast({ text: `Cancelled ${b.resourceName}, ${bookingWhen(b)}.` }),
    onError: (error, b, context) => {
      qc.setQueryData(keys.mine, context?.previousMine);
      qc.setQueryData(keys.booking(b.id), context?.previousOne);
      toast({ kind: 'error', text: `Could not cancel ${b.resourceName}: ${describeError(error).message}` });
    },
    onSettled: (_data, _error, b) => {
      void qc.invalidateQueries({ queryKey: keys.bookings });
      void qc.invalidateQueries({ queryKey: keys.availability(b.resourceId, dayOf(b)) });
    },
  });
}

/** The signed-in account, or null. Shared by the account menu and anything that depends on who is signed in. */
export function useMe() {
  return useQuery({ queryKey: keys.me, queryFn: api.me, staleTime: 5 * 60_000 });
}

/**
 * After signing in, up or out, "who am I" changes and so does every booking list (the customer is now the account,
 * or anonymous again): set the account and drop the cached bookings.
 */
function useAccountChange() {
  const qc = useQueryClient();
  return (account: Account | null) => {
    qc.setQueryData(keys.me, account);
    qc.removeQueries({ queryKey: keys.bookings });
    void qc.invalidateQueries({ queryKey: keys.availabilityAll });
  };
}

export function useSignIn() {
  const changed = useAccountChange();
  return useMutation({
    mutationFn: (v: { email: string; password: string }) => api.signIn(v.email, v.password),
    onSuccess: (signedIn) => changed(signedIn.account),
  });
}

export function useSignUp() {
  const changed = useAccountChange();
  return useMutation({
    mutationFn: (v: { email: string; password: string; name: string }) => api.signUp(v.email, v.password, v.name),
    onSuccess: (signedIn) => changed(signedIn.account),
  });
}

export function useSignOut() {
  const changed = useAccountChange();
  return useMutation({ mutationFn: api.signOut, onSuccess: () => changed(null) });
}

/** True when the signed-in account may use the staff tools. Only for showing the way there: the API decides. */
export function useIsStaff() {
  const me = useMe();
  return !!me.data?.roles.some((r) => r === 'Staff' || r === 'Admin');
}

/** The staff day schedule; refreshed every minute while visible, like availability. */
export function useSchedule(date: string) {
  return useQuery({ queryKey: keys.schedule(date), queryFn: () => api.schedule(date), refetchInterval: 60_000, refetchOnWindowFocus: 'always' });
}

export type StaffAction = { kind: 'complete' | 'noShow' } | { kind: 'cancel'; reason: string };

/** Complete, no-show or cancel a booking as staff; afterwards the day's schedule and slots are reloaded. */
export function useStaffAction(date: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ booking, action }: { booking: StaffBooking; action: StaffAction }) =>
      action.kind === 'cancel' ? api.staffCancel(booking.id, action.reason)
        : action.kind === 'complete' ? api.staffComplete(booking.id)
          : api.staffNoShow(booking.id),
    onSettled: (_r, _e, { booking }) => {
      void qc.invalidateQueries({ queryKey: keys.schedule(date) });
      void qc.invalidateQueries({ queryKey: keys.availability(booking.resourceId, booking.localStart.slice(0, 10)) });
      void qc.invalidateQueries({ queryKey: keys.bookings });
    },
  });
}

export function useBlockTime(date: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (v: { resourceId: string; start: string; end: string; reason: string }) => api.blockTime(v.resourceId, v.start, v.end, v.reason),
    onSettled: (_r, _e, v) => {
      void qc.invalidateQueries({ queryKey: keys.schedule(date) });
      void qc.invalidateQueries({ queryKey: keys.availability(v.resourceId, date) });
    },
  });
}

/** True when the signed-in account may change the venue set-up. Only for showing the way there: the API decides. */
export function useIsAdmin() {
  return !!useMe().data?.roles.includes('Admin');
}

export function useAdminClosures(from: string, to: string) {
  return useQuery({ queryKey: keys.adminClosures(from, to), queryFn: () => api.adminClosures(from, to) });
}

export function useAuditLog() {
  return useQuery({ queryKey: keys.audit, queryFn: () => api.audit(50) });
}

/**
 * An admin change. Afterwards everything it can affect is reloaded: resources and rules (basics), every day's slots,
 * closures, the staff schedule and the audit log.
 */
export function useAdminChange<TInput, TResult>(change: (input: TInput) => Promise<TResult>) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: change,
    onSettled: () => {
      for (const key of [keys.basics, keys.availabilityAll, ['admin'], ['schedule']]) void qc.invalidateQueries({ queryKey: key });
    },
  });
}

/** The first message the API gave for a field (keys are JSON names, e.g. "slotMinutes"), or null. */
export function fieldError(error: unknown, field: string): string | null {
  return error instanceof ApiError ? error.fieldErrors[field]?.[0] ?? null : null;
}

/** A problem that is not about one field: shown above the form. Field errors are shown next to their fields instead. */
export function formError(error: unknown): { message: string; traceId: string | null } | null {
  if (!error) return null;
  if (error instanceof ApiError && error.code === 'validation_failed' && Object.keys(error.fieldErrors).length > 0) return null;
  return describeError(error);
}

/** Message and trace id for any error a query or mutation can hold. */
export function describeError(error: unknown): { message: string; traceId: string | null } {
  return { message: error instanceof Error ? error.message : String(error), traceId: error instanceof ApiError ? error.traceId : null };
}
