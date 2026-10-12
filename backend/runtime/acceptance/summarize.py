"""Summarize independent runs; retain errors and absent diagnostics explicitly."""
from __future__ import annotations

import argparse
import csv
import json
import pathlib
import re
import statistics


def distribution(values):
    values = [value for value in values if value is not None]
    return {"count": len(values), "median": statistics.median(values),
            "min": min(values), "max": max(values)} if values else None


def process_sample(sample):
    if "rawProcAndCgroup" not in sample:
        return sample
    raw = sample["rawProcAndCgroup"]
    fields = {key: int(value) * 1024 for key, value in re.findall(r"^([A-Za-z_]+):\s+(\d+) kB$", raw, re.M)}
    cgroup = re.search(r"\n(\d+)\n(\d+)\nusage_usec (\d+)", raw)
    return {"utc": sample["utc"], "rssBytes": fields.get("Rss"), "pssBytes": fields.get("Pss"),
            "privateBytes": fields.get("Private_Clean", 0) + fields.get("Private_Dirty", 0),
            "containerBytes": int(cgroup[1]) if cgroup else None,
            "containerPeakBytes": int(cgroup[2]) if cgroup else None,
            "cpuSeconds": int(cgroup[3]) / 1e6 if cgroup else None}


def summarize(root):
    records = json.loads((root / "runs.json").read_text())
    runs = []
    for record in records:
        path = root / record["path"]
        result = dict(record)
        if (path / "fixture.json").exists():
            result["setupMeasurements"] = json.loads((path / "fixture.json").read_text()).get("setupMeasurements", [])
        samples = [process_sample(item) for item in json.loads((path / "process-samples.json").read_text())]
        result["memory"] = {name: distribution([item.get(name) for item in samples]) for name in
                            ["rssBytes", "pssBytes", "privateBytes", "containerBytes", "containerPeakBytes"]}
        cpu = [item["cpuSeconds"] for item in samples if item.get("cpuSeconds") is not None]
        result["observedCpuSeconds"] = cpu[-1] - cpu[0] if len(cpu) > 1 else None
        metrics = {}
        if (path / "counters.csv").exists():
            with (path / "counters.csv").open(encoding="utf-8-sig", newline="") as stream:
                for row in csv.DictReader(stream):
                    metrics.setdefault(row["Counter Name"], []).append(float(row["Mean/Increment"]))
        result["runtimeCounters"] = {key: {"samples": distribution(values),
            "intervalSum": sum(values) if " / 1 sec" in key else None} for key, values in metrics.items()}
        if (path / "workload.json").exists():
            workload = json.loads((path / "workload.json").read_text())
            result["phases"] = workload["phases"]
            result["workloadStatus"] = workload["status"]
            result["errors"] = workload["errors"]
        runs.append(result)
    comparisons = {}
    for flavor in sorted({item["flavor"] for item in runs}):
        selected = [item for item in runs if item["flavor"] == flavor]
        phases = {}
        for phase in ["warmup", "steady", "burst", "overload", "recovery"]:
            rows = [row for run in selected for row in run.get("phases", []) if row["phase"] == phase]
            phases[phase] = {"repeats": len(rows), "httpAndOperationLatencyMs": {
                metric: distribution([row["requests"][metric] for row in rows]) for metric in ["p50Ms", "p95Ms", "p99Ms"]},
                "effectiveCompletionsPerSecond": distribution([row["effectiveCompletionsPerSecond"] for row in rows]),
                "completedWorkerFactsPerSecond": distribution([row.get("completedWorkerFactsPerSecond") for row in rows]),
                "completedTheoryPapersPerSecond": distribution([row.get("completedTheoryPapersPerSecond") for row in rows]),
                "errorCount": distribution([row["requests"]["errors"] for row in rows]),
                "scenarios": {kind: {metric: distribution([row["scenarios"][kind][metric] for row in rows if kind in row["scenarios"]])
                    for metric in ["count", "errors", "p50Ms", "p95Ms", "p99Ms"]}
                    for kind in sorted({kind for row in rows for kind in row["scenarios"]})}}
        comparisons[flavor] = {"coldStartupSeconds": distribution([item.get("coldStartupSeconds") for item in selected]),
            "memoryPeakBytes": {name: distribution([item["memory"][name]["max"] for item in selected if item["memory"][name]])
                for name in ["rssBytes", "pssBytes", "privateBytes", "containerBytes", "containerPeakBytes"]},
            "observedCpuSeconds": distribution([item["observedCpuSeconds"] for item in selected]), "phases": phases}
    report = {"runs": runs, "comparisons": comparisons, "releaseApproval": False,
        "notes": ["Medians/ranges are across independent run statistics, not pooled percentiles.",
                  "Combined latency includes HTTP and operation/Worker samples; use per-scenario HTTP latency.",
                  "Counters cover seeding, workload and cooldown. Interval sums are not individual GC pauses.",
                  "RSS/PSS/container values are distinct from managed committed memory.",
                  "Missing counters are unavailable, not zero. Errors are never discarded."]}
    (root / "summary.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=pathlib.Path)
    args = parser.parse_args()
    summarize(args.directory.resolve())
