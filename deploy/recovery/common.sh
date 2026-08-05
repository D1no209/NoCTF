#!/usr/bin/env bash
set -euo pipefail

# shellcheck disable=SC2034 # Used by scripts that source this shared file.
readonly BACKUP_SCHEMA="noctf.disaster-recovery/1"
readonly -a protected_schemas=(public wolverine_api wolverine_worker wolverine_runner)

fail() {
    echo "ERROR: $*" >&2
    exit 1
}

require_command() {
    command -v "$1" >/dev/null 2>&1 || fail "Required command is unavailable: $1"
}

require_value() {
    local option_name="$1"
    local value="${2:-}"
    [[ -n "$value" ]] || fail "Missing required option: $option_name"
}

require_file() {
    local option_name="$1"
    local path="${2:-}"
    require_value "$option_name" "$path"
    [[ -f "$path" ]] || fail "$option_name does not name a readable file: $path"
}

read_single_line_secret() {
    local path="$1"
    local value
    value="$(cat -- "$path")"
    [[ -n "$value" ]] || fail "Secret file is empty: $path"
    [[ "$value" != *$'\n'* && "$value" != *$'\r'* ]] || fail "Secret file must contain exactly one line: $path"
    printf '%s' "$value"
}

escape_pgpass() {
    printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/:/\\:/g'
}

configure_postgres() {
    local work_dir="$1"
    local host="$2"
    local port="$3"
    local database="$4"
    local user="$5"
    local password_file="$6"
    local ssl_mode="$7"
    local password

    require_file "--postgres-password-file" "$password_file"
    password="$(read_single_line_secret "$password_file")"
    export PGHOST="$host"
    export PGPORT="$port"
    export PGDATABASE="$database"
    export PGUSER="$user"
    export PGSSLMODE="$ssl_mode"
    export PGPASSFILE="$work_dir/.pgpass"
    printf '%s:%s:%s:%s:%s\n' \
        "$(escape_pgpass "$host")" \
        "$(escape_pgpass "$port")" \
        "$(escape_pgpass "$database")" \
        "$(escape_pgpass "$user")" \
        "$(escape_pgpass "$password")" >"$PGPASSFILE"
    chmod 600 "$PGPASSFILE"
    unset password
}

psql_noctf() {
    psql -X --no-psqlrc --set=ON_ERROR_STOP=1 "$@"
}

assert_database_quiesced() {
    local other_clients
    other_clients="$(psql_noctf --tuples-only --no-align --command \
        "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND pid <> pg_backend_pid() AND backend_type = 'client backend';")"
    [[ "$other_clients" == "0" ]] || fail "PostgreSQL is not quiesced: $other_clients other client connection(s) remain. Stop API, Worker, Runner, migration jobs, and interactive clients."
}

assert_source_schemas() {
    local schema
    local exists
    for schema in "${protected_schemas[@]}"; do
        exists="$(psql_noctf --tuples-only --no-align --command \
            "SELECT count(*) FROM pg_namespace WHERE nspname = '$schema';")"
        [[ "$exists" == "1" ]] || fail "Required PostgreSQL schema is missing: $schema"
    done
}

capture_table_counts() {
    local output_path="$1"
    local listing_path
    local schema_name
    local table_name
    local qualified_name
    local row_count
    listing_path="${output_path}.tables"

    psql_noctf --tuples-only --no-align --field-separator=$'\t' --command \
        "SELECT n.nspname, c.relname, format('%I.%I', n.nspname, c.relname)
         FROM pg_class c
         JOIN pg_namespace n ON n.oid = c.relnamespace
         WHERE c.relkind IN ('r', 'p')
           AND n.nspname IN ('public', 'wolverine_api', 'wolverine_worker', 'wolverine_runner')
         ORDER BY n.nspname, c.relname;" >"$listing_path"

    : >"$output_path"
    while IFS=$'\t' read -r schema_name table_name qualified_name; do
        [[ -n "$qualified_name" ]] || continue
        row_count="$(psql_noctf --tuples-only --no-align --command "SELECT count(*) FROM $qualified_name;")"
        jq --compact-output --null-input \
            --arg schema "$schema_name" \
            --arg table "$table_name" \
            --argjson rows "$row_count" \
            '{schema: $schema, table: $table, rows: $rows}' >>"$output_path"
    done <"$listing_path"
    rm -f -- "$listing_path"
}

configure_mc_alias() {
    local config_dir="$1"
    local alias_name="$2"
    local endpoint="$3"
    local access_key_file="$4"
    local secret_key_file="$5"
    local access_key
    local secret_key

    require_file "--s3-access-key-file" "$access_key_file"
    require_file "--s3-secret-key-file" "$secret_key_file"
    access_key="$(read_single_line_secret "$access_key_file")"
    secret_key="$(read_single_line_secret "$secret_key_file")"
    export MC_CONFIG_DIR="$config_dir"
    mkdir -p -- "$MC_CONFIG_DIR"
    chmod 700 "$MC_CONFIG_DIR"
    mc alias set "$alias_name" "$endpoint" "$access_key" "$secret_key" --api S3v4 >/dev/null
    unset access_key secret_key
}

sha256_upper() {
    sha256sum -- "$1" | awk '{print toupper($1)}'
}

validate_object_field() {
    local label="$1"
    local value="$2"
    [[ "$value" != *$'\n'* && "$value" != *$'\r'* ]] || fail "$label contains a newline"
}

validate_object_key() {
    local object_key="$1"
    local component
    validate_object_field "Object key" "$object_key"
    [[ -n "$object_key" && "$object_key" != /* && "$object_key" != *\\* ]] \
        || fail "Object key is not a safe relative path"
    IFS='/' read -r -a components <<<"$object_key"
    for component in "${components[@]}"; do
        [[ -n "$component" && "$component" != "." && "$component" != ".." ]] \
            || fail "Object key contains an unsafe path component: $object_key"
    done
}
