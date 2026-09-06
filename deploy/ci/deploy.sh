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
archive_path=$(realpath "$archive_path")
config_root=$(realpath "$config_root")
[[ "$config_root" != / && -f "$config_root/.env" && ! -L "$config_root/.env" ]]
mkdir -p "$release_root"
release_root=$(realpath "$release_root")
[[ "$release_root" != / ]]
exec 9>"$config_root/.deploy.lock"
flock -n 9 || { echo 'Another deployment is active.' >&2; exit 1; }

# Release bundles contain deployment metadata only, never application source/build inputs.
while IFS= read -r member; do
    case "$member" in
        backend/|deploy/|backend/Directory.Build.props|deploy/docker-compose.yml) ;;
        *) echo 'Unexpected release archive entry.' >&2; exit 2 ;;
    esac
done < <(tar -tzf "$archive_path")
release_dir="$release_root/$commit_sha"
mkdir -p "$release_dir"
tar -xzf "$archive_path" -C "$release_dir" backend/Directory.Build.props deploy/docker-compose.yml
export NOCTF_PLATFORM_IMAGE="$platform_image"
compose=(docker compose --project-directory "$config_root" --env-file "$config_root/.env"
    --file "$release_dir/deploy/docker-compose.yml")
rendered=$(mktemp "$config_root/.compose-config.XXXXXX")
trap 'rm -f -- "$rendered"' EXIT
"${compose[@]}" config --format json > "$rendered"
docker network inspect 1panel-network >/dev/null

# Do not silently replace named volumes with empty directories or run beside the old split Host.
python3 - "$rendered" "$config_root" <<'PY'
import json, os, pathlib, subprocess, sys
config = json.load(open(sys.argv[1]))
root = pathlib.Path(sys.argv[2])
expected = {'noctf', 'postgres', 'redis', 'nats', 'registry'}
if set(config['services']) != expected:
    raise SystemExit('Expected the five-service directory-based deployment layout.')
env = config['services']['noctf']['environment']
if str(env.get('Database__AutoMigrate', '')).lower() != 'true':
    raise SystemExit('Enable Database__AutoMigrate in the NoCTF .env before deployment.')
if str(env.get('Observability__Enabled', '')).lower() != 'false' or env.get('ASPNETCORE_URLS') != 'http://+:8080':
    raise SystemExit('The stock deployment requires telemetry disabled and the single internal HTTP listener on 8080.')
for service in config['services'].values():
    for volume in service.get('volumes', []):
        if volume['type'] != 'bind' or not os.path.exists(volume['source']):
            raise SystemExit('A required bind source is missing; prepare/migrate the data directories first.')
if not (root / 'config/registry/auth/htpasswd').is_file() or not (root / 'config/registry/auth/htpasswd').stat().st_size:
    raise SystemExit('Create Registry bcrypt credentials in config/registry/auth/htpasswd first.')
ids = subprocess.check_output(['docker', 'ps', '-aq', '--filter', 'label=com.docker.compose.project='+config['name']], text=True).split()
containers = json.loads(subprocess.check_output(['docker', 'inspect', *ids])) if ids else []
running = set()
for container in containers:
    service = container['Config']['Labels'].get('com.docker.compose.service')
    if service not in expected:
        raise SystemExit('Legacy service '+str(service)+' still exists in this project. Operations must complete the one-time topology transition; CI will not stop or delete it.')
    mounts = {mount['Destination']: mount for mount in container['Mounts']}
    for desired in config['services'][service].get('volumes', []):
        current = mounts.get(desired['target'])
        if not current or current['Type'] != 'bind' or os.path.realpath(current['Source']) != os.path.realpath(desired['source']):
            raise SystemExit('Existing '+service+' data mounts do not match the new layout. Stop and migrate the exact NoCTF data explicitly; CI refuses to start an empty replacement.')
    if container['State']['Running']:
        running.add(service)
if not expected.difference({'noctf'}).issubset(running):
    raise SystemExit('Start the configured postgres, redis, nats and registry dependencies first. CI upgrades only the NoCTF application.')
