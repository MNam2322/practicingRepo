import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterAll, afterEach, beforeAll, beforeEach } from 'vitest';
import { server, XSRF } from './api';

// The app calls fetch('/api/...') like a browser does; Node's fetch needs an absolute URL.
const nodeFetch = globalThis.fetch;
globalThis.fetch = (input: RequestInfo | URL, init?: RequestInit) =>
  nodeFetch(typeof input === 'string' && input.startsWith('/') ? new URL(input, location.origin) : input, init);

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }));
beforeEach(() => {
  document.cookie = `XSRF-TOKEN=${XSRF}; path=/`;
  location.hash = '';
});
afterEach(() => {
  cleanup();
  server.resetHandlers();
});
afterAll(() => server.close());
