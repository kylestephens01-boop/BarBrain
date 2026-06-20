import { defineConfig, devices } from '@playwright/test';

/**
 * Playwright smoke config. The app under test is the docker compose stack
 * (web on :5000, proxying the API). Bring the stack up first, then run; CI
 * does `docker compose up` before this. Override the target with BASE_URL.
 */
export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  reporter: [['list'], ['html', { open: 'never' }]],
  outputDir: 'test-results',
  use: {
    baseURL: process.env.BASE_URL ?? 'http://localhost:5000',
    screenshot: 'on',
    trace: 'on-first-retry',
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
  ],
});
