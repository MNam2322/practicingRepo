import { formatDay, formatMoney, type Booking, type Venue } from '../lib/api';
import { downloadIcs } from '../lib/calendar';
import { DEPOSIT_LABEL, STATUS_LABEL } from '../lib/labels';
import { describeError, useBooking, useConfirm } from '../lib/queries';
import { useServerNow } from '../lib/useServerNow';
import { Button } from '../components/Button';
import { CancelBooking } from '../components/CancelBooking';
import { Card } from '../components/Card';
import { ErrorNotice, Notice } from '../components/Notice';
import { BookingsSkeleton } from '../components/Skeleton';
import { Checkout } from './booking/Checkout';

/**
 * One booking. While it is a hold, this is the payment step (Checkout); once the hold has run out, it says what
 * happened; otherwise it shows the details, cancel (if the API allows it) and an .ics download.
 */
export function BookingPage({ id, venue }: { id: string; venue: Venue }) {
  const booking = useBooking(id);
  const confirm = useConfirm();
  const now = useServerNow(booking.data?.status === 'pending');

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
  const again = `#/r/${b.resourceId}/${b.localStart.slice(0, 10)}`;

  if (b.status === 'pending') return (
    <>
      <title>{`Pay and confirm · ${b.resourceName}`}</title>
      <p className="crumb"><a href={again}>{b.resourceName}</a></p>
      <h1>Almost booked: {b.resourceName}</h1>
      <p className="muted">We are holding this time for you while you pay.</p>
      <Checkout booking={b} venue={venue} now={now} confirm={confirm} />
    </>
  );

  if (b.status === 'expired') return (
    <>
      <title>{`Hold expired · ${b.resourceName}`}</title>
      <p className="crumb"><a href={again}>{b.resourceName}</a></p>
      <h1>Your hold ran out</h1>
      <HoldExpired b={b} venue={venue} day={day} />
      <p><a className="btn" href={again}>Choose a time again</a></p>
    </>
  );

  const time = `${b.localStart.slice(11)}–${b.localEnd.slice(11)}`;
  const upcoming = b.status === 'confirmed' && Date.parse(b.end) > now;
  const justBooked = confirm.isSuccess && confirm.data.id === b.id && b.status === 'confirmed';

  return (
    <>
      <title>{`${b.resourceName} · ${day}`}</title>
      <p className="crumb"><a href="#/mine">My bookings</a></p>
      <h1>{b.resourceName}</h1>
      <p className="muted">{venue.name}</p>
      {justBooked && (
        <Notice kind="ok">
          <p><strong>Booked.</strong> {b.resourceName}, {day}, {time}.{b.deposit?.status === 'paid' && <> Deposit of {formatMoney(b.deposit)} paid.</>}</p>
        </Notice>
      )}

      <Card as="section" className="details" aria-label="Booking details">
        <dl>
          <dt>Date</dt><dd>{day}</dd>
          <dt>Time</dt><dd>{time} <span className="muted small">({venue.timeZone.replaceAll('_', ' ')} time)</span></dd>
          <dt>Status</dt><dd><span className={`status ${b.status}`}>{STATUS_LABEL[b.status]}</span></dd>
          <dt>Name</dt><dd>{b.customerName}</dd>
          {b.email && <><dt>Emails to</dt><dd>{b.email}</dd></>}
          {b.deposit && <><dt>Deposit</dt><dd>{formatMoney(b.deposit)} · {DEPOSIT_LABEL[b.deposit.status]}</dd></>}
          <dt>Booked</dt><dd>{new Date(b.createdAt).toLocaleString()}</dd>
          {b.cancelledAt && <><dt>Cancelled</dt><dd>{new Date(b.cancelledAt).toLocaleString()}</dd></>}
          {b.cancellationReason && <><dt>Reason</dt><dd>Cancelled by the club: {b.cancellationReason}</dd></>}
        </dl>
        <div className="actions">
          {upcoming && <Button onClick={() => downloadIcs(b, venue.name)}>Add to calendar</Button>}
          <CancelBooking booking={b} />
        </div>
        {b.status === 'confirmed' && !b.canCancel && upcoming && (
          <p className="muted small">It is too late to cancel online: the cut-off is {venue.policy.cancellationCutoffHours} hours before the start.</p>
        )}
        {b.status === 'confirmed' && b.canCancel && b.deposit?.status === 'paid' && (
          <p className="muted small">Cancel up to {venue.policy.cancellationCutoffHours} hours before the start and the deposit is refunded.</p>
        )}
      </Card>
      <p><a href={again}>See {b.resourceName} on {day}</a></p>
    </>
  );
}

/** What happened to a hold that ran out, and whether any money moved. */
function HoldExpired({ b, venue, day }: { b: Booking; venue: Venue; day: string }) {
  const at = b.holdExpiresAt
    ? new Intl.DateTimeFormat(undefined, { hour: '2-digit', minute: '2-digit', timeZone: venue.timeZone }).format(new Date(b.holdExpiresAt))
    : null;
  return (
    <Notice kind="info">
      <p>We held <strong>{b.resourceName}, {day}, {b.localStart.slice(11)}–{b.localEnd.slice(11)}</strong> for you
        {at && <> until {at}</>}, but it was not paid for in time, so the time was released for others to book.</p>
      <p>{b.deposit?.status === 'refunded' ? <>The deposit of {formatMoney(b.deposit)} was taken just as the hold ran out, so it has been refunded.</>
        : b.deposit?.status === 'paid' ? <>The deposit of {formatMoney(b.deposit)} was taken just as the hold ran out. It will be refunded automatically.</>
          : b.deposit?.status === 'pending' ? <>If your last payment went through after all, it will be refunded automatically.</>
            : <>You have not been charged.</>}</p>
    </Notice>
  );
}
