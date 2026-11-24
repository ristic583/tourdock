#!/usr/bin/env bash
# Regression check for ADR 0024: a client can't dodge the per-IP rate limits by faking X-Forwarded-For.
# Run against a local stack:  deploy/smoke-edge.sh [http://localhost:3000]
# Sends 130 cheap GETs, each with a different fake address. The API allows 100 per 10 s per IP, so some must get 429.
set -euo pipefail
base="${1:-http://localhost:${WEB_PORT:-3000}}"
sleep 11 # start with an empty rate-limit window
limited=0
for i in $(seq 1 130); do
  code=$(curl -s -o /dev/null -w '%{http_code}' -H "X-Forwarded-For: 203.0.113.$((i % 250 + 1))" "$base/api/bff/api/v1/cities?q=sp")
  [[ "$code" == 429 ]] && limited=$((limited + 1))
done
if (( limited == 0 )); then
  echo "FAIL: 130 requests with faked X-Forwarded-For were never rate limited" >&2
  exit 1
fi
echo "OK: $limited of 130 requests were rate limited despite faked X-Forwarded-For"
