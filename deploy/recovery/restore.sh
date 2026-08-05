#!/usr/bin/env bash
set -euo pipefail

# shellcheck source=common.sh
source /usr/local/lib/noctf-recovery/common.sh

input_path=""
age_identity_file=""
minisign_public_key_file=""
secret_set_id=""
postgres_host=""
postgres_port="5432"
postgres_database=""
postgres_user=""
postgres_password_file=""
postgres_ssl_mode="require"
s3_endpoint=""
s3_bucket=""
s3_access_key_file=""
s3_secret_key_file=""

while (($#)); do
    case "$1" in
        --input) input_path="$2"; shift 2 ;;
        --age-identity-file) age_identity_file="$2"; shift 2 ;;
        --minisign-public-key-file) minisign_public_key_file="$2"; shift 2 ;;
        --secret-set-id) secret_set_id="$2"; shift 2 ;;
        --postgres-host) postgres_host="$2"; shift 2 ;;
        --postgres-port) postgres_port="$2"; shift 2 ;;
        --postgres-database) postgres_database="$2"; shift 2 ;;
        --postgres-user) postgres_user="$2"; shift 2 ;;
        --postgres-password-file) postgres_password_file="$2"; shift 2 ;;
        --postgres-ssl-mode) postgres_ssl_mode="$2"; shift 2 ;;
        --s3-endpoint) s3_endpoint="$2"; shift 2 ;;
        --s3-bucket) s3_bucket="$2"; shift 2 ;;
        --s3-access-key-file) s3_access_key_file="$2"; shift 2 ;;
        --s3-secret-key-file) s3_secret_key_file="$2"; shift 2 ;;
        *) fail "Unknown restore option: $1" ;;
    esac
done

for command_name in age jq mc minisign pg_restore psql sha256sum tar; do
    require_command "$command_name"
done
require_file "--input" "$input_path"
require_file "--age-identity-file" "$age_identity_file"
require_file "--minisign-public-key-file" "$minisign_public_key_file"
require_file "backup signature" "${input_path}.minisig"
require_value "--secret-set-id" "$secret_set_id"
require_value "--postgres-host" "$postgres_host"
require_value "--postgres-database" "$postgres_database"
require_value "--postgres-user" "$postgres_user"
require_value "--s3-endpoint" "$s3_endpoint"
require_value "--s3-bucket" "$s3_bucket"

work_dir="$(mktemp -d)"
trap 'rm -rf -- "$work_dir"' EXIT
archive_path="$work_dir/archive.tar.gz"
minisign -V -q -p "$minisign_public_key_file" -m "$input_path" -x "${input_path}.minisig"
age --decrypt --identity "$age_identity_file" --output "$archive_path" "$input_path"

