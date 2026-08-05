#!/usr/bin/env bash
set -euo pipefail

recovery_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
run_id="$(date +%s)-$$"
network_name="noctf-recovery-$run_id"
source_postgres="noctf-recovery-source-postgres-$run_id"
target_postgres="noctf-recovery-target-postgres-$run_id"
source_minio="noctf-recovery-source-minio-$run_id"
target_minio="noctf-recovery-target-minio-$run_id"
tool_image="noctf-recovery-tool:$run_id"
work_dir="$(mktemp -d)"

cleanup() {
    docker rm -f "$source_postgres" "$target_postgres" "$source_minio" "$target_minio" >/dev/null 2>&1 || true
    docker network rm "$network_name" >/dev/null 2>&1 || true
    docker image rm "$tool_image" >/dev/null 2>&1 || true
    rm -rf -- "$work_dir"
}
trap cleanup EXIT

docker build --tag "$tool_image" "$recovery_dir"
docker network create "$network_name" >/dev/null
docker run --detach --name "$source_postgres" --network "$network_name" \
    --env POSTGRES_DB=noctf --env POSTGRES_USER=noctf --env POSTGRES_PASSWORD=rehearsal-postgres \
    postgres@sha256:16bc17c64a573ef34162af9298258d1aec548232985b33ed7b1eac33ba35c229 >/dev/null
docker run --detach --name "$target_postgres" --network "$network_name" \
    --env POSTGRES_DB=noctf --env POSTGRES_USER=noctf --env POSTGRES_PASSWORD=rehearsal-postgres \
    postgres@sha256:16bc17c64a573ef34162af9298258d1aec548232985b33ed7b1eac33ba35c229 >/dev/null
for minio_container in "$source_minio" "$target_minio"; do
    docker run --detach --name "$minio_container" --network "$network_name" \
        --env MINIO_ROOT_USER=rehearsal-minio --env MINIO_ROOT_PASSWORD=rehearsal-minio-secret \
        minio/minio@sha256:14cea493d9a34af32f524e538b8346cf79f3321eff8e708c1e2960462bd8936e \
        server /data >/dev/null
done

for postgres_container in "$source_postgres" "$target_postgres"; do
    for _ in $(seq 1 60); do
        if docker exec "$postgres_container" pg_isready --username noctf --dbname noctf >/dev/null 2>&1; then
            break
        fi
        sleep 1
    done
    docker exec "$postgres_container" pg_isready --username noctf --dbname noctf >/dev/null
done

docker exec --interactive "$source_postgres" psql --username noctf --dbname noctf \
    --set=ON_ERROR_STOP=1 <"$recovery_dir/seed-source.sql" >/dev/null

printf '%s' 'rehearsal-postgres' >"$work_dir/postgres-password"
printf '%s' 'rehearsal-minio' >"$work_dir/minio-access"
printf '%s' 'rehearsal-minio-secret' >"$work_dir/minio-secret"
chmod 600 "$work_dir/postgres-password" "$work_dir/minio-access" "$work_dir/minio-secret"
docker run --rm --user "$(id -u):$(id -g)" --volume "$work_dir:/work" --entrypoint age-keygen "$tool_image" \
    --output /work/age-identity.txt >/dev/null
grep '^# public key: ' "$work_dir/age-identity.txt" | sed 's/^# public key: //' >"$work_dir/age-recipients.txt"
docker run --rm --user "$(id -u):$(id -g)" --volume "$work_dir:/work" --entrypoint minisign "$tool_image" \
    -G -W -p /work/minisign.pub -s /work/minisign.key >/dev/null
printf '%s' 'fixture-attachment-content' >"$work_dir/attachment"
printf '%s' 'fixture-patch-content' >"$work_dir/patch"

