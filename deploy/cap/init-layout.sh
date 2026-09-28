#!/bin/sh
set -eu

target=${1:-/opt/noctf-cap}
mkdir -p "$target/data/cap" "$target/data/valkey"
chown 1000:1000 "$target/data/cap"
chown 999:1000 "$target/data/valkey"
chmod 0750 "$target" "$target/data"
chmod 0700 "$target/data/cap" "$target/data/valkey"

if [ ! -f "$target/.env" ]; then
  install -m 0600 "$(dirname "$0")/.env.example" "$target/.env"
fi

echo "Initialized $target. Fill $target/.env before starting the stack."
