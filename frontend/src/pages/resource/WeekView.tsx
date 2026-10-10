import { addDays, formatDay, type Availability, type Resource, type Slot } from '../../lib/api';
import { describeError, useAvailabilities } from '../../lib/queries';
import { resourceHref } from '../../lib/router';
import { ErrorNotice } from '../../components/Notice';
import { Skeleton } from '../../components/Skeleton';
import { SLOT_LABEL } from './slots';

/**
 * Seven days side by side for one resource: days as columns, times as rows. A free cell links to that day with
 * the slot already chosen. Days outside the booking window are left out.
 */
export function WeekView({ resource, start, lastDay }: { resource: Resource; start: string; lastDay: string }) {
  const days = Array.from({ length: 7 }, (_, i) => addDays(start, i)).filter((d) => d <= lastDay);
  const week = useAvailabilities(resource.id, days);

  if (week.error && !week.data) return <ErrorNotice {...describeError(week.error)} retry={week.refetch} />;
  if (!week.data) return (
    <div role="status" aria-label="Loading the week…" aria-busy="true" className="week-skeleton">
      {Array.from({ length: 8 }, (_, i) => <Skeleton key={i} className="line wide" />)}
    </div>
  );

  // Rows are every start time that occurs on any of the days (opening hours differ by weekday).
  const byDay = new Map<string, Map<string, Slot>>(week.data.map((a: Availability) => [a.date, new Map(a.slots.map((s) => [s.localStart, s]))]));
  const times = [...new Set(week.data.flatMap((a) => a.slots.map((s) => s.localStart)))].sort();
  if (times.length === 0) return <p className="muted">Closed all week.</p>;

  return (
    <div className="week-scroll" tabIndex={0} aria-label={`${resource.name}, week from ${formatDay(start, 'long')}`}>
      <table className="week">
        <thead>
          <tr>
            <th scope="col"><span className="visually-hidden">Time</span></th>
            {days.map((d) => <th key={d} scope="col"><a href={resourceHref(resource.id, d)}>{formatDay(d)}</a></th>)}
          </tr>
        </thead>
        <tbody>
          {times.map((t) => (
            <tr key={t}>
              <th scope="row">{t}</th>
              {days.map((d) => <td key={d}><Cell resourceId={resource.id} day={d} slot={byDay.get(d)?.get(t)} /></td>)}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function Cell({ resourceId, day, slot }: { resourceId: string; day: string; slot?: Slot }) {
  if (!slot) return <span className="cell closed" aria-label="Closed">–</span>;
  const label = `${formatDay(day, 'long')} ${slot.localStart}`;
  if (slot.state === 'free') return <a className="cell slot free" href={resourceHref(resourceId, day, 'day', slot.start)} aria-label={`${label}, free: choose`}>Free</a>;
  return <span className={`cell slot ${slot.state}`} aria-label={`${label}, ${SLOT_LABEL[slot.state].toLowerCase()}`}>{SLOT_LABEL[slot.state]}</span>;
}
