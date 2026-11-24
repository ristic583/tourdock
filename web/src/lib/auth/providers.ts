import "server-only";

import { unwrap } from "@/lib/api/client";
import type { components } from "@/lib/api/schema";
import { serverApi } from "@/lib/api/server";

type AuthProviders = components["schemas"]["AuthProvidersResponse"];

/** Google's OAuth client id when the API offers "Continue with Google"; null when it doesn't or can't be asked. */
export async function googleClientId(): Promise<string | null> {
  try {
    return (await unwrap<AuthProviders>(serverApi().GET("/api/v1/auth/providers"))).googleClientId ?? null;
  } catch {
    return null;
  }
}
