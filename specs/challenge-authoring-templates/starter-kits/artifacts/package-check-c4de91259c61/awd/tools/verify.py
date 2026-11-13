import argparse
import datetime as dt
import json
import os
import secrets
import subprocess
import tarfile
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
from pathlib import Path

from package import MODES, ROOT, TOOLS, kit_root, package


HTTP = urllib.request.build_opener(urllib.request.ProxyHandler({}))


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def request(url, body=None, method=None, token=None):
    headers = {} if token is None else {"Authorization": "Bearer " + token}
    if isinstance(body, dict):
        body = json.dumps(body).encode("utf-8")
        headers["Content-Type"] = "application/json"
    try:
        with HTTP.open(urllib.request.Request(url, body, headers, method=method), timeout=5) as response:
            return response.status, response.read(1024 * 1024)
    except urllib.error.HTTPError as error:
        with error:
            return error.code, error.read(4096)


class DockerRun:
    def __init__(self):
        self.prefix = "noctf-authoring-" + uuid.uuid4().hex[:12]
        self.network = self.prefix + "-network"
        self.images = []
        self.containers = []
        self.token = secrets.token_urlsafe(32)
        self.artifacts = ROOT / "artifacts" / self.prefix
        self.artifacts.mkdir(parents=True)

    def docker(self, *args, check=True, env=None, timeout=240):
        result = subprocess.run(["docker", *map(str, args)], capture_output=True, env=env, timeout=timeout)
        if check and result.returncode:
            # 错误只输出阶段，避免把 Flag、令牌或 HTTP 正文写入日志。
            raise RuntimeError("Docker command failed: " + str(args[0]))
        return result

    def build(self, mode, component):
        image = self.prefix + "-" + mode + "-" + component
        self.images.append(image)
        self.docker("build", "-q", "-t", image, kit_root(mode) / "private" / component)
        return image

    def start(self, image, environment=None, port=8080, start=True):
        name = self.prefix + "-" + str(len(self.containers))
        self.containers.append(name)
        args = ["create", "--name", name, "--network", self.network, "--memory=256m",
                "--cpus=0.5", "--pids-limit=256", "--cap-drop=ALL", "--security-opt=no-new-privileges"]
        if port is not None:
            args.extend(["-p", "127.0.0.1::" + str(port)])
        environment = environment or {}
        for key in environment:
            args.extend(["--env", key])
        args.append(image)
        self.docker(*args, env={**os.environ, **environment})
        if start:
            self.docker("start", name)
        return name

    def url(self, name, port=8080):
        data = json.loads(self.docker("inspect", name).stdout)[0]
        binding = data["NetworkSettings"]["Ports"][str(port) + "/tcp"]
        require(binding and binding[0]["HostPort"] != "0", "Docker did not assign a public port")
        return "http://127.0.0.1:" + binding[0]["HostPort"]

    def ready(self, name, port=8080):
        url = self.url(name, port)
        deadline = time.monotonic() + 20
        while time.monotonic() < deadline:
            try:
                if request(url + "/health")[0] == 200:
                    return url
            except OSError:
                pass
            time.sleep(0.1)
        raise TimeoutError("Target readiness failed")

    def checker(self, image, target, key, task=None, fix=None, reject=False):
        environment = {"TARGET_HOST": target, "TARGET_READY_TIMEOUT_SECONDS": "2",
                       "NOCTF_TARGET_HOST": target, "NOCTF_AWD_TARGETS": json.dumps({"main": target}),
                       "NOCTF_CALLBACK_TOKEN": self.token,
                       "NOCTF_CALLBACK_URL": "http://" + self.gateway + ":8090/" + ("reject" if reject else "result/" + key)}
        if task is not None:
            status, _ = request(self.gateway_url + "/task/" + key, task, "PUT", self.token)
            require(status == 204, "Could not register task material")
            environment["NOCTF_AWD_TASK_URL"] = "http://" + self.gateway + ":8090/task/" + key
            environment["NOCTF_AWD_DEADLINE"] = task["deadline"]
        name = self.start(image, environment, port=None, start=False)
        if fix is not None:
            self.docker("cp", str(fix) + "/.", name + ":/noctf/fix")
        self.docker("start", name)
        code = int(self.docker("wait", name, timeout=40).stdout)
        if reject:
            require(code != 0, "Callback failure must fail the Checker process")
            return None
        require(code == 0, "Checker exited abnormally")
        status, body = request(self.gateway_url + "/result/" + key, token=self.token)
        require(status == 200, "Checker did not deliver a callback")
        receipts = json.loads(body)
        require(len(receipts) == 1, "Checker must send one receipt")
        return receipts[0]

    def setup(self):
        self.docker("info")
        self.docker("network", "create", self.network)
        image = self.prefix + "-gateway"
        self.images.append(image)
        self.docker("build", "-q", "-t", image, "-f", TOOLS / "Dockerfile.gateway", TOOLS)
        self.gateway = self.start(image, {"TOKEN": self.token}, port=8090)
        self.gateway_url = self.ready(self.gateway, 8090)

    def cleanup(self):
        failures = []
        for name in reversed(self.containers):
            if self.docker("rm", "-f", name, check=False).returncode:
                failures.append(name)
        if self.docker("network", "rm", self.network, check=False).returncode:
            failures.append(self.network)
        for image in self.images:
            if self.docker("image", "rm", image, check=False).returncode:
                failures.append(image)
        require(not failures, "Could not clean this test's resources: " + ", ".join(failures))


