import { test, expect, Page, TestInfo } from '@playwright/test';

/**
 * Sprint 2 acceptance: Google + Apple flows (mock providers in CI — the same
 * scheme names and pipeline as production) land on the DOB-capture step
 * BEFORE any account exists, and under-21 is blocked on both.
 * Requires AUTH_ENABLE_MOCK_EXTERNAL=true in the stack (CI sets it).
 */

const ADULT_DOB = '1990-04-17';

function youngDob(): string {
  const d = new Date();
  d.setFullYear(d.getFullYear() - 20);
  return d.toISOString().slice(0, 10);
}

async function shot(page: Page, testInfo: TestInfo, name: string) {
  const screenshot = await page.screenshot({ fullPage: true });
  await testInfo.attach(name, { body: screenshot, contentType: 'image/png' });
}

/** Signup page → provider button → mock consent → DOB-capture page. */
async function danceToDobCapture(page: Page, testInfo: TestInfo, provider: 'google' | 'apple', email: string) {
  await page.goto('/signup');
  const button = page.getByTestId(`oauth-${provider}`);
  await expect(button, `mock ${provider} must be enabled in the stack`).toBeVisible({ timeout: 20_000 });
  await button.click();

  // The mock provider's consent page (plain HTML, plays the external role).
  await expect(page.locator('#email')).toBeVisible({ timeout: 15_000 });
  await shot(page, testInfo, `${provider}-consent`);
  await page.fill('#email', email);
  await page.getByRole('button', { name: 'Continue' }).click();

  // Post-auth DOB capture — no account exists yet.
  await expect(page.getByTestId('pending-provider')).toBeVisible({ timeout: 20_000 });
  await shot(page, testInfo, `${provider}-dob-capture`);
}

for (const provider of ['google', 'apple'] as const) {
  test(`${provider}: OAuth arrival hits DOB capture and activates after the gate`, async ({ page }, testInfo) => {
    const uniq = Date.now();
    await danceToDobCapture(page, testInfo, provider, `e2e_${provider}_${uniq}@example.test`);

    await page.fill('#handle', `${provider}_user_${uniq}`);
    await page.fill('#dob', ADULT_DOB);
    await page.getByTestId('complete-submit').click();

    // Lands signed in on the first-run search screen.
    await expect(page.getByTestId('first-run-hint')).toBeVisible({ timeout: 20_000 });
    await shot(page, testInfo, `${provider}-signed-in`);

    const me = await page.request.get('/api/auth/me');
    expect(me.ok()).toBeTruthy();
    expect((await me.json()).handle).toBe(`${provider}_user_${uniq}`);
  });

  test(`${provider}: under-21 is blocked at DOB capture with no account`, async ({ page }, testInfo) => {
    const uniq = Date.now();
    await danceToDobCapture(page, testInfo, provider, `e2e_kid_${provider}_${uniq}@example.test`);

    await page.fill('#handle', `kid_${provider}_${uniq}`);
    await page.fill('#dob', youngDob());
    await page.getByTestId('complete-submit').click();

    await expect(page.getByTestId('age-stop')).toBeVisible({ timeout: 15_000 });
    await shot(page, testInfo, `${provider}-under21-stop`);

    const me = await page.request.get('/api/auth/me');
    expect(me.status()).toBe(401);
  });
}
