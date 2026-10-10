import { useState } from 'react';
import { formatDay, type Booking, type Venue } from '../lib/api';
import { downloadIcs } from '../lib/calendar';
import { describeError, useBooking } from '../lib/queries';
import { Button } from '../components/Button';
import { CancelBooking } from '../components/CancelBooking';
import { Card } from '../components/Card';
import { ErrorNotice } from '../components/Notice';
import { BookingsSkeleton } from '../components/Skeleton';

const STATUS_LABEL: Record<Booking['status'], string> = {
  confirmed: 'Confirmed', cancelled: 'Cancelled', completed: 'Completed', noShow: 'No-show',
};

/** One booking: when and where, its status, cancel (if the API allows it) and an .ics download. */
export function BookingPage({ id, venue }: { id: string; venue: Venue }) {
  const booking = useBooking(id);
  const [now] = useState(() => Date.now());

  if (booking.isPending) return <><title>Booking</title><h1>Booking</h1><BookingsSkeleton rows={1} /></>;
  if (booking.isError) return (
    <>
      <title>Booking</title>
      <p className="crumb"><a href="#/mine">My bookings</a></p>
      <h1>Booking</h1>
      <ErrorNotice {...describeError(booking.error)} retry={() => void booking.refetch()} />
    </>
  );

  const b = booking.data;
  const day = formatDay(b.localStart.slice(0, 10), 'long');
  const time = `${b.localStart.slice(11)}–${b.localEnd.slice(11)}`;
  const upcoming = b.status === 'confirmed' && Date.parse(b.end) > now;

  return (
    <>
      <title>{`${b.resourceName} · ${day}`}</title>
      <p className="crumb"><a href="#/mine">My bookings</a></p>
      <h1>{b.resourceName}</h1>
      <p className="muted">{venue.name}</p>

      <Card as="section" className="details" aria-label="Booking details">
        <dl>
          <dt>Date</dt><dd>{day}</dd>
          <dt>Time</dt><dd>{time} <span className="muted small">({venue.timeZone.replaceAll('_', ' ')} time)</span></dd>
          <dt>Status</dt><dd><span className={`status ${b.status}`}>{STATUS_LABEL[b.status]}</span></dd>
          <dt>Name</dt><dd>{b.customerName}</dd>
          <dt>Booked</dt><dd>{new Date(b.createdAt).toLocaleString()}</dd>
          {b.cancelledAt && <><dt>Cancelled</dt><dd>{new Date(b.cancelledAt).toLocaleString()}</dd></>}
        </dl>
        <div className="actions">
          {upcoming && <Button onClick={() => downloadIcs(b, venue.name)}>Add to calendar</Button>}
          <CancelBooking booking={b} />
        </div>
        {b.status === 'confirmed' && !b.canCancel && upcoming && (
          <p className="muted small">It is too late to cancel online: the cut-off is {venue.policy.cancellationCutoffHours} hours before the start.</p>
        )}
      </Card>
      <p><a href={`#/r/${b.resourceId}/${b.localStart.slice(0, 10)}`}>See {b.resourceName} on {day}</a></p>
    </>
  );
}
