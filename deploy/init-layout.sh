#!/usr/bin/env bash
set -Eeuo pipefail
umask 077
source_root=$(cd -- "$(dirname -- "$0")" && pwd)
target=${1:?installation directory is required}
mkdir -p "$target"
target=$(realpath "$target")
[[ "$target" != / ]] || { echo 'Use a dedicated installation directory.' >&2; exit 2; }
for directory in data/postgres data/redis data/nats data/registry data/uploads data/backups config/docker config/registry/auth env/noctf env/postgres env/registry; do
    mkdir -p "$target/$directory"
done
for relative in .env env/noctf/.env env/postgres/.env env/registry/.env; do
    if [[ ! -e "$target/$relative" ]]; then
        cp "$source_root/$relative.example" "$target/$relative"
        chmod 0600 "$target/$relative"
    fi
done
if [[ ! -e "$target/docker-compose.yml" ]]; then
    cp "$source_root/docker-compose.yml" "$target/docker-compose.yml"
fi
echo "Layout prepared in $target. Existing files were preserved."
echo 'Fill .env, create Registry bcrypt credentials, and configure Docker login before starting.'
echo 'Existing installations: migrate and verify old data explicitly. This script copies no database data and starts no service.'
