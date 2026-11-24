/** Server-side configuration. Read at request time, so one image works in every environment. */

export function apiBaseUrl(): string {
  return (process.env.API_BASE_URL ?? "http://localhost:8080").replace(/\/+$/, "");
}

/**
 * Auth cookies are `Secure` unless explicitly turned off, which is only for plain-HTTP hosts other than localhost.
 * When the public origin is HTTPS they are always `Secure`: a forgotten flag can't send sessions in the clear.
 */
export function cookiesSecure(): boolean {
  if (process.env.PUBLIC_ORIGIN?.toLowerCase().startsWith("https://")) return true;
  return process.env.AUTH_COOKIE_SECURE !== "false";
}

/**
 * The site's public origin: PUBLIC_ORIGIN when it is set (a deployment behind a reverse proxy, whose OAuth issuer it is),
 * otherwise the scheme and host this request came in on. Used to show addresses and to build redirect URIs (Google
 * checks those against its own list), never to decide who someone is.
 */
export function siteOrigin(headers: Headers, configured = process.env.PUBLIC_ORIGIN): string {
  const fromConfig = configured?.trim();
  if (fromConfig) return fromConfig.replace(/\/+$/, "");
  const first = (name: string) => headers.get(name)?.split(",")[0]?.trim() || undefined;
  const host = first("x-forwarded-host") ?? first("host") ?? "localhost:3000";
  const proto = first("x-forwarded-proto") === "https" ? "https" : "http";
  return `${proto}://${host}`;
}
