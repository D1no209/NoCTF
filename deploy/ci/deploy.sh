#!/usr/bin/env bash
set -Eeuo pipefail

archive_path=${1:?release archive path is required}
commit_sha=${2:?commit SHA is required}
config_root=${3:-/root/NoCTF}
release_root=${4:-/root/noctf-releases}
kompose_asset_path=${5:?verified Kompose asset path is required}
minimum_free_kb=${NOCTF_DEPLOY_MIN_FREE_KB:-6291456}
kompose_sha256=65a6a720605bead3964e8b22d423a0763de451a236fe03de902e366cf3d9c147

[[ "$commit_sha" =~ ^[0-9a-f]{40}$ ]] || {
    echo "Invalid deployment commit SHA." >&2
    exit 2
}

archive_path=$(realpath "$archive_path")
upload_dir=$(dirname "$archive_path")
[[ $(basename "$archive_path") == "noctf-release.tar.gz" ]]
[[ $(basename "$upload_dir") == "noctf-ci-upload-$commit_sha" ]]
kompose_asset_path=$(realpath "$kompose_asset_path")
[[ $(dirname "$kompose_asset_path") == "$upload_dir" ]]
[[ $(basename "$kompose_asset_path") == "kompose-linux-amd64" ]]
echo "$kompose_sha256  $kompose_asset_path" | sha256sum -c -

config_root=$(realpath "$config_root")
mkdir -p "$release_root"
release_root=$(realpath "$release_root")
release_dir="$release_root/$commit_sha"
release_tmp="$release_dir.tmp"

cleanup_upload()
{
    if [[ -d "$upload_dir" && $(basename "$upload_dir") == "noctf-ci-upload-$commit_sha" ]]; then
        rm -rf -- "$upload_dir"
    fi
}
trap cleanup_upload EXIT

[[ -f "$config_root/.env" ]] || {
    echo "Deployment environment file is missing: $config_root/.env" >&2
    exit 2
}
[[ -f "$config_root/deploy/docker-compose.prod.yml" ]] || {
    echo "Production Compose override is missing." >&2
    exit 2
}

if [[ ! -d "$release_dir" ]]; then
    [[ "$release_tmp" == "$release_root/$commit_sha.tmp" ]]
    rm -rf -- "$release_tmp"
    mkdir -p "$release_tmp"
    tar -xzf "$archive_path" -C "$release_tmp"
    [[ -f "$release_tmp/deploy/docker-compose.yml" ]]
    [[ -f "$release_tmp/backend/Dockerfile" ]]
    install -d -m 0755 "$release_tmp/backend/docker-assets"
    install -m 0644 \
        "$kompose_asset_path" \
        "$release_tmp/backend/docker-assets/kompose-linux-amd64"
    echo "$kompose_sha256  $release_tmp/backend/docker-assets/kompose-linux-amd64" \
        | sha256sum -c -
    mv "$release_tmp" "$release_dir"
fi

[[ -f "$release_dir/backend/docker-assets/kompose-linux-amd64" ]]
echo "$kompose_sha256  $release_dir/backend/docker-assets/kompose-linux-amd64" \
    | sha256sum -c -

compose=(
    docker compose
    --project-name deploy
    --env-file "$config_root/.env"
    --file "$release_dir/deploy/docker-compose.yml"
    --file "$config_root/deploy/docker-compose.prod.yml"
)
"${compose[@]}" config --quiet

available_kb()
{
    df -Pk "$release_root" | awk 'NR == 2 { print $4 }'
}

image_is_referenced()
{
    [[ -n $(docker ps --all --quiet --filter "ancestor=$1") ]]
}

cleanup_platform_images()
{
    local repository image_id
    local -a image_ids

    for repository in deploy-backend deploy-worker deploy-runner deploy-migration; do
        mapfile -t image_ids < <(
            docker image ls \
                --filter "reference=$repository:*" \
                --format '{{.ID}}' \
                | awk '!seen[$0]++'
        )

        local kept_unused=0
        for image_id in "${image_ids[@]}"; do
            if image_is_referenced "$image_id"; then
                continue
            fi
            if (( kept_unused == 0 )); then
                kept_unused=1
                continue
            fi
            docker image rm --force "$image_id" >/dev/null 2>&1 || true
        done
    done
}

cleanup_stale_resources()
{
    docker container prune --force \
        --filter 'label=noctf.io/managed=true' \
        --filter 'until=24h' >/dev/null
    "${compose[@]}" rm --force --stop migration >/dev/null 2>&1 || true
    docker image prune --force >/dev/null
    docker builder prune --all --force --filter 'until=24h' >/dev/null
    cleanup_platform_images
}

ensure_build_space()
{
    cleanup_stale_resources
    if (( $(available_kb) >= minimum_free_kb )); then
        return
    fi

    echo "Free space is below the deployment threshold; clearing unused build cache."
    docker builder prune --all --force >/dev/null
    cleanup_platform_images

    if (( $(available_kb) < minimum_free_kb )); then
        echo "Insufficient disk space after scoped cleanup." >&2
        df -h "$release_root" >&2
        return 1
    fi
}

