import { useState } from 'react';
import type { Booking } from '../lib/api';
import { useCancelBooking } from '../lib/queries';
import { Button } from './Button';

/**
 * "Cancel" with a confirm step. Shown only when the API says the booking can be cancelled (`canCancel`).
 * The cancel is optimistic (lib/queries.ts): the booking shows as cancelled at once, a toast reports the outcome,
 * and it is put back if the API refuses.
 */
export function CancelBooking({ booking }: { booking: Booking }) {
  const cancel = useCancelBooking();
  const [confirming, setConfirming] = useState(false);

  if (!booking.canCancel) return null;

  return confirming ? (
    <span className="actions">
      <Button variant="danger" onClick={() => { setConfirming(false); cancel.mutate(booking); }}>Yes, cancel</Button>
      <Button variant="ghost" onClick={() => setConfirming(false)}>Keep</Button>
    </span>
  ) : (
    <Button variant="ghost" onClick={() => setConfirming(true)}>Cancel</Button>
  );
}
