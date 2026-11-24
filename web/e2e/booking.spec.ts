import { expect, test, type Page } from "@playwright/test";

import { expectAccessible } from "./axe";
import { confirmEmail, signIn } from "./mailpit";

/**
 * The main traveler journey on a fresh compose stack with demo data:
 * sign up → confirm the email from Mailpit → search → open a tour → hold seats → decline in the sandbox (test card) → back, retry → pay with a saved card → back, confirmed → cancel.
 * Uses a throwaway account on example.com; nothing here is a real credential.
 */

async function signUp(page: Page) {
  const email = `e2e-${Date.now()}-${Math.floor(Math.random() * 1e6)}@example.com`;
  const password = `e2e-${crypto.randomUUID()}`;
  await page.goto("/register?next=/tours");
  await page.waitForLoadState("networkidle");
  await page.getByLabel("Your name").fill("E2E Traveler");
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Create account" }).click();
  // Email-first: no session until the link is opened.
  await expect(page.getByRole("heading", { name: "Check your inbox" })).toBeVisible();
  await confirmEmail(page, email);
  await signIn(page, email, password, "/tours");
  await expect(page.getByText("Confirm your email to book tours.")).toBeHidden();
  return email;
}

test("search, book, decline then pay in the sandbox, confirmed", async ({ page }) => {
  const email = await signUp(page);

  // Search: only shared departures, so a seat booking is possible.
  await page.getByRole("complementary", { name: "Filters" }).getByText("Shared", { exact: true }).click();
  await page.waitForURL(/mode=Shared/);
  const results = page.locator("section[aria-labelledby=results-heading]");
  await expect(results.getByRole("link").first()).toBeVisible();
  await results.getByRole("link").first().click();
  await page.waitForURL(/\/tours\/[0-9a-f-]+/);

  // Tour detail: a departure is picked for one guest; hold the seat.
  const panel = page.getByRole("complementary", { name: "Book this tour" });
  await expect(panel.getByRole("button", { pressed: true }).first()).toBeVisible();
  await expect(panel.getByLabel("Guests", { exact: true })).toHaveValue("1");
  await panel.getByRole("button", { name: /Reserve/ }).click();

  // Checkout: countdown, accept the policy, go to the sandbox in the same tab.
  await page.waitForURL(/\/bookings\/[0-9a-f-]+\/checkout/);
  const checkoutPath = new URL(page.url()).pathname;
  await expect(page.getByRole("timer")).toContainText(/held for \d+:\d{2}/);
  await expectAccessible(page, "checkout");
  await page.getByLabel(/I accept the agency's cancellation policy/).check();
  await page.getByRole("button", { name: /^Pay .* and confirm$/ }).click();

  // Decline first with the sandbox's always-declining test card: the sandbox sends the traveler back, and the page
  // shows why once the webhook has landed.
  await page.waitForURL(/\/checkout\/[^/]+$/);
  await page.getByRole("radio", { name: /0002/ }).check();
  await page.getByRole("button", { name: /^Pay \d/ }).click();
  // The bank step: no code to type, a spinner, then the outcome, then back to TourDock on its own.
  await expect(page.getByRole("heading", { name: "Confirming with your bank…" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Declined by your bank" })).toBeVisible();
  await page.waitForURL((url) => url.pathname === checkoutPath && url.searchParams.get("payment") === "failed");
  await expect(page.getByRole("alert").filter({ hasText: "Payment declined" })).toContainText("Card declined (sandbox)");
  await expect(page.getByRole("timer")).toContainText(/held for/);

  // Try again and pay: back on the checkout page, the booking is confirmed.
  await page.getByRole("button", { name: /^Try again: pay / }).click();
  await page.waitForURL(/\/checkout\/[^/]+$/);
  await page.getByRole("radio", { name: /4242/ }).check();
  await page.getByRole("button", { name: /^Pay \d/ }).click();
  await expect(page.getByRole("heading", { name: "Payment approved" })).toBeVisible();
  await page.waitForURL((url) => url.pathname === checkoutPath && url.searchParams.get("payment") === "succeeded");
  await expect(page.getByRole("heading", { level: 1, name: /You're going on/ })).toBeVisible({ timeout: 30_000 });
  await expect(page.getByText(email)).toBeVisible();

  // And it's listed under upcoming bookings.
  await page.getByRole("link", { name: "View my bookings" }).click();
  const confirmed = page.getByRole("listitem").filter({ hasText: "Confirmed" }).first();
  await expect(confirmed).toBeVisible();
  await expectAccessible(page, "my bookings");

  // Cancel it: the dialog previews the refund, then the booking moves to the cancelled tab.
  await confirmed.getByRole("button", { name: "Change or cancel" }).click();
  const dialog = page.getByRole("dialog");
  await expect(dialog.getByRole("region", { name: "Refund preview" })).toContainText("You get back");
  await dialog.getByRole("button", { name: "Cancel whole booking" }).click();
  await expect(page.getByRole("status").filter({ hasText: /cancelled/ })).toBeVisible();
  await page.getByRole("link", { name: /Cancelled & expired/ }).click();
  await expect(page.getByRole("listitem").filter({ hasText: "You cancelled" }).first()).toBeVisible();
});
