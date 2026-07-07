import { test, expect, Page, TestInfo } from '@playwright/test';

/**
 * Sprint 3 acceptance: a NEW user goes interest gate → one quiz → a feed that
 * renders three sections WITH REASONS, immediately (content-based needs no
 * neighbors). Screenshots at every step for the Gate C1 phone review.
 */

async function shot(page: Page, testInfo: TestInfo, name: string) {
  const screenshot = await page.screenshot({ fullPage: true });
  await testInfo.attach(name, { body: screenshot, contentType: 'image/png' });
}

test('interest gate → staples quiz → radar → sectioned feed with reasons', async ({ page }, testInfo) => {
  test.setTimeout(180_000);
  const uniq = Date.now();

  // Fresh account lands on the interest gate.
  await page.goto('/signup');
  await page.fill('#handle', `quiz_${uniq}`);
  await page.fill('#email', `quiz_${uniq}@example.test`);
  await page.fill('#password', 'correct-horse-battery');
  await page.fill('#dob', '1990-04-17');
  await page.getByTestId('signup-submit').click();

  await expect(page.getByTestId('interest-beer')).toBeVisible({ timeout: 20_000 });
  await shot(page, testInfo, '01-interest-gate');

  // Claim beer only — the quiz must only ask about claimed categories.
  await page.getByTestId('interest-beer').click();
  await page.getByTestId('interest-continue').click();

  // The staples quiz (config-driven; CI seeds the corridor catalog).
  const quizItems = page.getByTestId('quiz-item');
  await expect(quizItems.first()).toBeVisible({ timeout: 20_000 });
  await shot(page, testInfo, '02-quiz');

  // Rate three staples with contrast, skip one — skips write nothing.
  await quizItems.nth(0).getByRole('button', { name: '5 stars', exact: true }).click();
  await expect(page.getByTestId('quiz-counter')).toContainText('1 rated', { timeout: 15_000 });
  await quizItems.nth(1).getByRole('button', { name: '4.5 stars', exact: true }).click();
  await expect(page.getByTestId('quiz-counter')).toContainText('2 rated');
  await quizItems.nth(2).getByRole('button', { name: '1.5 stars', exact: true }).click();
  await expect(page.getByTestId('quiz-counter')).toContainText('3 rated');
  await quizItems.nth(3).getByTestId('quiz-skip').click();
  await shot(page, testInfo, '03-quiz-rated');

  // One claimed category → the button finishes the quiz.
  await page.getByTestId('quiz-next').click();

  // Radar tease: the palate took shape from quiz ratings alone.
  await expect(page.getByTestId('onboarding-radar')).toBeVisible({ timeout: 20_000 });
  await shot(page, testInfo, '04-radar');
  await page.getByTestId('explore-feed').click();

  // The sectioned feed renders immediately with reasons on every card.
  await expect(page.getByTestId('feed-subtitle')).toBeVisible({ timeout: 20_000 });
  await expect(page.getByTestId('feed-subtitle')).toContainText('3 ratings');

  for (const section of ['up_your_alley', 'stretch_a_little', 'wildcard'] as const) {
    await page.getByTestId(`feed-tab-${section}`).click();
    const recs = page.getByTestId('feed-rec');
    await expect(recs.first(), `section ${section} must have items`).toBeVisible({ timeout: 15_000 });
    // EVERY rec shows its because (hard product requirement).
    const reasons = page.getByTestId('rec-reason');
    const count = await reasons.count();
    expect(count).toBeGreaterThan(0);
    for (let i = 0; i < count; i++) {
      await expect(reasons.nth(i)).not.toBeEmpty();
    }
    await shot(page, testInfo, `05-feed-${section}`);
  }

  // The matches slot exists but is deferred (Sprint 4).
  await expect(page.getByTestId('feed-tab-loved_by_your_matches')).toBeDisabled();

  // Quiz ratings are REAL ratings: they're in the journal with provenance.
  await page.goto('/profile');
  await expect(page.getByTestId('journal-item').first()).toBeVisible({ timeout: 15_000 });
  const journalCount = await page.getByTestId('journal-item').count();
  expect(journalCount).toBe(3); // three rated, one skipped → three rows
  await shot(page, testInfo, '06-journal-quiz-ratings');

  // And the palate tab renders the radar.
  await page.getByTestId('palate-tab').click();
  await expect(page.getByTestId('palate-beer')).toBeVisible({ timeout: 15_000 });
  await shot(page, testInfo, '07-palate-tab');
});
