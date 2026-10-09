import type { Resource, Venue } from '../lib/api';

const KIND_LABEL: Record<string, string> = { court: 'Court', room: 'Room', table: 'Table', other: 'Space' };

export function Home({ venue, resources }: { venue: Venue; resources: Resource[] }) {
  return (
    <>
      <section className="hero">
        <h1>Book at {venue.name}</h1>
        <p className="muted">
          Pick a court or room, choose a time, done. Book up to {venue.policy.maxDaysAhead} days ahead;
          cancel free of charge up to {venue.policy.cancellationCutoffHours} hours before.
        </p>
      </section>
      <div className="cards">
        {resources.map((r) => (
          <a key={r.id} className="card resource-card" href={`#/r/${r.id}`}>
            <span className="pill">{KIND_LABEL[r.kind] ?? r.kind}</span>
            <h2>{r.name}</h2>
            <p className="muted small">
              {r.slotMinutes}-minute slots · up to {r.capacity} people · max {r.maxSlotsPerBooking * r.slotMinutes / 60} h
            </p>
            <ul className="hours small">
              {r.hours.map((h) => <li key={h.day}><span>{h.day.slice(0, 3)}</span>{h.open}–{h.close}</li>)}
            </ul>
            <span className="cta">See times →</span>
          </a>
        ))}
      </div>
    </>
  );
}
