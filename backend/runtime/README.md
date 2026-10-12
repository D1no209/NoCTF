# Managed runtime builds

This directory builds NoCTF against either the official runtime or a pinned Satori
fork. `runtime-lock.json` records the security baseline, source commit, application
SDK, supported RIDs and immutable container image references. The default remains
**Official until both platforms pass acceptance**. Selecting Satori without verified
packages fails immediately.

## Build the runtime

Run commands from the repository root. Python 3.12+, Git and the pinned application
SDK are required for the publishing tools. Runtime compilation installs its own
SDK inside the source checkout, without replacing the global SDK.

Linux x64 uses Docker and the locked Ubuntu build image:

```sh
python backend/runtime/runtime.py build-linux --output backend/artifacts/satori/linux-x64
```

The build keeps its source in a commit-specific Docker volume. The default build
budget is 8 CPUs and 12 GiB; override with `--cpus` and `--memory` if needed. These
are build limits, not production runtime limits.

Windows x64 requires stable Visual Studio C++ tools, CMake, Ninja and PowerShell 7.
Use a short checkout path (or a dedicated `subst` drive) for native tool path limits:

```powershell
python backend/runtime/runtime.py fetch --source C:/satori-src
python backend/runtime/runtime.py build --source C:/satori-src --rid win-x64 --output backend/artifacts/satori/win-x64
```

`--powershell` and `--visual-studio` select explicit tool installations. If the
upstream Windows installer task cannot download its NuGet 3.5.0 executable, acquire
that exact tool from `https://dist.nuget.org/win-x86-commandline/v3.5.0/nuget.exe`,
verify its Microsoft Authenticode signature, and pass `--nuget-exe <path>`.

Source verification requires the pinned official runtime commit to be an ancestor
of the Satori commit, and rejects tracked source changes. Successful builds emit
runtime/apphost packages, matching native symbols, licenses and `manifest.json`.
An existing artifact output is never overwritten. Keep the manifest and its files
together; retain symbols in restricted artifact storage.

## Publish and verify

```powershell
python backend/runtime/runtime.py publish --flavor Satori --rid win-x64 --runtime-dir backend/artifacts/satori/win-x64 --project probe --output backend/artifacts/managed-runtime/probe-satori-win
backend/artifacts/managed-runtime/probe-satori-win/NoCTF.RuntimeProbe.exe
python backend/runtime/runtime.py publish --flavor Satori --rid win-x64 --runtime-dir backend/artifacts/satori/win-x64 --output backend/artifacts/managed-runtime/host-satori-win --spa
python backend/runtime/runtime.py publish --flavor Official --rid win-x64 --output backend/artifacts/managed-runtime/host-official-win --spa
```

Use `linux-x64` and the corresponding package directory on Linux. Both flavors use
the same application SDK and ASP.NET Core patch. NuGet source mapping restricts
custom packages to the exact runtime/apphost IDs; cache and intermediate outputs
are separated by flavor, runtime commit/version and RID. Satori publishes verify
CoreCLR, JIT and CoreLib against the hashed packages after publishing. There is no
DLL replacement step. ReadyToRun, trimming and NativeAOT are excluded.

The executable's `--runtime-info` command performs the actual GC identity check
before configuration, database initialization or role startup. A flavor mismatch
refuses startup. Satori detection uses the boolean `SatoriGC` runtime configuration
marker. The JSON is for local operators, not public health responses.

```powershell
python backend/runtime/runtime.py start --directory backend/artifacts/managed-runtime/host-satori-win
python backend/runtime/runtime.py start --flavor Official --directory backend/artifacts/managed-runtime/host-official-win
```

The development launcher defaults to Satori and prints the verified identity.
EF tooling and Wolverine generation use Official explicitly.

After publishing from a committed checkpoint, create a distributable archive and
its SHA-256 sidecar (Windows uses `.zip`, Linux uses `.tar.gz`):

```powershell
python backend/runtime/runtime.py archive --directory backend/artifacts/managed-runtime/host-satori-win --output backend/artifacts/managed-runtime/noctf-satori-win-x64.zip
```