print('Existing data layout and dependency containers verified.')
PY
readiness_timeout=$(python3 - "$rendered" <<'PY'
import json, sys
env = json.load(open(sys.argv[1]))['services']['noctf']['environment']
seconds = int(env.get('Database__StartupTimeoutSeconds', '180'))
if not 1 <= seconds <= 1800:
    raise SystemExit('Database startup timeout is outside the supported range.')
print(seconds + 180)
PY
)

available_kb=$(df -Pk "$config_root" | awk 'NR == 2 {print $4}')
(( available_kb >= ${NOCTF_DEPLOY_MIN_FREE_KB:-6291456} )) || {
    echo 'Insufficient free space. CI will not prune shared host images, containers or caches.' >&2; exit 1;
}
docker pull "$platform_image" </dev/null
[[ $(docker image inspect "$platform_image" --format '{{index .Config.Labels "org.opencontainers.image.revision"}}') == "$commit_sha" ]]
[[ $(docker image inspect "$platform_image" --format '{{json .Config.Healthcheck}}') != null ]]
postgres_id=$("${compose[@]}" ps -q postgres)
previous_id=$("${compose[@]}" ps -q noctf)
previous_image=""
if [[ -n "$previous_id" ]]; then previous_image=$(docker inspect "$previous_id" --format '{{.Config.Image}}'); fi
backup_dir="$config_root/data/backups/$commit_sha-$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$backup_dir"
cp -p "$config_root/.env" "$backup_dir/environment"
docker exec "$postgres_id" sh -ec 'exec pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc' </dev/null > "$backup_dir/database.dump"
[[ -s "$backup_dir/database.dump" ]]
docker exec -i "$postgres_id" pg_restore --list < "$backup_dir/database.dump" >/dev/null
sha256sum "$backup_dir/database.dump"

schema_fingerprint()
{
    docker exec "$postgres_id" sh -ec 'exec pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" --schema-only --no-owner --no-privileges' </dev/null \
        | sed -E '/^--/d; /^\\(un)?restrict /d; /^[[:space:]]*$/d' | sha256sum | cut -d' ' -f1
}
previous_schema=$(schema_fingerprint)
set_image()
{
    python3 - "$config_root/.env" "$1" <<'PY'
import os, pathlib, stat, sys
path = pathlib.Path(sys.argv[1])
lines = path.read_text().splitlines()
key = 'NOCTF_PLATFORM_IMAGE='
if sum(line.startswith(key) for line in lines) != 1:
    raise SystemExit('The root .env must have exactly one NOCTF_PLATFORM_IMAGE entry.')
temp = path.with_name('.env.deploy.tmp')
with temp.open('x') as out:
    out.write('\n'.join(key+sys.argv[2] if line.startswith(key) else line for line in lines)+'\n')
os.chmod(temp, stat.S_IMODE(path.stat().st_mode))
os.replace(temp, path)
PY
}

set_image "$platform_image"
# The image performs migration before its listeners/consumers start. No migration container is used.
if ! "${compose[@]}" up -d --no-deps --no-build --pull never --wait --wait-timeout "$readiness_timeout" noctf </dev/null; then
    "${compose[@]}" logs --no-color --tail 100 noctf >&2 || true
    if [[ -n "$previous_image" && $(schema_fingerprint) == "$previous_schema" ]]; then
        echo 'Database schema is unchanged; restoring the previous application image.' >&2
        cp -p "$backup_dir/environment" "$config_root/.env"
        NOCTF_PLATFORM_IMAGE="$previous_image" "${compose[@]}" up -d --no-deps --no-build --pull never noctf </dev/null || true
    else
        echo "Schema changed or no previous image exists. No automatic database rollback. Backup: $backup_dir" >&2
    fi
    exit 1
fi
container_id=$("${compose[@]}" ps -q noctf)
[[ $(docker inspect "$container_id" --format '{{.State.Health.Status}}') == healthy ]]
ln -sfn "$release_dir" "$release_root/current"
echo "Deployed $commit_sha. Application healthy; proxy and dependency services were not modified."
echo "Backup: $backup_dir"
