import type { ReactElement } from 'react';
import { QueryClientProvider } from '@tanstack/react-query';
import { render } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Toaster } from '../components/Toaster';
import { createQueryClient } from '../lib/queries';

/** Renders as main.tsx does (query client, toasts), with a fresh cache, and a user to click and type. */
export function renderApp(ui: ReactElement) {
  const client = createQueryClient();
  client.setDefaultOptions({ queries: { retry: false, refetchOnWindowFocus: false }, mutations: { retry: false } });
  return { user: userEvent.setup(), client, ...render(<QueryClientProvider client={client}><Toaster>{ui}</Toaster></QueryClientProvider>) };
}