def flag_bytes(run, mode, image):
    values = ("x", "flag{队伍 A} \n", "z" * 4096)
    for index, value in enumerate(values):
        target = run.start(image, {"FLAG": value})
        url = run.ready(target)
        query = urllib.parse.urlencode({"name": "../private/flag"})
        status, body = request(url + "/file?" + query)
        require(status == 200 and body == value.encode("utf-8"), mode + " changed Flag bytes")
        require(request(url + "/file?name=welcome.txt") == (200, b"NoCTF public document.\n"), "Public document failed")
        logs = run.docker("logs", target).stdout + run.docker("logs", target).stderr
        require(value.encode("utf-8") not in logs, "Runtime logs disclosed Flag content")
        require(run.docker("exec", target, "id", "-u").stdout.strip() == b"10001", "Unexpected Runtime user")
        if index == 0:
            first = target
        else:
            first_body = request(run.url(first) + "/file?" + query)[1]
            require(first_body == b"x", "Another Runtime changed the first team's Flag")
    invalid = run.start(image, {"FLAG": "x" * 4097})
    require(int(run.docker("wait", invalid, timeout=20).stdout) != 0, "Oversized Flag must fail")
    if mode == "ctf":
        missing = run.start(image)
        require(int(run.docker("wait", missing, timeout=20).stdout) != 0, "Missing CTF Flag must fail")


def verify_awd(run):
    target_image = run.build("awd", "runtime")
    checker_image = run.build("awd", "checker")
    target = run.start(target_image)
    url = run.ready(target)
    store = str(uuid.uuid4())
    first, second = str(uuid.uuid4()), str(uuid.uuid4())

    def material(identifier, historical, write=None):
        return {"flagId": identifier, "storeId": store, "storeName": "sample", "targetServiceName": "main",
                "flag": "flag{历史-" + identifier + "} \n", "historical": historical,
                "requiresWrite": not historical if write is None else write,
                "privateReadContext": None, "publicLocator": None}

    def task(items):
        return {"gameplayFactId": str(uuid.uuid4()), "attemptId": str(uuid.uuid4()),
                "deadline": (dt.datetime.now(dt.timezone.utc) + dt.timedelta(seconds=25)).isoformat(), "flags": items}

    def check(key, items, missing=False):
        receipt = run.checker(checker_image, target, key, task(items))
        require(receipt["connection"]["outcome"] == "Passed", "AWD connection failed")
        require(receipt["functionality"]["outcome"] == "Passed", "AWD functionality failed")
        require({item["flagId"] for item in receipt["flags"]} == {item["flagId"] for item in items}, "Incomplete AWD receipt")
        require(len(receipt["flags"]) == len(items), "Duplicate AWD receipt entries")
        for evidence, item in zip(receipt["flags"], items, strict=True):
            require(evidence["write"]["outcome"] == ("Passed" if item["requiresWrite"] else "NotRequired"), "Wrong AWD write evidence")
            expected = "TargetFailure" if missing and item["historical"] else "Passed"
            require(evidence["read"]["outcome"] == expected, "Wrong AWD historical evidence")
            if expected == "TargetFailure":
                require(evidence["read"]["reason"] == "HistoricalFlagMissing", "Missing historical reason")
        require("health" not in receipt and "state" not in receipt, "Checker computed final health")
        require(dt.datetime.fromisoformat(receipt["observedAt"]).tzinfo is not None, "Missing observation timezone")

    check("awd-current", [material(first, False)])
    check("awd-history", [material(second, False), material(first, True)])
    path = "/dev/shm/flags/" + uuid.UUID(store).hex + "/" + uuid.UUID(first).hex
    run.docker("exec", target, "rm", path)
    check("awd-missing", [material(second, False), material(first, True)], missing=True)
    current = material(str(uuid.uuid4()), False, False)
    route = "/flags/" + uuid.UUID(store).hex + "/" + uuid.UUID(current["flagId"]).hex
    require(request(url + route, current["flag"].encode("utf-8"), "PUT")[0] == 204, "RunnerCommand-style injection failed")
    check("awd-command-read", [current])
    run.checker(checker_image, target, "awd-reject", task([current]), reject=True)
    print("AWD: current write, historical read, missing history, read-only material, callback failure passed")


