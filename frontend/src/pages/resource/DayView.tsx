import { useState } from 'react';
import { ApiError, formatDay, formatMoney, type Resource, type Series, type Venue, type WaitlistEntry } from '../../lib/api';
import { DATE_FAILURE } from '../../lib/labels';
import { describeError, fieldError, useAvailability, useBook, useCreateSeries, useJoinWaitlist, useMe } from '../../lib/queries';
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
const WEEKS = Array.from({ length: 11 }, (_, i) => i + 2);   // a series is 2 to 12 weeks (the API's limit)

/**
 * One day's slots and the panel next to them: book a free time (once, or weekly), or join the waitlist for a booked
 * one. Keyed by resource, day and `initialAt` in App.tsx, so state starts clean.
 */
export function DayView({ venue, resource, day, initialAt }: { venue: Venue; resource: Resource; day: string; initialAt?: string }) {
  const availability = useAvailability(resource.id, day);
  const bookMutation = useBook();
  const seriesMutation = useCreateSeries();
  const joinMutation = useJoinWaitlist();

  const [selected, setSelected] = useState<string | null>(initialAt ?? null);   // UTC start of the chosen slot
  const [length, setLength] = useState(1);
  const [repeat, setRepeat] = useState(false);
  const [weeks, setWeeks] = useState(4);
  const me = useMe();
  const [typedName, setName] = useState(() => read(NAME_KEY));
  // The remembered name wins; a signed-in person without one gets their account name suggested.
  const name = typedName || (me.data?.name ?? '');
  // Optional for a booking. Left empty, a signed-in person is emailed at their account address (the API fills it in).
  const [email, setEmail] = useState(() => read(EMAIL_KEY));
  // One key per booking attempt: pressing the button again after a lost response repeats the same attempt, and the API
  // returns the first answer instead of booking twice. A new choice of time, length, name or repeat is a new attempt.
  const [attemptKey, setAttemptKey] = useState(() => crypto.randomUUID());
  const [message, setMessage] = useState<{ text: string; traceId?: string | null; emailError?: string | null } | null>(null);
  const [noDates, setNoDates] = useState<Series | null>(null);
  const [joined, setJoined] = useState<WaitlistEntry | null>(null);

  const busy = bookMutation.isPending || seriesMutation.isPending || joinMutation.isPending;
  const slots = availability.data?.slots ?? [];
  const found = slots.findIndex((s) => s.start === selected);
  const chosen = found >= 0 ? slots[found] : null;
  // Derived from fresh data, not stored: if the chosen time is taken meanwhile, the panel turns into the waitlist for
  // it (library 9.4 "Store less, compute more").
  const booking = bookMutation.isPending || seriesMutation.isPending;   // our own hold makes the time booked: not a waitlist
  const waitlistFor = !booking && chosen?.state === 'booked' ? chosen : null;
  const index = chosen?.state === 'free' ? found : -1;
  // How many consecutive free slots follow the selected one (the longest booking possible from there).
  let maxLength = 0;
  if (index >= 0) while (maxLength < resource.maxSlotsPerBooking && slots[index + maxLength]?.state === 'free') maxLength++;
  const chosenLength = Math.min(length, Math.max(maxLength, 1));
  const newAttempt = () => setAttemptKey(crypto.randomUUID());

  function choose(start: string) {
    setSelected(start); setLength(1); setMessage(null); setNoDates(null); setJoined(null); joinMutation.reset(); newAttempt();
  }

  function failed(e: unknown) {
    const text = e instanceof Error ? e.message : String(e);
    setMessage({ text, traceId: e instanceof ApiError ? e.traceId : null, emailError: e instanceof ApiError ? fieldError(e, 'email') : null });
  }

  async function book() {
    if (index < 0 || !name.trim()) return;
    setMessage(null);
    setNoDates(null);
    const what = { resourceId: resource.id, start: slots[index].start, slots: chosenLength, name: name.trim(), email: email.trim() || null, day, idempotencyKey: attemptKey };
    try {
      save(NAME_KEY, name.trim());
      save(EMAIL_KEY, email.trim());
      if (repeat) {
        const series = await seriesMutation.mutateAsync({ ...what, weeks });
        // Every date failed: say why here; otherwise the series page shows what was held and pays for it.
        if (series.held === 0) setNoDates(series);
        else location.hash = `#/s/${series.seriesId}`;
      } else {
        const hold = await bookMutation.mutateAsync(what);
        // The time is held: pay and confirm on the booking page (a link, so a reload keeps the checkout).
        location.hash = `#/b/${hold.id}`;
      }
    } catch (e) {
      failed(e);
      if (e instanceof ApiError && e.code === 'slot_taken') void availability.refetch();
    }
  }

  async function join() {
    if (!waitlistFor || !name.trim()) return;
    setMessage(null);
    try {
      const entry = await joinMutation.mutateAsync({ resourceId: resource.id, start: waitlistFor.start, slots: 1, name: name.trim(), email: email.trim() || null });
      save(NAME_KEY, name.trim());
      save(EMAIL_KEY, email.trim());
      setJoined(entry);
    } catch (e) {
      failed(e);
      if (e instanceof ApiError && e.code === 'slot_free') void availability.refetch();
    }
  }

  const nameField = (
    <Field label="Name for the booking">
      <input value={name} onChange={(e) => { setName(e.target.value); newAttempt(); }} required maxLength={80} autoComplete="name" />
    </Field>
  );

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
                // Booked times can be chosen too: to join their waitlist.
                <button key={s.start} type="button" disabled={s.state === 'past' || s.state === 'closed'}
                  className={`slot ${s.state}${inSelection || (waitlistFor && s.start === selected) ? ' chosen' : ''}`}
                  aria-pressed={s.start === selected && (index >= 0 || waitlistFor !== null)}
                  title={s.state === 'booked' ? 'Booked: choose it to join the waitlist' : undefined}
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
        {noDates && (
          <Notice kind="info">
            <p><strong>None of the {noDates.dates.length} dates could be held:</strong></p>
            <ul className="series-failed">
              {noDates.dates.map((d) => <li key={d.index}>{formatDay(d.localDate, 'long')}: {DATE_FAILURE[d.code ?? ''] ?? d.message}</li>)}
            </ul>
          </Notice>
        )}

        {waitlistFor ? (
          joined ? (
            <Notice kind="ok">
              <p><strong>You are on the waitlist</strong> for {waitlistFor.localStart}–{waitlistFor.localEnd}, number {joined.position} in line.</p>
              <p>We will email {joined.email} if it frees up. <a href="#/mine">See your waitlist</a></p>
            </Notice>
          ) : (
            <form onSubmit={(e) => { e.preventDefault(); void join(); }}>
              <h2 className="section-title">Join the waitlist</h2>
              <p><strong>{waitlistFor.localStart}–{waitlistFor.localEnd}</strong> · {formatDay(day, 'long')} is booked.
                {' '}Join the waitlist and we will email you if it frees up.</p>
              {nameField}
              <Field label={me.data ? 'Email (optional)' : 'Email'} error={message?.emailError}
                hint={me.data ? `Leave it empty to use ${me.data.email}.` : 'Where we tell you it is free.'}>
                <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} required={!me.data} maxLength={254} autoComplete="email" />
              </Field>
              <Button type="submit" disabled={busy || !name.trim()}>{joinMutation.isPending ? 'Joining…' : 'Join waitlist'}</Button>
            </form>
          )
        ) : index < 0 ? (
          <p className="muted">Choose a free time on the left, or a booked one to join its waitlist.</p>
        ) : (
          <form onSubmit={(e) => { e.preventDefault(); void book(); }}>
            <h2 className="section-title">Your booking</h2>
            <p><strong>{slots[index].localStart}–{slots[index + chosenLength - 1]?.localEnd}</strong> · {formatDay(day, 'long')}</p>
            {maxLength > 1 && (
              <Field label="Length">
                <select value={chosenLength} onChange={(e) => { setLength(Number(e.target.value)); newAttempt(); }}>
                  {Array.from({ length: maxLength }, (_, i) => i + 1).map((n) => (
                    <option key={n} value={n}>{n * resource.slotMinutes} minutes</option>
                  ))}
                </select>
              </Field>
            )}
            {nameField}
            <Field label="Email (optional)" error={message?.emailError}
              hint={me.data ? `For the confirmation and a reminder. Leave it empty to use ${me.data.email}.` : 'For the confirmation and a reminder the day before.'}>
              <input type="email" value={email} onChange={(e) => { setEmail(e.target.value); newAttempt(); }} maxLength={254} autoComplete="email" />
            </Field>
            <label className="check">
              <input type="checkbox" checked={repeat} onChange={(e) => { setRepeat(e.target.checked); newAttempt(); }} />
              Repeat weekly
            </label>
            {repeat && (
              <Field label="How many weeks" hint="Each date is booked on its own; we tell you which ones are not free.">
                <select value={weeks} onChange={(e) => { setWeeks(Number(e.target.value)); newAttempt(); }}>
                  {WEEKS.map((n) => <option key={n} value={n}>{n} weeks</option>)}
                </select>
              </Field>
            )}
            <Button type="submit" disabled={busy || !name.trim()}>
              {busy ? 'Holding…' : venue.deposit ? 'Continue to payment' : 'Continue'}
            </Button>
            <p className="muted small">
              {venue.deposit && <>We hold the time for you while you pay the {formatMoney(venue.deposit)} deposit{repeat ? ' for each date' : ''}. </>}
              Free cancellation up to {venue.policy.cancellationCutoffHours} hours before the start.
            </p>
          </form>
        )}
      </Card>
    </div>
  );
}
