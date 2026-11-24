import { expect, type Page } from "@playwright/test";

/** Mailpit's web UI/API of the stack under test (the compose default; override with E2E_MAILPIT_URL). */
const MAILPIT = process.env.E2E_MAILPIT_URL ?? "http://localhost:8025";

type Summary = { ID: string; Subject: string };

/** Waits for the "Confirm your email" message to `email` (the outbox sends it within seconds) and returns its link. */
export async function confirmationLink(email: string): Promise<URL> {
  let link: string | undefined;
  await expect
    .poll(
      async () => {
        const search = await fetch(`${MAILPIT}/api/v1/search?query=${encodeURIComponent(`to:"${email}"`)}`);
        const { messages } = (await search.json()) as { messages: Summary[] };
        const message = messages.find((item) => item.Subject === "Confirm your email for TourDock");
        if (!message) return undefined;
        const full = (await (await fetch(`${MAILPIT}/api/v1/message/${message.ID}`)).json()) as { Text: string };
        link = full.Text.match(/https?:\/\/\S+\/verify-email\?\S+/)?.[0];
        return link;
      },
      { message: `confirmation email to ${email} in Mailpit (${MAILPIT})`, timeout: 60_000, intervals: [1_000] },
    )
    .toBeTruthy();
  return new URL(link!);
}

/** Signs in on the login page and waits to land on `next`. */
export async function signIn(page: Page, email: string, password: string, next: string) {
  await page.goto(`/login?next=${encodeURIComponent(next)}`);
  await page.waitForLoadState("networkidle");
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await page.waitForURL(`**${next}`);
}

/** Opens the emailed link in this browser (on the app under test) and waits for the confirmation. */
export async function confirmEmail(page: Page, email: string) {
  const link = await confirmationLink(email);
  await page.goto(`${link.pathname}${link.search}`);
  await expect(page.getByText("Your email is confirmed.")).toBeVisible();
}
