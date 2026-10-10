import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';

// axe-core checks each main page against WCAG 2.2 A and AA (names, roles, labels, contrast …) in both themes.
const pages = [
  { name: 'resources', path: '/#/', ready: 'h1' },
  { name: 'a day of slots', path: '/#/r/court-1', ready: 'button.slot' },
  { name: 'the week', path: '/#/r/court-1/week', ready: 'h1' },
  { name: 'my bookings', path: '/#/mine', ready: 'h1' },
  { name: 'sign in', path: '/#/signin', ready: 'form' },
];

for (const scheme of ['light', 'dark'] as const) {
  test.describe(`${scheme} theme`, () => {
    test.use({ colorScheme: scheme });
    for (const p of pages) {
      test(`${p.name} has no accessibility violations`, async ({ page }) => {
        await page.goto(p.path);
        await page.locator(p.ready).first().waitFor();
        await page.waitForLoadState('networkidle');
        const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa']).analyze();
        expect(results.violations.map((v) => `${v.id}: ${v.help} (${v.nodes.length}× e.g. ${v.nodes[0]?.target.join(' ')})`)).toEqual([]);
      });
    }
  });
}
