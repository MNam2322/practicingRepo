/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// The .NET API (../backend) runs on http://localhost:5080. Proxying /api keeps one origin in the browser,
// so the customer cookie works without CORS. Override the target with API_URL if the API runs elsewhere.
const api = process.env.API_URL ?? 'http://localhost:5080'

export default defineConfig({
  plugins: [react()],
  server: { port: 5173, proxy: { '/api': api, '/health': api } },
  preview: { port: 4173, proxy: { '/api': api, '/health': api } },
  // Component tests (npm run test): jsdom, the API mocked with MSW (src/test/). Browser tests are in e2e/ (npm run e2e).
  // css: the contrast test reads index.css as text (Vitest stubs CSS otherwise).
  test: { environment: 'jsdom', setupFiles: ['src/test/setup.ts'], include: ['src/**/*.test.{ts,tsx}'], css: true },
})
