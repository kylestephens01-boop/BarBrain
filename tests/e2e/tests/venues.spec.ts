import { test, expect, Page, TestInfo } from '@playwright/test';

/**
 * Sprint 5 acceptance (the Gate D demo, headless): a profiled user creates a
 * venue, stocks its menu with 8 corridor drinks, sees the pre-check-in teaser,
 * checks in with one tap (no GPS — geolocation stays denied throughout), and
 * gets the four shelves with reasons. Screenshots at every step.
 */

async function shot(page: Page, testInfo: TestInfo, name: string) {
  const screenshot = await page.screenshot({ fullPage: true });
  await testInfo.attach(name, { body: screenshot, contentType: 'image/png' });
}

// CI seeds the corridor catalog; the first three are the quiz staples this
// user rates (5★ / 4.5★ / 1.5★), so the shelves have history to work with.
const MENU_DRINKS = [
  'Pseudo Sue',
  'King Sue',
  "Dorothy's New World Lager",
  'Easy Eddy',
  'Ruthie',
  'Schild Brau Amber',
  'Iowa Pale Ale',
  'Citrus Surfer',
];

test('create venue → 8 menu items → teaser → check in → four shelves with reasons', async ({ page }, testInfo) => {
  test.setTimeout(240_000);
  const uniq = Date.now();

  // --- A profiled user (same quiz path the Sprint 3 spec proves). -------------
  await page.goto('/signup');
  await page.fill('#handle', `venue_${uniq}`);
  await page.fill('#email', `venue_${uniq}@example.test`);
  await page.fill('#password', 'correct-horse-battery');
  await page.fill('#dob', '1990-04-17');
  await page.getByTestId('signup-submit').click();

  await expect(page.getByTestId('interest-beer')).toBeVisible({ timeout: 20_000 });
  await page.getByTestId('interest-beer').click();
  await page.getByTestId('interest-continue').click();

  const quizItems = page.getByTestId('quiz-item');
  await expect(quizItems.first()).toBeVisible({ timeout: 20_000 });
  await quizItems.nth(0).getByRole('button', { name: '5 stars', exact: true }).click();
  await expect(page.getByTestId('quiz-counter')).toContainText('1 rated', { timeout: 15_000 });
  await quizItems.nth(1).getByRole('button', { name: '4.5 stars', exact: true }).click();
  await expect(page.getByTestId('quiz-counter')).toContainText('2 rated');
  await quizItems.nth(2).getByRole('button', { name: '1.5 stars', exact: true }).click();
  await expect(page.getByTestId('quiz-counter')).toContainText('3 rated');
  await page.getByTestId('quiz-next').click();
  await expect(page.getByTestId('onboarding-radar')).toBeVisible({ timeout: 20_000 });

  // --- Create the venue (wiki add; geolocation denied → no geo, still works). --
  await page.goto('/venues');
  await expect(page.getByTestId('venue-add')).toBeVisible({ timeout: 20_000 });
  await shot(page, testInfo, '01-venue-list');

  await page.getByTestId('venue-add-open').click();
  await page.getByTestId('venue-add-name').fill(`Corridor Demo Bar ${uniq}`);
  await page.getByTestId('venue-add-address').fill('123 Third St SE, Cedar Rapids');
  await page.getByTestId('venue-add-submit').click();

  // Lands on the venue page, pre-check-in: the teaser is the acceptance signal.
  await expect(page.getByTestId('venue-name')).toContainText('Corridor Demo Bar', { timeout: 20_000 });
  await expect(page.getByTestId('checkin-teaser')).toBeVisible();
  await expect(page.getByTestId('checkin-teaser')).toContainText('Check in to see this menu sorted for you');
  await shot(page, testInfo, '02-precheckin-teaser');

  // --- Stock the menu: 8 corridor drinks through the wiki picker. --------------
  await page.getByTestId('menu-add-open').click();
  for (const name of MENU_DRINKS) {
    await page.getByTestId('menu-add-search').fill(name);
    const results = page.getByTestId('menu-add-results');
    await expect(results.locator('button').first()).toBeVisible({ timeout: 15_000 });
    await results.locator('button').first().click();
    await page.getByTestId('menu-add-submit').click();
    // The list reloads; the item shows in the flat pre-check-in menu.
    await expect(page.getByTestId('menu-item').filter({ hasText: name })).toBeVisible({ timeout: 15_000 });
  }
  const flatCount = await page.getByTestId('menu-item').count();
  expect(flatCount).toBe(8);
  await shot(page, testInfo, '03-flat-menu-8-items');

  // --- One-tap check-in (NO GPS permission granted anywhere in this test). -----
  await page.getByTestId('checkin-button').click();
  await expect(page.getByTestId('checkin-banner')).toBeVisible({ timeout: 20_000 });
  await shot(page, testInfo, '04-checked-in');

  // --- The four shelves, in the founder-ruled order, every rec with a reason. --
  for (const shelf of ['favorites', 'familiar', 'adventurous', 'new_for_you'] as const) {
    const tab = page.getByTestId(`shelf-tab-${shelf}`);
    await expect(tab).toBeVisible();
    await tab.click();
    await shot(page, testInfo, `05-shelf-${shelf}`);
  }

  // Favorites holds the 5★ quiz staple; Familiar the 1.5★ one.
  await page.getByTestId('shelf-tab-favorites').click();
  await expect(page.getByTestId('menu-rec').filter({ hasText: 'Pseudo Sue' }).first())
    .toBeVisible({ timeout: 15_000 });
  await page.getByTestId('shelf-tab-familiar').click();
  await expect(page.getByTestId('menu-rec').filter({ hasText: "Dorothy's New World Lager" }))
    .toBeVisible({ timeout: 15_000 });

  // Every visible rec carries its because (ADR-013 holds on menus too).
  for (const shelf of ['favorites', 'familiar', 'adventurous', 'new_for_you'] as const) {
    await page.getByTestId(`shelf-tab-${shelf}`).click();
    const recs = page.getByTestId('menu-rec');
    const count = await recs.count();
    for (let i = 0; i < count; i++) {
      await expect(recs.nth(i).getByTestId('menu-rec-reason')).not.toBeEmpty();
    }
  }

  // All 8 drinks landed on exactly one shelf between them.
  let total = 0;
  for (const shelf of ['favorites', 'familiar', 'adventurous', 'new_for_you'] as const) {
    await page.getByTestId(`shelf-tab-${shelf}`).click();
    total += await page.getByTestId('menu-rec').count();
  }
  expect(total).toBe(8);

  // --- The venue shows up on the nearby list (name order — no geo). ------------
  await page.goto('/venues');
  await expect(page.getByTestId('venue-card').filter({ hasText: 'Corridor Demo Bar' }))
    .toBeVisible({ timeout: 20_000 });
  await shot(page, testInfo, '06-venue-in-list');
});
