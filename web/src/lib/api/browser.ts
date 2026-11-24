import { createApiClient } from "./client";

/**
 * API client for Client Components. Requests go to the BFF (`/api/bff/api/v1/...`), which adds the
 * access token from the httpOnly cookie server-side; the browser never handles tokens.
 */
export const browserApi = createApiClient({ baseUrl: "/api/bff" });
