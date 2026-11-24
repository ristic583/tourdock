#!/usr/bin/env bash
set -euo pipefail
: "${IMAGE_PREFIX:?required}" "${IMAGE_TAG:?required}"
[[ "$IMAGE_TAG" =~ ^[0-9a-f]{40}$ ]] || { echo "Expected a commit SHA" >&2; exit 1; }
root=/opt/tourdock
umask 077
mkdir -p "$root/releases" "$root/backups"
exec 9>"$root/deploy.lock"
flock -w 600 9
release="$root/releases/$IMAGE_TAG"
mkdir -p "$release/deploy"
cp compose.yaml "$release/compose.yaml"
cp deploy/compose.lan.yaml "$release/deploy/compose.lan.yaml"
# The edge proxy's config is mounted from the release folder (compose.yaml: ./deploy/Caddyfile).
cp deploy/Caddyfile "$release/deploy/Caddyfile"
printf 'IMAGE_PREFIX=%s\nIMAGE_TAG=%s\n' "$IMAGE_PREFIX" "$IMAGE_TAG" > "$release/images.env"
compose=(docker compose --project-name tourdock --env-file "$root/.env"
  -f "$release/compose.yaml" -f "$release/deploy/compose.lan.yaml")
"${compose[@]}" config --quiet
# SKIP_PULL=1: the images were loaded by hand (docker load), e.g. a manual deploy without the registry.
if [ "${SKIP_PULL:-0}" != 1 ]; then
  "${compose[@]}" pull
fi
# Preserve a pre-migration backup; failed dumps stop the release.
if [ -n "$("${compose[@]}" ps --status running -q db)" ]; then
  backup="$root/backups/$(date -u +%Y%m%dT%H%M%SZ)-$IMAGE_TAG.sql.gz"
  "${compose[@]}" exec -T db sh -c 'pg_dump -U "$POSTGRES_USER" "$POSTGRES_DB"' | gzip > "$backup.tmp"
  mv "$backup.tmp" "$backup"
fi
# Docker can't change an existing network's address range in place. When compose.yaml asks for a different subnet
# (the web container has a fixed IP the API trusts), take the stack down once so the network is recreated.
# Volumes (database, photos) are kept.
wanted_subnet=$("${compose[@]}" config | grep -m1 -oE 'subnet: [0-9./]+' | cut -d' ' -f2 || true)
current_subnet=$(docker network inspect -f '{{range .IPAM.Config}}{{.Subnet}}{{end}}' tourdock_default 2>/dev/null || true)
if [ -n "$wanted_subnet" ] && [ -n "$current_subnet" ] && [ "$wanted_subnet" != "$current_subnet" ]; then
  echo "Network range changes from $current_subnet to $wanted_subnet: recreating the stack's network"
  "${compose[@]}" down --remove-orphans
fi
"${compose[@]}" up --detach --no-build --wait --wait-timeout 180
curl --fail --silent --show-error --retry 5 --retry-delay 3 http://127.0.0.1:8080/health/ready
curl --fail --silent --show-error --retry 5 --retry-delay 3 http://127.0.0.1:3000/api/health
ln -sfn "$release" "$root/current"
printf '%s\n' "$IMAGE_TAG" > "$root/deployed-sha"
find "$root/backups" -name '*.sql.gz' -mtime +7 -delete
# The server is dedicated to TourDock. Keep recent images for manual recovery.
docker image prune --all --force --filter 'until=168h'
