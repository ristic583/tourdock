import "server-only";

import { apiBaseUrl } from "@/lib/env";

import { createApiClient, type ApiClient } from "./client";

/** API client for server-side code (Server Components, Server Actions, Route Handlers). */
export function serverApi(accessToken?: string | null, headers?: Record<string, string>): ApiClient {
  return createApiClient({
    baseUrl: apiBaseUrl(),
    accessToken: accessToken ?? undefined,
    headers,
    fetch: (input) => fetch(input, { cache: "no-store" }),
  });
}
