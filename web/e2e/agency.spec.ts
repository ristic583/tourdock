import { expect, test } from "@playwright/test";

import { expectAccessible } from "./axe";
import { confirmEmail, signIn } from "./mailpit";

/**
 * The agency back office end to end: a new manager registers an agency, an admin approves it, and the manager sets up
 * a vehicle and a tour (photos, schedule, policy, publish), then blocks and unblocks a departure.
 * Needs an admin account: E2E_ADMIN_EMAIL / E2E_ADMIN_PASSWORD (the API seeds it from ADMIN_EMAIL / ADMIN_PASSWORD).
 */
const adminEmail = process.env.E2E_ADMIN_EMAIL;
const adminPassword = process.env.E2E_ADMIN_PASSWORD;

test.skip(!adminEmail || !adminPassword, "Set E2E_ADMIN_EMAIL and E2E_ADMIN_PASSWORD to run the agency flow.");

test("register an agency, get approved, set up and publish a tour", async ({ page, browser }) => {
  test.setTimeout(180_000);

  // Manager: sign up and register an agency.
  await page.goto("/register?next=/agency");
  await page.waitForLoadState("networkidle");
  await page.getByLabel("Your name").fill("E2E Manager");
  const managerEmail = `e2e-manager-${Date.now()}@example.com`;
  await page.getByLabel("Email").fill(managerEmail);
  const managerPassword = `e2e-${crypto.randomUUID()}`;
  await page.getByLabel("Password").fill(managerPassword);
  await page.getByRole("button", { name: "Create account" }).click();
  await expect(page.getByRole("heading", { name: "Check your inbox" })).toBeVisible();
  await confirmEmail(page, managerEmail);
  await signIn(page, managerEmail, managerPassword, "/agency");
  await page.waitForLoadState("networkidle");
  const agencyName = `E2E Harbour Tours ${Date.now() % 100000}`;
  await page.getByLabel("Agency name").fill(agencyName);
  await page.getByLabel("Contact email").fill("contact@example.com");
  await page.getByRole("button", { name: "Register agency" }).click();
  await page.waitForURL(/\/agency\/[0-9a-f-]+$/);
  const agencyPath = new URL(page.url()).pathname;
  await expect(page.getByText(/waiting for approval/)).toBeVisible();

  // Admin: approve it.
  const adminContext = await browser.newContext();
  const admin = await adminContext.newPage();
  await admin.goto("/login?next=/admin/agencies");
  await admin.waitForLoadState("networkidle");
  await admin.getByLabel("Email").fill(adminEmail!);
  await admin.getByLabel("Password").fill(adminPassword!);
  await admin.getByRole("button", { name: /sign in/i }).click();
  await admin.waitForURL("**/admin/agencies**");
  const row = admin.getByRole("row").filter({ hasText: agencyName });
  await row.getByRole("button", { name: "Approve" }).click();
  await admin.getByRole("alertdialog", { name: `Approve ${agencyName}?` }).getByRole("button", { name: "Approve agency" }).click();
  await expect(row).toBeHidden();
  await expect(admin.getByRole("status").filter({ hasText: `${agencyName} is approved.` })).toBeVisible();
  await adminContext.close();

  // Manager: a vehicle.
  await page.goto(`${agencyPath}/vehicles`);
  await expect(page.getByText(/waiting for approval/)).toBeHidden();
  await page.getByRole("button", { name: "+ Add vehicle" }).click();
  const vehicleDialog = page.getByRole("dialog");
  await vehicleDialog.getByLabel("Name").fill("Sea Swift");
  await vehicleDialog.getByLabel("Seats").fill("10");
  await vehicleDialog.getByRole("button", { name: "Add vehicle" }).click();
  await expect(page.getByText("Boat · 10 seats")).toBeVisible();

  // A tour.
  await page.goto(`${agencyPath}/tours/new`);
  await page.waitForLoadState("networkidle");
  await page.getByLabel("Title").fill("Evening Harbour Cruise");
  await page.getByLabel("Description").fill("A calm cruise around the harbour at sunset, with a swim stop.");
  // Picking the city fills the country and time zone and drops the pin on the map.
  await page.getByLabel("City").fill("Spli");
  await page.getByRole("option", { name: /^Split, HR/ }).click();
  await expect(page.getByText(/^43\.50810, 16\.44020/)).toBeVisible();
  await page.getByLabel("Meeting point", { exact: true }).fill("Pier 4, Riva");
  await page.getByLabel("Booking", { exact: true }).selectOption("Both");
  await page.getByLabel("Price per person (€)").fill("45");
  await page.getByLabel("Private trip price (€)").fill("380");
  await page.getByRole("button", { name: "Create draft" }).click();
  await page.waitForURL(/\/tours\/[0-9a-f-]{36}$/);
  await page.waitForLoadState("networkidle");
  await expectAccessible(page, "tour editor");

  // Photos: upload two of the bundled demo photos, make the second the cover.
  const files = [];
  for (const name of ["sunset-fishing.jpg", "dubrovnik-boat.jpg"]) {
    files.push({ name, mimeType: "image/jpeg", buffer: await (await page.request.get(`/images/demo-tours/${name}`)).body() });
  }
  await page.locator("input[type=file]").setInputFiles(files);
  await expect(page.getByRole("img", { name: "Photo 2" })).toBeVisible({ timeout: 30_000 });
  await page.getByRole("button", { name: "Make photo 2 the cover" }).click();
  await expect(page.getByRole("img", { name: "Photo 2 (cover)" })).toBeVisible();

  // Schedule, then publish.
  await page.getByRole("button", { name: "Save schedule" }).click();
  await expect(page.getByText(/Schedule saved: \d+ departures created/)).toBeVisible({ timeout: 20_000 });
  await page.getByRole("button", { name: "Publish" }).click();
  await expect(page.getByRole("button", { name: "Unpublish" })).toBeVisible();

  // Calendar: block and unblock a departure.
  await page.goto(`${agencyPath}/calendar`);
  await page.waitForLoadState("networkidle");
  await expectAccessible(page, "agency calendar");
  let departure = page.getByRole("button", { name: /Evening Harbour Cruise/ }).first();
  if (!(await departure.isVisible())) {
    await page.getByRole("link", { name: "Next week" }).click();
    departure = page.getByRole("button", { name: /Evening Harbour Cruise/ }).first();
  }
  await departure.click();
  await page.getByRole("dialog").getByRole("button", { name: "Block" }).click();
  await page.getByRole("alertdialog", { name: "Block this departure?" }).getByRole("button", { name: "Block departure" }).click();
  const blocked = page.getByRole("button", { name: /Evening Harbour Cruise, Blocked/ }).first();
  await expect(blocked).toBeVisible();
  await blocked.click();
  await page.getByRole("dialog").getByRole("button", { name: "Unblock" }).click();
  await expect(page.getByRole("button", { name: /Evening Harbour Cruise, Blocked/ })).toHaveCount(0);

  // Overview: the KPIs come from the stats endpoint and count the new departures.
  await page.goto(agencyPath);
  const kpis = page.getByRole("region", { name: "Key numbers" });
  await expect(kpis).toContainText(/0 of [1-9]\d* seats sold, next 30 days/);
  await expect(kpis).toContainText(/Revenue this month\s*€0paid, net of refunds/);
});
