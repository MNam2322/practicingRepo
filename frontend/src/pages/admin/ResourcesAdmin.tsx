import { useState, type FormEvent } from 'react';
import { api, type HoursInput, type Resource, type ResourceKind } from '../../lib/api';
import { fieldError, formError, useAdminChange } from '../../lib/queries';
import { useToast } from '../../lib/toast';
import { Button } from '../../components/Button';
import { Card } from '../../components/Card';
import { Field } from '../../components/Field';
import { ErrorNotice } from '../../components/Notice';

const KINDS: ResourceKind[] = ['court', 'room', 'table', 'other'];
const DAYS = ['monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday', 'sunday'] as const;

/** "" stays null, so the API reports the field as required instead of the browser guessing a number. */
const num = (v: string) => (v.trim() === '' ? null : Number(v));

export function ResourcesAdmin({ resources }: { resources: Resource[] }) {
  const [adding, setAdding] = useState(false);
  const [editing, setEditing] = useState<string | null>(null);
  return (
    <section aria-label="Courts and rooms">
      <div className="admin-bar">
        <p className="muted">Customers see these on the booking page. A new court or room is closed until you set its hours.</p>
        <Button onClick={() => { setAdding((a) => !a); setEditing(null); }} aria-expanded={adding}>{adding ? 'Close' : 'Add a court or room'}</Button>
      </div>
      {adding && <ResourceForm onDone={() => setAdding(false)} />}
      <ul className="admin-list">
        {resources.map((r) => (
          <li key={r.id} className="card admin-item">
            <div className="admin-item-head">
              <div>
                <strong>{r.name}</strong> <span className="muted small">· {r.id} · {r.kind} · {r.slotMinutes}-minute slots · up to {r.capacity} people</span>
                <div className="small muted">{r.hours.length === 0 ? 'Closed every day (no opening hours yet)' : r.hours.map((h) => `${h.day.slice(0, 3)} ${h.open}–${h.close}`).join(' · ')}</div>
              </div>
              <Button variant="ghost" onClick={() => { setEditing(editing === r.id ? null : r.id); setAdding(false); }} aria-expanded={editing === r.id}>
                {editing === r.id ? 'Close' : 'Edit'}
              </Button>
            </div>
            {editing === r.id && (
              <div className="admin-edit">
                <ResourceForm resource={r} onDone={() => setEditing(null)} />
                <HoursEditor resource={r} />
                <DeleteResource resource={r} onDeleted={() => setEditing(null)} />
              </div>
            )}
          </li>
        ))}
      </ul>
    </section>
  );
}

/** Add a resource (no `resource`) or change one. */
function ResourceForm({ resource, onDone }: { resource?: Resource; onDone: () => void }) {
  const toast = useToast();
  const [id, setId] = useState(resource?.id ?? '');
  const [name, setName] = useState(resource?.name ?? '');
  const [kind, setKind] = useState<ResourceKind>(resource?.kind ?? 'court');
  const [capacity, setCapacity] = useState(String(resource?.capacity ?? 4));
  const [slotMinutes, setSlotMinutes] = useState(String(resource?.slotMinutes ?? 60));
  const [maxSlots, setMaxSlots] = useState(String(resource?.maxSlotsPerBooking ?? 2));
  const save = useAdminChange(() => {
    const fields = { name, kind, capacity: num(capacity), slotMinutes: num(slotMinutes), maxSlotsPerBooking: num(maxSlots) };
    return resource ? api.updateResource(resource.id, fields) : api.createResource({ id: id.trim(), ...fields });
  });

  function submit(e: FormEvent) {
    e.preventDefault();
    save.mutate(undefined, { onSuccess: (r) => { toast({ text: resource ? `Saved ${r.name}.` : `Added ${r.name}. Set its opening hours next.` }); onDone(); } });
  }

  const err = (f: string) => fieldError(save.error, f);
  const problem = formError(save.error);
  return (
    <Card as="section" className="admin-form" aria-label={resource ? `Edit ${resource.name}` : 'Add a court or room'}>
      <h2 className="section-title">{resource ? 'Details' : 'Add a court or room'}</h2>
      {problem && <ErrorNotice {...problem} />}
      <form onSubmit={submit} noValidate className="form-grid">
        {!resource && (
          <Field label="Id (used in links)" hint="Lowercase letters, digits and dashes, such as court-3." error={err('id')}>
            <input value={id} onChange={(e) => setId(e.target.value)} required />
          </Field>
        )}
        <Field label="Name" error={err('name')}><input value={name} onChange={(e) => setName(e.target.value)} required maxLength={100} /></Field>
        <Field label="Kind" error={err('kind')}>
          <select value={kind} onChange={(e) => setKind(e.target.value as ResourceKind)}>{KINDS.map((k) => <option key={k} value={k}>{k}</option>)}</select>
        </Field>
        <Field label="Capacity (people)" error={err('capacity')}><input type="number" inputMode="numeric" value={capacity} onChange={(e) => setCapacity(e.target.value)} /></Field>
        <Field label="Slot length (minutes)" error={err('slotMinutes')}><input type="number" inputMode="numeric" value={slotMinutes} onChange={(e) => setSlotMinutes(e.target.value)} /></Field>
        <Field label="Longest booking (slots)" error={err('maxSlotsPerBooking')}><input type="number" inputMode="numeric" value={maxSlots} onChange={(e) => setMaxSlots(e.target.value)} /></Field>
        <div className="form-actions"><Button type="submit" disabled={save.isPending}>{save.isPending ? 'Saving…' : resource ? 'Save details' : 'Add'}</Button></div>
      </form>
    </Card>
  );
}

