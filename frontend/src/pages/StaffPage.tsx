import { useState, type FormEvent } from 'react';
import { addDays, ApiError, formatDay, venueToday, type Schedule, type ScheduleColumn, type StaffBooking, type Venue } from '../lib/api';
import { describeError, useAvailability, useBlockTime, useSchedule, useStaffAction, type StaffAction } from '../lib/queries';
import { STATUS_LABEL } from '../lib/labels';
import { useToast } from '../lib/toast';
import { Button } from '../components/Button';
import { Card } from '../components/Card';
import { Field } from '../components/Field';
import { ErrorNotice, Notice } from '../components/Notice';
import { Skeleton } from '../components/Skeleton';

/** Pixels per minute on the timeline: an hour is 64px tall. */
const PX = 64 / 60;

/** Minutes after local midnight of `day` for a venue-local "yyyy-MM-ddTHH:mm" from the API, clamped to the day. */
function minutesInto(day: string, local: string): number {
  if (local.slice(0, 10) < day) return 0;
  if (local.slice(0, 10) > day) return 24 * 60;
  return Number(local.slice(11, 13)) * 60 + Number(local.slice(14, 16));
}

const hhmm = (minutes: number) => `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`;
const toMinutes = (time: string) => Number(time.slice(0, 2)) * 60 + Number(time.slice(3, 5));

const STATUS = STATUS_LABEL;

/**
 * The staff day schedule: resources as columns, time as rows. Bookings are real buttons, placed at their venue-local
 * times; selecting one shows what can be done to it, exactly as the API allows (`actions`).
 */
export function StaffPage({ venue, date }: { venue: Venue; date?: string }) {
  const today = venueToday(venue.timeZone);
  const day = date ?? today;
  const schedule = useSchedule(day);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [blocking, setBlocking] = useState(false);

  const selected = schedule.data?.resources.flatMap((r) => r.bookings).find((b) => b.id === selectedId) ?? null;

  return (
    <>
      <title>{`Schedule · ${formatDay(day, 'long')}`}</title>
      <h1>Schedule</h1>
      <nav className="week-nav" aria-label="Choose a day">
        <a href={`#/staff/${addDays(day, -1)}`}>← Previous day</a>
        <strong>{formatDay(day, 'long')}{day === today ? ' (today)' : ''}</strong>
        <a href={`#/staff/${addDays(day, 1)}`}>Next day →</a>
      </nav>
      <div className="staff-bar">
        {day !== today && <a href="#/staff">Back to today</a>}
        <Button variant="ghost" onClick={() => { setBlocking((b) => !b); setSelectedId(null); }} aria-expanded={blocking}>
          {blocking ? 'Close' : 'Block time'}
        </Button>
      </div>

      {schedule.isPending && <div aria-busy="true" role="status" aria-label="Loading the schedule…"><Skeleton className="card-skeleton" /></div>}
      {schedule.isError && !schedule.data && <ErrorNotice {...describeError(schedule.error)} retry={() => void schedule.refetch()} />}
      {schedule.data && (
        <div className="staff-layout">
          <Timeline schedule={schedule.data} day={day} selectedId={selectedId} onSelect={(id) => { setSelectedId(id); setBlocking(false); }} />
          <aside className="staff-side" aria-live="polite">
            {blocking && <BlockTimeForm schedule={schedule.data} day={day} onDone={() => setBlocking(false)} />}
            {selected && <BookingPanel key={selected.id} booking={selected} day={day} onClose={() => setSelectedId(null)} />}
            {!blocking && !selected && <p className="muted">Select a booking to see what you can do with it.</p>}
          </aside>
        </div>
      )}
      {schedule.data && <CancelledList schedule={schedule.data} />}
    </>
  );
}

function Timeline({ schedule, day, selectedId, onSelect }: { schedule: Schedule; day: string; selectedId: string | null; onSelect: (id: string) => void }) {
  const open = schedule.resources.filter((r) => r.open && r.close);
  if (open.length === 0) return <p className="muted">Everything is closed on this day.</p>;
  const start = Math.min(...open.map((r) => toMinutes(r.open!)));
  const end = Math.max(...open.map((r) => toMinutes(r.close!)));
  const hours = Array.from({ length: Math.ceil((end - start) / 60) }, (_, i) => start + i * 60);

  return (
    <div className="timeline-scroll" tabIndex={0} aria-label={`Schedule for ${formatDay(day, 'long')}`}>
      <div className="timeline" style={{ gridTemplateColumns: `3.5rem repeat(${schedule.resources.length}, minmax(9rem, 1fr))` }}>
        <div className="timeline-corner" />
        {schedule.resources.map((r) => <div key={r.resourceId} className="timeline-head">{r.resourceName}</div>)}
        <div className="timeline-axis" style={{ height: (end - start) * PX }} aria-hidden="true">
          {hours.map((h) => <span key={h} style={{ top: (h - start) * PX }}>{hhmm(h)}</span>)}
        </div>
        {schedule.resources.map((r) => (
          <Column key={r.resourceId} column={r} day={day} start={start} end={end} selectedId={selectedId} onSelect={onSelect} />
        ))}
      </div>
    </div>
  );
}

