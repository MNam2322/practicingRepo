import { expect, test, type Page } from '@playwright/test';

/** A venue-local day far enough ahead to be free, different per test so they do not collide. */
async function dayAhead(page: Page, days: number): Promise<string> {
  return page.evaluate((n) => new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Ho_Chi_Minh' }).format(new Date(Date.now() + n * 864e5)), days);
}

test('book a time, see it in My bookings, then cancel it', async ({ page }) => {
  await page.goto('/');
  const day = await dayAhead(page, 3 + Math.floor(Math.random() * 20));
  await page.goto(`/#/r/court-1/${day}`);
  await page.locator('button.slot.free').first().click();
  const time = (await page.locator('.panel form p strong').first().innerText()).split('–')[0];
  await page.getByLabel('Name for the booking').fill('E2E booking');
  await page.getByRole('button', { name: /^Continue/ }).click();

  await expect(page).toHaveURL(/#\/b\//);
  await page.getByRole('button', { name: /^(Pay .* and confirm|Confirm booking)$/ }).click();
  await expect(page.locator('.notice.ok')).toContainText('Booked.');

  await page.getByRole('link', { name: 'My bookings' }).first().click();
  const row = page.locator('.booking', { hasText: 'E2E booking' }).filter({ hasText: time }).first();
  await expect(row).toContainText('Confirmed');

  await row.getByRole('button', { name: 'Cancel' }).click();
  await row.getByRole('button', { name: 'Yes, cancel' }).click();
  await expect(page.locator('.toast', { hasText: 'Cancelled' })).toBeVisible();
  await expect(page.locator('.booking.cancelled', { hasText: 'E2E booking' }).filter({ hasText: time }).first()).toContainText('Cancelled');
});

test('booking works with the keyboard alone', async ({ page }) => {
  await page.goto('/');
  const day = await dayAhead(page, 3 + Math.floor(Math.random() * 20));
  await page.goto(`/#/r/court-2/${day}`);
  await page.locator('button.slot.free').first().waitFor();

  // Tab to the first free slot, choose it with Space.
  for (let i = 0; i < 80 && !(await page.evaluate(() => document.activeElement?.matches('button.slot.free'))); i++) await page.keyboard.press('Tab');
  await expect(page.locator('button.slot.free:focus')).toHaveCount(1);
  await page.keyboard.press('Space');

  // Tab to the name field and type; then on to the submit button and press Enter.
  for (let i = 0; i < 80 && !(await page.evaluate(() => document.activeElement?.getAttribute('autocomplete') === 'name')); i++) await page.keyboard.press('Tab');
  await page.keyboard.type('Keyboard only');
  for (let i = 0; i < 20 && !(await page.evaluate(() => document.activeElement?.tagName === 'BUTTON' && /^Continue/.test(document.activeElement.textContent ?? ''))); i++) await page.keyboard.press('Tab');
  await page.keyboard.press('Enter');

  await expect(page).toHaveURL(/#\/b\//);
  // A new page puts focus at the start of its content, so the next Tab starts there.
  await expect(page.locator('main')).toBeFocused();
  await page.getByRole('button', { name: /and confirm|Confirm booking/ }).waitFor();
  for (let i = 0; i < 40 && !(await page.evaluate(() => document.activeElement?.tagName === 'BUTTON' && /and confirm|Confirm booking/.test(document.activeElement.textContent ?? ''))); i++) await page.keyboard.press('Tab');
  await page.keyboard.press('Enter');
  await expect(page.locator('.notice.ok')).toContainText('Booked.');
});
