import { test, expect } from '@playwright/test';

// Sprint 7 hardening: the security headers Caddy stamps on every response.
// These run against the real compose stack (Caddy → api), so a Caddyfile
// regression fails CI, not launch day.
test.describe('security headers', () => {
  test('document response carries CSP and hardening headers', async ({ request }) => {
    const response = await request.get('/');
    expect(response.status()).toBe(200);

    const csp = response.headers()['content-security-policy'];
    expect(csp).toBeTruthy();
    expect(csp).toContain("default-src 'self'");
    expect(csp).toContain("frame-ancestors 'none'");
    expect(csp).toContain('wasm-unsafe-eval'); // Blazor WASM must keep running

    expect(response.headers()['x-content-type-options']).toBe('nosniff');
    expect(response.headers()['x-frame-options']).toBe('DENY');
    expect(response.headers()['referrer-policy']).toBe('strict-origin-when-cross-origin');
    expect(response.headers()['permissions-policy']).toContain('geolocation=(self)');
    expect(response.headers()['strict-transport-security']).toContain('max-age=');
  });

  test('proxied API responses are covered too', async ({ request }) => {
    const response = await request.get('/health');
    expect(response.status()).toBe(200);
    expect(response.headers()['x-content-type-options']).toBe('nosniff');
    expect(response.headers()['content-security-policy']).toBeTruthy();
  });

  test('the app still boots under the CSP', async ({ page }) => {
    const violations: string[] = [];
    page.on('console', (msg) => {
      if (msg.text().includes('Content Security Policy')) violations.push(msg.text());
    });
    await page.goto('/');
    await expect(page.getByTestId('health-status')).toContainText('ok');
    expect(violations, `CSP violations:\n${violations.join('\n')}`).toHaveLength(0);
  });
});
