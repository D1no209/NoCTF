"""Timed synthetic all-role soak. Short pilots never satisfy the 24-hour gate."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import pathlib
import shutil
import subprocess
import sys
import time

import psutil

from compare import ADMIN_PASSWORD, HERE, Run, command, private_directory, write

DIND_IMAGE = "docker@sha256:ea9d20492ca1caaaba78e68453433895d256173c79281756e88b745647fcbcfd"
NGINX_IMAGE = "nginx@sha256:df221db836e1754089190208cee7eeda94f233197056426eda74a43ab1abeac2"


class Soak(Run):
    def __init__(self, args):
        super().__init__(args, args.flavor, 1)
        self.events = []
        self.volumes = []
        self.sample_limit = 300  # Full history streams to JSONL; keep controller memory bounded.

    def event(self, kind, **details):
        event = {"utc": time.time(), "kind": kind, **details}
        self.events.append(event)
        with (self.path / "events.jsonl").open("a", encoding="utf-8") as stream:
            stream.write(json.dumps(event) + "\n")
        print(json.dumps(event), flush=True)

    def prepare_runner(self):
        self.env.update({"Hosting__Roles__2": "Runner", "Runner__Id": self.id,
            "Runner__Pool": self.id, "Runtime__RunnerPool": self.id,
            "Runner__Heartbeat__IntervalSeconds": "5", "Runner__Heartbeat__TtlSeconds": "20",
            "Runner__Admission__FreshnessSeconds": "45"})
        if self.args.platform == "windows":
            config = json.loads(self.args.runner_environment.read_text())
            if config.get("synthetic") is not True:
                raise ValueError("Runner configuration must be synthetic")
            # Only provider settings enter the new owned dependency environment.
            self.env.update({key: str(value) for key, value in config["environment"].items()
                if key.startswith("Runtime__Kubernetes__") or key == "KUBECONFIG"})
            self.env.update({"Runtime__Provider": "Kubernetes", "Runner__Provider": "Kubernetes"})
            return
        volume = self.id + "-socket"
        command(["docker", "volume", "create", "--label", "io.noctf.acceptance=" + self.id, volume])
        self.volumes.append(volume)
        daemon = self.docker("daemon", DIND_IMAGE, options=["--privileged", "-e", "DOCKER_TLS_CERTDIR=",
            "-v", volume + ":/shared", "-v", "/proc:/host/proc:ro",
            "-v", "/sys/fs/cgroup:/host/sys/fs/cgroup:ro", "-v", "/etc/machine-id:/host/etc/machine-id:ro"],
            arguments=["dockerd", "--host=unix:///shared/docker.sock"])
        self.daemon = daemon
        for _ in range(60):
            try:
                self.inner("info")
                break
            except subprocess.CalledProcessError:
                time.sleep(1)
        else:
            raise RuntimeError("Isolated Docker daemon did not start")
        self.inner("pull", NGINX_IMAGE)
        self.inner("network", "create", "challenges")
        self.inner("network", "create", "--internal", "callbacks")
        # Dedicated network peer satisfies gateway membership for the synthetic
        # template lifecycle. Public reachability is a separate integration check.
        self.inner("run", "-d", "--name", "acceptance-network-peer", "--label",
            "io.noctf.acceptance-proxy=true", NGINX_IMAGE)
        self.env.update({"Runner__Provider": "Docker", "Runtime__Provider": "Docker",
            "Runtime__Docker__Endpoint": "unix:///shared/docker.sock", "Runtime__Docker__Network": "challenges",
            "Runtime__Docker__CallbackNetwork": "callbacks", "Runtime__Docker__PublicHost": "127.0.0.1",
            "Runtime__Docker__ProxyContainer": "acceptance-network-peer",
            "Runtime__Docker__ProxyContainerLabelKey": "io.noctf.acceptance-proxy",
            "Runtime__Docker__ProxyContainerLabelValue": "true"})
        self.host_options = ["-v", volume + ":/shared", "-v", "/proc:/host/proc:ro",
            "-v", "/sys/fs/cgroup:/host/sys/fs/cgroup:ro", "-v", "/etc/machine-id:/host/etc/machine-id:ro"]
        self.event("isolated-daemon", container=daemon, image=DIND_IMAGE,
                   limitation="Lifecycle only; nested public ports are not externally published")

    def inner(self, *arguments):
        return command(["docker", "exec", self.daemon, "docker", "--host", "unix:///shared/docker.sock", *arguments])

    def driver(self, kind, cycle):
        output = self.path / f"cycle-{cycle:05d}-{kind}.json"
        arguments = ["--command", kind, "--fixture", self.path / "fixture.json", "--output", output]
        if kind == "runtime":
            arguments += ["--runtime-image", self.args.runtime_image]
        else:
            arguments += ["--seconds", str(self.args.seconds)]
        executable = shutil.which("node")
        if executable is None:
            raise RuntimeError("Node executable is unavailable")
        for attempt in range(1, 4):
            prefix = self.path / f"cycle-{cycle:05d}-{kind}-attempt-{attempt}"
            started_receipt = prefix.with_suffix(".started.json")
            log_path = prefix.with_suffix(".log")
            started = time.time()
            timed_out = False
            with log_path.open("w", encoding="utf-8") as log:
                process = subprocess.Popen([executable, str(HERE / "bootstrap.mjs"), str(started_receipt),
                    *[str(item) for item in arguments]], stdout=log, stderr=subprocess.STDOUT)
                try:
                    process.wait(timeout=1800)
                except subprocess.TimeoutExpired:
                    timed_out = True
                    process.kill()
                    process.wait(timeout=30)
            elapsed = time.time() - started
            entered = started_receipt.exists()
            exists = output.exists()
            write(prefix.with_suffix(".process.json"), {"node": executable, "pid": process.pid,
                "startedAt": started, "elapsedSeconds": elapsed, "exitCode": process.returncode,
                "timedOut": timed_out, "enteredWorkload": entered, "reportExists": exists,
                "logBytes": log_path.stat().st_size})
            if exists and not timed_out:
                self.event("workload", cycle=cycle, scenario=kind, exitCode=process.returncode,
                           report=output.name, attempt=attempt)
                return process.returncode
            if not entered and not timed_out and elapsed < 10 and process.returncode != 0 and attempt < 3:
                self.event("driver-start-retry", cycle=cycle, scenario=kind, attempt=attempt,
                           exitCode=process.returncode, reason="No bootstrap receipt; business code was not entered")
                time.sleep(attempt)
                continue
            raise RuntimeError(f"Node driver exited without a complete report: {prefix.name}; "
                               f"enteredWorkload={entered}, exitCode={process.returncode}")

    def restart_host(self, force):
        self.event("host-restart-start", forced=force or self.args.platform == "windows")
        if self.process:
            self.process.terminate()
            self.process.wait(timeout=30)
            # Give the old Runner lease time to expire; never clear NATS state.
            time.sleep(35)
            directory = getattr(self.args, self.flavor.lower()).resolve()
            inherited = {key: value for key, value in os.environ.items()
                if "__" not in key and not key.startswith(("COMPlus_", "DOTNET_GC", "DOTNET_PROCESSOR", "ASPNETCORE_"))}
            self.process = subprocess.Popen([str(directory / "NoCTF.Host.exe")], cwd=directory,
                env=inherited | self.env, stdout=self.host_log, stderr=subprocess.STDOUT)
            self.observed = psutil.Process(self.process.pid)
            if self.args.cpus:
                self.observed.cpu_affinity(self.observed.cpu_affinity()[:self.args.cpus])
        else:
            command(["docker", "kill" if force else "stop", self.host_container])
            time.sleep(35)
            command(["docker", "start", self.host_container])
        self.wait_ready()
        if self.process and (self.path / "ownership.json").exists():
            ownership = json.loads((self.path / "ownership.json").read_text())
            ownership["pid"] = self.process.pid
            write(self.path / "ownership.json", ownership)
        self.event("host-restart-ready", pid=self.process.pid if self.process else None)

    def wait_ready(self):
        for _ in range(120):
            try:
                with self.http.open(self.url + "/health/ready", timeout=2) as response:
                    if response.status == 200:
                        return
            except OSError:
                pass
            time.sleep(1)
        raise RuntimeError("Recovery readiness timed out")

    def rollback(self):
        self.event("rollback-start", target="Official", preserves="database, Redis, NATS, storage, Runner identity")
        if self.process:
            identity = json.loads(command([self.args.official.resolve() / "NoCTF.Host.exe", "--runtime-info"]))
            self.flavor = "Official"
            self.restart_host(force=True)
        else:
            identity = json.loads(command(["docker", "run", "--rm", self.args.official_image, "--runtime-info"]))
            command(["docker", "stop", self.host_container])
            old_logs = subprocess.run(["docker", "logs", self.host_container], capture_output=True, text=True, check=True)
            (self.path / "host-satori-before-rollback.log").write_text(old_logs.stdout + old_logs.stderr, encoding="utf-8")
            time.sleep(35)
            port = self.url.rsplit(":", 1)[1]
            options = ["-p", f"127.0.0.1:{port}:8080", "-v", f"{self.path / 'diagnostic-ipc'}:/tmp", *self.host_options]
            options += [arg for key, value in self.env.items() for arg in ("-e", key + "=" + value)]
            if self.args.cpus:
                options += ["--cpus", str(self.args.cpus)]
            if self.args.memory:
                options += ["--memory", self.args.memory]
            self.host_container = self.docker("host-official-rollback", self.args.official_image, options=options)
            self.flavor = "Official"
            self.wait_ready()
        if identity["actualFlavor"] != "Official" or identity["expectedFlavor"] != "Official":
            raise RuntimeError("Rollback GC identity mismatch")
        write(self.path / "rollback-identity.json", identity)
        if self.driver("verify", 999999) or self.driver("runtime", 999999):
            raise RuntimeError("Rollback persistence or Runtime lifecycle verification failed")
        self.event("rollback-verified", actualFlavor=identity["actualFlavor"])

    def execute(self):
        write(self.path / "harness.json", {"applicationFlavor": self.flavor,
              "scripts": {name: hashlib.sha256((HERE / name).read_bytes()).hexdigest()
                          for name in ["soak.py", "compare.py", "bootstrap.mjs", "workload.mjs"]}})
        self.dependencies()
        self.dependency_ids = list(self.containers)
        self.prepare_runner()
        self.start()
        if self.args.hours >= 24 and self.args.platform == "windows":
            manifest = json.loads((self.path / "publication.json").read_text())
            if manifest.get("applicationDirty") is not False:
                raise ValueError("A qualifying duration run requires a clean committed publication")
        write(self.path / "ownership.json", {"id": self.id, "containers": self.containers, "volumes": self.volumes,
              "pid": self.process.pid if self.process else None, "url": self.url, "synthetic": True})
        command(["node", HERE / "workload.mjs", "--command", "seed", "--fixture", self.path / "fixture.json",
                 "--url", self.url, "--login", "acceptance_admin", "--password", ADMIN_PASSWORD, "--participants", "16"])
        time.sleep(20)
        started = time.monotonic()
        started_utc = time.time()
        write(self.path / "progress.json", {"status": "running", "startedAt": started_utc,
              "elapsedSeconds": 0, "requiredSeconds": 86400, "releaseApproval": False})
        cycle = 0
        next_restart = self.args.restart_seconds
        next_dependency = self.args.dependency_seconds
        failures = 0
        while time.monotonic() - started < self.args.hours * 3600:
            alive = self.process.poll() is None if self.process else command([
                "docker", "inspect", "--format", "{{.State.Running}}", self.host_container]) == "true"
            if not alive:
                raise RuntimeError("Host exited outside an intentional restart")
            cycle += 1
            failures += self.driver("run", cycle) != 0
            workload = json.loads((self.path / f"cycle-{cycle:05d}-run.json").read_text())
            if len(workload["phases"]) != 5 or any("InsufficientQuestions" in error["message"] for error in workload["errors"]):
                raise RuntimeError("Incomplete synthetic workload or exhausted theory inventory; duration is not qualified")
            failures += self.driver("runtime", cycle) != 0
            elapsed = time.monotonic() - started
            if elapsed >= next_dependency:
                for container in self.dependency_ids:
                    owner = command(["docker", "inspect", "--format", '{{index .Config.Labels "io.noctf.acceptance"}}', container])
                    if owner != self.id:
                        raise RuntimeError("Refusing dependency fault injection into unowned container")
                    command(["docker", "restart", container])
                    self.event("dependency-restart", container=container)
                self.wait_ready()
                if self.driver("verify", cycle + 200000):
                    raise RuntimeError("Score invariant failed after dependency recovery")
                next_dependency += self.args.dependency_seconds
            if elapsed >= next_restart:
                self.restart_host(force=cycle % 2 == 0)
                failures += self.driver("runtime", cycle + 100000) != 0
                if self.driver("verify", cycle + 100000):
                    raise RuntimeError("Score invariant failed after Host restart")
                next_restart += self.args.restart_seconds
            write(self.path / "process-samples.json", list(self.samples))
            write(self.path / "progress.json", {"status": "running", "startedAt": started_utc,
                  "elapsedSeconds": elapsed, "cycles": cycle, "workloadsWithErrors": failures,
                  "requiredSeconds": 86400, "releaseApproval": False})
        elapsed = time.monotonic() - started
        if self.flavor == "Satori" and (self.args.official if self.process else self.args.official_image):
            self.rollback()
        write(self.path / "result.json", {"status": "completed-with-errors" if failures else "completed",
              "elapsedSeconds": elapsed, "durationGateMet": elapsed >= 86400,
              "cycles": cycle, "workloadsWithErrors": failures, "releaseApproval": False})
        return 1 if failures else 0

    def cleanup(self, retain=False):
        self.close(retain=retain)
        if retain:
            write(self.path / "retained-resources.json", {"id": self.id, "containers": self.containers,
                  "volumes": self.volumes, "reason": "Failed acceptance; stopped resources retained for investigation"})
            return
        for volume in self.volumes:
            owner = command(["docker", "volume", "inspect", "--format", '{{index .Labels "io.noctf.acceptance"}}', volume])
            if owner != self.id:
                raise RuntimeError("Refusing cleanup of unowned volume")
            command(["docker", "volume", "rm", volume])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--platform", choices=["windows", "linux"], required=True)
    parser.add_argument("--flavor", choices=["Official", "Satori"], default="Satori")
    parser.add_argument("--official", type=pathlib.Path)
    parser.add_argument("--satori", type=pathlib.Path)
    parser.add_argument("--official-image")
    parser.add_argument("--satori-image")
    parser.add_argument("--runner-environment", type=pathlib.Path)
    parser.add_argument("--runtime-image", default=NGINX_IMAGE)
    parser.add_argument("--output", type=pathlib.Path, required=True)
    parser.add_argument("--hours", type=float, default=24)
    parser.add_argument("--seconds", type=int, default=30)
    parser.add_argument("--restart-seconds", type=int, default=21600)
    parser.add_argument("--dependency-seconds", type=int, default=14400)
    parser.add_argument("--cpus", type=int, default=4)
    parser.add_argument("--memory", default="2g")
    args = parser.parse_args()
    args.counters = None  # Continuous OS samples; counter qualification is separate.
    if args.hours <= 0 or args.restart_seconds < 60 or args.dependency_seconds < 60:
        parser.error("Positive duration and restart intervals of at least 60 seconds required")
    if args.platform == "windows" and (not args.runner_environment or not getattr(args, args.flavor.lower())):
        parser.error("Windows requires a native publication and isolated Runner environment")
    if args.platform == "linux" and not getattr(args, args.flavor.lower() + "_image"):
        parser.error("Linux requires a Host image")
    args.output = args.output.resolve()
    private_directory(args.output)
    run = Soak(args)
    failed = False
    try:
        return run.execute()
    except Exception as error:
        failed = True
        run.event("fatal", error=str(error))
        write(run.path / "result.json", {"status": "failed", "failure": str(error), "durationGateMet": False})
        return 1
    finally:
        run.cleanup(retain=failed)


if __name__ == "__main__":
    sys.exit(main())
