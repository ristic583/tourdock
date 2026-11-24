/** Liveness probe for the container healthcheck. Doesn't call the API, so a slow API can't restart the web app. */
export function GET() {
  return Response.json({ status: "ok" });
}
