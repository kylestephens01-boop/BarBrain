import { test, expect } from '@playwright/test';

// Sprint 7: legal placeholders + the required footer statement, with
// screenshot artifacts for the Gate E walk.
test.describe('footer + legal placeholders', () => {
  test('footer carries the 21+ / responsibility line and legal links', async ({ page }, testInfo) => {
    await page.goto('/');
    const footer = page.getByTestId('site-footer');
    await expect(footer).toContainText('21 and over');
    await expect(footer).toContainText('Drink responsibly.');
    await expect(footer.getByRole('link', { name: 'Terms' })).toBeVisible();
    await expect(footer.getByRole('link', { name: 'Privacy' })).toBeVisible();
    await testInfo.attach('footer', {
      body: await page.screenshot({ fullPage: true }),
      contentType: 'image/png',
    });
  });

  for (const [path, heading] of [
    ['/legal/terms', 'Terms of Service'],
    ['/legal/privacy', 'Privacy Policy'],
    ['/legal/contact', 'Contact & reporting'],
  ] as const) {
    test(`${path} renders`, async ({ page }, testInfo) => {
      await page.goto(path);
      await expect(page.getByRole('heading', { name: heading })).toBeVisible();
      if (path !== '/legal/contact') {
        await expect(page.getByTestId('legal-draft-banner')).toBeVisible();
      }
      await testInfo.attach(path.replaceAll('/', '_'), {
        body: await page.screenshot({ fullPage: true }),
        contentType: 'image/png',
      });
    });
  }
});
