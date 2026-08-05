#!/usr/bin/env bash
set -euo pipefail

# shellcheck source=common.sh
source /usr/local/lib/noctf-recovery/common.sh

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
age_recipients_file=""
minisign_secret_key_file=""
secret_set_id=""
output_path=""

while (($#)); do
    case "$1" in
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
        --age-recipients-file) age_recipients_file="$2"; shift 2 ;;
        --minisign-secret-key-file) minisign_secret_key_file="$2"; shift 2 ;;
        --secret-set-id) secret_set_id="$2"; shift 2 ;;
        --output) output_path="$2"; shift 2 ;;
        *) fail "Unknown backup option: $1" ;;
    esac
done

for command_name in age jq mc minisign pg_dump psql sha256sum tar; do
    require_command "$command_name"
done
require_value "--postgres-host" "$postgres_host"
require_value "--postgres-database" "$postgres_database"
require_value "--postgres-user" "$postgres_user"
require_value "--s3-endpoint" "$s3_endpoint"
require_value "--s3-bucket" "$s3_bucket"
require_file "--age-recipients-file" "$age_recipients_file"
require_file "--minisign-secret-key-file" "$minisign_secret_key_file"
require_value "--secret-set-id" "$secret_set_id"
require_value "--output" "$output_path"
[[ ! -e "$output_path" && ! -e "${output_path}.minisig" ]] || fail "Output or signature already exists: $output_path"
mkdir -p -- "$(dirname -- "$output_path")"

work_dir="$(mktemp -d)"
partial_output="${output_path}.partial"
partial_signature="${output_path}.minisig.partial"
backup_succeeded=false
cleanup() {
    rm -rf -- "$work_dir"
    rm -f -- "$partial_output"
    rm -f -- "$partial_signature"
    if [[ "$backup_succeeded" != true ]]; then
        rm -f -- "$output_path" "${output_path}.minisig"
    fi
}
trap cleanup EXIT

bundle_dir="$work_dir/noctf-recovery-v1"
mkdir -p -- "$bundle_dir/postgres" "$bundle_dir/objects"
configure_postgres "$work_dir" "$postgres_host" "$postgres_port" "$postgres_database" \
    "$postgres_user" "$postgres_password_file" "$postgres_ssl_mode"
configure_mc_alias "$work_dir/mc" source "$s3_endpoint" "$s3_access_key_file" "$s3_secret_key_file"

assert_database_quiesced
assert_source_schemas
mc stat "source/$s3_bucket" >/dev/null

pg_dump --format=custom --no-owner --no-privileges --serializable-deferrable \
    --file="$bundle_dir/postgres/database.dump"
capture_table_counts "$bundle_dir/postgres/table-counts.ndjson"

object_listing="$work_dir/object-listing.ndjson"
object_metadata_unsorted="$work_dir/object-metadata.unsorted.ndjson"
: >"$object_listing"
: >"$object_metadata_unsorted"
mc ls --recursive --json "source/$s3_bucket" >"$object_listing"
if [[ -s "$object_listing" ]]; then
    jq --exit-status --slurp 'all(.status == "success" and .type == "file")' "$object_listing" >/dev/null \
        || fail "Object listing contained an error or unsupported entry"
fi

while IFS= read -r encoded_key; do
    [[ -n "$encoded_key" ]] || continue
    object_key="$(printf '%s' "$encoded_key" | base64 --decode)"
    validate_object_key "$object_key"
    destination="$bundle_dir/objects/$object_key"
    mkdir -p -- "$(dirname -- "$destination")"
    mc cp --quiet "source/$s3_bucket/$object_key" "$destination" >/dev/null
    stat_json="$(mc stat --json "source/$s3_bucket/$object_key")"
    content_type="$(jq --raw-output '.metadata["Content-Type"] // "application/octet-stream"' <<<"$stat_json")"
    stored_sha="$(jq --raw-output '.metadata["X-Amz-Meta-Sha256"] // ""' <<<"$stat_json")"
    actual_sha="$(sha256_upper "$destination")"
    validate_object_field "Content-Type" "$content_type"
    [[ "$content_type" != *';'* ]] || fail "Content-Type contains an unsupported semicolon for object: $object_key"
    if [[ -n "$stored_sha" && "${stored_sha^^}" != "$actual_sha" ]]; then
        fail "Stored SHA-256 metadata does not match object content: $object_key"
    fi
    object_size="$(wc -c <"$destination" | tr -d ' ')"
    jq --compact-output --null-input \
        --arg key "$object_key" \
        --arg contentType "$content_type" \
        --arg sha256 "$actual_sha" \
        --argjson size "$object_size" \
        '{key: $key, size: $size, sha256: $sha256, contentType: $contentType}' \
        >>"$object_metadata_unsorted"
