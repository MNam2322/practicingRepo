import { useState } from 'react';
import { bookingWhen as when, formatDay, type Booking } from '../lib/api';
import { STATUS_LABEL, WAITLIST_LABEL } from '../lib/labels';
import { describeError, useLeaveWaitlist, useMyBookings, useMyWaitlist } from '../lib/queries';
import { Button } from '../components/Button';
import { CancelBooking } from '../components/CancelBooking';
import { ErrorNotice } from '../components/Notice';
import { BookingsSkeleton } from '../components/Skeleton';

function Row({ b }: { b: Booking }) {
  return (
    <li className={`booking ${b.status}`}>
      <div>
        <a className="booking-link" href={`#/b/${b.id}`}><strong>{b.resourceName}</strong></a>
        <span className="muted"> · {when(b)}</span>
        <div className="small muted">Under {b.customerName} · <span className={`status ${b.status}`}>{STATUS_LABEL[b.status]}</span></div>
        {b.cancellationReason && <div className="small muted">Cancelled by the club: {b.cancellationReason}</div>}
        {b.status === 'pending' && <div className="small"><a href={`#/b/${b.id}`}>Pay and confirm</a> before the hold runs out.</div>}
        {b.seriesId && <div className="small"><a href={`#/s/${b.seriesId}`}>Weekly: see all dates</a></div>}
      </div>
      <CancelBooking booking={b} />
    </li>
  );
}

/** Places on waitlists for times still to come: position in line, or told the time is free. */
function Waitlist() {
  const waitlist = useMyWaitlist();
  const leave = useLeaveWaitlist();
  const open = (waitlist.data ?? []).filter((e) => e.status === 'waiting' || e.status === 'notified');
  if (open.length === 0) return null;
  return (
    <>
      <h2 className="section-title">Waitlist</h2>
      <ul className="bookings waitlist">
        {open.map((e) => (
          <li key={e.id} className="booking">
            <div>
              <a className="booking-link" href={`#/r/${e.resourceId}/${e.localStart.slice(0, 10)}`}><strong>{e.resourceName}</strong></a>
              <span className="muted"> · {formatDay(e.localStart.slice(0, 10), 'long')}, {e.localStart.slice(11)}–{e.localEnd.slice(11)}</span>
              <div className="small muted">
                {e.status === 'waiting'
                  ? <><span className="position">Number {e.position} in line</span> · we email {e.email} if it frees up</>
                  : <><span className="position">{WAITLIST_LABEL[e.status]}</span> · <a href={`#/r/${e.resourceId}/${e.localStart.slice(0, 10)}?at=${encodeURIComponent(e.start)}`}>book it now</a></>}
              </div>
            </div>
            {e.status === 'waiting' && <Button variant="ghost" disabled={leave.isPending} onClick={() => leave.mutate(e)}>Leave</Button>}
          </li>
        ))}
      </ul>
    </>
  );
}

export function MyBookings() {
  const mine = useMyBookings();
  const [now] = useState(() => Date.now());
  const list = mine.data ?? [];
  // Holds still running count as upcoming (they can be paid); a hold that ran out without money moving is not worth
  // listing.
  const shown = list.filter((b) => b.status !== 'expired' || (b.deposit != null && b.deposit.status !== 'failed'));
  const upcoming = shown.filter((b) => (b.status === 'confirmed' || b.status === 'pending') && Date.parse(b.end) > now);
  const other = shown.filter((b) => !upcoming.includes(b)).reverse();

  return (
    <>
      <title>My bookings</title>
      <h1>My bookings</h1>
      {mine.isPending && <><h2 className="section-title">Upcoming</h2><BookingsSkeleton /></>}
      {mine.isError && <ErrorNotice {...describeError(mine.error)} retry={() => void mine.refetch()} />}
      {mine.isSuccess && (
        <>
          <h2 className="section-title">Upcoming</h2>
          {upcoming.length === 0 ? <p className="muted">Nothing booked yet. <a href="#/">Book a time</a>.</p>
            : <ul className="bookings">{upcoming.map((b) => <Row key={b.id} b={b} />)}</ul>}
          {other.length > 0 && <>
            <h2 className="section-title">Past and cancelled</h2>
            <ul className="bookings">{other.map((b) => <Row key={b.id} b={b} />)}</ul>
          </>}
          <Waitlist />
        </>
      )}
    </>
  );
}
