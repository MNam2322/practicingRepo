import { defineConfig, devices } from '@playwright/test';

// Browser tests against the running app: start the API (backend: dotnet run --project src/BookingApp.Api) and the
// UI (npm run dev) first, then npm run e2e. E2E_BASE_URL points at the UI if it is not on the default port.
export default defineConfig({
  testDir: 'e2e',
  timeout: 60_000,
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:5173',
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
