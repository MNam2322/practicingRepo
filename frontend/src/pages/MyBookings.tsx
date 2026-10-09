import { useState } from 'react';
import { api, formatDay, type Booking } from '../lib/api';
import { useApi } from '../lib/useApi';
import { CancelBooking } from '../components/CancelBooking';
import { ErrorNotice } from '../components/Notice';
import { BookingsSkeleton } from '../components/Skeleton';

function when(b: Booking) {
  return `${formatDay(b.localStart.slice(0, 10), 'long')}, ${b.localStart.slice(11)}–${b.localEnd.slice(11)}`;
}

function Row({ b, onCancelled }: { b: Booking; onCancelled: () => void }) {
  return (
    <li className={`booking ${b.status}`}>
      <div>
        <a className="booking-link" href={`#/b/${b.id}`}><strong>{b.resourceName}</strong></a>
        <span className="muted"> · {when(b)}</span>
        <div className="small muted">Under {b.customerName} · <span className={`status ${b.status}`}>{b.status}</span></div>
      </div>
      <CancelBooking booking={b} label={when(b)} onCancelled={onCancelled} />
    </li>
  );
}

export function MyBookings() {
  const [mine, reload] = useApi('mine', api.mine);
  const [now] = useState(() => Date.now());
  const list = mine.status === 'ready' ? mine.data : [];
  const upcoming = list.filter((b) => b.status === 'confirmed' && Date.parse(b.end) > now);
  const other = list.filter((b) => !upcoming.includes(b)).reverse();

  return (
    <>
      <title>My bookings</title>
      <h1>My bookings</h1>
      {mine.status === 'loading' && <><h2 className="section-title">Upcoming</h2><BookingsSkeleton /></>}
      {mine.status === 'error' && <ErrorNotice message={mine.error} traceId={mine.traceId} retry={reload} />}
      {mine.status === 'ready' && (
        <>
          <h2 className="section-title">Upcoming</h2>
          {upcoming.length === 0 ? <p className="muted">Nothing booked yet. <a href="#/">Book a time</a>.</p>
            : <ul className="bookings">{upcoming.map((b) => <Row key={b.id} b={b} onCancelled={reload} />)}</ul>}
          {other.length > 0 && <>
            <h2 className="section-title">Past and cancelled</h2>
            <ul className="bookings">{other.map((b) => <Row key={b.id} b={b} onCancelled={reload} />)}</ul>
          </>}
        </>
      )}
    </>
  );
}
