#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

archive_path=${1:?release archive path is required}
commit_sha=${2:?commit SHA is required}
config_root=${3:-/root/NoCTF}
release_root=${4:-/root/noctf-releases}
platform_image=${5:?published platform image is required}
[[ "$commit_sha" =~ ^[0-9a-f]{40}$ ]]
[[ "$platform_image" =~ ^[A-Za-z0-9.-]+(:[0-9]+)?/[A-Za-z0-9._/-]+@sha256:[a-f0-9]{64}$ ]]
[[ "$(hostname | tr '[:upper:]' '[:lower:]')" != dino209 ]] || {
    echo 'CI must never deploy to production.' >&2; exit 1;
}
archive_path=$(realpath "$archive_path")
config_root=$(realpath "$config_root")
[[ "$config_root" != / && -f "$config_root/.env" && ! -L "$config_root/.env" ]]
mkdir -p "$release_root"
release_root=$(realpath "$release_root")
[[ "$release_root" != / ]]
while IFS= read -r member; do
    case "$member" in
        backend/|deploy/|deploy/ci/|backend/Directory.Build.props|deploy/ci/update_image.py) ;;
        *) echo 'Unexpected release archive entry.' >&2; exit 2 ;;
    esac
done < <(tar -tzf "$archive_path")
release_dir="$release_root/$commit_sha"
[[ ! -L "$release_dir" ]]
mkdir -p "$release_dir"
tar -xzf "$archive_path" -C "$release_dir" backend/Directory.Build.props deploy/ci/update_image.py
# Reuse the installed docker-compose.yml and overrides, not the new-install layout.
docker pull "$platform_image" </dev/null
exec python3 "$release_dir/deploy/ci/update_image.py" \
    "$commit_sha" "$config_root" "$release_root" "$platform_image"
