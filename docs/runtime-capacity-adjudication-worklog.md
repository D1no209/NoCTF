# Runtime capacity and adjudication implementation

## Scope and delivery

Implement the approved capacity recovery and read-only historical adjudication plan.
Each verified step is a separate local commit. Do not push or deploy. Preserve the
19 pre-existing modified files (the initial patch is saved in local Git metadata).
CPU sharing defaults to 1; test 2 separately. Memory/PID remain strict. Libvirt
remains strict. Runtime active allocation metadata is an explicitly approved
extension of the minimal Runtime model, not a definition snapshot or workflow log.

## S0: baseline, 2026-09-18

Source baseline: `957a05e6b`, .NET SDK 10.0.300, Bun 1.3.14, Docker 29.7.2.
The initial solution Debug build succeeded with zero warnings and errors.
HistoricalAdjudication tests: 7 passed, including real PostgreSQL persistence.
RedisRunnerCapacityGate tests: 12 passed against real Redis containers.

Authorized read-only production sampling on 2026-09-18 supersedes the earlier
plan's assumption that no environment had been supplied. No server changes were
made. Data below comes from PostgreSQL, Redis, Docker and Prometheus; no protected
Flag, credentials, message bodies or player submissions were collected.

At 09:18 UTC (17:18 UTC+8):

| Evidence | Value |
| --- | --- |
| Host capacity | 4 CPUs, 8,337,412,096 bytes RAM |
| Runner allocation ceiling | 2 CPUs, 4 GiB, 2048 PIDs |
| Active Runtime rows | 4 (2 player, 2 template tests) |
| Available accounting CPU | 0 |
| Available accounting memory / PIDs | 3 GiB / 1536 |
| Actual host CPU (one-minute rate) | 2.7556% |
| Actual host available RAM | 4,553,240,576 bytes |
| Waiting Runtime gauge (max across reporters) | 7 |

The four currently referenced templates each declare 0.5 CPU, 256 MiB and 128
PIDs, consistent with that historical accounting. Current definitions are not
historical snapshots. Between 07:28 and 08:58 UTC, 65 one-minute samples have both
waiting Runtime instances and at least 0.5 accounting CPU free. Some never-assigned
rows waited 7,142 seconds before stopping. This establishes a dispatch recovery
symptom independently of CPU overreservation; its precise message failure window
still requires a local integration reproduction.

At sampling time all 69 Runtime rows were Stopped, with no challenge containers or
Redis claims. Runner capacity was fully restored. Capacity and heartbeat keys both
had approximately 13 seconds TTL. NATS consumers had no pending or unacknowledged
messages. Four historical ProvisionContainerRuntime Docker timeout dead letters
remain; they do not prove the cause of the later waiting interval. No sampled OOM
counter increase occurred in the preceding 24 hours.

Existing regression coverage already characterizes heartbeat/capacity expiration,
atomic claims and release, conservative registration, and current preview rules.
Add the missing lost-dispatch recovery and ordered-evidence regressions alongside
their fixes; do not claim the existing passing suite proves those behaviors.

## Implementation sequence

- S0: baseline and reproducible verification entry point.
- A1: workload identity, active allocation metadata and generated EF migration.
- A2: durable allocation/release and reconciliation, including lost dispatch.
- A3: quota changes and legacy allocation recovery.
- A4: main, Compose, target and checker accounting.
- A5: observations, admission state, startup slots and coalesced wakeups.
- A6: Budget/Limit policy and provider enforcement.
- A7: capacity API/monitoring and bilingual UI.
- A8: strict/shared validation and recovery/rollback documentation.
- B1: shared eligibility with scoring behavior regression coverage.
- B2: ordered evidence and classification.
- B3: read-only evidence API and UI.
- V1: complete verification and explicit unverified environments.

## Verification discipline

A1 completed: bounded typed allocation JSONB, deterministic workload identity and
owner/budget invariants. EF generated `AddRuntimeCapacityAllocations`; empty-db
migration plus real PostgreSQL JSON round-trip and three unit tests passed (4/4).
Model drift check passed. The change does not enable CPU sharing.

A2a completed: the Singular Agent dispatches bounded keyset batches of queued
Runtime facts every five seconds. Capacity rejection no longer creates a per-row
scheduled retry. A real PostgreSQL/NATS durable-inbox/outbox regression verifies
that a two-hour-old queued row is dispatched again after capacity returns without
any saved retry; stopped rows remain excluded (1/1 passed). This addresses the
observed recovery symptom without claiming a proven historical message-loss cause.

A2b completed for the new allocation protocol: allocation and provisioning are
committed before provider work; release removes metadata and durably publishes a
Redis release command. Recovery closes admission, excludes provider mutations and
rebuilds claims from committed allocations. Unknown legacy/resource ownership
remains blocked pending A3 reconciliation. Real PostgreSQL/Redis commit/rollback,
Redis loss, frozen replay budgets and repeated release tests passed (2/2); provider
handler regressions passed (25/25), mutation exclusion passed (1/1). No provider
calls are made while holding the capacity database transaction.

A3 completed: capacity hashes no longer expire with heartbeat. Quota changes apply
the total delta to available capacity, preserving existing claims and allowing a
negative balance. Provider failure closes admission but keeps liveness for cleanup.
Legacy full-budget claims are copied into PostgreSQL before Redis key replacement;
missing evidence remains blocked. Cleanup recognizes RecoveryRequired instead of
pretending release succeeded. Redis regression suite 13/13, explicit legacy/quota
test 1/1, and PostgreSQL/Redis publisher-cleanup test 1/1 passed.

Use `backend/scripts/Verify-CoreRecovery.ps1` for scoped checks. Integration tests
must use real PostgreSQL/Redis/Wolverine/NATS and Docker. Source-only assertions or
EF InMemory do not establish relational or recovery behavior. Compare performance
on the same machine with the same corpus, retaining distributions and call counts.
No Kubernetes/Libvirt runtime or <=5% performance claim is made without measurement.

Wolverine is pinned to 6.30.3. Consult the official EF transactional outbox,
PostgreSQL persistence, NATS transport, handler discovery, Sticky handlers and
Singular Agent documentation and verify APIs against the pinned package. Transport
remains the current NATS + PostgreSQL durability composition.
