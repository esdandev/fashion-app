import { defineConfig, devices } from '@playwright/test'

const baseURL = 'http://localhost:5173'
const apiURL = 'http://localhost:5210'

/**
 * Both servers are started by Playwright with an isolated test database and
 * uploads root (`frontend/.test-data`, wiped on every run). Stop any local dev
 * server on ports 5210/5173 before running the E2E, otherwise the isolated
 * processes cannot bind.
 */
export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 2 : 0,
  reporter: [['list']],
  use: {
    baseURL,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  webServer: [
    {
      command: 'node tests/test-backend.mjs',
      url: `${apiURL}/api/health`,
      reuseExistingServer: false,
      timeout: 180_000,
    },
    {
      command: 'pnpm dev --port 5173 --strictPort',
      url: baseURL,
      reuseExistingServer: false,
      timeout: 120_000,
      env: {
        VITE_API_URL: apiURL,
      },
    },
  ],
})