def verify_awdp(run):
    target_image = run.build("awdp", "runtime")
    checker_image = run.build("awdp", "checker")
    flag_bytes(run, "awdp", target_image)
    archives = package("awdp", run.artifacts / "packages")
    for case, expected in (("defense-succeeded", "DefenseSucceeded"), ("exploit-succeeded", "ExploitSucceeded"),
                           ("service-abnormal", "ServiceAbnormal"), ("nonzero", None), ("timeout", None)):
        archive = next(path for path in archives if path.name == "noctf-awdp-fix-" + case + ".tar.gz")
        directory = run.artifacts / case
        directory.mkdir()
        with tarfile.open(archive) as source:
            source.extractall(directory, filter="data")
        target = run.start(target_image)
        run.ready(target)
        run.docker("cp", str(directory) + "/.", target + ":/noctf/fix")
        result = run.docker("exec", target, "/bin/sh", "/noctf/fix/fix.sh", check=False, timeout=10) if case != "timeout" else None
        if case == "nonzero":
            require(result.returncode == 17, "Patch nonzero exit was lost")
        elif case == "timeout":
            execution = subprocess.Popen(["docker", "exec", target, "/bin/sh", "/noctf/fix/fix.sh"], stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            try:
                execution.communicate(timeout=1)
                raise AssertionError("Timeout sample completed unexpectedly")
            except subprocess.TimeoutExpired:
                run.docker("kill", target)
                execution.communicate(timeout=10)
        else:
            require(result.returncode == 0, "Patch failed")
            receipt = run.checker(checker_image, target, "awdp-" + case)
            require(receipt == {"outcome": expected}, "Incorrect Fix classification")
            receipt = run.checker(checker_image, target, "awdp-input-" + case, fix=directory)
            require(receipt == {"outcome": expected}, "Checker Fix input changed network classification")
            if case == "defense-succeeded":
                run.checker(checker_image, target, "awdp-reject", reject=True)
    print("AWDP: attack Flag bytes, real resolver patch, vulnerable/broken service, Fix input, Patch failures passed")


def verify_koh(run):
    image = run.build("koh", "runtime")
    target = run.start(image)
    url = run.ready(target)
    require(request(url + "/control") == (200, b""), "New Hill must start uncontrolled")
    for flag in ("flag{队伍-A}", "flag{队伍-B} \n", "x" * 4096):
        value = flag.encode("utf-8")
        require(request(url + "/claim", value, "POST")[0] == 204, "Hill write failed")
        require(request(url + "/control") == (200, value), "Hill changed Control Flag bytes")
    require(request(url + "/claim", b"x" * 4097, "POST")[0] == 400, "Hill accepted an oversized value")
    require(request(url + "/claim", b"bad\0value", "POST")[0] == 400, "Hill accepted NUL")
    port_data = json.loads(run.docker("inspect", target).stdout)[0]["NetworkSettings"]["Ports"]
    require(not port_data.get("8081/tcp"), "Private control listener was published")
    code = "from urllib.request import urlopen; assert len(urlopen('http://127.0.0.1:8081/control').read()) == 4096"
    run.docker("exec", target, "python", "-B", "-c", code)
    print("KoH: shared takeover, exact control bytes, invalid length/NUL, private listener passed")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--mode", choices=(*MODES, "all"), default="all")
    args = parser.parse_args()
    modes = MODES if args.mode == "all" else (args.mode,)
    run = DockerRun()
    try:
        run.setup()
        (run.artifacts / "packages").mkdir()
        for mode in modes:
            if mode == "ctf":
                flag_bytes(run, mode, run.build(mode, "runtime"))
                print("CTF: independent Runtime Flags, 1/4096 bytes, Unicode, missing/oversized rejection passed")
            else:
                {"awd": verify_awd, "awdp": verify_awdp, "koh": verify_koh}[mode](run)
    finally:
        run.cleanup()


if __name__ == "__main__":
    main()