while IFS= read -r archive_entry; do
    [[ "$archive_entry" == noctf-recovery-v1 || "$archive_entry" == noctf-recovery-v1/* ]] \
        || fail "Archive contains an unexpected top-level path: $archive_entry"
    [[ "$archive_entry" != /* && "/$archive_entry/" != *'/../'* && "$archive_entry" != *\\* ]] \
        || fail "Archive contains an unsafe path: $archive_entry"
done < <(tar --list --gzip --file "$archive_path")
tar --list --verbose --gzip --file "$archive_path" \
    | awk 'substr($1, 1, 1) !~ /^[-d]$/ { exit 1 }' \
    || fail "Archive contains a non-file entry"
tar --extract --gzip --file "$archive_path" --directory "$work_dir" \
    --no-same-owner --no-same-permissions

bundle_dir="$work_dir/noctf-recovery-v1"
[[ -f "$bundle_dir/manifest.json" && -f "$bundle_dir/checksums.sha256" ]] \
    || fail "Archive is missing its manifest or checksums"
(
    cd "$bundle_dir"
    sha256sum --check --strict checksums.sha256 >/dev/null
)

manifest_schema="$(jq --raw-output '.schemaVersion' "$bundle_dir/manifest.json")"
manifest_secret_set="$(jq --raw-output '.secretSetId' "$bundle_dir/manifest.json")"
manifest_bucket="$(jq --raw-output '.objectStorage.bucket' "$bundle_dir/manifest.json")"
manifest_database="$(jq --raw-output '.postgres.database' "$bundle_dir/manifest.json")"
[[ "$manifest_schema" == "$BACKUP_SCHEMA" ]] || fail "Unsupported backup schema: $manifest_schema"
[[ "$manifest_secret_set" == "$secret_set_id" ]] || fail "External Secret set does not match the backup manifest"
[[ "$manifest_bucket" == "$s3_bucket" ]] || fail "Target bucket must match manifest bucket: $manifest_bucket"
[[ "$manifest_database" == "$postgres_database" ]] || fail "Target database must match manifest database: $manifest_database"

configure_postgres "$work_dir" "$postgres_host" "$postgres_port" "$postgres_database" \
    "$postgres_user" "$postgres_password_file" "$postgres_ssl_mode"
configure_mc_alias "$work_dir/mc" target "$s3_endpoint" "$s3_access_key_file" "$s3_secret_key_file"
assert_database_quiesced

existing_relations="$(psql_noctf --tuples-only --no-align --command \
    "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
     WHERE c.relkind IN ('r', 'p')
       AND n.nspname IN ('public', 'wolverine_api', 'wolverine_worker', 'wolverine_runner');")"
existing_wolverine_schemas="$(psql_noctf --tuples-only --no-align --command \
    "SELECT count(*) FROM pg_namespace WHERE nspname IN ('wolverine_api', 'wolverine_worker', 'wolverine_runner');")"
[[ "$existing_relations" == "0" && "$existing_wolverine_schemas" == "0" ]] \
    || fail "Target PostgreSQL database is not empty"

mc mb --ignore-existing "target/$s3_bucket" >/dev/null
target_object_count="$(mc ls --recursive --json "target/$s3_bucket" | jq --slurp 'map(select(.status == "success" and .type == "file")) | length')"
[[ "$target_object_count" == "0" ]] || fail "Target object bucket is not empty"

pg_restore --exit-on-error --single-transaction --no-owner --no-privileges \
    --dbname="$postgres_database" "$bundle_dir/postgres/database.dump"

while IFS= read -r object_record; do
    object_key="$(jq --raw-output '.key' <<<"$object_record")"
    object_sha="$(jq --raw-output '.sha256' <<<"$object_record")"
    content_type="$(jq --raw-output '.contentType' <<<"$object_record")"
    validate_object_key "$object_key"
    validate_object_field "Content-Type" "$content_type"
    [[ "$content_type" != *';'* ]] || fail "Content-Type contains an unsupported semicolon for object: $object_key"
    source_path="$bundle_dir/objects/$object_key"
    [[ -f "$source_path" ]] || fail "Object file is missing from bundle: $object_key"
    mc cp --quiet \
        --attr "Content-Type=$content_type;x-amz-meta-sha256=$object_sha" \
        "$source_path" "target/$s3_bucket/$object_key" >/dev/null
done <"$bundle_dir/objects.ndjson"

/usr/local/lib/noctf-recovery/verify.sh \
    --bundle-dir "$bundle_dir" \
    --postgres-host "$postgres_host" \
    --postgres-port "$postgres_port" \
    --postgres-database "$postgres_database" \
    --postgres-user "$postgres_user" \
    --postgres-password-file "$postgres_password_file" \
    --postgres-ssl-mode "$postgres_ssl_mode" \
    --s3-endpoint "$s3_endpoint" \
    --s3-bucket "$s3_bucket" \
    --s3-access-key-file "$s3_access_key_file" \
    --s3-secret-key-file "$s3_secret_key_file"

assert_database_quiesced
echo "Restore completed into the isolated target. Keep API, Worker, and Runner stopped until operator acceptance is complete."
