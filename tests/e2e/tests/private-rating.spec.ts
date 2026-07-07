import { test, expect, Page, TestInfo } from '@playwright/test';

/**
 * Sprint 2 / Gate B: flip one rating private and it VANISHES from the drink
 * page in a logged-out browser (ADR-012 per-rating toggle; the authz behavior
 * a founder phone-check verifies by hand, run here on every PR).
 */

async function shot(page: Page, testInfo: TestInfo, name: string) {
  const screenshot = await page.screenshot({ fullPage: true });
  await testInfo.attach(name, { body: screenshot, contentType: 'image/png' });
}

test('a rating flipped private vanishes from the logged-out drink page', async ({ page, browser }, testInfo) => {
  test.setTimeout(180_000);
  const uniq = Date.now();
  const handle = `privy_${uniq}`;
  const note = `distinctive-note-${uniq}`;

  // Sign up and rate a seeded drink PUBLICLY, from the UI.
  await page.goto('/signup');
  await page.fill('#handle', handle);
  await page.fill('#email', `${handle}@example.test`);
  await page.fill('#password', 'correct-horse-battery');
  await page.fill('#dob', '1990-04-17');
  await page.getByTestId('signup-submit').click();
  await expect(page.getByTestId('interest-beer')).toBeVisible({ timeout: 20_000 });
  await page.getByTestId('onboarding-skip').click();
  await expect(page.getByTestId('search-input')).toBeVisible({ timeout: 20_000 });

  await page.getByTestId('search-input').fill('king sue');
  await page.getByTestId('search-result').first().click();
  await expect(page.getByTestId('drink-name')).toBeVisible({ timeout: 15_000 });
  const drinkUrl = page.url();

  await page.getByRole('button', { name: '5 stars', exact: true }).click();
  await page.getByTestId('rating-note').fill(note);
  await page.getByTestId('rating-save').click();
  await expect(page.getByTestId('rating-saved')).toBeVisible({ timeout: 15_000 });

  // A logged-out browser sees the pseudonymous public rating.
  const strangerContext = await browser.newContext();
  const stranger = await strangerContext.newPage();
  await stranger.goto(drinkUrl);
  await expect(stranger.getByTestId('public-ratings')).toContainText(handle, { timeout: 20_000 });
  await expect(stranger.getByTestId('public-ratings')).toContainText(note);
  await shot(stranger, testInfo, '1-public-rating-visible');

  // Owner flips it private from the journal.
  await page.goto('/profile');
  await expect(page.getByTestId('journal-item').first()).toBeVisible({ timeout: 15_000 });
  await page.getByTestId('journal-visibility').first().click();
  await expect(page.getByTestId('journal-list')).toContainText('private', { timeout: 15_000 });
  await shot(page, testInfo, '2-flipped-private-in-journal');

  // The stranger reloads: gone. Handle, note, count — all of it.
  await stranger.reload();
  await expect(stranger.getByTestId('no-public-ratings')).toBeVisible({ timeout: 20_000 });
  await expect(stranger.getByTestId('public-ratings')).not.toContainText(handle);
  await expect(stranger.getByTestId('public-ratings')).not.toContainText(note);
  await shot(stranger, testInfo, '3-vanished-when-logged-out');

  await strangerContext.close();
});
