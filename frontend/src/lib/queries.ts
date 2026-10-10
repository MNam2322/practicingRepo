// Server state through TanStack Query: every read has a key, every write invalidates exactly the keys it affects.
// Library: 9.7 "Server state libraries", 8.6 "Invalidation" and "Optimistic updates".
import { focusManager, QueryClient, useMutation, useQueries, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, ApiError, bookingWhen, type Availability, type Booking } from './api';
import { useToast } from './toast';

export const keys = {
  basics: ['basics'] as const,
  availabilityAll: ['availability'] as const,
  availability: (resourceId: string, date: string) => ['availability', resourceId, date] as const,
  bookings: ['bookings'] as const,
  mine: ['bookings', 'mine'] as const,
  booking: (id: string) => ['bookings', 'one', id] as const,
};

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

export function useBooking(id: string) {
  return useQuery({ queryKey: keys.booking(id), queryFn: () => api.booking(id) });
}

/** The venue-local day a booking is on, as the availability key uses it. */
const dayOf = (b: Booking) => b.localStart.slice(0, 10);

export function useBook() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (v: { resourceId: string; start: string; slots: number; name: string; day: string }) =>
      api.book(v.resourceId, v.start, v.slots, v.name),
    onSuccess: (booking) => {
      qc.setQueryData(keys.booking(booking.id), booking);
      void qc.invalidateQueries({ queryKey: keys.bookings });
    },
    // Whatever happened (booked, or slot_taken), that day's slots are worth reloading.
    onSettled: (_booking, _error, v) => qc.invalidateQueries({ queryKey: keys.availability(v.resourceId, v.day) }),
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

/** Message and trace id for any error a query or mutation can hold. */
export function describeError(error: unknown): { message: string; traceId: string | null } {
  return { message: error instanceof Error ? error.message : String(error), traceId: error instanceof ApiError ? error.traceId : null };
}