function Column({ column, day, start, end, selectedId, onSelect }: {
  column: ScheduleColumn; day: string; start: number; end: number; selectedId: string | null; onSelect: (id: string) => void;
}) {
  const top = (minutes: number) => (Math.max(minutes, start) - start) * PX;
  const height = (from: number, to: number) => Math.max((Math.min(to, end) - Math.max(from, start)) * PX, 0);
  const openAt = column.open ? toMinutes(column.open) : end;
  const closeAt = column.close ? toMinutes(column.close) : end;
  return (
    <div className="timeline-col" style={{ height: (end - start) * PX }}>
      {/* outside opening hours */}
      {openAt > start && <div className="tl-shut" style={{ top: 0, height: height(start, openAt) }} aria-hidden="true" />}
      {closeAt < end && <div className="tl-shut" style={{ top: top(closeAt), height: height(closeAt, end) }} aria-hidden="true" />}
      {column.closures.map((c) => {
        const from = minutesInto(day, c.localStart), to = minutesInto(day, c.localEnd);
        return (
          <div key={c.id} className="tl-closure" style={{ top: top(from), height: height(from, to) }}>
            <span>{c.reason}</span>
          </div>
        );
      })}
      {column.bookings.filter((b) => b.status !== 'cancelled' && b.status !== 'expired').map((b) => {
        const from = minutesInto(day, b.localStart), to = minutesInto(day, b.localEnd);
        return (
          <button key={b.id} type="button" className={`tl-booking ${b.status}`} aria-pressed={b.id === selectedId}
            style={{ top: top(from), height: height(from, to) }}
            aria-label={`${column.resourceName}, ${b.localStart.slice(11)} to ${b.localEnd.slice(11)}, ${b.customerName}, ${STATUS[b.status]}`}
            onClick={() => onSelect(b.id)}>
            <strong>{b.localStart.slice(11)}</strong> {b.customerName}
            {b.status !== 'confirmed' && <small>{STATUS[b.status]}</small>}
          </button>
        );
      })}
    </div>
  );
}

function BookingPanel({ booking, day, onClose }: { booking: StaffBooking; day: string; onClose: () => void }) {
  const toast = useToast();
  const act = useStaffAction(day);
  const [cancelling, setCancelling] = useState(false);
  const [reason, setReason] = useState('');
  const { actions } = booking;

  function run(action: StaffAction, done: string) {
    act.mutate({ booking, action }, { onSuccess: () => { toast({ text: done }); setCancelling(false); } });
  }

  function cancel(e: FormEvent) {
    e.preventDefault();
    run({ kind: 'cancel', reason: reason.trim() }, `Cancelled ${booking.customerName}'s booking. They will see the reason.`);
  }

  const reasonError = act.error instanceof ApiError ? act.error.fieldErrors.reason?.[0] ?? null : null;

  return (
    <Card as="section" className="staff-panel" aria-label="Booking">
      <h2 className="section-title">{booking.customerName}</h2>
      <dl className="staff-details">
        <dt>Time</dt><dd>{booking.localStart.slice(11)}–{booking.localEnd.slice(11)}</dd>
        <dt>Status</dt><dd><span className={`status ${booking.status}`}>{STATUS[booking.status]}</span></dd>
        {booking.cancellationReason && <><dt>Reason</dt><dd>{booking.cancellationReason}</dd></>}
      </dl>
      {act.isError && !reasonError && <ErrorNotice {...describeError(act.error)} />}
      {!actions.complete && !actions.noShow && !actions.cancel && (
        <p className="muted small">{booking.status === 'confirmed' ? 'It has ended; nothing more to do until it is marked.' : 'Nothing more can happen to this booking.'}</p>
      )}
      {booking.status === 'confirmed' && !actions.complete && actions.cancel && (
        <p className="muted small">Completed and no-show can be recorded once it starts.</p>
      )}
      <div className="actions">
        {actions.complete && <Button disabled={act.isPending} onClick={() => run({ kind: 'complete' }, 'Marked completed.')}>Mark completed</Button>}
        {actions.noShow && <Button variant="ghost" disabled={act.isPending} onClick={() => run({ kind: 'noShow' }, 'Marked as a no-show.')}>Mark no-show</Button>}
        {actions.cancel && !cancelling && <Button variant="ghost" onClick={() => setCancelling(true)}>Cancel booking</Button>}
      </div>
      {cancelling && (
        <form className="staff-form" onSubmit={cancel}>
          <Field label="Reason (the customer sees this)" error={reasonError}>
            <input value={reason} onChange={(e) => setReason(e.target.value)} required maxLength={200} />
          </Field>
          <span className="actions">
            <Button type="submit" variant="danger" disabled={act.isPending || !reason.trim()}>Cancel booking</Button>
            <Button variant="ghost" onClick={() => setCancelling(false)}>Keep</Button>
          </span>
        </form>
      )}
      <Button variant="ghost" className="close" onClick={onClose}>Close</Button>
    </Card>
  );
}

