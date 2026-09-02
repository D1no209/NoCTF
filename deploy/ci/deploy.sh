#!/usr/bin/env bash
set -Eeuo pipefail

archive_path=${1:?release archive path is required}
commit_sha=${2:?commit SHA is required}
config_root=${3:-/root/NoCTF}
release_root=${4:-/root/noctf-releases}
platform_image=${5:?published platform image is required}
minimum_free_kb=${NOCTF_DEPLOY_MIN_FREE_KB:-6291456}

[[ "$commit_sha" =~ ^[0-9a-f]{40}$ ]] || {
    echo "Invalid deployment commit SHA." >&2
    exit 2
}
[[ "$platform_image" =~ ^[A-Za-z0-9.-]+(:[0-9]+)?/[A-Za-z0-9._/-]+@sha256:[a-f0-9]{64}$ ]] || {
    echo "Invalid deployment image reference." >&2
    exit 2
}

archive_path=$(realpath "$archive_path")
upload_dir=$(dirname "$archive_path")
[[ $(basename "$archive_path") == "noctf-release.tar.gz" ]]
[[ $(basename "$upload_dir") == "noctf-ci-upload-$commit_sha" ]]
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

observability_env="$config_root/deploy/observability.env"
if [[ ! -f "$observability_env" ]]; then
    install -d -m 0755 "$(dirname "$observability_env")"
    umask 077
    grafana_password=$(od -An -N32 -tx1 /dev/urandom | tr -d ' \n')
    [[ ${#grafana_password} -eq 64 ]]
    printf 'GRAFANA_ADMIN_PASSWORD=%s\n' "$grafana_password" > "$observability_env"
fi
chmod 0600 "$observability_env"
grep -Eq '^GRAFANA_ADMIN_PASSWORD=.{16,}$' "$observability_env" || {
    echo "Grafana deployment credential is missing or too short." >&2
    exit 2
}

if [[ ! -d "$release_dir" ]]; then
    [[ "$release_tmp" == "$release_root/$commit_sha.tmp" ]]
    rm -rf -- "$release_tmp"
    mkdir -p "$release_tmp"
    tar -xzf "$archive_path" -C "$release_tmp"
    [[ -f "$release_tmp/deploy/docker-compose.yml" ]]
    [[ -f "$release_tmp/deploy/docker-compose.ci.yml" ]]
    [[ -f "$release_tmp/deploy/docker-compose.observability.yml" ]]
    [[ -f "$release_tmp/deploy/observability/prometheus/prometheus.yml" ]]
    [[ -f "$release_tmp/deploy/observability/grafana/provisioning/dashboards/dashboards.yml" ]]
    mv "$release_tmp" "$release_dir"
fi

export NOCTF_MIGRATION_IMAGE="$platform_image"
export NOCTF_BACKEND_IMAGE="$platform_image"
export NOCTF_WORKER_IMAGE="$platform_image"
export NOCTF_RUNNER_IMAGE="$platform_image"

compose=(
    docker compose
    --project-name deploy
    --env-file "$config_root/.env"
    --env-file "$observability_env"
    --file "$release_dir/deploy/docker-compose.yml"
    --file "$config_root/deploy/docker-compose.prod.yml"
    --file "$release_dir/deploy/docker-compose.observability.yml"
    --file "$release_dir/deploy/docker-compose.ci.yml"
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
    cleanup_platform_images
}

ensure_deploy_space()
{
    if (( $(available_kb) >= minimum_free_kb )); then
        return
    fi

    echo "Free space is below the deployment threshold; clearing stale deployment resources."
    cleanup_stale_resources
    if (( $(available_kb) >= minimum_free_kb )); then
        return
    fi

    echo "Free space remains below the deployment threshold; clearing unused build cache."
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
    local service image_id rollback_tag
    local backend_image="$platform_image"
    local worker_image="$platform_image"
    local runner_image="$platform_image"
    echo "Deployment failed; restoring the previous platform images." >&2
    for service in backend worker runner; do
        image_id=${previous_images[$service]:-}
        if [[ -n "$image_id" ]]; then
            rollback_tag="deploy-$service:rollback-${commit_sha:0:8}"
            docker image tag "$image_id" "$rollback_tag"
            case "$service" in
                backend) backend_image=$rollback_tag ;;
                worker) worker_image=$rollback_tag ;;
                runner) runner_image=$rollback_tag ;;
            esac
        fi
    done
    NOCTF_BACKEND_IMAGE="$backend_image" \
    NOCTF_WORKER_IMAGE="$worker_image" \
    NOCTF_RUNNER_IMAGE="$runner_image" \
        "${compose[@]}" up \
            --detach --no-deps --force-recreate --no-build --pull never \
            backend worker runner || true
}

