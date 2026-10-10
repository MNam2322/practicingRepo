import { useState, type FormEvent } from 'react';
import { api, type Venue } from '../../lib/api';
import { fieldError, formError, useAdminChange } from '../../lib/queries';
import { useToast } from '../../lib/toast';
import { Button } from '../../components/Button';
import { Card } from '../../components/Card';
import { Field } from '../../components/Field';
import { ErrorNotice } from '../../components/Notice';

const num = (v: string) => (v.trim() === '' ? null : Number(v));

/** The booking rules. They apply to new bookings and cancellations as soon as they are saved. */
export function RulesAdmin({ venue }: { venue: Venue }) {
  const toast = useToast();
  const p = venue.policy;
  const [maxDaysAhead, setMaxDaysAhead] = useState(String(p.maxDaysAhead));
  const [minLead, setMinLead] = useState(String(p.minLeadMinutes));
  const [cutoff, setCutoff] = useState(String(p.cancellationCutoffHours));
  const [maxActive, setMaxActive] = useState(String(p.maxActiveBookingsPerCustomer));
  const save = useAdminChange(() => api.updatePolicy({
    maxDaysAhead: num(maxDaysAhead), minLeadMinutes: num(minLead), cancellationCutoffHours: num(cutoff), maxActiveBookingsPerCustomer: num(maxActive),
  }));

  function submit(e: FormEvent) {
    e.preventDefault();
    save.mutate(undefined, { onSuccess: () => toast({ text: 'Saved the booking rules. They apply from now on.' }) });
  }

  const err = (f: string) => fieldError(save.error, f);
  const problem = formError(save.error);
  return (
    <Card as="section" className="admin-form" aria-label="Booking rules">
      <h2 className="section-title">Booking rules</h2>
      {problem && <ErrorNotice {...problem} />}
      <form onSubmit={submit} noValidate className="form-grid">
        <Field label="Book up to (days ahead)" hint="1 to 365" error={err('maxDaysAhead')}>
          <input type="number" inputMode="numeric" value={maxDaysAhead} onChange={(e) => setMaxDaysAhead(e.target.value)} />
        </Field>
        <Field label="Book at least (minutes before the start)" hint="0 to 1440" error={err('minLeadMinutes')}>
          <input type="number" inputMode="numeric" value={minLead} onChange={(e) => setMinLead(e.target.value)} />
        </Field>
        <Field label="Free cancellation until (hours before)" hint="0 to 168" error={err('cancellationCutoffHours')}>
          <input type="number" inputMode="decimal" step="0.5" value={cutoff} onChange={(e) => setCutoff(e.target.value)} />
        </Field>
        <Field label="Upcoming bookings per customer (at most)" hint="1 to 100" error={err('maxActiveBookingsPerCustomer')}>
          <input type="number" inputMode="numeric" value={maxActive} onChange={(e) => setMaxActive(e.target.value)} />
        </Field>
        <div className="form-actions"><Button type="submit" disabled={save.isPending}>{save.isPending ? 'Saving…' : 'Save rules'}</Button></div>
      </form>
    </Card>
  );
}
