# Satori candidate validation

This is a running evidence record, not a release approval. The default is still
Official. Production has not been changed. Test data and dependency containers are
local and synthetic. Logs below are relative to `backend/artifacts/managed-runtime`.

## Runtime provenance

- Application SDK: 10.0.401; official runtime and ASP.NET Core: 10.0.12.
- Satori: `07c917bdeea06c8eecd1a65a28e171c72713cdfb`, packages `10.0.12-dev`.
- Verified ancestry from official `v10.0.12`, commit
  `4271d88e0aebf3d04f188f1334c2220d80555ef6`.
- Native Release `clr+libs+packs+host` builds completed on Windows and Linux.
- Runtime package hashes, build tools, symbols and licenses are retained in
  `backend/artifacts/satori/{win-x64,linux-x64}/manifest.json` and sibling files.
- Both operating systems executed both flavors' probe and complete Host binaries.
  All four processes reported the expected actual GC. Satori reported the real
  boolean configuration marker; no allocation heuristic was used.

## Host integration checks (2026-10-12)

All four flavor/OS combinations completed `--initialize-storage-only` against a
temporary MinIO instance, `--migrate-only` against separate empty PostgreSQL
databases, and `--export-openapi`. Offline MFA recovery reached the application
guard and rejected the unconfigured synthetic mail system with `EmailNotConfigured`
in both flavors. This is a negative-path check, not a successful recovery grant or
mail-delivery test. Logs: `maintenance-{storage,migrate,export,mfa}-{flavor}-{os}.log`.

Both Windows Host publications include the generated SPA. Its type check and
architecture audit passed (zero architecture violations). Linux Host publications
have executed in the pinned `runtime-deps` container. The paired images
`noctf:satori-f0fd2133` and `noctf:official-f0fd2133` were built from the same
committed business source and both passed `--runtime-info`. Final Windows release
archives still require a clean committed-source publication and verification.

## Functional evidence so far

| OS / suite | Official | Satori |
|---|---|---|
| Windows unit | 1491 pass, 1 fail | 1491 pass, 1 fail |
| Linux unit | 1491 pass, 1 fail | 1491 pass, 1 fail |
| Windows integration | 509 pass, 8 fail, 8 skip | 510 pass, 7 fail, 8 skip |
| Windows architecture | 97 pass, 2 fail | 97 pass, 2 fail |
| Linux integration | 503 pass, 14 fail, 8 skip | 504 pass, 13 fail, 8 skip |
| Linux architecture | 95 pass, 4 fail | 95 pass, 4 fail |

With Docker CLI/Compose installed, Linux architecture reruns produced 96 pass and
3 fail for each flavor. The additional shared failure is Compose JSON omitting
`create_host_path`; it is a fixture/tool-version difference, not a GC difference.

These are development publications with `applicationDirty=true`; each session
validated its own actual GC. They are not final clean-commit acceptance receipts.
The three self-contained architecture path regressions were corrected and rerun.
Linux tests require the full repository mount and Python; the initial smaller
runtime-only fixture lacked those test prerequisites and is superseded by the
source-mounted run.

The shared unit failure is
`LiveSoloRoundSchedulePolicyTests.Failed_readiness_after_countdown_uses_bounded_recovery_instead_of_an_immediate_spin`.
Shared architecture failures concern administrator OpenAPI metadata and existing
service-location use in `NatsCompetitionEventRefreshRelay`.

Six shared Windows integration failures concern four-mode enumeration now
including LiveSolo, LiveSolo capacity failure classification, two staff-webhook
projection cases, hidden-competition login and development Host shutdown.
The seventh shared failure was a self-contained test fixture path regression in
LiveKit egress, corrected and rerun successfully on both runtimes and platforms.
Official additionally failed one concurrent migration case. A repeat is
needed to characterize that difference; it is not evidence of a GC improvement.
The eight skips include GitOps, Kubernetes, libvirt and explicit daemon pressure
fixtures. None of these skips counts as acceptance of its scenario.

