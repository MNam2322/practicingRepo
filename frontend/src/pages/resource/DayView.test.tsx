import { http } from 'msw';
import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { availability, booking, court, DAY, json, problem, server, venue, write } from '../../test/api';
import { renderApp } from '../../test/render';
import { DayView } from './DayView';

const renderDay = () => renderApp(<DayView venue={venue} resource={court} day={DAY} />);
const slot = (time: string) => screen.findByRole('button', { name: new RegExp(`^${time}`) });

describe('DayView', () => {
  it('shows the chosen free time in the booking panel', async () => {
    const { user } = renderDay();
    await user.click(await slot('10:00'));

    expect(await slot('10:00')).toHaveAttribute('aria-pressed', 'true');
    const panel = screen.getByRole('heading', { name: 'Your booking' }).closest('form')!;
    expect(within(panel).getByText('10:00–11:00')).toBeInTheDocument();
    expect(within(panel).getByRole('button', { name: 'Continue to payment' })).toBeInTheDocument();
  });

  it('holds the time with the antiforgery token and goes to the payment step', async () => {
    let sent: unknown = null;
    server.use(http.post('/api/bookings', write(async (request) => {
      sent = await request.json();
      return json(booking({ status: 'pending', canConfirm: true, holdExpiresAt: '2026-06-01T00:10:00+00:00' }), 201);
    })));
    const { user } = renderDay();
    await user.click(await slot('10:00'));
    await user.type(screen.getByLabelText('Name for the booking'), 'Nam');
    await user.click(screen.getByRole('button', { name: 'Continue to payment' }));

    await waitFor(() => expect(location.hash).toBe(`#/b/${booking().id}`));
    expect(sent).toMatchObject({ resourceId: 'court-1', start: '2026-06-02T03:00:00+00:00', slots: 1, name: 'Nam' });
  });

  it('says when someone else took the time first, and offers its waitlist', async () => {
    let taken = false;
    server.use(
      http.post('/api/bookings', write(() => {
        taken = true;
        return problem(409, 'slot_taken', 'Someone has just booked that time.');
      })),
      http.get('/api/resources/court-1/availability', () => json(availability(taken ? { '10:00': 'booked' } : {}))),
    );
    const { user } = renderDay();
    await user.click(await slot('10:00'));
    await user.type(screen.getByLabelText('Name for the booking'), 'Nam');
    await user.click(screen.getByRole('button', { name: 'Continue to payment' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Someone has just booked that time. Pick another slot.');
    // The slots are reloaded: 10:00 is booked now, and the panel offers its waitlist instead of a booking.
    expect(await screen.findByRole('heading', { name: 'Join the waitlist' })).toBeInTheDocument();
    expect(location.hash).toBe('');
  });

  it('shows the API\'s field error next to the email field, announced', async () => {
    server.use(http.post('/api/bookings', write(() => json({
      type: 'about:blank', title: 'One or more validation errors occurred.', status: 400, code: 'validation_failed',
      errors: { email: ['Enter an email address such as name@example.com, or leave it empty.'] },
    }, 400))));
    const { user } = renderDay();
    await user.click(await slot('10:00'));
    await user.type(screen.getByLabelText('Name for the booking'), 'Nam');
    await user.click(screen.getByRole('button', { name: 'Continue to payment' }));

    const email = screen.getByLabelText('Email (optional)');
    await waitFor(() => expect(email).toHaveAttribute('aria-invalid', 'true'));
    expect(email).toHaveAccessibleDescription('Enter an email address such as name@example.com, or leave it empty.');
    expect(screen.getAllByRole('alert').map((a) => a.textContent)).toContain('Enter an email address such as name@example.com, or leave it empty.');
  });
});
