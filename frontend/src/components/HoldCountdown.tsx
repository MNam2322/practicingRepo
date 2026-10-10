/**
 * Time left on a hold: minutes and seconds, and a bar that empties. Screen readers are not told every second; the
 * polite live region below changes only when a threshold is crossed (five minutes, one minute, time up).
 */
export function HoldCountdown({ createdAt, expiresAt, now }: { createdAt: string; expiresAt: string; now: number }) {
  const total = Math.max(Date.parse(expiresAt) - Date.parse(createdAt), 1);
  // Never more than the hold's length: the clock estimate can be a second ahead of the server.
  const left = Math.min(Math.max(Date.parse(expiresAt) - now, 0), total);
  const seconds = Math.floor(left / 1000);
  const text = `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
  const announcement = left === 0 ? 'Time is up.'
    : left <= 60_000 ? 'Less than a minute left to pay.'
      : left <= 5 * 60_000 ? 'Less than five minutes left to pay.' : '';

  return (
    <div className={`countdown${left <= 60_000 ? ' urgent' : ''}`}>
      <p>
        <span role="timer" className="countdown-time" aria-label={`${text} left to pay`}>{text}</span>
        <span className="muted"> left to pay. After that the time is released for others.</span>
      </p>
      <div className="countdown-bar" aria-hidden="true"><div style={{ width: `${(left / total) * 100}%` }} /></div>
      <p className="visually-hidden" aria-live="polite">{announcement}</p>
    </div>
  );
}