/** The week's opening hours: one row per day; untick a day to close it. */
function HoursEditor({ resource }: { resource: Resource }) {
  const toast = useToast();
  const initial = Object.fromEntries(DAYS.map((d) => {
    const h = resource.hours.find((x) => x.day === d);
    return [d, { open: !!h, from: h?.open ?? '08:00', to: h?.close ?? '20:00' }];
  }));
  const [week, setWeek] = useState(initial);
  const save = useAdminChange(() => api.setHours(resource.id,
    DAYS.filter((d) => week[d].open).map((d): HoursInput => ({ day: d, open: week[d].from, close: week[d].to }))));
  const set = (day: string, patch: Partial<(typeof initial)[string]>) => setWeek((w) => ({ ...w, [day]: { ...w[day], ...patch } }));

  function submit(e: FormEvent) {
    e.preventDefault();
    save.mutate(undefined, { onSuccess: () => toast({ text: `Saved the opening hours of ${resource.name}.` }) });
  }

  const problem = formError(save.error);
  return (
    <Card as="section" className="admin-form" aria-label={`Opening hours of ${resource.name}`}>
      <h2 className="section-title">Opening hours <span className="muted small">(venue time, {resource.slotMinutes}-minute slots)</span></h2>
      {problem && <ErrorNotice {...problem} />}
      <form onSubmit={submit} noValidate>
        <table className="hours-table">
          <thead><tr><th scope="col">Day</th><th scope="col">Open</th><th scope="col">From</th><th scope="col">Until</th></tr></thead>
          <tbody>
            {DAYS.map((d) => (
              <tr key={d}>
                <th scope="row" className="day-name">{d}</th>
                <td><input type="checkbox" aria-label={`Open on ${d}`} checked={week[d].open} onChange={(e) => set(d, { open: e.target.checked })} /></td>
                <td><input type="time" aria-label={`${d} opens`} value={week[d].from} disabled={!week[d].open} onChange={(e) => set(d, { from: e.target.value })} /></td>
                <td><input type="time" aria-label={`${d} closes`} value={week[d].to} disabled={!week[d].open} onChange={(e) => set(d, { to: e.target.value })} /></td>
              </tr>
            ))}
          </tbody>
        </table>
        <Button type="submit" disabled={save.isPending}>{save.isPending ? 'Saving…' : 'Save hours'}</Button>
      </form>
    </Card>
  );
}

function DeleteResource({ resource, onDeleted }: { resource: Resource; onDeleted: () => void }) {
  const toast = useToast();
  const [confirming, setConfirming] = useState(false);
  const remove = useAdminChange(() => api.deleteResource(resource.id));
  const problem = formError(remove.error);
  return (
    <div className="admin-danger">
      {problem && <ErrorNotice {...problem} />}
      {confirming ? (
        <span className="actions">
          <Button variant="danger" disabled={remove.isPending}
            onClick={() => remove.mutate(undefined, { onSuccess: () => { toast({ text: `Deleted ${resource.name}.` }); onDeleted(); }, onSettled: () => setConfirming(false) })}>
            Yes, delete {resource.name}
          </Button>
          <Button variant="ghost" onClick={() => setConfirming(false)}>Keep</Button>
        </span>
      ) : <Button variant="ghost" onClick={() => setConfirming(true)}>Delete…</Button>}
    </div>
  );
}
