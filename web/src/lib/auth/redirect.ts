/**
 * Validates a post-login `next` target so it can only point back into this app
 * (blocks open redirects such as `//evil.example.com` or `https://...`).
 */
export function safeNextPath(raw: string | null | undefined, fallback = "/"): string {
  if (!raw) return fallback;
  if (!raw.startsWith("/") || raw.startsWith("//") || raw.startsWith("/\\")) return fallback;
  if (/[\u0000-\u001f]/.test(raw)) return fallback;
  return raw;
}

export function loginPath(next?: string): string {
  const target = safeNextPath(next, "");
  return target && target !== "/" ? `/login?next=${encodeURIComponent(target)}` : "/login";
}
