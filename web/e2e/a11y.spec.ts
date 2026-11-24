import { test } from "@playwright/test";

import { expectAccessible } from "./axe";

/**
 * Automated accessibility checks (axe-core, WCAG 2.1 A and AA rules) on the public pages, in light and dark mode.
 * Serious and critical findings fail the test; the rest are printed. Automated checks find roughly a third of real
 * problems, so this complements keyboard and screen-reader testing, it doesn't replace it.
 */
const PAGES = ["/", "/tours", "/login", "/register", "/help", "/privacy"];

for (const scheme of ["light", "dark"] as const) {
  test.describe(`${scheme} mode`, () => {
    test.use({ colorScheme: scheme });

    for (const path of PAGES) {
      test(`no serious accessibility problems on ${path}`, async ({ page }) => {
        await page.goto(path);
        await page.waitForLoadState("networkidle");
        await expectAccessible(page, `${scheme} ${path}`);
      });
    }

    test("no serious accessibility problems on a tour page", async ({ page }) => {
      await page.goto("/tours");
      await page.waitForLoadState("networkidle");
      await page.locator("a[href^='/tours/']").first().click();
      await page.waitForURL(/\/tours\/[0-9a-f-]{36}/);
      await page.waitForLoadState("networkidle");
      await expectAccessible(page, `${scheme} tour page`);
    });
  });
}
