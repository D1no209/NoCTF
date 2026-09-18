# Runtime capacity and adjudication implementation

## Review follow-up R2

Worker re-dispatch rejects a resource definition that differs from the committed
Limit, retains the allocation for confirmed cleanup and checks reconstructed Budget.
Runner also checks the concrete container/Compose/OVA request before invoking a
provider. Real PostgreSQL/Redis regressions passed 2/2 for single-container and
Compose edits that double Limit while factor 2 would mask the Budget mismatch.
Existing template lifecycle 1/1, CTF flag persistence 2/2, budget policy 3/3 and
claim factory 13/13 passed. No definition snapshot or concurrent version was added.

## Review follow-up R1

Atomic unconfirmed-claim indexing and normal Runner audit close the
claim/rollback/cancel leak. PostgreSQL is checked again after provider absence,
under the short allocation lock; committed or physically uncertain claims stay
charged. Regression tests use real PostgreSQL/Redis and exercise normal audit
handling without restart/key deletion, including duplicate audits and unknown
provider state. Persisted gate 4/4, Redis gate 13/13, real messaging 2/2 and SQL
architecture guard 1/1 passed. Provider absence in the new rollback test is a
controlled probe; full process-kill coverage is still a separate V1 requirement.

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

A4 completed: AWD and Patch Checkers claim their own deterministic operation
identity and retain capacity until exact provider inventory confirms cleanup.
Auxiliary reconciliation preserves active executions and cleans terminal leftovers.
Main provider handlers use scoped dependencies and explicitly nontransactional
provider execution. Capacity-denied AWD checks become PlatformFailed, not Down;
Patch checks use the existing platform-failure path. Nontransactional Patch result
publication explicitly saves its outbox before dispatch. Checker regressions 44/44,
real PostgreSQL/Redis parent/checker identity and release tests 2/2, real Docker
parent-versus-checker inventory 1/1 and Runner registration 1/1 passed.

A5 completed: 5-second resource observations, 15-second freshness, pressure
hysteresis/OOM gates, a PostgreSQL session lock for resource-domain ownership,
independent liveness and atomic startup/auxiliary admission. Provider replay checks
both the committed allocation and current Redis gate. Startup and release counters
are reconstructed independently of resource balances. Queued and provisioning facts
are redispatched with 500 ms coalescing. Docker/Libvirt read explicit host mounts;
Kubernetes reads node metrics and pressure conditions (unknown PID usage remains
null). Local Compose has read-only host mounts; Kubernetes RBAC adds metrics reads.
Validation: pressure policy 3/3; 16/64 real Redis concurrency 2/2; Redis regressions
13/13; real PostgreSQL ownership plus controlled host-file sampling 1/1; deployment
contracts 15/15; PostgreSQL/NATS recovery 1/1; provider handlers 25/25. Real Linux
host stress and Kubernetes execution remain separate A8/V1 verification items.

A6 completed: `Runtime:CpuOvercommitFactor` accepts 1 (default) or 2 for new
container allocations. Libvirt and auxiliary jobs remain strict. Compose budgets
sum per-service resources and Kubernetes CPU rounding. Replays reconstruct requests
from committed amounts, not a subsequently changed policy. Kubernetes explicitly
sets Requests and Limits; Docker still applies original hard limits. Budget policy
tests 3/3, actual Docker hard-limit inspection at factors 1/2 (2/2), and Kubernetes
Compose manifest regression (1/1) passed. No Kubernetes execution claim is made.

A7 completed: player-safe waiting reasons, cancellable queued environments,
administrator capacity/usage monitoring and per-workload Runtime detail. Capacity
diagnostics distinguish unknown limits, zero usage and negative remaining budget;
global waiting gauges are deduplicated across Workers. HTTP permission/disclosure
tests 10/10, real PostgreSQL/Redis diagnostics 2/2, frontend tests 584/584,
architecture audit and typecheck passed. Nuxt production generation passed with
existing bundle/Nitro warnings. OpenAPI export and repeated SDK generation passed.
The optional shadcn CLI docs command could not resolve upstream packages; official
component documentation and the installed primitives were used without dependency changes.

B1 completed: shared current membership, track resolution, interaction and stable
ordering policy. Flag and Patch blood generation excludes nonparticipants and uses
earlier facts rather than completion order. Public Patch blood metadata follows the
same completion kind as scoring; numerical score calculations are unchanged.
Leaderboard regression baseline and after-change runs both passed 53/53; new shared
eligibility/Flag/Patch tests passed 3/3, real PostgreSQL gameplay ordering 18/18 and
Patch target persistence 1/1. Historical qualification remains a separate evidence
question; B2 adds track context and ordered events to the preview reader.

