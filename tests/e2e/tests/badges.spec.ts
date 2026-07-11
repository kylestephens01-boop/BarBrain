import { test, expect, Page, TestInfo } from '@playwright/test';

/**
 * Sprint 6 acceptance: badge awarded end-to-end — rate 5 styles → breadth
 * badge toast + gallery, with screenshots. Uses seeded national-catalog
 * drinks with five DISTINCT styles (ADR-016: distinct counts only).
 */

const ADULT_DOB = '1990-04-17';

// (search term, expected result fragment) — five distinct BJCP styles:
// 2A, 16D, 21A, 20A, 24A.
const FIVE_STYLES: Array<[string, RegExp]> = [
  ['modelo especial', /modelo/i],
  ['foreign extra', /foreign extra/i],
  ['torpedo', /torpedo/i],
  ['black butte porter', /black butte/i],
  ['allagash white', /allagash white/i],
];

async function shot(page: Page, testInfo: TestInfo, name: string) {
  const screenshot = await page.screenshot({ fullPage: true });
  await testInfo.attach(name, { body: screenshot, contentType: 'image/png' });
}

test('rating five styles earns the breadth badge: toast + gallery', async ({ page }, testInfo) => {
  test.setTimeout(240_000);
  const uniq = Date.now();

  // Fresh account.
  await page.goto('/signup');
  await page.fill('#handle', `badges_${uniq}`);
  await page.fill('#email', `badges_${uniq}@example.test`);
  await page.fill('#password', 'correct-horse-battery');
  await page.fill('#dob', ADULT_DOB);
  await page.getByTestId('signup-submit').click();
  await expect(page.getByTestId('interest-beer')).toBeVisible({ timeout: 20_000 });
  await page.getByTestId('onboarding-skip').click();
  await expect(page.getByTestId('search-input')).toBeVisible({ timeout: 20_000 });

  // Rate five drinks across five distinct styles.
  for (const [term, fragment] of FIVE_STYLES) {
    await page.goto('/search');
    await page.getByTestId('search-input').fill(term);
    const results = page.getByTestId('search-result');
    await expect(results.filter({ hasText: fragment }).first()).toBeVisible({ timeout: 15_000 });
    await results.filter({ hasText: fragment }).first().click();

    await expect(page.getByTestId('drink-name')).toBeVisible({ timeout: 15_000 });
    await page.getByRole('button', { name: '4 stars', exact: true }).click();
    await page.getByTestId('rating-save').click();
    await expect(page.getByTestId('rating-saved')).toBeVisible({ timeout: 15_000 });
  }

  // The fifth distinct style fires Style Sampler — the award toast shows.
  const toast = page.getByTestId('badge-toast').filter({ hasText: 'Style Sampler' });
  await expect(toast).toBeVisible({ timeout: 15_000 });
  await shot(page, testInfo, '01-badge-toast');

  // The gallery shows it earned, in the amber earned treatment, with the
  // streak card present (weekly discovery streak, ADR-016).
  await page.goto('/profile');
  await page.getByTestId('badges-tab').click();
  await expect(page.getByTestId('streak-card')).toBeVisible({ timeout: 15_000 });
  await expect(page.getByTestId('streak-weeks')).toHaveText('1');

  const earned = page.getByTestId('badge-earned');
  await expect(earned.filter({ hasText: 'Style Sampler' })).toBeVisible();
  await expect(earned.filter({ hasText: 'First Taste' })).toBeVisible();
  // Locked badges render too — the gallery shows the whole ladder.
  await expect(page.getByTestId('badge-locked').first()).toBeVisible();
  await shot(page, testInfo, '02-badge-gallery');

  // The header stat line counts badges.
  await expect(page.getByTestId('profile-stats')).toContainText('badge');
});
