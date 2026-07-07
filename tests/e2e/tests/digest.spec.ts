import { test, expect } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';

/**
 * Sprint 4 acceptance: "digest renders correctly (litmus screenshot)". The .NET
 * DigestRendererTests writes the rendered email to TestResults/digest-litmus.html
 * (runs before e2e in CI). Here we load it and screenshot it so the founder's
 * Gate C2 review has a picture of the actual email, and assert the CAN-SPAM
 * footer is present in what ships.
 */
test('weekly digest litmus renders with a CAN-SPAM footer', async ({ page }, testInfo) => {
  const litmus = path.resolve(__dirname, '../../../TestResults/digest-litmus.html');
  test.skip(!fs.existsSync(litmus), 'digest-litmus.html not produced (run dotnet test first)');

  const html = fs.readFileSync(litmus, 'utf-8');
  await page.setContent(html, { waitUntil: 'load' });

  await expect(page.getByText('Top picks')).toBeVisible();
  await expect(page.getByText(/palate match/i)).toBeVisible();
  // CAN-SPAM footer: physical address + unsubscribe.
  await expect(page.getByRole('link', { name: /unsubscribe/i })).toBeVisible();
  await expect(page.getByText(/Registered Agent Way/i)).toBeVisible();

  await testInfo.attach('weekly-digest', {
    body: await page.screenshot({ fullPage: true }),
    contentType: 'image/png',
  });
});
