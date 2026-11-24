/**
 * Reads the `exp` claim of a JWT without verifying it. The BFF only uses this to decide *when* to refresh;
 * the API verifies every token it receives.
 */
export function jwtExpiresAt(token: string): number | null {
  const payload = token.split(".")[1];
  if (!payload) return null;
  try {
    const json = JSON.parse(base64UrlDecode(payload)) as { exp?: unknown };
    return typeof json.exp === "number" ? json.exp * 1000 : null;
  } catch {
    return null;
  }
}

/** True when the token is missing, unreadable, or expires within `skewMs`. */
export function isExpiring(token: string | undefined, now = Date.now(), skewMs = 60_000): boolean {
  if (!token) return true;
  const exp = jwtExpiresAt(token);
  return exp === null || exp - skewMs <= now;
}

function base64UrlDecode(input: string): string {
  const base64 = input.replace(/-/g, "+").replace(/_/g, "/");
  const padded = base64 + "=".repeat((4 - (base64.length % 4)) % 4);
  const bytes = Uint8Array.from(atob(padded), (c) => c.charCodeAt(0));
  return new TextDecoder().decode(bytes);
}
