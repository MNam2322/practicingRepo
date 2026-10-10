import { useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { formatDay, formatMoney, type Series, type SeriesChanges, type Venue } from '../lib/api';
import { DATE_FAILURE, DEPOSIT_LABEL, STATUS_LABEL, TEST_CARDS } from '../lib/labels';
import { describeError, keys, useSeries, useSeriesChange } from '../lib/queries';
import { useServerNow } from '../lib/useServerNow';
import { Button } from '../components/Button';
import { Card } from '../components/Card';
import { HoldCountdown } from '../components/HoldCountdown';
import { ErrorNotice, Notice } from '../components/Notice';
import { BookingsSkeleton } from '../components/Skeleton';
import { TestCardField } from './booking/Checkout';

/**
 * A weekly series: which dates were held and which could not be (from the answer to booking it), paying for the held
 * dates in one go, and cancelling what is left. Every outcome is the API's, date by date.
 */
export function SeriesPage({ id, venue }: { id: string; venue: Venue }) {
  const series = useSeries(id);
  const change = useSeriesChange();
  const created = useQueryClient().getQueryData<Series>(keys.seriesCreated(id));
  const [card, setCard] = useState(TEST_CARDS[0].token);
  const [confirmingCancel, setConfirmingCancel] = useState(false);
  const list = series.data ?? [];
  const held = list.filter((b) => b.status === 'pending');
  const now = useServerNow(held.length > 0);

  if (series.isPending) return <><title>Weekly booking</title><h1>Weekly booking</h1><BookingsSkeleton rows={3} /></>;
  if (series.isError) return (
    <>
      <title>Weekly booking</title>
      <p className="crumb"><a href="#/mine">My bookings</a></p>
      <h1>Weekly booking</h1>
      <ErrorNotice {...describeError(series.error)} retry={() => void series.refetch()} />
    </>
  );

  const first = list[0];
  const weekday = new Intl.DateTimeFormat(undefined, { weekday: 'long' }).format(new Date(`${first.localStart.slice(0, 10)}T12:00`));
  const soonest = held.reduce<(typeof held)[number] | null>((a, b) => (a && Date.parse(a.holdExpiresAt!) <= Date.parse(b.holdExpiresAt!) ? a : b), null);
  const timeUp = soonest?.holdExpiresAt != null && Date.parse(soonest.holdExpiresAt) <= now;
  const deposit = held.find((b) => b.deposit)?.deposit;
  const total = deposit ? { amount: deposit.amount * held.length, currency: deposit.currency } : null;
  const cancellable = list.filter((b) => (b.status === 'pending' || b.status === 'confirmed') && Date.parse(b.start) > now && b.canCancel);
  const busy = change.isPending;
  const failedDates = created?.dates.filter((d) => d.outcome === 'failed') ?? [];

  return (
    <>
      <title>{`Weekly · ${first.resourceName}`}</title>
      <p className="crumb"><a href="#/mine">My bookings</a></p>
      <h1>{first.resourceName}, every {weekday} at {first.localStart.slice(11)}</h1>
      <p className="muted">{venue.name} · {venue.timeZone.replaceAll('_', ' ')} time</p>

      {created && (
        <Notice kind={failedDates.length ? 'info' : 'ok'}>
          <p><strong>{created.held} of {created.dates.length} dates held.</strong>{failedDates.length > 0 && ' These could not be:'}</p>
          {failedDates.length > 0 && (
            <ul className="series-failed">
              {failedDates.map((d) => (
                <li key={d.index}>{formatDay(d.localDate, 'long')}: {DATE_FAILURE[d.code ?? ''] ?? d.message}</li>
              ))}
            </ul>
          )}
        </Notice>
      )}
      {change.isSuccess && <ChangeOutcome result={change.data} />}
      {change.isError && <ErrorNotice {...describeError(change.error)} />}

      {held.length > 0 && soonest?.holdExpiresAt && (
        <Card as="section" className="details checkout" aria-label="Pay and confirm the dates">
          <HoldCountdown createdAt={soonest.createdAt} expiresAt={soonest.holdExpiresAt} now={now} />
          <form onSubmit={(e) => { e.preventDefault(); change.mutate({ id, action: 'confirm', paymentToken: deposit ? card : '' }); }}>
            {total ? (
              <>
                <p>A deposit of <strong>{formatMoney(deposit!)}</strong> for each date confirms it: <strong>{formatMoney(total)}</strong> for {held.length} dates.</p>
                <TestCardField value={card} onChange={(token) => { setCard(token); change.reset(); }} disabled={busy} />
              </>
            ) : <p>Confirm to book the held dates.</p>}
            <div className="actions">
              <Button type="submit" disabled={busy || timeUp}>
                {busy ? 'Paying…' : total ? `Pay ${formatMoney(total)} and confirm ${held.length} dates` : `Confirm ${held.length} dates`}
              </Button>
            </div>
          </form>
        </Card>
      )}

      <Card as="section" className="details" aria-label="Dates">
        <h2 className="section-title">Dates</h2>
        <ul className="series-dates">
          {list.map((b) => (
            <li key={b.id}>
              <a href={`#/b/${b.id}`}>{formatDay(b.localStart.slice(0, 10), 'long')}</a>
              <span className="muted small"> · {b.localStart.slice(11)}–{b.localEnd.slice(11)}</span>
              <span className={`status ${b.status}`}> {STATUS_LABEL[b.status]}</span>
              {b.deposit && <span className="muted small"> · deposit {DEPOSIT_LABEL[b.deposit.status].toLowerCase()}</span>}
            </li>
          ))}
        </ul>
        {cancellable.length > 0 && (
          confirmingCancel ? (
            <div className="actions">
              <Button variant="danger" disabled={busy} onClick={() => { setConfirmingCancel(false); change.mutate({ id, action: 'cancel' }); }}>
                Yes, cancel {cancellable.length} dates
              </Button>
              <Button variant="ghost" onClick={() => setConfirmingCancel(false)}>Keep them</Button>
            </div>
          ) : (
            <Button variant="ghost" disabled={busy} onClick={() => setConfirmingCancel(true)}>Cancel all remaining dates</Button>
          )
        )}
        <p className="muted small">To cancel one date, open it. Deposits are refunded as for one booking.</p>
      </Card>
    </>
  );
}

/** What confirming or cancelling the series did: how many dates, and which failed and why. */
function ChangeOutcome({ result }: { result: SeriesChanges }) {
  const failed = result.results.filter((r) => !r.ok);
  if (result.results.length === 0) return null;
  return (
    <Notice kind={failed.length ? 'info' : 'ok'}>
      <p><strong>{result.succeeded} of {result.results.length} dates done.</strong></p>
      {failed.length > 0 && (
        <ul className="series-failed">
          {failed.map((r) => <li key={r.bookingId}>{formatDay(r.booking.localStart.slice(0, 10), 'long')}: {r.message}</li>)}
        </ul>
      )}
    </Notice>
  );
}
