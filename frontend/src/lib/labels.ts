import type { BookingStatus } from './api';

/** How each booking status reads on screen, everywhere (My bookings, the booking page, the staff schedule). */
export const STATUS_LABEL: Record<BookingStatus, string> = {
  confirmed: 'Confirmed', cancelled: 'Cancelled', completed: 'Completed', noShow: 'No-show',
};
