import { useState } from 'react';
import { ApiError, formatDay, formatMoney, type Resource, type Venue } from '../../lib/api';
import { describeError, fieldError, useAvailability, useBook, useMe } from '../../lib/queries';
import { Button } from '../../components/Button';
import { Card } from '../../components/Card';
import { Field } from '../../components/Field';
import { ErrorNotice, Notice } from '../../components/Notice';
import { SlotsSkeleton } from '../../components/Skeleton';
import { SLOT_LABEL, closureTimes } from './slots';

const NAME_KEY = 'bk-name';
const EMAIL_KEY = 'bk-email';
const read = (key: string) => { try { return localStorage.getItem(key) ?? ''; } catch { return ''; } };
const save = (key: string, value: string) => { try { localStorage.setItem(key, value); } catch { /* storage unavailable */ } };

/** One day's slots and the booking panel. Keyed by resource, day and `initialAt` in App.tsx, so state starts clean. */
export function DayView({ venue, resource, day, initialAt }: { venue: Venue; resource: Resource; day: string; initialAt?: string }) {
  const availability = useAvailability(resource.id, day);
  const bookMutation = useBook();

  const [selected, setSelected] = useState<string | null>(initialAt ?? null);   // UTC start of the chosen slot
  const [length, setLength] = useState(1);
  const me = useMe();
  const [typedName, setName] = useState(() => read(NAME_KEY));
  // The remembered name wins; a signed-in person without one gets their account name suggested.
  const name = typedName || (me.data?.name ?? '');
  // Optional. Left empty, a signed-in person is emailed at their account address (the API fills it in).
  const [email, setEmail] = useState(() => read(EMAIL_KEY));
  // One key per booking attempt: pressing "Book it" again after a lost response repeats the same attempt, and the API
  // returns the first answer instead of booking twice. A new choice of time, length or name is a new attempt.
  const [attemptKey, setAttemptKey] = useState(() => crypto.randomUUID());
  const [message, setMessage] = useState<{ text: string; traceId?: string | null; emailError?: string | null } | null>(null);

  const busy = bookMutation.isPending;
  const slots = availability.data?.slots ?? [];
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
    setSelected(start); setLength(1); setMessage(null); setAttemptKey(crypto.randomUUID());
  }

  async function book() {
    if (index < 0 || !name.trim()) return;
    setMessage(null);
    try {
      const hold = await bookMutation.mutateAsync({
        resourceId: resource.id, start: slots[index].start, slots: chosenLength, name: name.trim(), email: email.trim() || null, day, idempotencyKey: attemptKey,
      });
      save(NAME_KEY, name.trim());
      save(EMAIL_KEY, email.trim());
      // The time is held: pay and confirm on the booking page (a link, so a reload keeps the checkout).
      location.hash = `#/b/${hold.id}`;
    } catch (e) {
      const text = e instanceof Error ? e.message : String(e);
      setMessage({ text, traceId: e instanceof ApiError ? e.traceId : null, emailError: e instanceof ApiError ? fieldError(e, 'email') : null });
      if (e instanceof ApiError && e.code === 'slot_taken') setSelected(null);
    }
  }

  return (
    <div className="layout-2">
      <section aria-label={`Times on ${formatDay(day, 'long')}`}>
        <h2 className="section-title">{formatDay(day, 'long')}</h2>
        {availability.isPending && <SlotsSkeleton />}
        {availability.isError && !availability.data && <ErrorNotice {...describeError(availability.error)} retry={() => void availability.refetch()} />}
        {availability.data?.closures.map((c) => (
          <Notice key={c.id} kind="info">
            <p><strong>Closed {closureTimes(c, day)}</strong>: {c.reason}</p>
          </Notice>
        ))}
        {availability.data && slots.length === 0 && <p className="muted">Closed on this day.</p>}
        {slots.length > 0 && (
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
        {message && <ErrorNotice message={message.text} traceId={message.traceId} />}
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
                <select value={chosenLength} onChange={(e) => { setLength(Number(e.target.value)); setAttemptKey(crypto.randomUUID()); }}>
                  {Array.from({ length: maxLength }, (_, i) => i + 1).map((n) => (
                    <option key={n} value={n}>{n * resource.slotMinutes} minutes</option>
                  ))}
                </select>
              </Field>
            )}
            <Field label="Name for the booking">
              <input value={name} onChange={(e) => { setName(e.target.value); setAttemptKey(crypto.randomUUID()); }} required maxLength={80} autoComplete="name" />
            </Field>
            <Field label="Email (optional)" error={message?.emailError}
              hint={me.data ? `For the confirmation and a reminder. Leave it empty to use ${me.data.email}.` : 'For the confirmation and a reminder the day before.'}>
              <input type="email" value={email} onChange={(e) => { setEmail(e.target.value); setAttemptKey(crypto.randomUUID()); }} maxLength={254} autoComplete="email" />
            </Field>
            <Button type="submit" disabled={busy || !name.trim()}>{busy ? 'Holding…' : venue.deposit ? 'Continue to payment' : 'Continue'}</Button>
            <p className="muted small">
              {venue.deposit && <>We hold the time for you while you pay the {formatMoney(venue.deposit)} deposit. </>}
              Free cancellation up to {venue.policy.cancellationCutoffHours} hours before the start.
            </p>
          </form>
        )}
      </Card>
    </div>
  );
}