pull_platform_image()
{
    local attempt
    local max_attempts=${NOCTF_PLATFORM_PULL_ATTEMPTS:-5}
    local retry_delay_seconds=${NOCTF_PLATFORM_PULL_RETRY_DELAY_SECONDS:-10}

    [[ "$max_attempts" =~ ^[1-9][0-9]*$ ]]
    [[ "$retry_delay_seconds" =~ ^[1-9][0-9]*$ ]]

    for (( attempt = 1; attempt <= max_attempts; attempt++ )); do
        if docker pull "$platform_image"; then
            return
        fi
        if (( attempt == max_attempts )); then
            echo "Unable to pull the published platform image after $max_attempts attempts." >&2
            return 1
        fi
        echo "Platform image pull attempt $attempt/$max_attempts failed; retrying in $retry_delay_seconds seconds." >&2
        sleep "$retry_delay_seconds"
    done
}

ensure_observability_images()
{
    local attempt
    local max_attempts=${NOCTF_OBSERVABILITY_PULL_ATTEMPTS:-5}
    local retry_delay_seconds=${NOCTF_OBSERVABILITY_PULL_RETRY_DELAY_SECONDS:-10}
    local -a services=(
        prometheus
        grafana
        postgres-exporter
        redis-exporter
        nats-exporter
        node-exporter
    )

    [[ "$max_attempts" =~ ^[1-9][0-9]*$ ]]
    [[ "$retry_delay_seconds" =~ ^[1-9][0-9]*$ ]]

    for (( attempt = 1; attempt <= max_attempts; attempt++ )); do
        if "${compose[@]}" pull --policy missing "${services[@]}"; then
            return
        fi

        if (( attempt == max_attempts )); then
            echo "Unable to ensure observability images after $max_attempts attempts." >&2
            return 1
        fi

        echo "Observability image check attempt $attempt/$max_attempts failed; retrying in $retry_delay_seconds seconds." >&2
        sleep "$retry_delay_seconds"
    done
}

ensure_deploy_space
if ! ensure_observability_images; then
    exit 1
fi
if ! pull_platform_image; then
    exit 1
fi
backup_database

if ! "${compose[@]}" run --rm migration; then
    rollback_services
    exit 1
fi

if ! "${compose[@]}" up \
    --detach \
    --no-deps \
    --force-recreate \
    --no-build \
    --pull never \
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

if ! "${compose[@]}" up \
    --detach \
    --no-deps \
    --force-recreate \
    --no-build \
    --pull never \
    --wait \
    --wait-timeout 180 \
    prometheus grafana postgres-exporter redis-exporter nats-exporter node-exporter; then
    rollback_services
    exit 1
fi

version_suffix=$(sed -n \
    's:.*<VersionSuffix>\([^<]*\)</VersionSuffix>.*:\1:p' \
    "$release_dir/backend/Directory.Build.props" | head -n 1)
version_tag=${version_suffix//./}
version_tag=${version_tag:-ci}
for service in backend worker runner migration; do
    container_id=$("${compose[@]}" ps --quiet "$service" 2>/dev/null || true)
    image_id=""
    if [[ -n "$container_id" ]]; then
        image_id=$(docker inspect --format '{{.Image}}' "$container_id")
    fi
    if [[ -n "$image_id" ]]; then
        docker image tag "$image_id" "deploy-$service:$version_tag-${commit_sha:0:8}"
    fi
done
docker image rm "$platform_image" >/dev/null 2>&1 || true

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
