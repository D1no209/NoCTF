#!/bin/sh
set -eu

target=${1:-/opt/noctf-cap}
mkdir -p "$target"
target=$(realpath "$target")
[ "$target" != / ] || { echo 'Use a dedicated installation directory.' >&2; exit 2; }
# Existing data owners and permissions are not rewritten during layout updates.
if [ ! -d "$target/data/cap" ]; then
  mkdir -p "$target/data/cap"
  chown 1000:1000 "$target/data/cap"
  chmod 0700 "$target/data/cap"
fi
if [ ! -d "$target/data/valkey" ]; then
  mkdir -p "$target/data/valkey"
  chown 999:1000 "$target/data/valkey"
  chmod 0700 "$target/data/valkey"
fi

if [ ! -f "$target/.env" ]; then
  install -m 0600 "$(dirname "$0")/.env.example" "$target/.env"
fi

echo "Initialized $target. Fill $target/.env before starting the stack."
