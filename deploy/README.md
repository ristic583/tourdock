# Deploying TourDock

The Compose overlay in this folder runs TourDock from prebuilt images on one Linux server with Docker. The overlay is
`compose.lan.yaml`, and the release script is `deploy.sh`. The demo at
[tourdock.stefanristic.com](https://tourdock.stefanristic.com) runs this way, behind an HTTPS reverse proxy.

## Images

Build the images once per commit and tag them with the full commit SHA:

```bash
IMAGE_PREFIX=ghcr.io/<you>/tourdock IMAGE_TAG=$(git rev-parse HEAD) \
  docker compose -f compose.yaml -f deploy/compose.build.yaml build api web migrate payments db
```

Push them to a registry, or copy them to the server with `docker save | ssh … docker load`. The server never builds app
images.

## Server

Runtime files and secrets live in `/opt/tourdock`, and the secrets stay on the server. `/opt/tourdock/.env` is never
copied from the repository. It needs:

```dotenv
JWT_SIGNING_KEY=...            # long random value
POSTGRES_PASSWORD=...
PAYMENTS_API_KEY=...
PAYMENTS_WEBHOOK_SECRET=...
STORAGE_ACCESS_KEY=...
STORAGE_SECRET_KEY=...
LAN_HOST=192.0.2.10            # the server's address, for links when PUBLIC_BASE_URL is unset
SMTP_HOST=192.0.2.25           # an SMTP relay; the overlay never starts Mailpit
EMAIL_FROM=noreply@mail.example.com
TRUSTED_PROXIES="192.0.2.1/32" # every reverse proxy in front of port 3000 (see below)
```

All the optional features are configured in the same file, as described in `.env.example`:
- semantic search (`VOYAGE_API_KEY`, with `VOYAGE_TOKEN_BUDGET`);
- the travel assistant (`ANTHROPIC_API_KEY`);
- stable OAuth keys for MCP clients (`OAUTH_SIGNING_KEY_PEM`, `OAUTH_ENCRYPTION_KEY`);
- Google sign-in (`GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`).

Then release a commit:

```bash
IMAGE_PREFIX=ghcr.io/<you>/tourdock IMAGE_TAG=<sha> bash deploy/deploy.sh   # SKIP_PULL=1 for images loaded by hand
```

`deploy.sh` does the following:
1. Copies the Compose files into `/opt/tourdock/releases/<sha>`.
2. Pulls the images.
3. Dumps the database to `/opt/tourdock/backups` (kept for seven days).
4. Runs the migrations and starts everything with `--wait`.
5. Checks API readiness and frontend health.
6. Only then points `/opt/tourdock/current` at the release.

Releases are serialized with a lock. There is no automatic schema rollback: a failed release needs a look at the
container logs. Unused images older than seven days are pruned after a successful release.

## Behind an HTTPS reverse proxy

Route `^/checkout/[^/]+(/complete)?$` to port 8090 and `^/tour-photos/.+` to port 9000. Route everything else to
port 3000, the edge. The edge passes `/mcp`, `/connect` and the OAuth `.well-known` documents to the API, and the rest
to the web app. Keep the payment sandbox's `/v1` API, `/health` and storage writes internal. Then set:

```dotenv
PUBLIC_BASE_URL=https://tours.example.com
AUTH_COOKIE_SECURE=true
```

`TRUSTED_PROXIES` lists every address the proxy connects from, including each replica if it has several. The edge
believes `X-Forwarded-For` and `X-Forwarded-Proto` only from those addresses. If one is missing:
- its share of visitors look like one address to the rate limits;
- the API sees plain HTTP, so OAuth discovery advertises `http://` endpoints and the MCP resource metadata returns 404.

The proxy must pass the `Host` header through unchanged and should redirect HTTP to HTTPS.

## Operations

```bash
cd /opt/tourdock/current && set -a && . ./images.env && set +a
docker compose --project-name tourdock --env-file /opt/tourdock/.env \
  -f compose.yaml -f deploy/compose.lan.yaml ps        # or: logs --tail 100
```

No admin account is seeded by default. Set `ADMIN_EMAIL` and `ADMIN_PASSWORD` in `/opt/tourdock/.env` and recreate
the API container to get one. The backups sit on the same server, so copy them and the photo volume elsewhere if they
matter.
