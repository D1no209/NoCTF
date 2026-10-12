"""Run paired 24h soaks, then sequential five-repeat comparisons without overlap."""
from __future__ import annotations

import argparse
import json
import pathlib
import subprocess
import sys
import time

from compare import HERE, private_directory, write
from summarize import summarize


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--configuration", type=pathlib.Path, required=True)
    parser.add_argument("--output", type=pathlib.Path, required=True)
    args = parser.parse_args()
    config = json.loads(args.configuration.read_text())
    if config.get("synthetic") is not True:
        parser.error("An explicitly synthetic campaign configuration is required")
    output = args.output.resolve()
    private_directory(output)
    state = {"status": "running", "startedAt": time.time(), "jobs": [], "releaseApproval": False}

    def save():
        write(output / "state.json", state)

    def launch(name, script, arguments):
        path = output / name
        log = (output / (name + ".log")).open("w", encoding="utf-8")
        job = {"name": name, "startedAt": time.time(), "output": str(path)}
        process = subprocess.Popen([sys.executable, str(HERE / script), *arguments, "--output", str(path)],
                                   stdout=log, stderr=subprocess.STDOUT)
        job["pid"] = process.pid
        state["jobs"].append(job)
        save()
        return process, log, job

    def complete(handle):
        process, log, job = handle
        job["exitCode"] = process.wait()
        job["finishedAt"] = time.time()
        log.close()
        save()

    soak_handles = []
    try:
        for platform in ["windows", "linux"]:
            arguments = ["--platform", platform, "--hours", "24", "--seconds", "30"]
            if platform == "windows":
                arguments += ["--satori", config["satoriWindows"], "--official", config["officialWindows"], "--runner-environment", config["windowsRunnerEnvironment"],
                              "--runtime-image", config["kubernetesRuntimeImage"]]
            else:
                arguments += ["--satori-image", config["satoriImage"], "--official-image", config["officialImage"]]
            soak_handles.append(launch("soak-" + platform, "soak.py", arguments))
        for handle in soak_handles:
            complete(handle)
        for _, _, job in soak_handles:
            result_path = pathlib.Path(job["output"]) / "01-satori" / "result.json"
            if not result_path.exists() or json.loads(result_path.read_text()).get("durationGateMet") is not True:
                state["status"] = "duration-incomplete"
                return 1
        for name, platform, limits in [("comparison-windows", "windows", ["--cpus", "4"]),
                ("comparison-linux-limited", "linux", ["--cpus", "4", "--memory", "2g"]),
                ("comparison-linux-unlimited", "linux", ["--cpus", "0"])]:
            arguments = ["--platform", platform, "--repeats", "5", "--seconds", "30", *limits]
            if platform == "windows":
                arguments += ["--official", config["officialWindows"], "--satori", config["satoriWindows"],
                              "--counters", config["windowsCounters"]]
            else:
                arguments += ["--official-image", config["officialImage"], "--satori-image", config["satoriImage"],
                              "--baseline-image", config["baselineImage"], "--counters", config["linuxCounters"]]
            handle = launch(name, "compare.py", arguments)
            complete(handle)
            if (output / name / "runs.json").exists():
                summarize(output / name)
        state["status"] = "completed-for-review"
    except Exception as error:
        state["status"] = "controller-failed"
        state["failure"] = str(error)
        raise
    finally:
        state["finishedAt"] = time.time()
        save()
    return 0 if all(item.get("exitCode") == 0 for item in state["jobs"]) else 1


if __name__ == "__main__":
    sys.exit(main())
