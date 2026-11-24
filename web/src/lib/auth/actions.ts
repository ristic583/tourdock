"use server";

import { cookies, headers } from "next/headers";
import { redirect } from "next/navigation";

import type { AuthResponse } from "@/lib/api/contracts";
import { unwrap } from "@/lib/api/client";
import { toActionError, type ActionResult } from "@/lib/api/problem";
import { serverApi } from "@/lib/api/server";
import { forwardedForHeader } from "@/lib/client-ip";

import { clearedCookies, readTokenCookies, tokenCookies, writeCookies } from "./cookies";
import { readAccessToken } from "./session";
import { safeNextPath } from "./redirect";
import { loginSchema, registerSchema, zodFieldErrors, type LoginInput, type RegisterInput } from "./schemas";

const INVALID_INPUT = "Please check the highlighted fields.";

/**
 * Signs in and continues to `next`. Pages under /oauth/ load in full instead (the result tells the form where to
 * go): they carry their own Content-Security-Policy, and a client-side navigation would keep the sign-in page's.
 */
export async function login(input: LoginInput, next?: string): Promise<ActionResult<{ fullPageLoad?: string }>> {
  const parsed = loginSchema.safeParse(input);
  if (!parsed.success) {
    return { ok: false, error: { message: INVALID_INPUT, fieldErrors: zodFieldErrors(parsed.error) } };
  }

  try {
    const result = await unwrap<AuthResponse>(serverApi(null, forwardedForHeader(await headers())).POST("/api/v1/auth/login", { body: parsed.data }));
    writeCookies(await cookies(), tokenCookies(result.tokens));
  } catch (error) {
    return { ok: false, error: toActionError(error) };
  }

  const target = safeNextPath(next);
  if (target.startsWith("/oauth/")) return { ok: true, data: { fullPageLoad: target } };
  redirect(target);
}

/**
 * Email-first sign-up: no session yet. The API answers the same whether or not the address already has an account
 * (the inbox gets either a confirmation link or a notice), so the form can't reveal who is registered.
 */
export async function register(input: RegisterInput): Promise<ActionResult> {
  const parsed = registerSchema.safeParse(input);
  if (!parsed.success) {
    return { ok: false, error: { message: INVALID_INPUT, fieldErrors: zodFieldErrors(parsed.error) } };
  }

  try {
    await unwrap(serverApi(null, forwardedForHeader(await headers())).POST("/api/v1/auth/register", { body: parsed.data, parseAs: "text" }));
    return { ok: true, data: undefined };
  } catch (error) {
    return { ok: false, error: toActionError(error) };
  }
}

export async function logout(): Promise<void> {
  const store = await cookies();
  const { refreshToken } = readTokenCookies(store);
  if (refreshToken) {
    // Best effort: revoke the session in the API; the cookies are cleared either way.
    await serverApi(null, forwardedForHeader(await headers()))
      .POST("/api/v1/auth/logout", { body: { refreshToken } })
      .catch(() => undefined);
  }
  writeCookies(store, clearedCookies());
  redirect("/");
}

/** Confirms the email from the link's user id and token. Works signed out too: the link may open on another device. */
export async function confirmEmail(userId: string, token: string): Promise<ActionResult> {
  try {
    await unwrap(serverApi(null, forwardedForHeader(await headers())).POST("/api/v1/auth/verify-email", { body: { userId, token } }));
    return { ok: true, data: undefined };
  } catch (error) {
    return { ok: false, error: toActionError(error) };
  }
}

/** Emails the signed-in user a new confirmation link. */
export async function resendEmailConfirmation(): Promise<ActionResult> {
  const token = await readAccessToken();
  if (!token) return { ok: false, error: { message: "Sign in to get a new link." } };
  try {
    await unwrap(serverApi(token, forwardedForHeader(await headers())).POST("/api/v1/auth/verify-email/resend"));
    return { ok: true, data: undefined };
  } catch (error) {
    return { ok: false, error: toActionError(error) };
  }
}

/** Starts an email change: the new address gets a link; the account keeps the old one until it's opened. */
export async function requestEmailChange(newEmail: string, password: string): Promise<ActionResult> {
  const token = await readAccessToken();
  if (!token) return { ok: false, error: { message: "Sign in to change your email." } };
  try {
    await unwrap(serverApi(token, forwardedForHeader(await headers())).POST("/api/v1/me/email", { body: { newEmail: newEmail.trim(), password } }));
    return { ok: true, data: undefined };
  } catch (error) {
    return { ok: false, error: toActionError(error) };
  }
}

/** Switches to the new address from the link's user id and token (works signed out too). */
export async function confirmEmailChange(userId: string, token: string): Promise<ActionResult> {
  try {
    await unwrap(serverApi(null, forwardedForHeader(await headers())).POST("/api/v1/auth/verify-email-change", { body: { userId, token } }));
    return { ok: true, data: undefined };
  } catch (error) {
    return { ok: false, error: toActionError(error) };
  }
}

/** Admin support: mark an account's email as confirmed. */
export async function adminConfirmEmail(email: string): Promise<ActionResult> {
  const token = await readAccessToken();
  if (!token) return { ok: false, error: { message: "Sign in as an admin." } };
  try {
    await unwrap(serverApi(token, forwardedForHeader(await headers())).POST("/api/v1/admin/users/confirm-email", { body: { email: email.trim() } }));
    return { ok: true, data: undefined };
  } catch (error) {
    return { ok: false, error: toActionError(error) };
  }
}