/** Block time: a resource, a start slot and a length from that resource's real slot grid, and a reason. */
function BlockTimeForm({ schedule, day, onDone }: { schedule: Schedule; day: string; onDone: () => void }) {
  const toast = useToast();
  const block = useBlockTime(day);
  const [resourceId, setResourceId] = useState(schedule.resources[0]?.resourceId ?? '');
  const slots = useAvailability(resourceId, day).data?.slots ?? [];
  const [startIndex, setStartIndex] = useState(0);
  const [count, setCount] = useState(1);
  const [reason, setReason] = useState('');

  const first = slots[Math.min(startIndex, slots.length - 1)];
  const last = slots[Math.min(startIndex + count - 1, slots.length - 1)];

  function submit(e: FormEvent) {
    e.preventDefault();
    if (!first || !last) return;
    block.mutate({ resourceId, start: first.start, end: last.end, reason: reason.trim() }, {
      onSuccess: () => { toast({ text: `Blocked ${first.localStart}–${last.localEnd}. Customers cannot book it.` }); onDone(); },
    });
  }

  return (
    <Card as="section" className="staff-panel" aria-label="Block time">
      <h2 className="section-title">Block time</h2>
      {block.isError && <ErrorNotice {...describeError(block.error)} />}
      <form className="staff-form" onSubmit={submit}>
        <Field label="Court or room">
          <select value={resourceId} onChange={(e) => { setResourceId(e.target.value); setStartIndex(0); setCount(1); }}>
            {schedule.resources.map((r) => <option key={r.resourceId} value={r.resourceId}>{r.resourceName}</option>)}
          </select>
        </Field>
        {slots.length === 0 ? <Notice kind="info"><p>Closed on this day.</p></Notice> : (
          <>
            <Field label="From">
              <select value={startIndex} onChange={(e) => { setStartIndex(Number(e.target.value)); setCount(1); }}>
                {slots.map((s, i) => <option key={s.start} value={i}>{s.localStart}</option>)}
              </select>
            </Field>
            <Field label="Until">
              <select value={count} onChange={(e) => setCount(Number(e.target.value))}>
                {slots.slice(startIndex).map((s, i) => <option key={s.end} value={i + 1}>{s.localEnd}</option>)}
              </select>
            </Field>
          </>
        )}
        <Field label="Reason">
          <input value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Net repair" required maxLength={200} />
        </Field>
        <Button type="submit" disabled={block.isPending || !reason.trim() || slots.length === 0}>Block {first && last ? `${first.localStart}–${last.localEnd}` : ''}</Button>
      </form>
    </Card>
  );
}

function CancelledList({ schedule }: { schedule: Schedule }) {
  const cancelled = schedule.resources.flatMap((r) => r.bookings.filter((b) => b.status === 'cancelled').map((b) => ({ r, b })));
  if (cancelled.length === 0) return null;
  return (
    <section aria-label="Cancelled today">
      <h2 className="section-title">Cancelled</h2>
      <ul className="bookings">
        {cancelled.map(({ r, b }) => (
          <li key={b.id} className="booking cancelled">
            <div><strong>{r.resourceName}</strong> <span className="muted">· {b.localStart.slice(11)}–{b.localEnd.slice(11)} · {b.customerName}</span>
              {b.cancellationReason && <div className="small muted">Reason: {b.cancellationReason}</div>}</div>
          </li>
        ))}
      </ul>
    </section>
  );
}
