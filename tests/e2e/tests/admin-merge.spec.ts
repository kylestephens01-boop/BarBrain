import { test, expect } from '@playwright/test';

/**
 * Sprint 1 acceptance: seeded near-duplicate fixtures produce merge
 * candidates; approve works from the admin queue stub; screenshot artifact.
 * CI seeds the stack first: `import bundled` then `import demo-dupes`.
 */
test('merge queue lists seeded duplicate candidates and approve clears one', async ({ page }, testInfo) => {
  await page.goto('/admin/merge-queue');

  await expect(page.getByRole('heading', { name: 'Merge queue' })).toBeVisible();

  // Token blank: local/dev stub allows admin calls (ADMIN_TOKEN unset in CI compose).
  await page.getByTestId('load-btn').click();

  const rows = page.getByTestId('merge-row');
  await expect(rows.first()).toBeVisible({ timeout: 15_000 });
  const before = await rows.count();
  expect(before).toBeGreaterThanOrEqual(1);

  // The demo-dupes fixture plants a Toppling Goliath near-duplicate.
  await expect(page.getByTestId('merge-table')).toContainText(/goliath/i);

  const screenshot = await page.screenshot({ fullPage: true });
  await testInfo.attach('admin-merge-queue', { body: screenshot, contentType: 'image/png' });

  await page.getByTestId('approve-btn').first().click();
  await expect(rows).toHaveCount(before - 1, { timeout: 15_000 });
});
