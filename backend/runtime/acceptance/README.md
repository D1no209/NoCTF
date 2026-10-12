# Local runtime acceptance

These tools use synthetic accounts and local dependency containers. Run them from
the repository root on the Windows test machine with Python 3.12+, Node 24, Docker,
and `pip install -r backend/runtime/acceptance/requirements.txt`. Linux Hosts run in
native Linux containers on Docker Desktop. Results apply to that environment;
they do not replace production hardware measurements. Keep fixtures, logs,
diagnostic IPC and raw reports in private ignored artifact directories.

## Independent application comparisons

Publish both flavors from the same clean commit first. Use immutable image IDs or
digest references for Linux. Each repetition creates fresh PostgreSQL, Redis and
NATS instances, and exercises five phases after measuring cold readiness (which
includes first-time migration). Runtime order rotates between repetitions.

```powershell
python backend/runtime/acceptance/compare.py --platform windows --official backend/artifacts/managed-runtime/host-official-win --satori backend/artifacts/managed-runtime/host-satori-win --output backend/artifacts/managed-runtime/comparison-windows --repeats 5 --seconds 30 --cpus 4 --counters backend/artifacts/managed-runtime/tools/dotnet-counters.exe
python backend/runtime/acceptance/summarize.py backend/artifacts/managed-runtime/comparison-windows
```

Linux substitutes `--official-image` and `--satori-image` for publication paths,
and `--counters` points to a Linux dotnet-counters installation directory. Run once
with `--cpus 4 --memory 2g` and again with `--cpus 0` and no memory limit. An optional
`--baseline-image` adds the original .NET 10.0.10 deployment image as group A.
That legacy image has no actual-GC marker command: its runtime inventory and image
identity are recorded explicitly, without claiming the newer startup check ran.
There is no pre-existing Windows production deployment baseline to measure.

Do not overlap comparison processes or run builds/tests during measurements.
Warmup, steady, burst, overload and recovery are separate. Recovery waits 65 seconds
for admission windows to drain. The workload records rejections and failures,
including those induced deliberately at overload. Exit code 1 and
`workload-errors` remain visible; they are not automatically qualified or ignored.
Inspect per-scenario percentiles: the combined distribution also includes Worker
completion and SignalR timing. Completed Worker facts and theory papers have
separate throughput counters. Upload timing is retained with fixture setup data.

Windows uses CPU affinity plus `DOTNET_PROCESSOR_COUNT`; Linux uses a cgroup CPU
quota. They are different controls and must not be compared across OSes. Managed
committed bytes, OS private bytes, RSS, PSS and cgroup memory are distinct metrics.
Pause counter sums cover intervals and do not reveal individual GC pause tails.
Missing metrics are unavailable, not zero. Raw reports are preserved alongside
run-level medians and ranges in `summary.json`.

## Three-role recovery and duration

`soak.py` creates a fresh dependency set and runs Api+Worker+Runner. Linux uses a
private Docker-in-Docker daemon and owned socket volume. It never gives Runner
the developer daemon's socket. This nested topology tests lifecycle and recovery;
its challenge public ports are not mapped through the outer daemon. Public access
is separately tested by the existing Docker/Kubernetes integration fixtures.
Each repeated workload creates a new practice activity from the same eight
synthetic source questions. A participant cannot draw the same source twice in
one activity; `allowExposedQuestions` does not reset that inventory. Exhaustion or
an incomplete workload stops duration qualification instead of silently spending
the remaining soak time on rejected theory requests. Per-run reports retain the
activity ID and each soak records its harness file hashes.
Node workload processes enter through `bootstrap.mjs`, which writes a startup
receipt before importing any business workload code. The supervisor records PID,
exit status, duration and report presence for every attempt. Only a quick failed
start with no bootstrap receipt may retry (at most three attempts). A process that
entered the workload but did not produce a report fails the run without replay.
On an exceptional soak failure, owned resources are stopped and retained for
investigation; their receipt identifies the exact IDs. Successful completion uses
the normal verified cleanup path.

Windows requires a synthetic JSON `--runner-environment` containing `KUBECONFIG`
and `Runtime__Kubernetes__*` settings for an exclusively owned test cluster.
`Initialize-Kubernetes.ps1` can create the pinned cluster and checks ownership
before resume. It does not change the global kubectl context. Only one acceptance
Runner may use that cluster at a time, including across independent databases.
Supply a pinned `--runtime-image` available to that cluster.

```powershell
python backend/runtime/acceptance/soak.py --platform linux --satori-image <immutable-candidate-image> --output backend/artifacts/managed-runtime/soak-linux --hours 24
python backend/runtime/acceptance/soak.py --platform windows --satori backend/artifacts/managed-runtime/host-satori-win --runner-environment <private-synthetic-config.json> --runtime-image <pinned-test-image> --output backend/artifacts/managed-runtime/soak-windows --hours 24
```

Use a short `--hours 0.01 --seconds 5 --restart-seconds 60 --dependency-seconds 60`
pilot to verify the harness first. It cannot satisfy the 86,400-second gate.
The default sustained run restarts the dependencies every four hours and Host
every six hours, without deleting their data. Host restarts preserve Runner
identity and respect lease expiry. Linux exercises Docker stop/kill; automated
Windows restarts are forced. Windows graceful console cancellation is a separate
manual functional check. `events.jsonl`, `progress.json`, per-cycle reports and
process samples retain evidence. A completed duration alone is not release
approval; errors and memory trends still require comparison and review.
Full process history streams to `process-samples.jsonl`; a soak's JSON snapshot
contains only its last 300 samples to bound controller memory and disk writes.

The controller cleans up only verified IDs it created. For an interrupted
controller, consult its ownership receipt and verify labels before manual cleanup.
Never use broad container prune or delete someone else's cluster. Keep the
machine awake and Docker running throughout the timed soak.

Providing `--official` (Windows) or `--official-image` (Linux) rehearses rollback
after the timed Satori portion. It preserves the same data and Runner identity,
verifies the durable score, and repeats the Runtime lifecycle with Official.
The rollback interval does not count toward the Satori duration.

`campaign.py --configuration <private-json> --output <new-private-directory>`
starts the two 24-hour soaks and waits for both to finish before running Windows,
limited Linux and unlimited Linux comparisons sequentially. Its configuration
contains `synthetic: true`, `satoriWindows`, `officialWindows`, `satoriImage`,
`officialImage`, `baselineImage`, `windowsRunnerEnvironment`,
`kubernetesRuntimeImage`, `windowsCounters`, and `linuxCounters`. Use absolute paths
and immutable image references. The controller writes `state.json` and never
switches a default or approves a release. Review every workload error and actual
duration even if the campaign reaches `completed-for-review`.
If either duration run ends before satisfying its duration gate, the campaign
stops with `duration-incomplete` instead of starting the performance comparisons.
