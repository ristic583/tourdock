import "server-only";

import { notFound, redirect } from "next/navigation";
import { cache } from "react";

import type { CurrentUser, MyAgency } from "@/lib/api/contracts";
import { ApiError } from "@/lib/api/problem";
import { serverApi } from "@/lib/api/server";
import { unwrap } from "@/lib/api/client";

import { loginPath } from "./redirect";
import { readAccessToken } from "./session";

/**
 * Data access layer for the signed-in user. Reads the access token from the httpOnly cookie
 * (already refreshed by the proxy) and asks the API who it belongs to. Deduplicated per request.
 */
export const getCurrentUser = cache(async (): Promise<CurrentUser | null> => {
  const token = await readAccessToken();
  if (!token) return null;
  try {
    return await unwrap<CurrentUser>(serverApi(token).GET("/api/v1/me"));
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) return null;
    throw error;
  }
});

/** For protected pages: the current user, or a redirect to the login page that comes back to `returnTo`. */
export async function requireUser(returnTo: string): Promise<CurrentUser> {
  const user = await getCurrentUser();
  if (!user) redirect(loginPath(returnTo));
  return user;
}

export const getMyAgencies = cache(async (): Promise<MyAgency[]> => {
  const token = await readAccessToken();
  if (!token) return [];
  return unwrap<MyAgency[]>(serverApi(token).GET("/api/v1/me/agencies"));
});

/** For admin pages: signed in with the Admin role, or a 404 (the area isn't advertised to others). */
export async function requireAdmin(returnTo: string): Promise<CurrentUser> {
  const user = await requireUser(returnTo);
  if (!user.roles.includes("Admin")) notFound();
  return user;
}
