import type { Metadata } from "next";

import { Button } from "@/components/ui/button";
import { unwrap } from "@/lib/api/client";
import type { components } from "@/lib/api/schema";
import { serverApi } from "@/lib/api/server";
import { requireUser } from "@/lib/auth/dal";
import { readAccessToken } from "@/lib/auth/session";
import { describeScopes, redirectTarget } from "@/lib/oauth-consent";

export const metadata: Metadata = { title: "Allow access", robots: { index: false } };
export const instant = false;

type OAuthClient = components["schemas"]["OAuthClientResponse"];
type Props = { searchParams: Promise<Record<string, string | string[] | undefined>> };

function one(value: string | string[] | undefined): string | undefined {
  return typeof value === "string" ? value : undefined;
}

/**
 * Consent for an AI app (an MCP client) that wants to act for the signed-in user (ADR 0028). The API parked the
 * authorization request and sent the visitor here with its id and what to show; the form posts the decision straight
 * back to the API's authorization endpoint, which checks that the request still matches what this page showed.
 */
export default async function ConsentPage({ searchParams }: Props) {
  const query = await searchParams;
  const [requestUri, clientId, scope, redirectUri] = [query.request_uri, query.client_id, query.scope, query.redirect_uri].map(one);
  await requireUser(`/oauth/consent?${new URLSearchParams(Object.entries({ request_uri: requestUri, client_id: clientId, scope, redirect_uri: redirectUri }).filter((e): e is [string, string] => Boolean(e[1])))}`);

  const client = clientId ? await findClient(clientId) : null;
  const target = redirectUri ? redirectTarget(redirectUri) : null;
  if (!requestUri || !client || !target || !client.redirectUris.some((uri) => sameRedirect(uri, redirectUri!))) {
    return (
      <Card title="This link doesn't work">
        <p className="text-muted-foreground">Start connecting TourDock again from the app you were using.</p>
      </Card>
    );
  }

  return (
    <Card title={`Allow ${client.name} to use your TourDock account?`}>
      <p className="text-muted-foreground">It will be able to:</p>
      <ul className="mt-2 list-disc space-y-1 pl-5">
        <li>search tours and read tour details (anyone can)</li>
        {describeScopes(scope ?? "").map((line) => <li key={line}>{line}</li>)}
      </ul>
      <p className="mt-4 text-sm text-muted-foreground">After you decide, you&apos;ll go back to <strong className="text-ink">{target.label}</strong>.</p>
      {target.loopback && (
        <p className="mt-2 rounded-control bg-urgent-soft px-3 py-2 text-sm text-urgent-ink">
          This app runs on your own computer. Allow it only if you just started connecting it yourself.
        </p>
      )}
      <form method="post" action="/connect/authorize" className="mt-6 flex flex-wrap gap-3">
        <input type="hidden" name="client_id" value={clientId} />
        <input type="hidden" name="request_uri" value={requestUri} />
        <input type="hidden" name="shown_scope" value={scope ?? ""} />
        <input type="hidden" name="shown_redirect_uri" value={redirectUri} />
        <Button type="submit" name="decision" value="allow">Allow</Button>
        <Button type="submit" name="decision" value="deny" variant="outline">Don&apos;t allow</Button>
      </form>
    </Card>
  );
}

function Card({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="mx-auto max-w-[560px] px-4 pt-12 pb-20 sm:px-6">
      <div className="rounded-card border border-line bg-surface p-6 shadow-card">
        <p className="label-caps">Connect an app</p>
        <h1 className="mt-1 text-2xl leading-tight font-bold">{title}</h1>
        <div className="mt-4">{children}</div>
      </div>
    </div>
  );
}

async function findClient(clientId: string): Promise<OAuthClient | null> {
  try {
    const token = await readAccessToken();
    return await unwrap<OAuthClient>(serverApi(token).GET("/api/v1/oauth/clients/{clientId}", { params: { path: { clientId } } }));
  } catch {
    return null;
  }
}

/** Loopback redirect URIs are registered without a port (native apps listen on a random one). */
function sameRedirect(registered: string, requested: string): boolean {
  try {
    const a = new URL(registered);
    const b = new URL(requested);
    const loopback = ["localhost", "127.0.0.1", "[::1]"].includes(b.hostname);
    return a.protocol === b.protocol && a.hostname === b.hostname && a.pathname === b.pathname && (loopback || a.port === b.port);
  } catch {
    return false;
  }
}
