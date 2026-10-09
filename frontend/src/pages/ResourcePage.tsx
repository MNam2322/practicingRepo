import { useEffect, useMemo, useState } from 'react';
import { addDays, api, ApiError, formatDay, venueToday, type Booking, type Resource, type Venue } from '../lib/api';
import { useApi } from '../lib/useApi';
import { useToast } from '../lib/toast';
import { Button } from '../components/Button';
import { Card } from '../components/Card';
import { Field } from '../components/Field';
import { ErrorNotice, Notice } from '../components/Notice';
import { SlotsSkeleton } from '../components/Skeleton';

const NAME_KEY = 'bk-name';
const readName = () => { try { return localStorage.getItem(NAME_KEY) ?? ''; } catch { return ''; } };
const saveName = (n: string) => { try { localStorage.setItem(NAME_KEY, n); } catch { /* storage unavailable */ } };

/** Keyed by resource and day in App.tsx, so choosing another day starts with a clean selection. */
export function ResourcePage({ venue, resource, date }: { venue: Venue; resource: Resource; date?: string }) {
  const toast = useToast();
  const today = venueToday(venue.timeZone);
  const day = date ?? today;
  const days = useMemo(() => Array.from({ length: Math.min(14, venue.policy.maxDaysAhead + 1) }, (_, i) => addDays(today, i)), [today, venue.policy.maxDaysAhead]);
  const [availability, reload] = useApi(`${resource.id}:${day}`, () => api.availability(resource.id, day));

  const [selected, setSelected] = useState<string | null>(null);   // UTC start of the chosen slot
  const [length, setLength] = useState(1);
  const [name, setName] = useState(readName);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ kind: 'error' | 'ok'; text: string; booking?: Booking } | null>(null);

  useEffect(() => { document.title = `${resource.name} · ${formatDay(day, 'long')}`; }, [resource.name, day]);

  const slots = availability.status === 'ready' ? availability.data.slots : [];
  const index = slots.findIndex((s) => s.start === selected);
  // How many consecutive free slots follow the selected one (the longest booking possible from there).
  let maxLength = 0;
  if (index >= 0) while (maxLength < resource.maxSlotsPerBooking && slots[index + maxLength]?.state === 'free') maxLength++;

  async function book() {
    if (!selected || !name.trim()) return;
    setBusy(true); setMessage(null);
    try {
      const booking = await api.book(resource.id, selected, length, name.trim());
      saveName(name.trim());
      setMessage({ kind: 'ok', text: 'Booked.', booking });
      setSelected(null);
      toast({
        text: `Booked ${booking.resourceName}, ${booking.localStart.slice(11)}–${booking.localEnd.slice(11)}.`,
        link: { href: '#/mine', label: 'My bookings' },
      });
    } catch (e) {
      const text = e instanceof Error ? e.message : String(e);
      setMessage({ kind: 'error', text });
      if (e instanceof ApiError && e.code === 'slot_taken') setSelected(null);
    } finally {
      setBusy(false);
      reload();
    }
  }

  return (
    <>
      <p className="crumb"><a href="#/">All resources</a></p>
      <h1>{resource.name}</h1>
      <p className="muted">{resource.slotMinutes}-minute slots · times are {venue.timeZone.replaceAll('_', ' ')} time</p>

      <nav className="days" aria-label="Choose a day">
        {days.map((d) => (
          <a key={d} href={`#/r/${resource.id}/${d}`} className={d === day ? 'day on' : 'day'} aria-current={d === day ? 'date' : undefined}>
            {d === today ? 'Today' : formatDay(d)}
          </a>
        ))}
      </nav>

      <div className="layout-2">
        <section aria-label={`Times on ${formatDay(day, 'long')}`}>
          <h2 className="section-title">{formatDay(day, 'long')}</h2>
          {availability.status === 'loading' && <SlotsSkeleton />}
          {availability.status === 'error' && <ErrorNotice message={availability.error} retry={reload} />}
          {availability.status === 'ready' && slots.length === 0 && <p className="muted">Closed on this day.</p>}
          {availability.status === 'ready' && slots.length > 0 && (
            <div className="slots">
              {slots.map((s, i) => {
                const inSelection = index >= 0 && i >= index && i < index + length;
                return (
                  <button key={s.start} type="button" disabled={s.state !== 'free'}
                    className={`slot ${s.state}${inSelection ? ' chosen' : ''}`}
                    aria-pressed={s.start === selected}
                    onClick={() => { setSelected(s.start); setLength(1); setMessage(null); }}>
                    <span>{s.localStart}</span>
                    <small>{s.state === 'free' ? 'Free' : s.state === 'booked' ? 'Booked' : 'Past'}</small>
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
              <a href="#/mine">See my bookings</a>
            </Notice>
          )}
          {message?.kind === 'error' && <ErrorNotice message={message.text} />}
          {index < 0 ? (
            <p className="muted">Choose a free time on the left.</p>
          ) : (
            <form onSubmit={(e) => { e.preventDefault(); void book(); }}>
              <h2 className="section-title">Your booking</h2>
              <p><strong>{slots[index].localStart}–{slots[index + length - 1]?.localEnd}</strong> · {formatDay(day, 'long')}</p>
              {maxLength > 1 && (
                <Field label="Length">
                  <select value={length} onChange={(e) => setLength(Number(e.target.value))}>
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
    </>
  );
}
