import { test, expect, Page, TestInfo } from '@playwright/test';

/**
 * Sprint 4.5 gate: the rapid-rate surface lets a user express their palate
 * across many drinks fast — inline star taps on a browse list, zero per-drink
 * navigation. Test 1 proves the flow (8 inline ratings + a private 9th + the
 * unrated filter). Test 2 measures the SAME run's per-drink time in the rapid
 * flow vs the old search-per-drink flow and attaches a founder-readable
 * timing summary as the gate evidence.
 */

const ADULT_DOB = '1990-04-17';

async function shot(page: Page, testInfo: TestInfo, name: string) {
  const screenshot = await page.screenshot({ fullPage: true });
  await testInfo.attach(name, { body: screenshot, contentType: 'image/png' });
}

async function signup(page: Page, prefix: string) {
  const uniq = Date.now();
  await page.goto('/signup');
  await page.fill('#handle', `${prefix}_${uniq}`);
  await page.fill('#email', `${prefix}_${uniq}@example.test`);
  await page.fill('#password', 'correct-horse-battery');
  await page.fill('#dob', ADULT_DOB);
  await page.getByTestId('signup-submit').click();
  // Fresh accounts land on the interest gate; skip routes to /search.
  await expect(page.getByTestId('onboarding-skip')).toBeVisible({ timeout: 20_000 });
  await page.getByTestId('onboarding-skip').click();
  await expect(page.getByTestId('search-input')).toBeVisible({ timeout: 20_000 });
}

/** Rate the nth visible rapid-rate card and wait for its saved state. */
async function rateCard(page: Page, index: number, stars: string) {
  const card = page.getByTestId('rr-card').nth(index);
  await card.getByRole('button', { name: stars, exact: true }).click();
  await expect(card.getByTestId('rr-saved')).toBeVisible({ timeout: 15_000 });
}

test('browse, filter, rate 8 drinks inline, private 9th, unrated filter', async ({ page }, testInfo) => {
  test.setTimeout(180_000);
  await signup(page, 'rapid');

  // The Search page carries the doorway into rapid rate (no nav item — spec).
  await expect(page.getByTestId('search-rapid-rate')).toBeVisible();
  await page.getByTestId('search-rapid-rate').click();

  const cards = page.getByTestId('rr-card');
  await expect(cards.first()).toBeVisible({ timeout: 20_000 });
  await shot(page, testInfo, '01-rapid-rate-list');

  // Category framing: rip through beers only.
  await page.getByTestId('rr-cat-beer').click();
  await expect(cards.first()).toBeVisible({ timeout: 15_000 });
  await shot(page, testInfo, '02-beer-filter');

  // Rate 8 drinks inline — varied values (palate expression, not a click-through).
  const values = ['4.5 stars', '3 stars', '5 stars', '2.5 stars',
                  '4 stars', '3.5 stars', '1.5 stars', '4 stars'];
  const ratedNames: string[] = [];
  for (let i = 0; i < 8; i++) {
    ratedNames.push((await cards.nth(i).locator('.bb-rr-card__name').innerText()).trim());
    await rateCard(page, i, values[i]);
    if (i === 0) await shot(page, testInfo, '03-first-noted');
    if (i === 3) await shot(page, testInfo, '04-four-noted');
  }
  await shot(page, testInfo, '05-eight-noted');

  // 9th drink: the secondary private toggle must not interrupt the flow.
  const ninth = cards.nth(8);
  ratedNames.push((await ninth.locator('.bb-rr-card__name').innerText()).trim());
  await ninth.getByTestId('rr-private').click();
  await rateCard(page, 8, '3.5 stars');
  await shot(page, testInfo, '06-private-ninth');

  // All 9 landed through the real rating pipeline with correct visibility.
  const journal = await (await page.request.get('/api/ratings/mine?pageSize=50')).json();
  expect(journal.total).toBe(9);
  const byVisibility = (v: string) =>
    journal.items.filter((r: { visibility: string }) => r.visibility === v).length;
  expect(byVisibility('private')).toBe(1);
  expect(byVisibility('public')).toBe(8);
  expect(journal.items.every((r: { locationContext: string }) => r.locationContext === 'home_bar')).toBe(true);

  // "Haven't rated" excludes everything just rated on the next fetch.
  await page.getByTestId('rr-unrated').click();
  await expect(page.getByTestId('rr-list').or(page.getByTestId('rr-empty'))).toBeVisible({ timeout: 15_000 });
  const remaining = await cards.locator('.bb-rr-card__name').allInnerTexts();
  for (const name of ratedNames) {
    expect(remaining.map(n => n.trim())).not.toContain(name);
  }
  await shot(page, testInfo, '07-unrated-filter');

  // Hard Rule 4 guard: no volume celebration anywhere on the surface.
  const copy = await page.locator('body').innerText();
  expect(copy).not.toMatch(/rated!|keep going|keep it up|streak|almost there/i);
});

