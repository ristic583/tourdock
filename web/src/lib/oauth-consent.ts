/** What each scope lets an app do, in the words the consent page uses (ADR 0028). Unknown scopes are never granted. */
const SCOPES: Record<string, string> = {
  bookings: "see your bookings, hold seats for you (you still pay on TourDock) and cancel bookings after you confirm",
  "agency:read": "see your agency's bookings, numbers and reviews",
  "agency:write": "reply to reviews of your agency's tours",
};

export function describeScopes(scope: string): string[] {
  return scope.split(" ").flatMap((s) => (SCOPES[s] ? [SCOPES[s]] : []));
}

/** Where the decision sends the visitor: a site's host, or "this computer" for a loopback address. */
export function redirectTarget(uri: string): { label: string; loopback: boolean } | null {
  try {
    const url = new URL(uri);
    const loopback = url.protocol === "http:" && ["localhost", "127.0.0.1", "[::1]"].includes(url.hostname);
    if (!loopback && url.protocol !== "https:") return null;
    return { label: loopback ? "an app on this computer" : url.host, loopback };
  } catch {
    return null;
  }
}
