import { useState } from 'react';
import { api, ApiError, type Booking } from '../lib/api';
import { useToast } from '../lib/toast';
import { Button } from './Button';

/** "Cancel" with a confirm step. Shown only when the API says the booking can be cancelled (`canCancel`). */
export function CancelBooking({ booking, label, onCancelled }: { booking: Booking; label: string; onCancelled: (b: Booking) => void }) {
  const toast = useToast();
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<{ message: string; traceId: string | null } | null>(null);

  if (!booking.canCancel) return null;

  async function cancel() {
    setBusy(true); setError(null);
    try {
      const cancelled = await api.cancel(booking.id);
      toast({ text: `Cancelled ${booking.resourceName}, ${label}.` });
      onCancelled(cancelled);
    } catch (e) {
      setError({ message: e instanceof Error ? e.message : String(e), traceId: e instanceof ApiError ? e.traceId : null });
    } finally { setBusy(false); setConfirming(false); }
  }

  return (
    <div className="cancel-booking">
      {confirming ? (
        <span className="actions">
          <Button variant="danger" disabled={busy} onClick={() => void cancel()}>{busy ? 'Cancelling…' : 'Yes, cancel'}</Button>
          <Button variant="ghost" onClick={() => setConfirming(false)}>Keep</Button>
        </span>
      ) : (
        <Button variant="ghost" onClick={() => setConfirming(true)}>Cancel</Button>
      )}
      {error && <div className="small error-text" role="alert">{error.message}{error.traceId && <> (reference <code>{error.traceId}</code>)</>}</div>}
    </div>
  );
}
