import createClient, { type Client } from "openapi-fetch";

import type { paths } from "./schema";
import { networkError, toApiError } from "./problem";

export type ApiClient = Client<paths>;

type ClientOptions = {
  baseUrl: string;
  accessToken?: string;
  headers?: Record<string, string>;
  fetch?: typeof globalThis.fetch;
};

/**
 * Typed client for the TourDock API, generated from the OpenAPI document.
 * On the server it talks to the API directly; in the browser it goes through the BFF (`/api/bff`).
 */
export function createApiClient({ baseUrl, accessToken, headers, fetch }: ClientOptions): ApiClient {
  const client = createClient<paths>({
    baseUrl,
    headers: {
      accept: "application/json",
      ...(accessToken ? { authorization: `Bearer ${accessToken}` } : {}),
      ...headers,
    },
    fetch: fetch ?? globalThis.fetch,
  });

  // No openapi-fetch middleware on purpose: it generates a random request id, which Cache Components
  // doesn't allow during prerendering. Connection failures are mapped in `unwrap` instead.
  return client;
}

type FetchResult = { data?: unknown; error?: unknown; response: Response };

/**
 * Returns the response body typed as `T`, or throws an `ApiError` built from the ProblemDetails body.
 * The API's OpenAPI document doesn't declare response bodies yet, so callers name the type (see ./contracts).
 */
export async function unwrap<T>(request: Promise<FetchResult>): Promise<T> {
  let result: FetchResult;
  try {
    result = await request;
  } catch {
    // Connection failures become an ApiError, so callers have a single error type to handle.
    throw networkError();
  }
  const { data, error, response } = result;
  if (!response.ok) throw toApiError(response.status, error);
  return data as T;
}
