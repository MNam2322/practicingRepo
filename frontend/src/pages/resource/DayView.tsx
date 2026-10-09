import { useState } from 'react';
import { api, ApiError, formatDay, type Booking, type Resource, type Venue } from '../../lib/api';
import { useApi } from '../../lib/useApi';
import { useToast } from '../../lib/toast';
import { Button } from '../../components/Button';
import { Card } from '../../components/Card';
import { Field } from '../../components/Field';
import { ErrorNotice, Notice } from '../../components/Notice';
import { SlotsSkeleton } from '../../components/Skeleton';
import { AVAILABILITY_REFRESH } from './refresh';
import { SLOT_LABEL, closureTimes } from './slots';

const NAME_KEY = 'bk-name';
const readName = () => { try { return localStorage.getItem(NAME_KEY) ?? ''; } catch { return ''; } };
const saveName = (n: string) => { try { localStorage.setItem(NAME_KEY, n); } catch { /* storage unavailable */ } };

/** One day's slots and the booking panel. Keyed by resource, day and `initialAt` in App.tsx, so state starts clean. */
export function DayView({ venue, resource, day, initialAt }: { venue: Venue; resource: Resource; day: string; initialAt?: string }) {
  const toast = useToast();
  const [availability, reload] = useApi(`${resource.id}:${day}`, () => api.availability(resource.id, day), AVAILABILITY_REFRESH);

  const [selected, setSelected] = useState<string | null>(initialAt ?? null);   // UTC start of the chosen slot
  const [length, setLength] = useState(1);
  const [name, setName] = useState(readName);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ kind: 'error' | 'ok'; text: string; booking?: Booking; traceId?: string | null } | null>(null);

  const slots = availability.status === 'ready' ? availability.data.slots : [];
  const found = slots.findIndex((s) => s.start === selected);
  // The chosen slot can be taken by someone else between refreshes. That is derived from fresh data, not stored:
  // the selection simply stops counting, and the panel says why (library 9.4 "Store less, compute more").
  const taken = !busy && found >= 0 && slots[found].state !== 'free';
  const index = taken ? -1 : found;
  // How many consecutive free slots follow the selected one (the longest booking possible from there).
  let maxLength = 0;
  if (index >= 0) while (maxLength < resource.maxSlotsPerBooking && slots[index + maxLength]?.state === 'free') maxLength++;
  const chosenLength = Math.min(length, Math.max(maxLength, 1));

  function choose(start: string) {
    setSelected(start); setLength(1); setMessage(null);
  }

  async function book() {
    if (index < 0 || !name.trim()) return;
    setBusy(true); setMessage(null);
    try {
      const booking = await api.book(resource.id, slots[index].start, chosenLength, name.trim());
      saveName(name.trim());
      setMessage({ kind: 'ok', text: 'Booked.', booking });
      setSelected(null);
      toast({
        text: `Booked ${booking.resourceName}, ${booking.localStart.slice(11)}–${booking.localEnd.slice(11)}.`,
        link: { href: `#/b/${booking.id}`, label: 'Details' },
      });
    } catch (e) {
      const text = e instanceof Error ? e.message : String(e);
      setMessage({ kind: 'error', text, traceId: e instanceof ApiError ? e.traceId : null });
      if (e instanceof ApiError && e.code === 'slot_taken') setSelected(null);
    } finally {
      setBusy(false);
      reload();
    }
  }

  return (
    <div className="layout-2">
      <section aria-label={`Times on ${formatDay(day, 'long')}`}>
        <h2 className="section-title">{formatDay(day, 'long')}</h2>
        {availability.status === 'loading' && <SlotsSkeleton />}
        {availability.status === 'error' && <ErrorNotice message={availability.error} traceId={availability.traceId} retry={reload} />}
        {availability.status === 'ready' && availability.data.closures.map((c) => (
          <Notice key={c.id} kind="info">
            <p><strong>Closed {closureTimes(c, day)}</strong>: {c.reason}</p>
          </Notice>
        ))}
        {availability.status === 'ready' && slots.length === 0 && <p className="muted">Closed on this day.</p>}
        {availability.status === 'ready' && slots.length > 0 && (
          <div className="slots">
            {slots.map((s, i) => {
              const inSelection = index >= 0 && i >= index && i < index + chosenLength;
              return (
                <button key={s.start} type="button" disabled={s.state !== 'free'}
                  className={`slot ${s.state}${inSelection ? ' chosen' : ''}`}
                  aria-pressed={index >= 0 && s.start === selected}
                  onClick={() => choose(s.start)}>
                  <span>{s.localStart}</span>
                  <small>{SLOT_LABEL[s.state]}</small>
                </button>
              );
            })}
          </div>
        )}
      </section>

      <Card as="aside" className="panel" aria-live="polite">
        {message?.kind === 'ok' && message.booking && (
          <Notice kind="ok">
            <p><strong>Booked.</strong> {message.booking.resourceName}, {formatDay(message.booking.localStart.slice(0, 10), 'long')},{' '}
              {message.booking.localStart.slice(11)}–{message.booking.localEnd.slice(11)}.</p>
            <p><a href={`#/b/${message.booking.id}`}>Booking details and calendar</a> · <a href="#/mine">My bookings</a></p>
          </Notice>
        )}
        {message?.kind === 'error' && <ErrorNotice message={message.text} traceId={message.traceId} />}
        {taken && (
          <Notice kind="info">
            <p><strong>{slots[found].localStart} has just been taken.</strong> Someone else booked it. Pick another time.</p>
          </Notice>
        )}
        {index < 0 ? (
          !taken && <p className="muted">Choose a free time on the left.</p>
        ) : (
          <form onSubmit={(e) => { e.preventDefault(); void book(); }}>
            <h2 className="section-title">Your booking</h2>
            <p><strong>{slots[index].localStart}–{slots[index + chosenLength - 1]?.localEnd}</strong> · {formatDay(day, 'long')}</p>
            {maxLength > 1 && (
              <Field label="Length">
                <select value={chosenLength} onChange={(e) => setLength(Number(e.target.value))}>
                  {Array.from({ length: maxLength }, (_, i) => i + 1).map((n) => (
                    <option key={n} value={n}>{n * resource.slotMinutes} minutes</option>
                  ))}
                </select>
              </Field>
            )}
            <Field label="Name for the booking">
              <input value={name} onChange={(e) => setName(e.target.value)} required maxLength={80} autoComplete="name" />
            </Field>
            <Button type="submit" disabled={busy || !name.trim()}>{busy ? 'Booking…' : 'Book it'}</Button>
            <p className="muted small">Free cancellation up to {venue.policy.cancellationCutoffHours} hours before the start.</p>
          </form>
        )}
      </Card>
    </div>
  );
}
