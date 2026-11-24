/** Pages that need a signed-in user. The proxy redirects anonymous visitors to the login page. */
export const PROTECTED_PREFIXES = ["/account", "/bookings", "/agency", "/admin", "/oauth"] as const;

export function isProtectedPath(pathname: string): boolean {
  return PROTECTED_PREFIXES.some((prefix) => pathname === prefix || pathname.startsWith(`${prefix}/`));
}
