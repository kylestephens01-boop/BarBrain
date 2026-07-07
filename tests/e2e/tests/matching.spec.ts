import { test, expect, Page, BrowserContext, TestInfo } from '@playwright/test';

/**
 * Sprint 4 / Gate C2 acceptance (the human-reviewable slice — the numbers are
 * proven by the MatchEval suite in CI):
 *  - Two accounts that rate the same beers alike see each other in Your Matches.
 *  - Flipping the match-% display flag (eager↔conservative) changes the UI with
 *    NO deploy.
 *  - Hide-me removes a user from matches in BOTH directions, immediately.
 *
 * Both users rate the SAME corridor staples so they co-rate and match. CI seeds
 * the catalog; ADMIN_TOKEN is unset in the CI compose, so the admin match
 * rebuild + flag flip go through the stub (same as the merge-queue spec).
 */

const ADULT_DOB = '1990-04-17';
// Shared corridor beers (from quiz.staples.beer) — both users rate these.
const BEERS: [string, string][] = [
  ['pseudo sue', '5 stars'],
  ['king sue', '5 stars'],
  ['ruthie', '2 stars'],
  ['easy eddy', '4 stars'],
];

async function shot(page: Page, testInfo: TestInfo, name: string) {
  await testInfo.attach(name, { body: await page.screenshot({ fullPage: true }), contentType: 'image/png' });
}

async function signUpAndRate(context: BrowserContext, handle: string): Promise<Page> {
  const page = await context.newPage();
  await page.goto('/signup');
  await page.fill('#handle', handle);
  await page.fill('#email', `${handle}@example.test`);
  await page.fill('#password', 'correct-horse-battery');
  await page.fill('#dob', ADULT_DOB);
  await page.getByTestId('signup-submit').click();

  await expect(page.getByTestId('interest-beer')).toBeVisible({ timeout: 20_000 });
  await page.getByTestId('onboarding-skip').click();

  for (const [query, stars] of BEERS) {
    await page.goto('/search');
    await page.getByTestId('search-input').fill(query);
    const results = page.getByTestId('search-result');
    await expect(results.first()).toBeVisible({ timeout: 15_000 });
    await results.first().click();
    await expect(page.getByTestId('drink-name')).toBeVisible({ timeout: 15_000 });
    await page.getByRole('button', { name: stars, exact: true }).click();
    await page.getByTestId('rating-save').click();
    await expect(page.getByTestId('rating-saved')).toBeVisible({ timeout: 15_000 });
  }
  return page;
}

test('two like-palated accounts match; display flag flips; hide-me removes both ways', async ({ browser }, testInfo) => {
  test.setTimeout(240_000);
  const uniq = Date.now();
  const handleA = `matchA_${uniq}`;
  const handleB = `matchB_${uniq}`;

  const ctxA = await browser.newContext();
  const ctxB = await browser.newContext();

  try {
    const pageA = await signUpAndRate(ctxA, handleA);
    const pageB = await signUpAndRate(ctxB, handleB);

    // Build the match graph on demand (the nightly job hasn't run in a test).
    const rebuild = await pageA.request.post('/api/admin/matches/rebuild');
    expect(rebuild.ok()).toBeTruthy();

    // --- A sees B as a match, with a % (eager is the default display mode) ---
    await pageA.goto('/matches');
    const rowB = pageA.getByTestId('match-row').filter({ hasText: handleB });
    await expect(rowB).toBeVisible({ timeout: 20_000 });
    await expect(rowB.getByTestId('match-pct')).toBeVisible();
    await shot(pageA, testInfo, '01-matches-eager');

    // --- Flag flip: eager → conservative hides the % for a low-confidence match ---
    // (4 co-rated drinks is below the med-confidence threshold.)
    const toConservative = await pageA.request.put('/api/admin/settings/match.display_mode', {
      data: { value: 'conservative' },
    });
    expect(toConservative.ok()).toBeTruthy();

    await pageA.goto('/matches');
    const rowBConservative = pageA.getByTestId('match-row').filter({ hasText: handleB });
    await expect(rowBConservative).toBeVisible({ timeout: 20_000 });
    await expect(rowBConservative.getByTestId('match-pct')).toHaveCount(0);
    await shot(pageA, testInfo, '02-matches-conservative-no-pct');

    // Flip back — no deploy, just a settings write.
    await pageA.request.put('/api/admin/settings/match.display_mode', { data: { value: 'eager' } });

    // --- Hide-me: B opts out; the removal is immediate in BOTH directions ---
    await pageB.goto('/profile');
    await expect(pageB.getByTestId('hide-me-toggle')).toBeVisible({ timeout: 15_000 });
    await pageB.getByTestId('hide-me-toggle').check();
    await shot(pageB, testInfo, '03-hide-me-on');

    // Direction 1: A no longer sees B.
    await pageA.goto('/matches');
    await expect(pageA.getByTestId('match-row').filter({ hasText: handleB })).toHaveCount(0, { timeout: 20_000 });

    // Direction 2: B sees the hidden state, no matches.
    await pageB.goto('/matches');
    await expect(pageB.getByTestId('matches-hidden')).toBeVisible({ timeout: 20_000 });
    await shot(pageB, testInfo, '04-hidden-both-directions');
  } finally {
    await ctxA.close();
    await ctxB.close();
  }
});
