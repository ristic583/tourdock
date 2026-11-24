import AxeBuilder from "@axe-core/playwright";
import { expect, type Page } from "@playwright/test";

/**
 * Runs axe-core (WCAG 2.1 A and AA rules) on the current page. Serious and critical findings fail; everything found is
 * printed. Automated checks catch only part of real problems: they complement keyboard and screen-reader testing.
 */
export async function expectAccessible(page: Page, label: string) {
  // Dynamic pages stream their <title> with the metadata; wait for it like a screen reader user would get it.
  await page.waitForFunction(() => document.title.length > 0);
  const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).analyze();
  for (const v of results.violations) {
    console.log(`[a11y] ${label}: ${v.impact} ${v.id} (${v.nodes.length}) ${v.help}\n${v.nodes.slice(0, 3).map((n) => `    ${n.target.join(" ")}`).join("\n")}`);
  }
  const blocking = results.violations.filter((v) => v.impact === "serious" || v.impact === "critical");
  expect(blocking.map((v) => `${v.id}: ${v.help} (${v.nodes.map((n) => n.target.join(" ")).slice(0, 3).join(", ")})`), `accessibility on ${label}`).toEqual([]);
}