done < <(jq --raw-output 'select(.status == "success" and .type == "file") | .key | @base64' "$object_listing")

jq --compact-output --slurp 'sort_by(.key)[]' "$object_metadata_unsorted" \
    >"$bundle_dir/objects.ndjson"

assert_database_quiesced

created_at="$(date --utc +'%Y-%m-%dT%H:%M:%SZ')"
database_version="$(psql_noctf --tuples-only --no-align --command 'SHOW server_version;')"
database_dump_sha="$(sha256_upper "$bundle_dir/postgres/database.dump")"
table_count="$(wc -l <"$bundle_dir/postgres/table-counts.ndjson" | tr -d ' ')"
object_count="$(wc -l <"$bundle_dir/objects.ndjson" | tr -d ' ')"
object_bytes="$(jq --slurp 'map(.size) | add // 0' "$bundle_dir/objects.ndjson")"
objects_index_sha="$(sha256_upper "$bundle_dir/objects.ndjson")"

jq --indent 2 --null-input \
    --arg schemaVersion "$BACKUP_SCHEMA" \
    --arg createdAt "$created_at" \
    --arg secretSetId "$secret_set_id" \
    --arg database "$postgres_database" \
    --arg serverVersion "$database_version" \
    --arg dumpSha256 "$database_dump_sha" \
    --arg bucket "$s3_bucket" \
    --arg objectsIndexSha256 "$objects_index_sha" \
    --argjson tableCount "$table_count" \
    --argjson objectCount "$object_count" \
    --argjson objectBytes "$object_bytes" \
    '{
        schemaVersion: $schemaVersion,
        createdAt: $createdAt,
        consistency: "application-quiesced",
        secretSetId: $secretSetId,
        postgres: {
            database: $database,
            serverVersion: $serverVersion,
            schemas: ["public", "wolverine_api", "wolverine_worker", "wolverine_runner"],
            tableCount: $tableCount,
            dumpSha256: $dumpSha256
        },
        objectStorage: {
            bucket: $bucket,
            objectCount: $objectCount,
            objectBytes: $objectBytes,
            objectsIndexSha256: $objectsIndexSha256
        },
        wolverine: {
            restoreMode: "preserve-all-durable-state",
            schemas: ["wolverine_api", "wolverine_worker", "wolverine_runner"]
        },
        redis: {included: false, recovery: "rebuild-from-postgresql-and-live-runners"},
        secrets: {included: false, externalSetRequired: true}
    }' >"$bundle_dir/manifest.json"

(
    cd "$bundle_dir"
    checksum_path="$(mktemp)"
    find . -type f ! -name checksums.sha256 -print0 \
        | LC_ALL=C sort --zero-terminated \
        | xargs -0 sha256sum >"$checksum_path"
    mv -- "$checksum_path" checksums.sha256
)

archive_path="$work_dir/noctf-recovery-v1.tar.gz"
tar --create --gzip --file "$archive_path" --directory "$work_dir" noctf-recovery-v1
age --encrypt --recipients-file "$age_recipients_file" --output "$partial_output" "$archive_path"
chmod 600 "$partial_output"
minisign -S -s "$minisign_secret_key_file" -m "$partial_output" \
    -x "$partial_signature" -t "NoCTF recovery artifact $created_at"
mv -- "$partial_output" "$output_path"
mv -- "$partial_signature" "${output_path}.minisig"
backup_succeeded=true
echo "Backup created: $output_path"
