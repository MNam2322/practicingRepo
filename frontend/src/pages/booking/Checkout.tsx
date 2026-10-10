import { useState } from 'react';
import { ApiError, formatDay, formatMoney, type Booking, type Venue } from '../../lib/api';
import { TEST_CARDS } from '../../lib/labels';
import { describeError, useCancelBooking, type useConfirm } from '../../lib/queries';
import { Button } from '../../components/Button';
import { Card } from '../../components/Card';
import { Field } from '../../components/Field';
import { HoldCountdown } from '../../components/HoldCountdown';
import { ErrorNotice, Notice } from '../../components/Notice';

/**
 * The payment step for a hold: what is held, the time left, the deposit, and a test card to pay with. Paying and
 * releasing go through the API; when the time is up the booking page asks the API what happened and shows that.
 */
export function Checkout({ booking: b, venue, now, confirm }: {
  booking: Booking; venue: Venue; now: number; confirm: ReturnType<typeof useConfirm>;
}) {
  const release = useCancelBooking();
  const [card, setCard] = useState(TEST_CARDS[0].token);
  const timeUp = b.holdExpiresAt != null && Date.parse(b.holdExpiresAt) <= now;
  const deposit = b.deposit;
  const busy = confirm.isPending || release.isPending;
  // A provider that did not answer: the same request again is safe, the API reuses the payment.
  const retryable = confirm.error instanceof ApiError && confirm.error.code === 'payment_unavailable';

  const pay = () => confirm.mutate({ booking: b, paymentToken: deposit ? card : '' });

  return (
    <Card as="section" className="details checkout" aria-label="Pay and confirm">
      <p><strong>{formatDay(b.localStart.slice(0, 10), 'long')}, {b.localStart.slice(11)}–{b.localEnd.slice(11)}</strong>
        {' '}<span className="muted small">({venue.timeZone.replaceAll('_', ' ')} time)</span></p>
      <p className="muted">Under {b.customerName}{b.email && <>; the confirmation goes to {b.email}</>}</p>

      {b.holdExpiresAt && <HoldCountdown createdAt={b.createdAt} expiresAt={b.holdExpiresAt} now={now} />}

      {deposit?.status === 'pending' && (
        <Notice kind="info">
          <p>We are waiting to hear from the payment provider about your last attempt. Paying again is safe: you will not be charged twice.</p>
        </Notice>
      )}
      {confirm.isError && <ErrorNotice {...describeError(confirm.error)} retry={retryable ? pay : undefined} />}

      <form onSubmit={(e) => { e.preventDefault(); pay(); }}>
        {deposit ? (
          <>
            <p>A deposit of <strong>{formatMoney(deposit)}</strong> confirms the booking. It is refunded if you cancel at least{' '}
              {venue.policy.cancellationCutoffHours} hours before the start, or if the club cancels.</p>
            <Field label="Test card" hint="This is a test payment step: no real card and no real money. Choose how the payment should go.">
              <select value={card} onChange={(e) => { setCard(e.target.value); confirm.reset(); }} disabled={busy}>
                {TEST_CARDS.map((c) => <option key={c.token} value={c.token}>{c.label}</option>)}
              </select>
            </Field>
          </>
        ) : <p>Confirm to book this time.</p>}
        <div className="actions">
          <Button type="submit" disabled={busy || timeUp}>
            {confirm.isPending ? 'Paying…' : deposit ? `Pay ${formatMoney(deposit)} and confirm` : 'Confirm booking'}
          </Button>
          <Button variant="ghost" disabled={busy} onClick={() => release.mutate(b)}>Release this time</Button>
        </div>
      </form>
    </Card>
  );
}
