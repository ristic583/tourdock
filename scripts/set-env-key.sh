#!/usr/bin/env bash
# Saves one secret into .env without showing it on screen or putting it in shell history.
#
#   scripts/set-env-key.sh VOYAGE_API_KEY
#   scripts/set-env-key.sh ANTHROPIC_API_KEY
#
# Paste the key when asked and press Enter. An existing line for the same name is replaced;
# every other line in .env stays as it was. The file is readable only by you (chmod 600).
set -euo pipefail

name="${1:-}"
if [[ ! "$name" =~ ^[A-Z][A-Z0-9_]*$ ]]; then
  echo "Usage: $0 NAME   (for example VOYAGE_API_KEY or ANTHROPIC_API_KEY)" >&2
  exit 2
fi

cd "$(dirname "$0")/.."
env_file="${ENV_FILE:-.env}"

# Never write a secret into a file git would pick up (ENV_FILE is only overridden for testing).
if [[ -z "${ENV_FILE:-}" ]] && ! git check-ignore -q "$env_file"; then
  echo "Refusing: $env_file is not gitignored." >&2
  exit 1
fi

read -r -s -p "Paste $name (input is hidden), then press Enter: " value
echo
value="${value//[[:space:]]/}"

if [[ -z "$value" ]]; then
  echo "Nothing pasted; $env_file unchanged." >&2
  exit 1
fi
if [[ "$value" == *"'"* ]]; then
  echo "The value contains a single quote and can't be stored safely; $env_file unchanged." >&2
  exit 1
fi

action="Added"
if [[ -f "$env_file" ]] && grep -q "^${name}=" "$env_file"; then
  action="Replaced"
fi

umask 077
tmp="$(mktemp "${env_file}.XXXXXX")"
trap 'rm -f "$tmp"' EXIT
if [[ -f "$env_file" ]]; then
  grep -v "^${name}=" "$env_file" > "$tmp" || true
fi
# Single quotes keep Docker Compose from expanding anything inside the value.
printf "%s='%s'\n" "$name" "$value" >> "$tmp"
chmod 600 "$tmp"
mv "$tmp" "$env_file"
trap - EXIT

echo "$action $name in $env_file (${#value} characters)."
