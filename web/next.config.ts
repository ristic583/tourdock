import type { NextConfig } from "next";

/**
 * Content-Security-Policy. Static (not nonce-based) so pages can stay statically rendered and prefetched: a nonce
 * forces every page to render per request. Next's inline bootstrap scripts therefore need 'unsafe-inline'; what the
 * policy still stops: scripts, plugins and frames from other origins, <base> hijacking, framing of our pages, form
 * posts and fetches to other origins. Photos come from the configurable storage origin, so images may load from any.
 */
export function contentSecurityPolicy(dev = process.env.NODE_ENV === "development", formAction = "'self'"): string {
  return [
    "default-src 'self'",
    `script-src 'self' 'unsafe-inline'${dev ? " 'unsafe-eval'" : ""}`,
    "style-src 'self' 'unsafe-inline'",
    "img-src 'self' data: blob: http: https:",
    "font-src 'self' data:",
    `connect-src 'self'${dev ? " ws:" : ""}`,
    "frame-src https://www.openstreetmap.org",
    "object-src 'none'",
    "base-uri 'self'",
    `form-action ${formAction}`,
    "frame-ancestors 'none'",
  ].join("; ");
}

/**
 * The OAuth consent page posts to our own /connect/authorize, which then redirects to the connecting app (an https
 * site, or a loopback address for an app on this computer). Browsers apply form-action to that redirect too.
 */
export const CONSENT_FORM_ACTION = "'self' https: http://localhost:* http://127.0.0.1:*";

const otherHeaders = [
  { key: "X-Content-Type-Options", value: "nosniff" },
  { key: "X-Frame-Options", value: "DENY" },
  { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
  { key: "Permissions-Policy", value: "camera=(), microphone=(), geolocation=()" },
];

const nextConfig: NextConfig = {
  // Self-contained server bundle for the Docker image (see Dockerfile).
  output: "standalone",
  poweredByHeader: false,
  cacheComponents: true,
  partialPrefetching: true,
  async headers() {
    return [
      { source: "/((?!oauth/consent).*)", headers: [{ key: "Content-Security-Policy", value: contentSecurityPolicy() }, ...otherHeaders] },
      {
        source: "/oauth/consent",
        headers: [{ key: "Content-Security-Policy", value: contentSecurityPolicy(undefined, CONSENT_FORM_ACTION) }, ...otherHeaders],
      },
    ];
  },
};

export default nextConfig;
