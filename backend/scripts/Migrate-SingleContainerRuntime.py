"""One-off backfill from a protected pre-upgrade JSON export, after EF creates the new schema.

This deployment tool accepts only single-container data. It does not add runtime API compatibility.
Usage: python3 Migrate-SingleContainerRuntime.py exported.json backfill.sql
Execute the output with psql -v ON_ERROR_STOP=1 while the Host is stopped.
"""

import json
import os
import sys
import uuid
from decimal import Decimal
from pathlib import Path


def literal(value):
    if value is None:
        return "NULL"
    if isinstance(value, bool):
        return "TRUE" if value else "FALSE"
    if isinstance(value, (int, Decimal)):
        return str(value)
    return "'" + str(value).replace("'", "''") + "'"


def main(source_path, output_path):
    data = json.loads(Path(source_path).read_text(encoding="utf-8"))
    templates = data["templates"]
    if any(row["runtime_kind"] != 0 for row in templates):
        raise ValueError("Only single-container definitions can be backfilled.")
    if any(row["state"] in (0, 1, 2, 3) for row in data["instances"]):
        raise ValueError("Stop all existing Runtime instances before the cutover export.")
    statements = ["BEGIN;", "SET LOCAL statement_timeout = '60s';"]

    def insert(table, columns, values):
        statements.append(f"INSERT INTO {table} ({', '.join(columns)}) VALUES ({', '.join(map(literal, values))});")

    template_ids = {row["challenge_id"] for row in templates}
    for row in templates:
        if not row.get("image"):
            raise ValueError("Every exported single-container definition needs an image.")
        memory = row["limits_memory_bytes"] if row.get("has_explicit_limits") else 512 * 1024 * 1024
        nano_cpus = row["limits_nano_cpus"] if row.get("has_explicit_limits") else 500_000_000
        if memory <= 0 or memory % (1024 * 1024) or nano_cpus <= 0 or nano_cpus % 1_000_000:
            raise ValueError("Existing resources cannot be represented exactly as MiB and millicores.")
        insert("challenge_runtime_services", ["challenge_id", "name", "position", "image", "cpu_cores", "memory_mi_b", "flag_environment_variable_name"],
               [row["challenge_id"], "main", 0, row["image"], Decimal(nano_cpus) / Decimal(1_000_000_000), memory // (1024 * 1024), row.get("flag_environment_variable_name")])
    for row in data["commands"]:
        if row["challenge_id"] in template_ids:
            insert("challenge_runtime_service_commands", ["challenge_id", "service_name", "is_argument", "position", "value"],
                   [row["challenge_id"], "main", False, row["position"], row["value"]])
    for row in data["key_values"]:
        if row["challenge_id"] in template_ids and row["kind"] == 0:
            insert("challenge_runtime_service_environment", ["challenge_id", "service_name", "name", "value"],
                   [row["challenge_id"], "main", row["key"], row["value"]])
    for row in data["internal_ports"]:
        if row["challenge_id"] in template_ids:
            insert("challenge_runtime_service_internal_ports", ["challenge_id", "service_name", "port"],
                   [row["challenge_id"], "main", row["port"]])
    statements.extend([
        "UPDATE challenge_runtime_url_bindings SET service_name = 'main' WHERE challenge_id IN (SELECT challenge_id FROM challenge_runtime_services);",
        "UPDATE challenge_checker_definitions SET target_service_name = 'main' WHERE challenge_id IN (SELECT challenge_id FROM challenge_runtime_services);",
        "UPDATE challenge_definitions SET flag_injection_service_name = 'main' WHERE flag_injection_command IS NOT NULL AND challenge_id IN (SELECT challenge_id FROM challenge_runtime_services);",
    ])
    instances = {row["id"]: row for row in data["instances"]}
    service_ids = {}
    for row in data["receipts"]:
        runtime_id = row["runtime_instance_id"]
        service_id = str(uuid.uuid5(uuid.NAMESPACE_URL, "NoCTF/runtime-service/" + runtime_id + "/main"))
        service_ids[runtime_id] = service_id
        insert("runtime_receipt_services", ["id", "runtime_instance_id", "name", "resource_id", "status", "internal_host"],
               [service_id, runtime_id, "main", row["resource_id"], row["status"], row.get("internal_host")])
        created_at = instances[runtime_id]["created_at"]
        # EF can infer unrelated column renames; overwrite every new receipt field from the export.
        statements.append("UPDATE runtime_receipts SET " + ", ".join([
            "project_name = " + literal("noctf-rt-" + uuid.UUID(runtime_id).hex),
            "namespace = " + literal("" if row["provider"] == 0 else row.get("namespace") or "runtime"),
            "public_host = " + literal(row.get("public_host") or ""),
            "container_runtime_receipt_created_at = " + literal(created_at),
            "owned_network_id = NULL", "discovery_service_name = NULL",
        ]) + " WHERE runtime_instance_id = " + literal(runtime_id) + ";")
    for row in data["receipt_ports"]:
        service_id = service_ids.get(row["runtime_instance_id"])
        if service_id:
            insert("runtime_receipt_service_ports", ["id", "service_id", "container_port", "host_port"],
                   [row["id"], service_id, row["container_port"], row["host_port"]])
    statements.extend([
        "UPDATE runtime_published_ports SET service_name = 'main' WHERE runtime_instance_id IN (SELECT id FROM runtime_instances WHERE runtime_kind = 0);",
        "UPDATE runtime_capacity_allocations SET budget_cpu_millicores = budget_cpu_millicores / 1000000, limit_cpu_millicores = limit_cpu_millicores / 1000000;",
        "UPDATE challenge_runtime_templates SET has_explicit_limits = NULL, limits_memory_bytes = NULL, limits_pids_limit = NULL, limits_cpu_millicores = NULL WHERE runtime_kind = 0;",
        "COMMIT;",
    ])
    output = Path(output_path)
    output.write_text("\n".join(statements) + "\n", encoding="utf-8")
    os.chmod(output, 0o600)
    print(f"Prepared {len(templates)} service definitions, {len(service_ids)} service receipts, and {len(data['internal_ports'])} internal ports.")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit("Usage: Migrate-SingleContainerRuntime.py exported.json backfill.sql")
    main(sys.argv[1], sys.argv[2])
