#!/usr/bin/env python3
"""Upgrade existing NoCTF application services without replacing storage or network topology."""
import argparse
import fcntl
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import socket
import subprocess
import tempfile


APPLICATIONS = {"backend", "worker", "runner", "noctf"}
ENTRYPOINTS = {"NoCTF.Host.dll", "NoCTF.API.dll", "NoCTF.Worker.dll", "NoCTF.Runner.dll"}


def run(args, **kwargs):
    return subprocess.run(args, check=True, **kwargs)


def output(args):
    return subprocess.check_output(args, text=True).strip()


def docker_inspect(ids):
    return json.loads(output(["docker", "inspect", *ids])) if ids else []


def labels(container):
    return container["Config"].get("Labels") or {}


def environment(container):
    return dict(value.split("=", 1) for value in container["Config"].get("Env", []))


def application_containers(containers, root):
    result = []
    for container in containers:
        meta = labels(container)
        env_files = meta.get("com.docker.compose.project.environment_file", "").split(",")
        if str(root / ".env") not in env_files:
            continue
        if meta.get("com.docker.compose.service") not in APPLICATIONS:
            continue
        if not any(value in ENTRYPOINTS for value in container["Config"].get("Entrypoint") or []):
            continue
        result.append(container)
    if not result or len({labels(c)["com.docker.compose.project"] for c in result}) != 1:
        raise RuntimeError("Cannot identify exactly one installed NoCTF application project.")
    services = [labels(c)["com.docker.compose.service"] for c in result]
    if len(set(services)) != len(services):
        raise RuntimeError("Multiple containers for one service require an explicit rolling deployment.")
    return result


def validate_existing_config(config, containers):
    for container in containers:
        name = labels(container)["com.docker.compose.service"]
        desired = config["services"][name]
        actual_env = environment(container)
        changed = [key for key, value in desired.get("environment", {}).items()
                   if actual_env.get(key) != str(value)]
        if changed:
            raise RuntimeError("Unapplied environment changes in " + name + ": " + ", ".join(changed))
        mounts = {mount["Destination"]: mount for mount in container["Mounts"]}
        for desired_mount in desired.get("volumes", []):
            current = mounts.get(desired_mount["target"])
            kind = desired_mount["type"]
            source = desired_mount.get("source")
            if kind == "volume":
                source = config.get("volumes", {}).get(source, {}).get("name", source)
                match = current and current.get("Name") == source
            elif kind == "bind":
                match = current and os.path.realpath(current["Source"]) == os.path.realpath(source)
            else:
                raise RuntimeError("Unsupported persistent mount type: " + kind)
            if not match or current["Type"] != kind or current["RW"] == desired_mount.get("read_only", False):
                raise RuntimeError("Existing data mount differs for " + name + ":" + desired_mount["target"])
        actual_targets = {m["Destination"] for m in container["Mounts"] if m["Type"] in {"volume", "bind"}}
        if actual_targets != {m["target"] for m in desired.get("volumes", [])}:
            raise RuntimeError("The installed application has mounts absent from its Compose files.")


def schema_fingerprint(postgres):
    schema = output(["docker", "exec", postgres, "sh", "-ec",
                     'exec pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" --schema-only --no-owner --no-privileges'])
    normalized = "\n".join(line for line in schema.splitlines()
                           if line.strip() and not line.startswith(("--", "\\restrict", "\\unrestrict")))
    return hashlib.sha256(normalized.encode()).hexdigest()


def business_counts(postgres):
    sql = " UNION ALL ".join("SELECT '" + table + "',count(*) FROM " + table
                             for table in ("users", "competitions", "teams", "challenges", "files", "gameplay_facts"))
    result = output(["docker", "exec", postgres, "sh", "-ec",
                     'exec psql -X -At -F : -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "$1"', "sh", sql])
    return {line.split(":")[0]: int(line.split(":")[1]) for line in result.splitlines()}


