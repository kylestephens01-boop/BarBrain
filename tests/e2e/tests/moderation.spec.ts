import { test, expect, Page, TestInfo } from '@playwright/test';

/**
 * Sprint 6 acceptance: report → appears in the unified admin queue → action
 * (hide) reflected publicly. Two personas (author + reporter) via separate
 * browser contexts; admin rides the token stub (blank in CI compose).
 */

const ADULT_DOB = '1990-04-17';

async function shot(page: Page, testInfo: TestInfo, name: string) {
  const screenshot = await page.screenshot({ fullPage: true });
  await testInfo.attach(name, { body: screenshot, contentType: 'image/png' });
}

async function signup(page: Page, handle: string) {
  await page.goto('/signup');
  await page.fill('#handle', handle);
  await page.fill('#email', `${handle}@example.test`);
  await page.fill('#password', 'correct-horse-battery');
  await page.fill('#dob', ADULT_DOB);
  await page.getByTestId('signup-submit').click();
  await expect(page.getByTestId('interest-beer')).toBeVisible({ timeout: 20_000 });
  await page.getByTestId('onboarding-skip').click();
  await expect(page.getByTestId('search-input')).toBeVisible({ timeout: 20_000 });
}

async function openDrink(page: Page, term: string) {
  await page.goto('/search');
  await page.getByTestId('search-input').fill(term);
  const results = page.getByTestId('search-result');
  await expect(results.first()).toBeVisible({ timeout: 15_000 });
  await results.first().click();
  await expect(page.getByTestId('drink-name')).toBeVisible({ timeout: 15_000 });
}

test('report → unified admin queue → hide reflected publicly', async ({ browser }, testInfo) => {
  test.setTimeout(240_000);
  const uniq = Date.now();

  // Persona 1: the author rates a drink publicly with a note.
  const authorContext = await browser.newContext();
  const author = await authorContext.newPage();
  await signup(author, `author_${uniq}`);
  await openDrink(author, 'sculpin');
  await author.getByRole('button', { name: '1 stars', exact: true }).click();
  await author.getByTestId('rating-note').fill('e2e content to be reported');
  await author.getByTestId('rating-save').click();
  await expect(author.getByTestId('rating-saved')).toBeVisible({ timeout: 15_000 });
  await authorContext.close();

  // Persona 2: the reporter sees the public rating and files a report.
  const reporterContext = await browser.newContext();
  const reporter = await reporterContext.newPage();
  await signup(reporter, `reporter_${uniq}`);
  await openDrink(reporter, 'sculpin');
  const reportedRating = reporter
    .getByTestId('public-rating')
    .filter({ hasText: `author_${uniq}` });
  await expect(reportedRating).toBeVisible({ timeout: 15_000 });

  await reportedRating.getByTestId('report-open').click();
  await reportedRating.getByTestId('report-reason').selectOption('offensive');
  await reportedRating.getByTestId('report-note').fill('e2e report');
  await shot(reporter, testInfo, '01-report-form');
  await reportedRating.getByTestId('report-submit').click();
  await expect(reportedRating.getByTestId('report-done')).toBeVisible({ timeout: 15_000 });

  // Admin: the report is in the unified queue's Reports tab.
  await reporter.goto('/admin');
  await reporter.getByTestId('admin-tab-reports').click();
  const row = reporter.getByTestId('report-row').filter({ hasText: `reporter_${uniq}` });
  await expect(row).toBeVisible({ timeout: 15_000 });
  await expect(row).toContainText('offensive');
  await shot(reporter, testInfo, '02-admin-reports-queue');

  // Action it: hide the content.
  await row.getByTestId('report-hide-btn').click();
  await expect(row).toHaveCount(0, { timeout: 15_000 });

  // Reflected publicly: the rating is gone from the drink page.
  await openDrink(reporter, 'sculpin');
  await expect(
    reporter.getByTestId('public-rating').filter({ hasText: `author_${uniq}` }),
  ).toHaveCount(0, { timeout: 15_000 });
  await shot(reporter, testInfo, '03-hidden-publicly');

  // The audit log recorded the decision.
  await reporter.goto('/admin');
  await reporter.getByTestId('admin-tab-audit').click();
  await expect(reporter.getByTestId('audit-row').first()).toBeVisible({ timeout: 15_000 });
  await expect(reporter.getByTestId('audit-table')).toContainText('report actioned');
  await shot(reporter, testInfo, '04-audit-log');

  await reporterContext.close();
});
