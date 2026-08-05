#!/usr/bin/env bash
set -euo pipefail

# shellcheck source=common.sh
source /usr/local/lib/noctf-recovery/common.sh

bundle_dir=""
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
        --bundle-dir) bundle_dir="$2"; shift 2 ;;
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
        *) fail "Unknown verify option: $1" ;;
    esac
done

for command_name in jq mc psql sha256sum; do
    require_command "$command_name"
done
require_value "--bundle-dir" "$bundle_dir"
require_value "--postgres-host" "$postgres_host"
require_value "--postgres-database" "$postgres_database"
require_value "--postgres-user" "$postgres_user"
require_value "--s3-endpoint" "$s3_endpoint"
require_value "--s3-bucket" "$s3_bucket"
[[ -d "$bundle_dir" ]] || fail "Bundle directory does not exist: $bundle_dir"

work_dir="$(mktemp -d)"
trap 'rm -rf -- "$work_dir"' EXIT
configure_postgres "$work_dir" "$postgres_host" "$postgres_port" "$postgres_database" \
    "$postgres_user" "$postgres_password_file" "$postgres_ssl_mode"
configure_mc_alias "$work_dir/mc" target "$s3_endpoint" "$s3_access_key_file" "$s3_secret_key_file"

actual_counts="$work_dir/table-counts.ndjson"
capture_table_counts "$actual_counts"
cmp --silent "$bundle_dir/postgres/table-counts.ndjson" "$actual_counts" \
    || fail "Restored PostgreSQL table counts differ from the backup manifest"

target_listing="$work_dir/object-listing.ndjson"
: >"$target_listing"
mc ls --recursive --json "target/$s3_bucket" >"$target_listing"
if [[ -s "$target_listing" ]]; then
    jq --exit-status --slurp 'all(.status == "success" and .type == "file")' "$target_listing" >/dev/null \
        || fail "Restored object listing contained an error or unsupported entry"
fi
jq --compact-output --slurp 'map(select(.status == "success" and .type == "file") | .key) | sort' \
    "$target_listing" >"$work_dir/actual-object-keys.json"
jq --compact-output --slurp 'map(.key) | sort' "$bundle_dir/objects.ndjson" \
    >"$work_dir/expected-object-keys.json"
cmp --silent "$work_dir/expected-object-keys.json" "$work_dir/actual-object-keys.json" \
    || fail "Restored object keys differ from the backup manifest"

while IFS= read -r object_record; do
    object_key="$(jq --raw-output '.key' <<<"$object_record")"
    expected_sha="$(jq --raw-output '.sha256' <<<"$object_record")"
    expected_content_type="$(jq --raw-output '.contentType' <<<"$object_record")"
    validate_object_key "$object_key"
    destination="$work_dir/object"
    rm -f -- "$destination"
    mc cp --quiet "target/$s3_bucket/$object_key" "$destination" >/dev/null
    actual_sha="$(sha256_upper "$destination")"
    [[ "$actual_sha" == "$expected_sha" ]] || fail "Restored object SHA-256 mismatch: $object_key"
    stat_json="$(mc stat --json "target/$s3_bucket/$object_key")"
    metadata_sha="$(jq --raw-output '.metadata["X-Amz-Meta-Sha256"] // ""' <<<"$stat_json")"
    actual_content_type="$(jq --raw-output '.metadata["Content-Type"] // "application/octet-stream"' <<<"$stat_json")"
    [[ "${metadata_sha^^}" == "$expected_sha" ]] || fail "Restored object SHA-256 metadata mismatch: $object_key"
    [[ "$actual_content_type" == "$expected_content_type" ]] || fail "Restored object Content-Type mismatch: $object_key"
done <"$bundle_dir/objects.ndjson"

echo "Recovery verification passed: PostgreSQL rows, Wolverine schemas, object keys, content, and metadata match."