Linux additionally exposed test fixture assumptions about SMTP container DNS,
host bind mounts for SMTP certificates/Loki configuration, and the LiveKit fixture
root. These test-only fixes were rerun on both OSes and both runtimes: SMTP 5/5,
Loki 1/1 and LiveKit 1/1. The initial full-suite counts above remain the actual
initial results; targeted reruns do not retroactively turn them into green suites.
Linux architecture also needs Docker CLI/Compose in its test image. Official alone
missed a two-second webhook timing assertion during the shared-daemon run.

The explicitly enabled GitOps HTTP apply/reapply test passed on both Windows
runtimes. The documented template pin `794651c0bf158fa98fdacf88cb0f3a52b9db4b2b`
fails the separate fixture export contract: it lacks `Ctf/Services` and
`Awd/Services`. Inspecting upstream `d708c29199b76cda22253b6b75e279e0eac5a16a`
showed no corresponding fixture update. The pin was not silently moved.
The Kubernetes named-services test initially failed on both Windows runtimes.
Extracting the shell constant from the actual assembly reproduced exit code 2:
Windows CRLF source newlines had reached Linux `/bin/sh`. Normalizing that owned
script to LF at its transport boundary fixed both real-cluster runs (1/1 each),
including DNS, network isolation, public access and resource cleanup. Evidence:
`{official,satori}-windows-kubernetes-shellfix.{log,json}`. The explicitly enabled
Kubernetes one-shot test also passed on both Windows runtimes (1/1 each).
GitOps fixture export remains outstanding.

Evidence: `official-integration-bounded.{log,json}`,
`satori-integration-bounded.{log,json}`, `*-architecture-updated.{log,json}`,
`satori-linux-unit-source.log`, `official-linux-unit.log`, and their TUnit reports
and fresh identity receipts. Do not infer performance from suite elapsed times:
these runs shared the host and Docker daemon.

## Diagnostic evidence

Both platforms connected with EventPipe, collected System.Runtime counters and
decoded managed sampled stacks using local diagnostic tools version 10.0.750501.
The Windows trace is `diagnostics/satori-windows.nettrace`; Linux artifacts are under
`diagnostics/satori-linux.*` and `diagnostics/satori-linux-managed-stacks.log`.
Generation sizes and fragmentation returned zero as expected from unimplemented
Satori interfaces. They are unsupported metrics, not evidence of reduced memory.
Working-set and committed-heap counters are available.

Synthetic `Environment.FailFast` generated Windows mini and full dumps. SOS via
`dotnet-dump analyze`, even with the matching runtime directory, failed DAC
initialization with `0x80004002`; managed dump/heap analysis is not qualified.
Linux mini-dump collection failed in DAC memory-region enumeration. Linux full
dump collection succeeded (151,572,480 bytes), and GDB with the exact Noble
runtime libraries plus matching Satori native symbols produced usable stacks,
including `Environment_FailFast`, `EEPolicy::HandleFatalError` and Satori threads.
Evidence: `diagnostics/satori-linux-matched-native-stacks.log`. The earlier
unmatched-library GDB log is superseded and must not be used as evidence.
Windows CDB 10.0.29617.1000 loaded the matching private `coreclr.pdb` and decoded
the full dump's native stacks, including FailFast and Satori threads. Evidence:
`diagnostics/satori-windows-native-stacks-matched.log`. This does not qualify SOS
or managed heap analysis. Diagnostic directory ACLs restrict access to the current
user, SYSTEM and Administrators. All crashes were in the
synthetic probe; no production process or production data was dumped.

## Application workload progress

`acceptance/workload.mjs` exercises password login, challenge reads, Flag intake
and Worker completion, leaderboard projection/read, SignalR connections and
reconnections, 8 MiB binary download/hash verification, interrupted downloads and
theory paper issue/answer/submit. Input files contain synthetic credentials and
must remain private. The theory API's current numeric enum boundary is preserved.