def write_private(path, value):
    with path.open("x", encoding="utf-8") as stream:
        json.dump(value, stream, indent=2)


def deploy(args):
    hostname = socket.gethostname().lower()
    if hostname == "dino209" and not args.manual_production:
        raise RuntimeError("CI must never deploy to production.")
    if args.manual_production and hostname != "dino209":
        raise RuntimeError("Manual production deployment requires the verified production host.")
    if not re.fullmatch(r"[0-9a-f]{40}", args.commit) or not re.fullmatch(
            r"[A-Za-z0-9.-]+(?::[0-9]+)?/[A-Za-z0-9._/-]+@sha256:[a-f0-9]{64}", args.image):
        raise RuntimeError("An exact commit and immutable image digest are required.")
    os.umask(0o077)
    root = Path(args.config_root).resolve(strict=True)
    if root == Path("/") or not (root / ".env").is_file():
        raise RuntimeError("The installed configuration root is missing.")
    with (root / ".deploy.lock").open("a") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        _deploy_locked(args, root)


def _deploy_locked(args, root):
    all_containers = docker_inspect(output(["docker", "ps", "-q"]).split())
    apps = application_containers(all_containers, root)
    primary = next((c for c in apps if labels(c)["com.docker.compose.service"] in {"backend", "noctf"}), None)
    if primary is None:
        raise RuntimeError("The installed API service is missing.")
    meta = labels(primary)
    project = meta["com.docker.compose.project"]
    files = meta["com.docker.compose.project.config_files"].split(",")
    env_files = meta["com.docker.compose.project.environment_file"].split(",")
    if not all(Path(path).is_file() for path in files + env_files):
        raise RuntimeError("Installed Compose files are missing; refusing a new empty deployment.")
    compose = ["docker", "compose", "--project-name", project,
               "--project-directory", meta["com.docker.compose.project.working_dir"]]
    for path in env_files:
        compose += ["--env-file", path]
    for path in files:
        compose += ["--file", path]
    # Older CI overlays used process-local image variables. Restore only those image
    # references for interpolation; the retired migration service is never selected.
    for container in apps:
        service = labels(container)["com.docker.compose.service"]
        key = "NOCTF_PLATFORM_IMAGE" if service == "noctf" else "NOCTF_" + service.upper() + "_IMAGE"
        os.environ.setdefault(key, container["Config"]["Image"])
    os.environ.setdefault("NOCTF_MIGRATION_IMAGE", primary["Config"]["Image"])
    config = json.loads(output(compose + ["config", "--format", "json"]))
    validate_existing_config(config, apps)
    project_containers = [c for c in all_containers if labels(c).get("com.docker.compose.project") == project]
    postgres = next(c for c in project_containers if labels(c).get("com.docker.compose.service") == "postgres")["Id"]
    pending = output(["docker", "exec", postgres, "sh", "-ec",
                      'exec psql -X -At -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "SELECT count(*) FROM gameplay_facts WHERE state IN (0,1,2)"'])
    if pending != "0":
        raise RuntimeError("Gameplay work is still pending; wait for it to finish before upgrading.")
    networks = {}
    for container in apps:
        for name, network in container["NetworkSettings"]["Networks"].items():
            networks[name] = network["NetworkID"]
    for service in (config["services"][labels(c)["com.docker.compose.service"]] for c in apps):
        for name in service.get("networks", {}):
            if config["networks"][name]["name"] not in networks:
                raise RuntimeError("A configured network is not attached; refusing topology changes.")
    if args.check_only:
        print("Installed application config, data mounts and networks verified: " + ", ".join(sorted(labels(c)["com.docker.compose.service"] for c in apps)))
        return
    image = json.loads(output(["docker", "image", "inspect", args.image]))[0]
    if image["Config"].get("Labels", {}).get("org.opencontainers.image.revision") != args.commit:
        raise RuntimeError("The image revision does not match the requested commit.")
    if not image["Config"].get("Healthcheck"):
        raise RuntimeError("The published image must include its health check.")
    if shutil.disk_usage(root).free < 1024**3:
        raise RuntimeError("Insufficient free disk space. No global pruning is performed.")
    backups = root / "deployment-backups"
    backups.mkdir(exist_ok=True, mode=0o700)
    backup = Path(tempfile.mkdtemp(prefix=args.commit + "-", dir=backups))
    write_private(backup / "containers.json", apps)
    write_private(backup / "compose.json", config)
    for index, path in enumerate(env_files):
        shutil.copyfile(path, backup / ("environment-" + str(index)))
    before_counts = business_counts(postgres)
    with (backup / "database.dump").open("xb") as stream:
        run(["docker", "exec", postgres, "sh", "-ec", 'exec pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc'], stdout=stream)
    with (backup / "database.dump").open("rb") as stream:
        run(["docker", "exec", "-i", postgres, "pg_restore", "--list"], stdin=stream, stdout=subprocess.DEVNULL)
    before_schema = schema_fingerprint(postgres)
    release = Path(args.release_root).resolve() / args.commit
    release.mkdir(parents=True, exist_ok=True, mode=0o700)
    overlay = release / ("application-image-" + backup.name.rsplit("-", 1)[-1] + ".json")
    services = {labels(c)["com.docker.compose.service"]: {"image": args.image, "user": c["Config"]["User"]} for c in apps}
    write_private(overlay, {"services": services})
    updated = compose + ["--file", str(overlay)]
    names = sorted(services)
    print("Updating application services only: " + ", ".join(names), flush=True)
    try:
        run(updated + ["up", "-d", "--no-deps", "--no-build", "--pull", "never", "--wait", "--wait-timeout", "360", *names])
        running = docker_inspect(output(updated + ["ps", "-q", *names]).split())
        if len(running) != len(apps) or any(c["State"].get("Health", {}).get("Status") != "healthy" for c in running):
            raise RuntimeError("Application readiness did not converge.")
        if any(c["Config"]["Image"] != args.image for c in running):
            raise RuntimeError("An application service did not use the requested image.")
        validate_existing_config(config, running)
        after_counts = business_counts(postgres)
        if any(after_counts[k] < count for k, count in before_counts.items()):
            raise RuntimeError("Business data counts decreased; manual investigation required.")
        for name, identity in networks.items():
            if output(["docker", "network", "inspect", name, "--format", "{{.Id}}"] ) != identity:
                raise RuntimeError("Network identity changed: " + name)
        other_ids = [c["Id"] for c in all_containers if c["Id"] not in {app["Id"] for app in apps}]
        if any(not c["State"]["Running"] for c in docker_inspect(other_ids)):
            raise RuntimeError("An unrelated container is no longer running.")
        write_private(backup / "verified.json", {"commit": args.commit, "image": args.image,
                      "before": before_counts, "after": after_counts, "networks": networks})
        print("Deployed " + args.commit + "; readiness, data counts and network identities verified.", flush=True)
        print("Backup: " + str(backup), flush=True)
    except Exception:
        if schema_fingerprint(postgres) == before_schema:
            rollback = backup / "rollback.json"
            write_private(rollback, {"services": {labels(c)["com.docker.compose.service"]:
                          {"image": c["Config"]["Image"], "user": c["Config"]["User"]} for c in apps}})
            print("Schema unchanged; restoring previous application images.", flush=True)
            run(compose + ["--file", str(rollback), "up", "-d", "--no-deps", "--no-build", "--pull", "never", *names])
        else:
            print("Schema changed. No automatic database rollback. Backup: " + str(backup), flush=True)
        raise


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("commit")
    parser.add_argument("config_root")
    parser.add_argument("release_root")
    parser.add_argument("image")
    parser.add_argument("--manual-production", action="store_true")
    parser.add_argument("--check-only", action="store_true")
    deploy(parser.parse_args())
