import type { Booking } from './api';

// An iCalendar (RFC 5545) file for one booking. Times are the API's UTC instants written with a Z, so every calendar
// app shows them in its own time zone; nothing about slot times is computed here.

const utc = (iso: string) => new Date(iso).toISOString().replace(/[-:]/g, '').replace(/\.\d{3}/, '');

/** Commas, semicolons, backslashes and newlines must be escaped in text values. */
const text = (value: string) => value.replace(/\\/g, '\\\\').replace(/;/g, '\\;').replace(/,/g, '\\,').replace(/\r?\n/g, '\\n');

/** Lines longer than 75 octets are folded: CRLF then a space (RFC 5545 section 3.1). */
function fold(line: string): string {
  const bytes = new TextEncoder().encode(line);
  if (bytes.length <= 75) return line;
  const parts: string[] = [];
  let current = '';
  for (const ch of line) {
    if (new TextEncoder().encode(current + ch).length > (parts.length ? 74 : 75)) { parts.push(current); current = ''; }
    current += ch;
  }
  parts.push(current);
  return parts.join('\r\n ');
}

export function bookingIcs(booking: Booking, venueName: string): string {
  const lines = [
    'BEGIN:VCALENDAR',
    'VERSION:2.0',
    'PRODID:-//Booking app//Bookings//EN',
    'CALSCALE:GREGORIAN',
    'METHOD:PUBLISH',
    'BEGIN:VEVENT',
    `UID:${booking.id}@booking-app`,
    `DTSTAMP:${utc(booking.createdAt)}`,
    `DTSTART:${utc(booking.start)}`,
    `DTEND:${utc(booking.end)}`,
    `SUMMARY:${text(`${booking.resourceName} at ${venueName}`)}`,
    `LOCATION:${text(venueName)}`,
    `DESCRIPTION:${text(`Booked under ${booking.customerName}. Booking ${booking.id}.`)}`,
    booking.status === 'cancelled' ? 'STATUS:CANCELLED' : 'STATUS:CONFIRMED',
    'END:VEVENT',
    'END:VCALENDAR',
  ];
  return lines.map(fold).join('\r\n') + '\r\n';
}

/** Saves the booking as an .ics file through a temporary link. */
export function downloadIcs(booking: Booking, venueName: string) {
  const blob = new Blob([bookingIcs(booking, venueName)], { type: 'text/calendar;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = `booking-${booking.localStart.slice(0, 10)}-${booking.resourceId}.ics`;
  document.body.append(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 0);
}
