import { useState } from 'react';
import { bookingWhen as when, type Booking } from '../lib/api';
import { describeError, useMyBookings } from '../lib/queries';
import { CancelBooking } from '../components/CancelBooking';
import { ErrorNotice } from '../components/Notice';
import { BookingsSkeleton } from '../components/Skeleton';

function Row({ b }: { b: Booking }) {
  return (
    <li className={`booking ${b.status}`}>
      <div>
        <a className="booking-link" href={`#/b/${b.id}`}><strong>{b.resourceName}</strong></a>
        <span className="muted"> · {when(b)}</span>
        <div className="small muted">Under {b.customerName} · <span className={`status ${b.status}`}>{b.status}</span></div>
      </div>
      <CancelBooking booking={b} />
    </li>
  );
}

export function MyBookings() {
  const mine = useMyBookings();
  const [now] = useState(() => Date.now());
  const list = mine.data ?? [];
  const upcoming = list.filter((b) => b.status === 'confirmed' && Date.parse(b.end) > now);
  const other = list.filter((b) => !upcoming.includes(b)).reverse();

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
        </>
      )}
    </>
  );
}