test('timing: rapid flow per-drink beats the old search-per-drink flow', async ({ page }, testInfo) => {
  test.setTimeout(240_000);
  await signup(page, 'timing');

  // --- OLD FLOW: search → drink page → rate → back, per drink (2 samples). ---
  // NOT 'golden nugget' — private-rating.spec asserts GLOBAL drink-page state
  // on it and requires that no other spec rates it publicly.
  const oldFlowDrinks = ['fat tire', 'two hearted'];
  const oldStart = Date.now();
  for (const query of oldFlowDrinks) {
    await page.goto('/search');
    await page.getByTestId('search-input').fill(query);
    const results = page.getByTestId('search-result');
    await expect(results.first()).toBeVisible({ timeout: 15_000 });
    await results.first().click();
    await expect(page.getByTestId('drink-name')).toBeVisible({ timeout: 15_000 });
    await page.getByRole('button', { name: '4 stars', exact: true }).click();
    await page.getByTestId('rating-save').click();
    await expect(page.getByTestId('rating-saved')).toBeVisible({ timeout: 15_000 });
  }
  const oldElapsedMs = Date.now() - oldStart;
  const oldPerDrinkMs = oldElapsedMs / oldFlowDrinks.length;

  // --- RAPID FLOW: one list, 8 inline ratings. Unrated filter first so the ---
  // --- two old-flow drinks (now this user's most-rated) don't repeat.      ---
  await page.goto('/rapid-rate');
  const cards = page.getByTestId('rr-card');
  await expect(cards.first()).toBeVisible({ timeout: 20_000 });
  await page.getByTestId('rr-unrated').click();
  await expect(cards.first()).toBeVisible({ timeout: 15_000 });

  const rapidStart = Date.now();
  for (let i = 0; i < 8; i++) {
    await rateCard(page, i, i % 2 === 0 ? '4 stars' : '3.5 stars');
  }
  const rapidElapsedMs = Date.now() - rapidStart;
  const rapidPerDrinkMs = rapidElapsedMs / 8;
  await shot(page, testInfo, 'timing-rapid-eight-rated');

  // Gate assertion 1: 8 ratings land comfortably fast in absolute terms
  // (expected ~10s; 60s ceiling absorbs slow CI without flaking).
  expect(rapidElapsedMs, `8 rapid ratings took ${rapidElapsedMs}ms`).toBeLessThan(60_000);

  // Gate assertion 2: per-drink, rapid beats the old flow measured in the
  // SAME run on the SAME box — machine speed cancels out. The old flow
  // carries a search round-trip + two navigations per drink, so the real
  // ratio is large; asserting a specific ratio would only add flake.
  expect(rapidPerDrinkMs, `rapid ${rapidPerDrinkMs}ms/drink vs old ${oldPerDrinkMs}ms/drink`)
    .toBeLessThan(oldPerDrinkMs);

  const summary = [
    '# Rapid-rate timing summary (same run, same machine)',
    '',
    `| Flow | Drinks | Total | Per drink |`,
    `| --- | --- | --- | --- |`,
    `| Old: search → drink page → rate → back | ${oldFlowDrinks.length} | ${(oldElapsedMs / 1000).toFixed(1)}s | ${(oldPerDrinkMs / 1000).toFixed(1)}s |`,
    `| Rapid rate: inline list | 8 | ${(rapidElapsedMs / 1000).toFixed(1)}s | ${(rapidPerDrinkMs / 1000).toFixed(1)}s |`,
    '',
    `Extrapolated: 8 drinks the old way ≈ ${((oldPerDrinkMs * 8) / 1000).toFixed(1)}s`
    + ` vs ${(rapidElapsedMs / 1000).toFixed(1)}s in rapid rate`
    + ` (${(oldPerDrinkMs / rapidPerDrinkMs).toFixed(1)}× faster per drink).`,
  ].join('\n');
  await testInfo.attach('timing-summary.md', { body: summary, contentType: 'text/markdown' });
});