docker run --rm --network "$network_name" --volume "$work_dir:/work" --entrypoint sh "$tool_image" -c \
    'mc alias set source http://'"$source_minio"':9000 rehearsal-minio rehearsal-minio-secret >/dev/null &&
     mc mb source/noctf >/dev/null &&
     mc cp --attr "Content-Type=text/plain;x-amz-meta-sha256=4397F0E6BFE6A92DFBB3E76C055B17FA557D8E4E26B5547CCE19CA44D2F458EE" /work/attachment source/noctf/attachments/fixture >/dev/null &&
     mc cp --attr "Content-Type=application/gzip;x-amz-meta-sha256=E5749CD3509DCBAC790D8DC8116DFB868B6AC81DA8803008E2D539755558F4CA" /work/patch source/noctf/fix-uploads/fixture >/dev/null'

docker run --rm --user "$(id -u):$(id -g)" --network "$network_name" --volume "$work_dir:/work" "$tool_image" backup \
    --postgres-host "$source_postgres" --postgres-database noctf --postgres-user noctf \
    --postgres-password-file /work/postgres-password --postgres-ssl-mode disable \
    --s3-endpoint "http://$source_minio:9000" --s3-bucket noctf \
    --s3-access-key-file /work/minio-access --s3-secret-key-file /work/minio-secret \
    --age-recipients-file /work/age-recipients.txt --minisign-secret-key-file /work/minisign.key \
    --secret-set-id rehearsal-v1 \
    --output /work/noctf-recovery.tar.gz.age

docker exec "$source_postgres" psql --username noctf --dbname noctf --set=ON_ERROR_STOP=1 \
    --command "INSERT INTO public.users VALUES ('00000000-0000-0000-0000-000000000099', 'after-snapshot');" >/dev/null

docker run --rm --user "$(id -u):$(id -g)" --network "$network_name" --volume "$work_dir:/work" "$tool_image" restore \
    --input /work/noctf-recovery.tar.gz.age --age-identity-file /work/age-identity.txt \
    --minisign-public-key-file /work/minisign.pub \
    --secret-set-id rehearsal-v1 \
    --postgres-host "$target_postgres" --postgres-database noctf --postgres-user noctf \
    --postgres-password-file /work/postgres-password --postgres-ssl-mode disable \
    --s3-endpoint "http://$target_minio:9000" --s3-bucket noctf \
    --s3-access-key-file /work/minio-access --s3-secret-key-file /work/minio-secret

restored_users="$(docker exec "$target_postgres" psql --username noctf --dbname noctf --tuples-only --no-align \
    --command 'SELECT count(*) FROM public.users;')"
[[ "$restored_users" == "1" ]] || { echo "Point-in-time PostgreSQL assertion failed" >&2; exit 1; }

if docker run --rm --user "$(id -u):$(id -g)" --network "$network_name" --volume "$work_dir:/work" "$tool_image" restore \
    --input /work/noctf-recovery.tar.gz.age --age-identity-file /work/age-identity.txt \
    --minisign-public-key-file /work/minisign.pub \
    --secret-set-id rehearsal-v1 \
    --postgres-host "$target_postgres" --postgres-database noctf --postgres-user noctf \
    --postgres-password-file /work/postgres-password --postgres-ssl-mode disable \
    --s3-endpoint "http://$target_minio:9000" --s3-bucket noctf \
    --s3-access-key-file /work/minio-access --s3-secret-key-file /work/minio-secret >/dev/null 2>&1; then
    echo "Non-empty restore target was not rejected" >&2
    exit 1
fi

cp -- "$work_dir/noctf-recovery.tar.gz.age" "$work_dir/tampered.tar.gz.age"
cp -- "$work_dir/noctf-recovery.tar.gz.age.minisig" "$work_dir/tampered.tar.gz.age.minisig"
printf '\000' | dd of="$work_dir/tampered.tar.gz.age" bs=1 seek=128 conv=notrunc status=none
if docker run --rm --user "$(id -u):$(id -g)" --volume "$work_dir:/work" --entrypoint minisign "$tool_image" \
    -V -q -p /work/minisign.pub -m /work/tampered.tar.gz.age \
    -x /work/tampered.tar.gz.age.minisig >/dev/null 2>&1; then
    echo "Tampered encrypted archive was not rejected" >&2
    exit 1
fi

echo "NoCTF recovery rehearsal passed."
