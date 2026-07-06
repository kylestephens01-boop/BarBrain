import { test, expect } from '@playwright/test';

/**
 * Sprint 0 smoke: the page loads, the API health line reports ok, and we
 * capture a screenshot artifact. Failing this blocks merge (CI gate).
 */
test('home loads and API reports healthy', async ({ page }, testInfo) => {
  await page.goto('/');

  // Wordmark renders (Blazor WASM booted and the shell painted).
  await expect(page.getByRole('heading', { name: 'BarBrain' })).toBeVisible();

  // Flag-driven banner is present and non-empty.
  const banner = page.getByTestId('home-banner');
  await expect(banner).toBeVisible();
  await expect(banner).not.toBeEmpty();

  // Live API health, proxied same-origin through Caddy, resolves to "ok".
  await expect(page.getByTestId('health-status')).toContainText('ok', { timeout: 15_000 });

  // Screenshot artifact (uploaded by CI; also attached to the report).
  const screenshot = await page.screenshot({ fullPage: true });
  await testInfo.attach('home', { body: screenshot, contentType: 'image/png' });
});

/**
 * The API is reachable through the same origin as the web app (Caddy proxy).
 */
test('GET /health returns version and sha', async ({ request }) => {
  const response = await request.get('/health');
  expect(response.ok()).toBeTruthy();

  const body = await response.json();
  expect(body.status).toBe('ok');
  expect(body.version).toBeTruthy();
  expect(body.sha).toBeTruthy();
});