B2 completed: repeatable-read evidence now preserves event identity, state, time and
parent relationships. Each fact reads a latest prefix of 128 events with truncation
detection; an independent bounded query reads eligibility adjustments. CTF Flag and
Patch facts share current track/interaction rules. Legal rejudge changes and retained
results are informational; ambiguous time ties, legacy missing fields and unlinked
duplicates require review. Current projection blood rank never becomes an automatic
historical correction. New blood events reference the adjudication event. Analyzer,
HTTP and real PostgreSQL tests passed 21/21, including bounded queries, read-only
snapshot behavior, event-prefix truncation and track/Patch handling.

B3 completed: typed preview classifications and optional informational findings,
signed single-fact evidence pages (50 default / 100 maximum), observer/internal-team
scope isolation and on-demand bilingual UI. Historical analyzer/HTTP/real PostgreSQL
tests passed 22/22; frontend 588/588, architecture audit and typecheck passed.
OpenAPI export and two SDK generations produced identical SDK bytes. The concurrent
CAP workload configuration was independently committed as `e4e6a2a44`; this step
extends that contract without including the original question-editor changes.

Pool diagnostic follow-up: an individual undersized candidate no longer produces
the "exceeds all nodes" reason. Only a complete bounded pool inventory where every
node is undersized supports that conclusion; a larger pressure-blocked node retains
its pressure reason. Real Redis pool/admission tests passed 4/4.

A8 completed locally: four strict/shared × 16/64 real Redis/Docker load cases passed,
covering idle, startup burst, continuous load, checker reserve, 12 isolated Redis
losses and 66 exact provider cleanup/release cycles. A separate Linux daemon-host
experiment passed with the default 90%/20-second CPU threshold and three healthy
recovery samples. Default configuration remains factor 1. Recovery/rollback guidance
and repeatable measurement scripts are included. Raw samples and warmed baseline/
current leaderboard benchmarks are retained under `docs/validation/runtime-capacity-20260918`.
The measured CTF projection's latency and allocations remain within the 5% threshold;
this does not establish a platform-wide HTTP or crash-recovery latency guarantee.

Preview follow-up: independently bounded adjudication and eligibility prefixes now
share one database read, restoring the seven-query bound. Eligibility-prefix
truncation is explicitly reflected in completeness. Historical tests passed 23/23,
including the new 65-adjustment boundary. Fifty-sample preview measurements are
retained for both the original reader and the extended reader; the latter does more
work and does not meet a 5% latency comparison against the older audit semantics.

Recovery follow-up: Redis inventory is bounded across all scanned claims and runs
outside the PostgreSQL critical section; Kubernetes observes the same attested
node selector as workload scheduling. Auxiliary creation rechecks the committed
allocation and processing fact, rejecting owner changes and completed facts.
Admission checks now follow terminal-work detection so stale commands can clean up
even without healthy observations. Development host registration, SQL guardrails
and the migration expectation were updated. Scoped tests passed: persisted claims
2, publisher 1, architecture 5, development hosting 1, observation scope 1,
real PostgreSQL/NATS delivery 2, model schema 1. The force-delete file-count test
also fails unchanged at baseline `957a05e6b` (expected 4, actual 5); it is outside scope.

Use `backend/scripts/Verify-CoreRecovery.ps1` for scoped checks. Integration tests
must use real PostgreSQL/Redis/Wolverine/NATS and Docker. Source-only assertions or
EF InMemory do not establish relational or recovery behavior. Compare performance
on the same machine with the same corpus, retaining distributions and call counts.
No Kubernetes/Libvirt runtime or <=5% performance claim is made without measurement.

Wolverine is pinned to 6.30.3. Consult the official EF transactional outbox,
PostgreSQL persistence, NATS transport, handler discovery, Sticky handlers and
Singular Agent documentation and verify APIs against the pinned package. Transport
remains the current NATS + PostgreSQL durability composition.

V1 execution record: Release build/analyzers, 1356 non-integration tests, 49 scoped
core checks, the final 23 historical checks, frontend 588 tests/typecheck/architecture/
production generation, EF drift and OpenAPI/SDK idempotence passed. Full-suite
remaining failures and unverified environments are explicitly recorded in
`docs/runtime-capacity-validation.md`. The extended preview's 500-fact latency
comparison exceeds 5%; the full crash-injection and platform-wide performance
matrix is not established. V1 is therefore not marked complete. All changes stay
local; the original 19-file patch is byte-identical to its initial capture.