The 16-participant Windows Satori pilot had zero errors in warmup, steady and
recovery, with transaction conflicts and admission refusals during burst/overload.
This is driver validation, not a performance conclusion. Five-second phases do
not provide a qualified tail-latency distribution. The earlier four-participant
prototype also had insufficient theory question inventory and incorrectly treated
leaderboard HTTP 202 as a failure; it is not acceptance evidence.

`acceptance/compare.py` creates fresh owned dependency containers for every run,
alternates runtime order, records cold readiness, process CPU/memory and raw
workload reports, and removes only its verified owned container IDs. It currently
covers Api+Worker with local file storage. Both runtime flavors on both OSes now
have real committed-heap, allocation and pause counters. Pause rates come from
`GC.GetTotalPauseDuration().TotalSeconds`; committed bytes come from
`GC.GetGCMemoryInfo().TotalCommittedBytes`. Neither is an individual pause latency
distribution. Windows cleanup is explicitly recorded as forced termination.
`summarize.py` reports medians and ranges across independent runs, with errors and
missing counters retained. Short counter pilots are not the five-repeat benchmark.

Windows combined Api+Worker+Runner completed start/reset/new-UUID/stop cycles
against the dedicated `noctf-gc-acceptance` Kubernetes cluster. Switching from
Satori to Official against the same database, Redis, NATS and Runner identity
completed another lifecycle cycle without clearing state. Lease expiry was
respected between Hosts. Both Hosts were then stopped. This is a real Windows
rollback rehearsal; Linux rollback and the timed soak remain separate gates.
The subsequent Linux same-data rollback is recorded below.

The two mixed CTF leaderboard benchmark cases ran under both verified Windows
runtimes using BenchmarkDotNet's InProcessNoEmit toolchain. Single-iteration
smokes validate execution only; their timing and generation columns are not
performance or GC-semantics conclusions.

The original deployed local image `noctf-host:v0.4.1-3144c32927c2` was inspected:
its actual runtime inventory contains .NET/ASP.NET Core 10.0.10. A Linux application
pilot and counter collection completed with that image. Its legacy Host lacks the
new GC identity command, so runtime inventory is explicitly distinguished from
the actual-GC marker validation used for B and C. No production Host was contacted.

`acceptance/soak.py` now exercises combined roles, real dependency restarts, Host
restarts, persistent score checks and same-data Official rollback. Windows uses
the dedicated Kubernetes cluster; Linux uses an exclusively owned Docker daemon.
The Linux nested daemon lifecycle does not qualify external public-port access.
Both short recovery pilots completed Runtime start/reset/stop and restored ready
health after PostgreSQL, Redis and NATS restarts. Score remained 500 after cache
loss and Host restart. Both Windows and Linux Official rollback preserved the score and
completed the Runtime lifecycle. Short pilots remain far below 86,400 seconds.
Their overload errors remain in the reports; Linux also recorded one recovery
error, requiring the independent comparison before a stability conclusion.

Harness corrections discovered during pilots include fixed dependency host ports
(Docker's automatically assigned ports changed on restart), actual provider
configuration key names, a pinned available Kubernetes image, and downloading the
attachment before a correct static Flag submission as required by acquisition
policy. The earlier failed harness pilots are retained and not relabeled passed.
All comparison/soak output roots now restrict access to the current user, SYSTEM
and Administrators on Windows; full process samples stream to JSONL with bounded
controller memory. `campaign.py` schedules the duration runs before sequential
comparisons so no soak load overlaps the performance measurements.

## Outstanding release gates

- Resolve or characterize remaining shared failures and explicitly enabled
  infrastructure fixtures; rerun the complete matrix at a clean commit.
- Verify isolated role combinations, restart/recovery and successful maintenance
  recovery workflows; distinguish every pre-existing failure from new differences.
- Run the three-group application workload comparison with at least five independent
  repetitions per primary scenario, reporting latency, memory and throughput.
- Complete at least 86,400 seconds of mixed business workload on each OS, including
  dependency recovery and restarts. No qualifying soak has completed yet.
- Publish and verify committed-source paired images/archives and rehearse rollback.

Until these gates pass, there is no performance conclusion, stability approval,
default switch or production deployment authorization.
