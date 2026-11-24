/**
 * The visitor's IP, to pass on to the API as `X-Forwarded-For`. Every API call comes from the web
 * container, so without this the API's per-IP rate limits (for example on sign-in) would count all
 * visitors together.
 *
 * The app is reached only through the edge proxy (`deploy/Caddyfile`), which replaces whatever the
 * client sent with exactly one address it determined itself. The rightmost entry is therefore the
 * edge's answer even if another hop is ever added in between.
 */
export function clientIp(headers: Headers): string | undefined {
  const forwarded = headers.get("x-forwarded-for")?.split(",").at(-1)?.trim();
  return forwarded || headers.get("x-real-ip")?.trim() || undefined;
}

export function forwardedForHeader(headers: Headers): Record<string, string> {
  const ip = clientIp(headers);
  return ip ? { "x-forwarded-for": ip } : {};
}
