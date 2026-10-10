import type { BookingStatus, Deposit } from './api';

/** How each booking status reads on screen, everywhere (My bookings, the booking page, the staff schedule). */
export const STATUS_LABEL: Record<BookingStatus, string> = {
  confirmed: 'Confirmed', cancelled: 'Cancelled', completed: 'Completed', noShow: 'No-show',
  pending: 'Held', expired: 'Expired',
};

/** How a deposit's state reads next to a booking. */
export const DEPOSIT_LABEL: Record<Deposit['status'], string> = {
  due: 'Due', pending: 'Being processed', paid: 'Paid', failed: 'Not paid', refunded: 'Refunded',
};

/**
 * The fake payment provider's test cards (backend: FakePaymentGateway). A real provider's card form would hand back a
 * token like these; here the customer picks the outcome, so every path can be tried.
 */
export const TEST_CARDS: { token: string; label: string }[] = [
  { token: 'tok_visa', label: 'Visa •••• 4242 (approved)' },
  { token: 'tok_declined', label: 'Card declined' },
  { token: 'tok_insufficient_funds', label: 'Not enough funds' },
  { token: 'tok_expired_card', label: 'Expired card' },
  { token: 'tok_unavailable', label: 'Provider down (nothing charged)' },
  { token: 'tok_timeout', label: 'Provider times out (charged, answer lost)' },
];
