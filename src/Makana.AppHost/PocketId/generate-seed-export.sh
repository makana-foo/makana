#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
container_name="pocketid-seedgen"
host_port="${POCKETID_SEED_PORT:-14112}"
image="${POCKETID_IMAGE:-ghcr.io/pocket-id/pocket-id:v2}"
client_id="e739a9df-f34a-4b72-990e-005553be013c"
client_name="Makana"
web_console_url="${MAKANA_WEB_CONSOLE_URL:-https://makana.foo}"
client_callback_url="${web_console_url%/}/signin-oidc"
client_logout_url="${web_console_url%/}/signout-callback-oidc"
encryption_key="${POCKETID_ENCRYPTION_KEY:-ephemeral-pocketid-encryption-key-change-if-state-becomes-durable}"
static_api_key="${POCKETID_STATIC_API_KEY:-seed-static-api-key}"
seed_zip_path="$script_dir/pocketid-seed.zip"
working_dir="$(mktemp -d)"
client_payload_path="$working_dir/client.json"

cleanup() {
  docker rm -f "$container_name" >/dev/null 2>&1 || true
  rm -rf "$working_dir"
}

trap cleanup EXIT

docker rm -f "$container_name" >/dev/null 2>&1 || true

docker run -d --rm \
  --name "$container_name" \
  -p "$host_port:1411" \
  -v "$working_dir:/seed-data" \
  -e "APP_URL=http://127.0.0.1:$host_port" \
  -e "ENCRYPTION_KEY=$encryption_key" \
  -e "STATIC_API_KEY=$static_api_key" \
  -e "DB_CONNECTION_STRING=/seed-data/pocket-id.db" \
  -e "FILE_BACKEND=database" \
  "$image" \
  >/dev/null

until curl -fsS "http://127.0.0.1:$host_port/.well-known/openid-configuration" >/dev/null; do
  sleep 1
done

curl -fsS \
  -X POST \
  -H "X-API-KEY: $static_api_key" \
  -H "Content-Type: application/json" \
  "http://127.0.0.1:$host_port/api/oidc/clients" \
  -d "{\"id\":\"$client_id\",\"name\":\"$client_name\"}" \
  >/dev/null

secret_json="$(curl -fsS \
  -X POST \
  -H "X-API-KEY: $static_api_key" \
  "http://127.0.0.1:$host_port/api/oidc/clients/$client_id/secret")"

client_secret="$(printf '%s' "$secret_json" | sed -n 's/.*"secret":"\([^"]*\)".*/\1/p')"
if [ -z "$client_secret" ]; then
  echo "Failed to parse Pocket ID client secret from: $secret_json" >&2
  exit 1
fi

cat >"$client_payload_path" <<JSON
{
  "id": "$client_id",
  "name": "$client_name",
  "isPublic": false,
  "pkceEnabled": true,
  "launchURL": "$web_console_url",
  "callbackURLs": ["$client_callback_url"],
  "logoutCallbackURLs": ["$client_logout_url"]
}
JSON

curl -fsS \
  -X PUT \
  -H "X-API-KEY: $static_api_key" \
  -H "Content-Type: application/json" \
  "http://127.0.0.1:$host_port/api/oidc/clients/$client_id" \
  --data-binary "@$client_payload_path" \
  >/dev/null

# Keep Pocket ID on its default signing key algorithm. The built-in
# Microsoft/ASP.NET Core OIDC stack used by Makana does not currently
# materialize usable signing keys from Pocket ID's Ed25519 JWKS.
docker exec "$container_name" /app/pocket-id export -p /seed-data/pocketid-seed.zip >/dev/null
docker stop "$container_name" >/dev/null
cp "$working_dir/pocketid-seed.zip" "$seed_zip_path"

printf 'Seed export written to %s\n' "$seed_zip_path"
printf 'Makana OIDC client secret: %s\n' "$client_secret"
