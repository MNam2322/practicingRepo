import { addDays, formatDay, venueToday, type Resource, type Venue } from '../lib/api';
import { resourceHref, type ResourceView } from '../lib/router';
import { SlotLegend } from '../components/SlotLegend';
import { DayView } from './resource/DayView';
import { WeekView } from './resource/WeekView';

/** A resource's times, as one day with a booking panel or as a week overview. */
export function ResourcePage({ venue, resource, date, view, at }: {
  venue: Venue; resource: Resource; date?: string; view: ResourceView; at?: string;
}) {
  const today = venueToday(venue.timeZone);
  const lastDay = addDays(today, venue.policy.maxDaysAhead);
  const day = date && date >= today ? date : today;
  const days = Array.from({ length: Math.min(14, venue.policy.maxDaysAhead + 1) }, (_, i) => addDays(today, i));

  const prevWeek = addDays(day, -7) >= today ? addDays(day, -7) : day > today ? today : null;
  const nextWeek = addDays(day, 7) <= lastDay ? addDays(day, 7) : null;

  return (
    <>
      {/* React 19 hoists <title> into <head>; no effect needed. */}
      <title>{view === 'week' ? `${resource.name} · week of ${formatDay(day, 'long')}` : `${resource.name} · ${formatDay(day, 'long')}`}</title>
      <p className="crumb"><a href="#/">All resources</a></p>
      <h1>{resource.name}</h1>
      <p className="muted">{resource.slotMinutes}-minute slots · times are {venue.timeZone.replaceAll('_', ' ')} time</p>

      <div className="view-bar">
        <nav className="toggle" aria-label="View">
          <a href={resourceHref(resource.id, date, 'day')} aria-current={view === 'day' ? 'page' : undefined}>Day</a>
          <a href={resourceHref(resource.id, date, 'week')} aria-current={view === 'week' ? 'page' : undefined}>Week</a>
        </nav>
        <SlotLegend showChosen={view === 'day'} />
      </div>

      {view === 'day' ? (
        <>
          <nav className="days" aria-label="Choose a day">
            {days.map((d) => (
              <a key={d} href={resourceHref(resource.id, d)} className={d === day ? 'day on' : 'day'} aria-current={d === day ? 'date' : undefined}>
                {d === today ? 'Today' : formatDay(d)}
              </a>
            ))}
          </nav>
          <DayView venue={venue} resource={resource} day={day} initialAt={at} />
        </>
      ) : (
        <>
          <nav className="week-nav" aria-label="Choose a week">
            {prevWeek ? <a href={resourceHref(resource.id, prevWeek, 'week')}>← Earlier</a> : <span />}
            <strong>{formatDay(day, 'long')} – {formatDay(addDays(day, 6) <= lastDay ? addDays(day, 6) : lastDay, 'long')}</strong>
            {nextWeek ? <a href={resourceHref(resource.id, nextWeek, 'week')}>Later →</a> : <span />}
          </nav>
          <WeekView resource={resource} start={day} lastDay={lastDay} />
        </>
      )}
    </>
  );
}
