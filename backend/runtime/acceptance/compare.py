"""Local synthetic macrobenchmark. Each run owns fresh PostgreSQL, Redis and NATS.

Requires Python 3.12+, psutil, Node 24 and Docker. This controller never deploys to
production, switches GC defaults, or interprets a short workload as a 24h soak.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import pathlib
import platform
import socket
import statistics
import sys
import subprocess
import threading
import time
import urllib.error
import urllib.request
import uuid

import psutil

HERE = pathlib.Path(__file__).resolve().parent
PASSWORD = "synthetic-runtime-acceptance-only"
ADMIN_PASSWORD = "Synthetic-Acceptance-Password-2026"
IMAGES = {
    "postgres": "sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193",
    "redis": "sha256:e7723ff73d963f5cc6d9c4643ea3d989527a402a319239054e9472a7fb9219a2",
    "nats": "sha256:2d5fce3229ae5741f4ef9225aff95dc4dc036455931eaf77a3eec33fddaa192d",
}


def command(arguments, **kwargs):
    return subprocess.run([str(item) for item in arguments], check=True, text=True,
                          stdout=subprocess.PIPE, stderr=subprocess.PIPE, **kwargs).stdout.strip()


def write(path, value):
    path.write_text(json.dumps(value, indent=2), encoding="utf-8")


def private_directory(path):
    path.mkdir(parents=True, exist_ok=False, mode=0o700)
    if os.name == "nt":
        sid = command(["powershell", "-NoProfile", "-Command",
                       "[System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value"])
        command(["icacls", path, "/inheritance:r", "/grant:r", "*" + sid + ":(OI)(CI)F",
                 "*S-1-5-18:(OI)(CI)F", "*S-1-5-32-544:(OI)(CI)F"])


def free_port():
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 0))
        return listener.getsockname()[1]


class Run:
    def __init__(self, args, flavor, repeat):
        self.args, self.flavor = args, flavor
        self.id = "noctf-gc-" + uuid.uuid4().hex[:12]
        self.path = args.output / f"{repeat:02d}-{flavor.lower()}"
        self.path.mkdir(parents=True, exist_ok=False)
        self.containers = []
        self.process = None
        self.host_container = None
        self.stop_sampling = threading.Event()
        self.sampler = None
        self.samples = []
        self.sample_limit = None
        self.counter_process = None
        self.counter_container = None
        self.env = {}
        self.host_options = []
        self.http = urllib.request.build_opener(urllib.request.ProxyHandler({}))

    def docker(self, suffix, image, port=None, options=(), arguments=()):
        name = self.id + "-" + suffix
        result = command(["docker", "run", "-d", "--name", name,
                          "--label", "io.noctf.acceptance=" + self.id,
                          *(["-p", f"127.0.0.1:{free_port()}:{port}"] if port else []),
                          *options, image, *arguments])
        self.containers.append(result)
        if not port:
            return result
        mapping = command(["docker", "port", result, str(port)])
        return int(mapping.rsplit(":", 1)[1])

    def dependencies(self):
        pg = self.docker("postgres", IMAGES["postgres"], 5432,
                         options=["-e", "POSTGRES_PASSWORD=" + PASSWORD, "-e", "POSTGRES_DB=acceptance"])
        pg_container = self.containers[-1]
        redis = self.docker("redis", IMAGES["redis"], 6379)
        nats = self.docker("nats", IMAGES["nats"], 4222, arguments=["-js"])
        for attempt in range(60):
            result = subprocess.run(["docker", "exec", pg_container, "pg_isready", "-U", "postgres"],
                                    stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            if result.returncode == 0:
                break
            time.sleep(1)
        else:
            raise RuntimeError("Synthetic PostgreSQL did not become ready")
        address = "host.docker.internal" if self.args.platform == "linux" else "127.0.0.1"
        self.env = {
            "ASPNETCORE_ENVIRONMENT": "Production", "DOTNET_ENVIRONMENT": "Production",
            "Hosting__Roles__0": "Api", "Hosting__Roles__1": "Worker",
            "Authentication__SigningKey": "SyntheticRuntimeAcceptanceSigningKey2026Only",
            "RunnerScoring__SigningKey": "SyntheticRuntimeAcceptanceSigningKey2026Only",
            "Webhooks__PublicBaseUrl": "https://acceptance.example.invalid",
            "ConnectionStrings__PostgreSql": f"Host={address};Port={pg};Database=acceptance;Username=postgres;Password={PASSWORD}",
            "ConnectionStrings__Redis": f"{address}:{redis}", "ConnectionStrings__Nats": f"nats://{address}:{nats}",
            "Database__AutoMigrate": "true", "SeedAdmin__UserName": "acceptance_admin",
            "SeedAdmin__Email": "acceptance-admin@example.invalid", "SeedAdmin__Password": ADMIN_PASSWORD,
            "Observability__Enabled": "false",
            "EmailVerification__EncryptionKey": "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=",
            "Storage__Provider": "Local",
        }

    def start(self):
        started = time.perf_counter()
        if self.args.platform == "windows":
            directory = getattr(self.args, self.flavor.lower()).resolve()
            manifest = json.loads((directory / "noctf-runtime-manifest.json").read_text())
            if manifest["flavor"] != self.flavor or manifest["rid"] != "win-x64":
                raise ValueError("Native publication identity mismatch")
            identity = json.loads(command([directory / "NoCTF.Host.exe", "--runtime-info"]))
            write(self.path / "publication.json", manifest)
            self.env["Storage__LocalRoot"] = str(self.path / "storage")
            port = free_port()
            self.url = f"http://127.0.0.1:{port}"
            self.env["ASPNETCORE_URLS"] = self.url
            self.env["RunnerScoring__CallbackBaseUrl"] = self.url
            if self.args.cpus:
                self.env["DOTNET_PROCESSOR_COUNT"] = str(self.args.cpus)
            # Do not inherit unrelated deployment or GC tuning configuration.
            inherited = {key: value for key, value in os.environ.items()
                         if "__" not in key and not key.startswith(("COMPlus_", "DOTNET_GC", "DOTNET_PROCESSOR", "ASPNETCORE_"))}
            self.host_log = (self.path / "host.log").open("w", encoding="utf-8")
            started = time.perf_counter()
            self.process = subprocess.Popen([str(directory / "NoCTF.Host.exe")], cwd=directory,
                                            env=inherited | self.env, stdout=self.host_log, stderr=subprocess.STDOUT)
            self.observed = psutil.Process(self.process.pid)
            if self.args.cpus:
                self.observed.cpu_affinity(self.observed.cpu_affinity()[:self.args.cpus])
            self.observed.cpu_percent()
        else:
            image = getattr(self.args, self.flavor.lower() + "_image")
            if self.flavor == "Baseline":
                info = command(["docker", "run", "--rm", "--entrypoint", "dotnet", image, "--info"])
                if "Microsoft.NETCore.App 10.0.10" not in info or "Microsoft.AspNetCore.App 10.0.10" not in info:
                    raise ValueError("Baseline image does not contain the expected original 10.0.10 runtime")
                identity = {"actualFlavor": "Baseline", "expectedFlavor": "Baseline", "dotnetInfo": info,
                    "verification": "Original deployed image runtime inventory; legacy Host has no GC marker check"}
            else:
                identity = json.loads(command(["docker", "run", "--rm", image, "--runtime-info"]))
            inspect = json.loads(command(["docker", "image", "inspect", image]))[0]
            write(self.path / "publication.json", {"imageId": inspect["Id"], "labels": inspect["Config"]["Labels"]})
            self.env["ASPNETCORE_URLS"] = "http://0.0.0.0:8080"
            self.env["RunnerScoring__CallbackBaseUrl"] = "http://127.0.0.1:8080"
            self.env["Storage__LocalRoot"] = "/tmp/acceptance-storage"
            options = [argument for key, value in self.env.items() for argument in ("-e", key + "=" + value)]
            ipc = self.path / "diagnostic-ipc"
            ipc.mkdir()
            options += ["-v", f"{ipc}:/tmp"]
            options += self.host_options
            if self.args.cpus:
                options += ["--cpus", str(self.args.cpus)]
            if self.args.memory:
                options += ["--memory", self.args.memory]
            started = time.perf_counter()
            port = self.docker("host", image, 8080, options=options)
            self.host_container = self.containers[-1]
            self.url = f"http://127.0.0.1:{port}"
        if identity["actualFlavor"] != self.flavor or identity["expectedFlavor"] != self.flavor:
            raise ValueError("Actual GC verification failed")
        write(self.path / "identity.json", identity)
        for attempt in range(120):
            try:
                with self.http.open(self.url + "/health/ready", timeout=2) as response:
                    if response.status == 200:
                        break
            except (OSError, urllib.error.URLError):
                pass
            if self.process and self.process.poll() is not None:
                raise RuntimeError("Native host exited during startup")
            time.sleep(.5)
        else:
            raise RuntimeError("Host readiness timed out")
        self.cold_seconds = time.perf_counter() - started
        self.sampler = threading.Thread(target=self.sample, daemon=True)
        self.sampler.start()
        counters = "System.Runtime[dotnet.gc.heap.total_allocated,dotnet.gc.last_collection.memory.committed_size,dotnet.gc.pause.time]"
        if self.args.counters:
            if self.process:
                self.counter_log = (self.path / "counters.log").open("w", encoding="utf-8")
                self.counter_process = subprocess.Popen([str(self.args.counters.resolve()), "collect", "-p", str(self.process.pid),
                    "--counters", counters, "--format", "csv", "--output", str(self.path / "counters.csv")],
                    stdout=self.counter_log, stderr=subprocess.STDOUT)
            else:
                lock = json.loads((HERE.parent / "runtime-lock.json").read_text())
                self.counter_container = self.docker("counters", lock["sdkImage"], options=[
                    "--pid", "container:" + self.host_container, "--volumes-from", self.host_container,
                    "-v", f"{self.args.counters.resolve()}:/diagnostic-tools:ro", "-v", f"{self.path}:/results"],
                    arguments=["/diagnostic-tools/dotnet-counters", "collect", "-p", "1", "--counters", counters,
                               "--format", "csv", "--output", "/results/counters.csv"])

    def sample(self):
        while not self.stop_sampling.is_set():
            try:
                if self.process:
                    memory = self.observed.memory_info()
                    cpu = self.observed.cpu_times()
                    value = {"rssBytes": memory.rss, "privateBytes": getattr(memory, "private", None),
                             "cpuSeconds": cpu.user + cpu.system, "cpuPercentOfOneCore": self.observed.cpu_percent(),
                             "threads": self.observed.num_threads()}
                else:
                    # The Host is PID 1 in the final image. PSS requires the matching
                    # process mapping; neither cgroup usage nor RSS substitutes for it.
                    output = command(["docker", "exec", self.host_container, "sh", "-c",
                        "cat /proc/1/status /proc/1/smaps_rollup; cat /sys/fs/cgroup/memory.current /sys/fs/cgroup/memory.peak; cat /sys/fs/cgroup/cpu.stat"])
                    value = {"rawProcAndCgroup": output}
                self.samples.append({"utc": time.time(), **value})
            except (OSError, psutil.Error, subprocess.CalledProcessError) as error:
                self.samples.append({"utc": time.time(), "error": type(error).__name__})
            with (self.path / "process-samples.jsonl").open("a", encoding="utf-8") as stream:
                stream.write(json.dumps(self.samples[-1]) + "\n")
            if self.sample_limit and len(self.samples) > self.sample_limit:
                del self.samples[:-self.sample_limit]
            self.stop_sampling.wait(1)

    def workload(self):
        fixture = self.path / "fixture.json"
        command(["node", HERE / "workload.mjs", "--command", "seed", "--fixture", fixture,
                 "--url", self.url, "--login", "acceptance_admin", "--password", ADMIN_PASSWORD,
                 "--participants", "16"])
        with (self.path / "driver.log").open("w", encoding="utf-8") as log:
            result = subprocess.run(["node", str(HERE / "workload.mjs"), "--command", "run", "--fixture", str(fixture),
                                     "--seconds", str(self.args.seconds), "--output", str(self.path / "workload.json")],
                                    stdout=log, stderr=subprocess.STDOUT, timeout=3600)
        return result.returncode

    def close(self, retain=False):
        self.stop_sampling.set()
        if self.sampler:
            self.sampler.join(timeout=15)
        if self.process:
            # The Windows benchmark records forced stop explicitly; graceful exit
            # is a separate functional scenario, not inferred from TerminateProcess.
            self.process.terminate()
            self.process.wait(timeout=30)
            self.host_log.close()
        if self.host_container:
            subprocess.run(["docker", "stop", "--time", "30", self.host_container], capture_output=True)
            logs = subprocess.run(["docker", "logs", self.host_container], capture_output=True, text=True)
            (self.path / "host.log").write_text(logs.stdout + logs.stderr, encoding="utf-8")
        if self.counter_process:
            try:
                self.counter_process.wait(timeout=15)
            except subprocess.TimeoutExpired:
                self.counter_process.terminate()
                self.counter_process.wait(timeout=10)
            self.counter_log.close()
        if self.counter_container:
            subprocess.run(["docker", "stop", "--time", "5", self.counter_container], capture_output=True)
            logs = subprocess.run(["docker", "logs", self.counter_container], capture_output=True, text=True)
            (self.path / "counters.log").write_text(logs.stdout + logs.stderr, encoding="utf-8")
        write(self.path / "process-samples.json", self.samples)
        for container in reversed(self.containers):
            # Only remove exact IDs successfully created by this run, after label verification.
            owner = command(["docker", "inspect", "--format", '{{index .Config.Labels "io.noctf.acceptance"}}', container])
            if owner != self.id:
                raise RuntimeError("Refusing cleanup of an unowned container")
            arguments = ["docker", "stop", "--time", "30", container] if retain else ["docker", "rm", "-f", "-v", container]
            subprocess.run(arguments, capture_output=True, check=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--platform", choices=["windows", "linux"], required=True)
    parser.add_argument("--official", type=pathlib.Path)
    parser.add_argument("--satori", type=pathlib.Path)
    parser.add_argument("--official-image")
    parser.add_argument("--satori-image")
    parser.add_argument("--baseline-image", help="Optional original Linux .NET 10.0.10 deployment image")
    parser.add_argument("--flavors", nargs="+", choices=["Baseline", "Official", "Satori"],
                        help="Optional subset for harness smoke checks; full comparisons use all supplied flavors")
    parser.add_argument("--output", type=pathlib.Path, required=True)
    parser.add_argument("--repeats", type=int, default=5)
    parser.add_argument("--seconds", type=int, default=30)
    parser.add_argument("--cpus", type=int, default=4, help="Windows affinity / Linux quota; zero means no explicit limit")
    parser.add_argument("--memory", help="Optional Linux container memory limit, e.g. 2g")
    parser.add_argument("--counters", type=pathlib.Path, help="Windows dotnet-counters executable or Linux tool installation directory")
    args = parser.parse_args()
    if args.platform == "windows" and (os.name != "nt" or not args.official or not args.satori):
        parser.error("Windows requires both native publication directories on Windows")
    if args.platform == "linux" and (not args.official_image or not args.satori_image):
        parser.error("Linux requires both committed Host image references")
    if not 1 <= args.repeats <= 20 or not 5 <= args.seconds <= 600 or args.cpus < 0:
        parser.error("Invalid repeat count, phase duration or CPU count")
    args.output = args.output.resolve()
    flavors = args.flavors or ((["Baseline"] if args.baseline_image and args.platform == "linux" else []) + ["Official", "Satori"])
    if "Baseline" in flavors and (args.platform != "linux" or not args.baseline_image):
        parser.error("Baseline requires the original Linux deployment image")
    if args.repeats >= 5:
        revisions = []
        for flavor in ("official", "satori"):
            if args.platform == "windows":
                manifest = json.loads((getattr(args, flavor) / "noctf-runtime-manifest.json").read_text())
                if manifest.get("applicationDirty") is not False:
                    parser.error("Formal repeats require clean committed Windows publications")
                revisions.append(manifest["applicationRevision"])
            else:
                metadata = json.loads(command(["docker", "image", "inspect", getattr(args, flavor + "_image")]))[0]
                revisions.append(metadata["Config"]["Labels"].get("org.opencontainers.image.revision"))
        if not revisions[0] or revisions[0] != revisions[1]:
            parser.error("Official and Satori comparison artifacts must have the same application revision")
    private_directory(args.output)
    write(args.output / "environment.json", {"platform": platform.platform(), "processor": platform.processor(),
          "logicalCpus": psutil.cpu_count(), "physicalCpus": psutil.cpu_count(logical=False),
          "memoryBytes": psutil.virtual_memory().total, "python": platform.python_version(), "psutil": psutil.__version__,
          "node": command(["node", "--version"]), "docker": command(["docker", "version", "--format", "{{json .Server}}"]),
          "driverSha256": hashlib.sha256((HERE / "workload.mjs").read_bytes()).hexdigest(),
          "parameters": {key: str(value) if isinstance(value, pathlib.Path) else value for key, value in vars(args).items()},
          "roles": ["Api", "Worker"], "storage": "Local", "observability": False,
          "limitations": ["Runner workload is separately gated", "No managed heap or GC pause data inferred from RSS",
                           "Windows cleanup uses forced termination", "Host shares hardware with Docker dependencies and load driver"]})
    records = []
    for repeat in range(1, args.repeats + 1):
        offset = (repeat - 1) % len(flavors)
        for flavor in flavors[offset:] + flavors[:offset]:
            run = Run(args, flavor, repeat)
            record = {"repeat": repeat, "flavor": flavor, "path": run.path.name, "startedAt": time.time()}
            try:
                run.dependencies()
                run.start()
                record["coldStartupSeconds"] = run.cold_seconds
                record["exitCode"] = run.workload()
            except Exception as error:
                record["failure"] = str(error)
            finally:
                try:
                    run.close()
                except Exception as error:
                    record["cleanupFailure"] = str(error)
                record["finishedAt"] = time.time()
                records.append(record)
                write(args.output / "runs.json", records)
                print(json.dumps(record), flush=True)
    write(args.output / "startup-summary.json", {
        flavor: {"count": len(times), "medianSeconds": statistics.median(times), "minSeconds": min(times), "maxSeconds": max(times)}
        for flavor in (["Baseline"] if args.baseline_image else []) + ["Official", "Satori"]
        if (times := [item["coldStartupSeconds"] for item in records if item["flavor"] == flavor and "coldStartupSeconds" in item])})
    failed = any(item.get("exitCode") != 0 or "failure" in item or "cleanupFailure" in item for item in records)
    write(args.output / "status.json", {"status": "workload-errors" if failed else "completed",
          "runs": len(records), "releaseApproval": False})
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
