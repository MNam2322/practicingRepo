import { http } from 'msw';
import { screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { booking, json, server, write } from '../test/api';
import { renderApp } from '../test/render';
import { CancelBooking } from './CancelBooking';

describe('CancelBooking', () => {
  it('asks before cancelling, and Keep changes nothing', async () => {
    let calls = 0;
    server.use(http.post('/api/bookings/:id/cancel', write(() => {
      calls++;
      return json(booking({ status: 'cancelled', canCancel: false }));
    })));
    const { user } = renderApp(<CancelBooking booking={booking()} />);

    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    await user.click(screen.getByRole('button', { name: 'Keep' }));
    expect(calls).toBe(0);

    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    await user.click(screen.getByRole('button', { name: 'Yes, cancel' }));
    await waitFor(() => expect(calls).toBe(1));
    expect(await screen.findByText(/^Cancelled Tennis court 1/)).toBeInTheDocument();   // the toast
  });

  it('is not offered when the API says the booking cannot be cancelled', () => {
    renderApp(<CancelBooking booking={booking({ canCancel: false })} />);
    expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument();
  });
});
