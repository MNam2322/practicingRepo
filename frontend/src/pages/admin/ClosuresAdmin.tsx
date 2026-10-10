import { useState, type FormEvent } from 'react';
import { addDays, api, formatDay, venueToday, type Closure, type Resource, type Venue } from '../../lib/api';
import { describeError, fieldError, formError, useAdminChange, useAdminClosures } from '../../lib/queries';
import { useToast } from '../../lib/toast';
import { Button } from '../../components/Button';
import { Card } from '../../components/Card';
import { Field } from '../../components/Field';
import { ErrorNotice } from '../../components/Notice';
import { BookingsSkeleton } from '../../components/Skeleton';

/** "Thursday, December 25 – Friday, December 26", from the API's venue-local times (an end at 00:00 is the day before). */
function closedDays(c: Closure): string {
  const first = c.localStart.slice(0, 10);
  const last = c.localEnd.slice(11) === '00:00' ? addDays(c.localEnd.slice(0, 10), -1) : c.localEnd.slice(0, 10);
  const times = c.localStart.slice(11) !== '00:00' || c.localEnd.slice(11) !== '00:00' ? ` (${c.localStart.slice(11)}–${c.localEnd.slice(11)})` : '';
  return (first === last ? formatDay(first, 'long') : `${formatDay(first, 'long')} – ${formatDay(last, 'long')}`) + times;
}

export function ClosuresAdmin({ venue, resources }: { venue: Venue; resources: Resource[] }) {
  const toast = useToast();
  const today = venueToday(venue.timeZone);
  const closures = useAdminClosures(today, addDays(today, 365));
  const [resourceId, setResourceId] = useState('');
  const [from, setFrom] = useState(addDays(today, 1));
  const [to, setTo] = useState(addDays(today, 1));
  const [reason, setReason] = useState('');
  const add = useAdminChange(() => api.addClosure({ resourceId: resourceId || null, from, to, reason }));
  const remove = useAdminChange((id: string) => api.deleteClosure(id));
  const names = new Map(resources.map((r) => [r.id, r.name]));

  function submit(e: FormEvent) {
    e.preventDefault();
    add.mutate(undefined, { onSuccess: () => { toast({ text: 'Closure added. Customers cannot book those days.' }); setReason(''); } });
  }

  const err = (f: string) => fieldError(add.error, f);
  const problem = formError(add.error);
  return (
    <section aria-label="Closures">
      <Card as="section" className="admin-form" aria-label="Add a closure">
        <h2 className="section-title">Close for whole days</h2>
        <p className="muted small">For holidays choose “The whole venue”. For a few hours on one court, use Block time on the staff schedule.</p>
        {problem && <ErrorNotice {...problem} />}
        <form onSubmit={submit} noValidate className="form-grid">
          <Field label="What closes" error={err('resourceId')}>
            <select value={resourceId} onChange={(e) => setResourceId(e.target.value)}>
              <option value="">The whole venue (holiday)</option>
              {resources.map((r) => <option key={r.id} value={r.id}>{r.name}</option>)}
            </select>
          </Field>
          <Field label="First day" error={err('from')}><input type="date" value={from} min={today} onChange={(e) => setFrom(e.target.value)} /></Field>
          <Field label="Last day" error={err('to')}><input type="date" value={to} min={from} onChange={(e) => setTo(e.target.value)} /></Field>
          <Field label="Reason (customers see it)" error={err('reason')}><input value={reason} onChange={(e) => setReason(e.target.value)} maxLength={200} placeholder="Public holiday" /></Field>
          <div className="form-actions"><Button type="submit" disabled={add.isPending}>{add.isPending ? 'Adding…' : 'Add closure'}</Button></div>
        </form>
      </Card>

      <h2 className="section-title">Upcoming closures</h2>
      {remove.isError && <ErrorNotice {...describeError(remove.error)} />}
      {closures.isPending && <BookingsSkeleton rows={2} />}
      {closures.isError && <ErrorNotice {...describeError(closures.error)} retry={() => void closures.refetch()} />}
      {closures.data && closures.data.length === 0 && <p className="muted">No closures in the next year.</p>}
      {closures.data && closures.data.length > 0 && (
        <ul className="bookings">
          {closures.data.map((c) => (
            <li key={c.id} className="booking">
              <div>
                <strong>{c.resourceId ? names.get(c.resourceId) ?? c.resourceId : 'Whole venue'}</strong>
                <span className="muted"> · {closedDays(c)}</span>
                <div className="small muted">{c.reason}</div>
              </div>
              <Button variant="ghost" disabled={remove.isPending}
                onClick={() => remove.mutate(c.id, { onSuccess: () => toast({ text: 'Closure removed. Those times can be booked again.' }) })}>
                Remove
              </Button>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
