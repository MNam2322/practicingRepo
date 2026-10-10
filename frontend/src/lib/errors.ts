// One friendly message per API error code, in one place. The UI switches on `code`, never on the server's wording
// (library 8.5 "Errors going out"). Codes not listed here fall back to the problem's title from the API.

export const ERROR_MESSAGES: Record<string, string> = {
  // availability and booking
  slot_taken: 'Someone has just booked that time. Pick another slot.',
  too_soon: 'That time is too close to now. Pick a later slot.',
  too_far_ahead: 'That date is too far ahead. Pick a date inside the booking window.',
  not_whole_slots: 'Bookings last a whole number of slots.',
  too_long: 'That booking is longer than this court or room allows. Choose a shorter length.',
  outside_opening_hours: 'It is closed at that time. Pick a time inside opening hours.',
  resource_closed: 'It is closed at that time (holiday or maintenance). Pick another time or day.',
  not_on_slot_grid: 'Bookings start on the slot times shown. Pick one of them.',
  name_required: 'Enter the name for the booking.',
  too_many_bookings: 'You already have the most upcoming bookings allowed. Cancel one or wait until one has passed.',
  resource_not_found: 'That court or room no longer exists.',
  // one booking
  booking_not_found: 'That booking was not found. It may belong to someone else.',
  not_your_booking: 'You can only change your own bookings.',
  not_cancellable: 'This booking can no longer be cancelled.',
  too_late_to_cancel: 'It is too late to cancel this booking online. Contact the club.',
  changed_meanwhile: 'This booking was changed at the same moment. Reload and try again.',
  // staff
  invalid_transition: 'That cannot be done to this booking any more. The schedule has been refreshed.',
  not_started: 'This booking has not started yet. Mark it completed or a no-show once it starts.',
  already_ended: 'This booking has already ended. Mark it completed or a no-show instead.',
  reason_required: 'Give a reason. The customer will see it.',
  bookings_in_the_way: 'Confirmed bookings overlap that time. Cancel them first (with a reason), then block the time.',
  end_before_start: 'The end must be after the start.',
  // accounts
  email_taken: 'There is already an account with that email. Sign in instead.',
  invalid_credentials: 'That email and password do not match an account.',
  account_locked: 'Too many failed attempts. Wait a few minutes and try again.',
  unauthenticated: 'Sign in to do that.',
  forbidden: 'Your account is not allowed to do that.',
  // the request itself
  validation_failed: 'Some details are missing or not valid. Check the form and try again.',
  rate_limited: 'Too many requests. Wait a minute and try again.',
  network: 'Can\'t reach the booking service. Check your connection and try again.',
};

/** The message to show for an error code, or the fallback (usually the API's problem title). */
export function messageFor(code: string | null, fallback: string): string {
  return (code && ERROR_MESSAGES[code]) || fallback;
}