backup_database()
{
    local postgres_id backup_dir backup_file timestamp
    postgres_id=$("${compose[@]}" ps --quiet postgres 2>/dev/null || true)
    [[ -n "$postgres_id" ]] || return 0

    backup_dir="$(dirname "$config_root")/backups"
    mkdir -p "$backup_dir"
    backup_dir=$(realpath "$backup_dir")
    timestamp=$(date -u +%Y%m%dT%H%M%SZ)
    backup_file="$backup_dir/noctf-ci-${commit_sha:0:8}-$timestamp.dump"

    docker exec "$postgres_id" sh -ec \
        'exec pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc' \
        > "$backup_file"
    [[ -s "$backup_file" ]]
    docker exec --interactive "$postgres_id" pg_restore --list \
        < "$backup_file" >/dev/null
    sha256sum "$backup_file"

    local -a backups
    mapfile -t backups < <(
        find "$backup_dir" -maxdepth 1 -type f -name 'noctf-ci-*.dump' \
            -printf '%T@ %p\n' | sort -rn | cut -d' ' -f2-
    )
    local index candidate
    for (( index = 10; index < ${#backups[@]}; index++ )); do
        candidate=$(realpath "${backups[$index]}")
        [[ $(dirname "$candidate") == "$backup_dir" ]]
        [[ $(basename "$candidate") == noctf-ci-*.dump ]]
        rm -f -- "$candidate"
    done
}

declare -A previous_images=()
for service in backend worker runner; do
    container_id=$("${compose[@]}" ps --quiet "$service" 2>/dev/null || true)
    if [[ -n "$container_id" ]]; then
        previous_images[$service]=$(docker inspect --format '{{.Image}}' "$container_id")
    fi
done

rollback_services()
{
    local service image_id
    echo "Deployment failed; restoring the previous platform images." >&2
    for service in backend worker runner; do
        image_id=${previous_images[$service]:-}
        if [[ -n "$image_id" ]]; then
            docker image tag "$image_id" "deploy-$service:latest"
        fi
    done
    "${compose[@]}" up --detach --no-deps --force-recreate backend worker runner || true
}

build_images()
{
    local build_log
    build_log=$(mktemp)
    if "${compose[@]}" build migration backend worker runner 2>&1 | tee "$build_log"; then
        rm -f -- "$build_log"
        return
    fi

    if ! grep -Eiq 'no space left on device|disk quota exceeded' "$build_log"; then
        rm -f -- "$build_log"
        return 1
    fi

    echo "Docker build exhausted disk space; cleaning build cache and retrying once."
    rm -f -- "$build_log"
    docker builder prune --all --force >/dev/null
    cleanup_platform_images
    "${compose[@]}" build migration backend worker runner
}

ensure_build_space
backup_database

if ! build_images; then
    rollback_services
    exit 1
fi

if ! "${compose[@]}" run --rm migration; then
    rollback_services
    exit 1
fi

if ! "${compose[@]}" up \
    --detach \
    --no-deps \
    --force-recreate \
    --wait \
    --wait-timeout 180 \
    backend worker runner; then
    rollback_services
    exit 1
fi

if ! curl --fail --silent --show-error \
    http://127.0.0.1:8080/health/ready >/dev/null; then
    rollback_services
    exit 1
fi

version_suffix=$(sed -n \
    's:.*<VersionSuffix>\([^<]*\)</VersionSuffix>.*:\1:p' \
    "$release_dir/backend/Directory.Build.props" | head -n 1)
version_tag=${version_suffix//./}
version_tag=${version_tag:-ci}
for service in backend worker runner migration; do
    image_id=$(docker image inspect "deploy-$service:latest" --format '{{.Id}}' 2>/dev/null || true)
    if [[ -n "$image_id" ]]; then
        docker image tag "$image_id" "deploy-$service:$version_tag-${commit_sha:0:8}"
    fi
done

current_link="$release_root/current"
previous_link="$release_root/previous"
if [[ -L "$current_link" ]]; then
    current_target=$(realpath "$current_link")
    [[ $(dirname "$current_target") == "$release_root" ]]
    ln -sfn "$current_target" "$previous_link"
fi
ln -sfn "$release_dir" "$current_link"

current_target=$(realpath "$current_link")
previous_target=$(realpath "$previous_link" 2>/dev/null || true)
while IFS= read -r candidate; do
    candidate=$(realpath "$candidate")
    [[ $(dirname "$candidate") == "$release_root" ]]
    if [[ ! $(basename "$candidate") =~ ^[0-9a-f]{40}$ ]]; then
        continue
    fi
    if [[ "$candidate" != "$current_target" && "$candidate" != "$previous_target" ]]; then
        rm -rf -- "$candidate"
    fi
done < <(find "$release_root" -mindepth 1 -maxdepth 1 -type d -name '[0-9a-f]*' -print)

cleanup_stale_resources
"${compose[@]}" ps
df -h "$release_root"
echo "Deployed commit $commit_sha successfully."
