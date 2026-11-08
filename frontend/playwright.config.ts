import { defineConfig, devices } from '@playwright/test'

const baseURL = 'http://localhost:5173'

/**
 * The backend is started separately in fake-AI mode (see README). Keeping it out
 * of `webServer` avoids starting a .NET process from every test run and makes
 * failures easier to diagnose.
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
  webServer: {
    command: 'pnpm dev',
    url: baseURL,
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
    env: {
      VITE_API_URL: process.env.VITE_API_URL ?? 'http://localhost:5210',
    },
  },
})
