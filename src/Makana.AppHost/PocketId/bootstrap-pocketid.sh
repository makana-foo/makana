#!/bin/sh
set -eu

db_path="$DB_CONNECTION_STRING"
db_dir="$(dirname "$db_path")"
runtime_uid="${PUID:-1000}"
runtime_gid="${PGID:-1000}"

mkdir -p "$db_dir"

if [ ! -f "$db_path" ]; then
  /app/pocket-id import --yes -p /bootstrap/pocketid-seed.zip
fi

# The Pocket ID image drops privileges to PUID/PGID at runtime. The init
# container runs as root, so hand ownership of the imported SQLite files back to
# the runtime user before the main container starts.
chown -R "$runtime_uid:$runtime_gid" "$db_dir"
