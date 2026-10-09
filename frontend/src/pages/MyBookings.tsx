import { useEffect, useState } from 'react';
import { api, formatDay, type Booking } from '../lib/api';
import { useApi } from '../lib/useApi';
import { ErrorBox, Loading } from '../components/Layout';

function Row({ b, onCancelled }: { b: Booking; onCancelled: () => void }) {
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function cancel() {
    setBusy(true); setError(null);
    try { await api.cancel(b.id); onCancelled(); }
    catch (e) { setError(e instanceof Error ? e.message : String(e)); }
    finally { setBusy(false); setConfirming(false); }
  }

  return (
    <li className={`booking ${b.status}`}>
      <div>
        <strong>{b.resourceName}</strong>
        <span className="muted"> · {formatDay(b.localStart.slice(0, 10), 'long')}, {b.localStart.slice(11)}–{b.localEnd.slice(11)}</span>
        <div className="small muted">Under {b.customerName} · <span className={`status ${b.status}`}>{b.status}</span></div>
        {error && <div className="small error-text">{error}</div>}
      </div>
      {b.canCancel && (confirming ? (
        <span className="actions">
          <button type="button" className="btn danger" disabled={busy} onClick={() => void cancel()}>{busy ? 'Cancelling…' : 'Yes, cancel'}</button>
          <button type="button" className="btn ghost" onClick={() => setConfirming(false)}>Keep</button>
        </span>
      ) : (
        <button type="button" className="btn ghost" onClick={() => setConfirming(true)}>Cancel</button>
      ))}
    </li>
  );
}

export function MyBookings() {
  const [mine, reload] = useApi('mine', api.mine);
  useEffect(() => { document.title = 'My bookings'; }, []);
  const [now] = useState(() => Date.now());
  const list = mine.status === 'ready' ? mine.data : [];
  const upcoming = list.filter((b) => b.status === 'confirmed' && Date.parse(b.end) > now);
  const other = list.filter((b) => !upcoming.includes(b)).reverse();

  return (
    <>
      <h1>My bookings</h1>
      {mine.status === 'loading' && <Loading />}
      {mine.status === 'error' && <ErrorBox message={mine.error} retry={reload} />}
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
