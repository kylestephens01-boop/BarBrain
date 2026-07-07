import { test, expect, Page, TestInfo } from '@playwright/test';

/**
 * Sprint 2 / Gate B acceptance: a stranger signs up (email path), clears the
 * 21+ gate, and logs their first drink — in ≤6 screens and under 2 minutes,
 * with a screenshot artifact of every step. The under-21 branch is the polite
 * hard stop with no account.
 */

const ADULT_DOB = '1990-04-17';

function youngDob(): string {
  const d = new Date();
  d.setFullYear(d.getFullYear() - 20); // 20 years old today
  return d.toISOString().slice(0, 10);
}

async function shot(page: Page, testInfo: TestInfo, name: string) {
  const screenshot = await page.screenshot({ fullPage: true });
  await testInfo.attach(name, { body: screenshot, contentType: 'image/png' });
}

test('email signup → 21+ gate → first rating, under 2 minutes, ≤6 screens', async ({ page }, testInfo) => {
  test.setTimeout(180_000); // generous harness budget; the ASSERTED budget is 120s below
  const uniq = Date.now();
  const started = Date.now();

  // Screen 1: signup.
  await page.goto('/signup');
  await expect(page.getByRole('heading', { name: 'Create your account' })).toBeVisible();
  await shot(page, testInfo, '01-signup');

  await page.fill('#handle', `gateb_${uniq}`);
  await page.fill('#email', `gateb_${uniq}@example.test`);
  await page.fill('#password', 'correct-horse-battery');
  await page.fill('#dob', ADULT_DOB); // DOB inline — the age gate (screen 1½)
  await shot(page, testInfo, '02-signup-filled');
  await page.getByTestId('signup-submit').click();

  // Screen 2: search (first-run hint confirms we're signed in and activated).
  await expect(page.getByTestId('first-run-hint')).toBeVisible({ timeout: 20_000 });
  await shot(page, testInfo, '03-search');
  await page.getByTestId('search-input').fill('pseudo sue');
  const results = page.getByTestId('search-result');
  await expect(results.first()).toBeVisible({ timeout: 15_000 });
  await shot(page, testInfo, '04-search-results');
  await results.first().click();

  // Screen 3: the drink page — stars, note, location, save.
  await expect(page.getByTestId('drink-name')).toBeVisible({ timeout: 15_000 });
  await page.getByRole('button', { name: '4.5 stars', exact: true }).click();
  await page.getByTestId('rating-note').fill('First log — bright and juicy.');
  // Home Bar is already the default location (ADR-015); leave it.
  await shot(page, testInfo, '05-drink-rating-filled');
  await page.getByTestId('rating-save').click();

  await expect(page.getByTestId('rating-saved')).toBeVisible({ timeout: 15_000 });
  await expect(page.getByTestId('rating-saved')).toContainText('4.5');
  await shot(page, testInfo, '06-rating-saved');

  // Gate B's clock: signup page → saved rating in under 2 minutes.
  const elapsedMs = Date.now() - started;
  expect(elapsedMs, `signup→first-rating took ${elapsedMs}ms`).toBeLessThan(120_000);

  // And it landed in the journal (screen count stays ≤6: signup, search,
  // drink, profile = 4 distinct screens total).
  await page.goto('/profile');
  await expect(page.getByTestId('journal-item').first()).toBeVisible({ timeout: 15_000 });
  await expect(page.getByTestId('journal-list')).toContainText('Pseudo Sue');
  await shot(page, testInfo, '07-journal');
});

test('under-21 email signup is a polite hard stop with no account', async ({ page }, testInfo) => {
  const uniq = Date.now();
  await page.goto('/signup');
  await page.fill('#handle', `kid_${uniq}`);
  await page.fill('#email', `kid_${uniq}@example.test`);
  await page.fill('#password', 'correct-horse-battery');
  await page.fill('#dob', youngDob());
  await page.getByTestId('signup-submit').click();

  await expect(page.getByTestId('age-stop')).toBeVisible({ timeout: 15_000 });
  await expect(page.getByTestId('age-stop')).toContainText('21 and over');
  await shot(page, testInfo, 'under21-email-stop');

  // No session came out of it.
  const me = await page.request.get('/api/auth/me');
  expect(me.status()).toBe(401);
});