Commit an atomic source checkpoint before building images:

```sh
python backend/runtime/runtime.py image --flavor Satori --runtime-dir backend/artifacts/satori/linux-x64 --tag noctf:satori-candidate
python backend/runtime/runtime.py image --flavor Official --tag noctf:official-fallback
docker run --rm noctf:satori-candidate --runtime-info
docker run --rm noctf:official-fallback --runtime-info
```

Image builds export tracked files from the current commit into a temporary context,
add only approved runtime inputs, and run the self-contained Host on the locked
`runtime-deps` image. No image is pushed by these commands. Both images preserve the
same healthcheck, upload path and Docker socket/network conventions.

## Functional evidence and acceptance

Publish TUnit as a self-contained executable for **each** flavor/RID pair, then
execute it on that OS:

```powershell
python backend/runtime/runtime.py publish --flavor Satori --rid win-x64 --runtime-dir backend/artifacts/satori/win-x64 --project tests --output backend/artifacts/managed-runtime/tests-satori-win
python backend/runtime/runtime.py test --flavor Satori --directory backend/artifacts/managed-runtime/tests-satori-win --log backend/artifacts/managed-runtime/tests-satori-win.log
```

The test session checks its own GC. The wrapper requires a fresh identity receipt,
records the exit code, source revision, dirty state and log hash, and enables the
repository's required Docker integration switch. A zero exit without a receipt
does not pass. Inspect test counts and skips; one filtered run is not full acceptance.
The wrapper defaults to four parallel tests; use `--parallel` to set an explicit
capacity appropriate for the test host and keep it identical across variants.
Native memory, pinned buffers, cross-thread large objects, weak references and
finalizers are covered by `ManagedRuntimeMemoryTests`.

Acceptance must additionally cover all role combinations, authentication, scoring,
LiveSolo, real PostgreSQL/Redis/NATS recovery, Docker/Kubernetes lifecycle, files,
SignalR, migration/storage commands and shutdown/restart. Preserve official failures
as a baseline and identify every new candidate failure separately.

Compare A (current official deployment), B (matching official self-contained) and
C (Satori self-contained) on the same OS/hardware. Run at least five independent
repeats per workload; report latency percentiles, effective throughput, errors,
CPU and memory with medians and spread. Separate cold, warm, steady, burst,
overload and recovery phases. Test Linux with and without isolated resource limits.
Both platforms require at least 86,400 seconds of mixed business workload including
dependency recovery and restarts. A GC allocation loop does not satisfy this gate.

The BenchmarkDotNet entry point runs in the verified process to prevent a generated
child silently selecting the installed official runtime. Treat these component
benchmarks as supporting evidence; they do not replace HTTP/SignalR/Runner workloads.

## Diagnostics and rollout

The pinned fork has incomplete diagnostics. `--runtime-info` lists unsupported GC
capabilities, and Satori telemetry drops generation-size/fragmentation meters that
would otherwise expose placeholder values. Do not interpret zero fragmentation,
pinned-object or per-generation values as evidence of improvement. Verify EventPipe,
available counters/events, managed stack sampling and native crash capture with
matching symbols; record unsupported SOS/heap operations explicitly.

Only switch the default after functional, stability, diagnostic and rollback gates
pass on both platforms. Keep the same-business-commit Official artifacts. Production
deployment is a separate manual authorization: stop the old combined Host before
starting the replacement, preserve Runner identity/resource-domain ownership, and
verify GC identity, health, consumers and Runner registration. Rollback replaces the
runtime artifact without rolling back business data or clearing Redis/NATS.

For each security patch, update the lock, verify upstream ancestry again, rebuild
both platforms and repeat acceptance. If the fork lacks required fixes, use the
matching Official build until a qualifying Satori revision is available.

References: [Satori](https://github.com/VSadov/Satori/blob/07c917bdeea06c8eecd1a65a28e171c72713cdfb/src/coreclr/gc/satori/README.md),
[shipping-package build](https://github.com/VSadov/Satori/blob/07c917bdeea06c8eecd1a65a28e171c72713cdfb/docs/workflow/testing/using-dev-shipping-packages.md).
