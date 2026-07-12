import { test, expect, Page, TestInfo } from '@playwright/test';

/**
 * Sprint 7 / Gate E acceptance: privacy self-serve end to end — export
 * downloads valid JSON with the account's data, deletion schedules with the
 * grace window shown, and cancel undoes it. (Deletion EXECUTION and both
 * modes' DB effects are integration-tested; the grace period means execution
 * never happens inside an e2e run.)
 */

const ADULT_DOB = '1990-04-17';

async function shot(page: Page, testInfo: TestInfo, name: string) {
  await testInfo.attach(name, {
    body: await page.screenshot({ fullPage: true }),
    contentType: 'image/png',
  });
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

test('export downloads valid JSON holding my profile', async ({ page }, testInfo) => {
  const handle = `privacy_export_${Date.now()}`;
  await signup(page, handle);

  await page.goto('/profile');
  await expect(page.getByTestId('profile-data')).toBeVisible();
  await shot(page, testInfo, '01-your-data-card');

  const downloadPromise = page.waitForEvent('download');
  await page.getByTestId('export-data').click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toMatch(/^barbrain-export-.*\.json$/);

  const body = JSON.parse(
    (await (await import('node:fs/promises')).readFile(await download.path(), 'utf8')),
  );
  expect(body.profile.handle).toBe(handle);
  expect(Array.isArray(body.ratings)).toBe(true);
  expect(Array.isArray(body.badges)).toBe(true);
  expect(body.profile.birthYear).toBe(1990); // year only — never the full DOB
});

test('deletion schedules with a grace window, cancel undoes it', async ({ page }, testInfo) => {
  const handle = `privacy_delete_${Date.now()}`;
  await signup(page, handle);

  await page.goto('/profile');
  await page.getByTestId('delete-account').click();
  await expect(page.getByTestId('delete-form')).toBeVisible();
  await shot(page, testInfo, '02-delete-form');

  // Wrong password is refused, politely.
  await page.getByTestId('delete-password').fill('not-my-password');
  await page.getByTestId('confirm-deletion').click();
  await expect(page.getByTestId('delete-error')).toBeVisible();
  await shot(page, testInfo, '03-wrong-password');

  // Right password schedules it; the pending banner shows the date.
  await page.getByTestId('delete-password').fill('correct-horse-battery');
  await page.getByTestId('confirm-deletion').click();
  await expect(page.getByTestId('deletion-pending')).toBeVisible();
  await expect(page.getByTestId('deletion-pending')).toContainText('anonymized');
  await shot(page, testInfo, '04-deletion-pending');

  // Change of heart: cancel restores the quiet state, account intact.
  await page.getByTestId('cancel-deletion').click();
  await expect(page.getByTestId('delete-account')).toBeVisible();
  await shot(page, testInfo, '05-cancelled');

  await page.reload();
  await expect(page.getByTestId('profile-handle')).toContainText(handle);
});
