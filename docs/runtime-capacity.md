# Runtime capacity contract

This document describes the approved capacity-model extension. It does not change
challenge schemas or select a provider/pool from challenge data.

## Limits, budgets and observation

- Limit is the workload's enforced resource ceiling; Budget is its allocation
  charge. Actual Usage is independently sampled. Allocatable is the configured
  deployment allowance bounded by the observed execution resource domain.
- `Runtime:CpuOvercommitFactor` defaults to `1` and supports `2` for new container
  allocations. Memory, PIDs, Libvirt and Checkers remain strict. Existing allocations
  retain their original amounts. Compose charges one aggregate of service budgets.
- Kubernetes explicitly receives Requests and Limits, with CPU rounded upwards to
  millicores before accounting. Missing Budget in an old command means full limits.
- `Runner:Admission` configures sampling (5 seconds), freshness (15 seconds), CPU
  pressure duration (20 seconds), high/recovery thresholds (90%/80%), available
  memory thresholds (10%/15%), PID thresholds (90%/80%) and recovery samples (3).
- A recent OOM, stale observation or unavailable provider blocks new work. CPU
  observations do not rewrite budget balances. Unknown measurements remain unknown.
- Main startup concurrency defaults to 2. Auxiliary concurrency defaults to 1;
  long-lived workloads leave 256 MiB, 0.25 CPU and 128 PIDs reserved for Checkers.

## Execution-domain observation

Linux Docker Runner uses a local Unix socket and explicit read-only host mounts at
`/host/proc`, `/host/sys/fs/cgroup` and `/host/etc/machine-id`. These must describe
the actual daemon host. The Runner container's own limits are not host capacity.
Remote Docker and Windows named-pipe hosts without trusted host observation cannot
accept production work through this sampler. Development mode remains separate.

Kubernetes requires read access to node metrics, node conditions and the runtime
namespace. PID pressure conditions gate admission; unavailable numeric PID usage
is returned as null, not zero. A PostgreSQL session advisory lock prevents multiple
active Runner owners of the same Docker engine, Kubernetes cluster or Libvirt host.
Loss of that ownership stops the Runner. Liveness renewal is independent of a long
inventory scan, so stopping and cleanup can still reach a reconciling node.

## Recovery

`runtime_instances.capacity_allocations` is a versioned JSONB document containing
only active workload identity, Runtime/GameplayFact association, Runner/resource
domain and Limit/Budget amounts. It is not a definition snapshot, scheduling cursor
or operation history. Only EF CLI generates migrations.

Heartbeat expires; the ledger and claims do not. Redis admission and PostgreSQL
allocation/outbox commits have explicit recovery handling. Provider creation checks
the committed allocation and current admission gate again. Cleanup confirms actual
resource absence before removing allocation metadata and publishing durable release.
Repeated claim/release is idempotent. A missing ledger cannot be credited by cleanup.

New typed claims are atomically indexed as unconfirmed in Redis. The ordinary
resource audit confirms committed allocations or, after provider absence and a
second PostgreSQL check under the allocation lock, releases rolled-back claims.
Cancelled/unassigned Runtime rows are included through claim identity. The index
has no TTL, processes bounded batches and rotates uncertain entries; unknown
provider state never returns budget. This does not depend on Runner restart.

Recovery pauses admission, drains coordinated resource mutations, reads provider
inventory outside the database transaction, and rebuilds under a short allocation
critical section. Legacy full-budget claims are persisted before their Redis keys
are replaced. Missing evidence blocks admission instead of treating resources as
absent. Quota reductions preserve allocations and can produce a negative balance.

The Singular Agent rebuilds queued/provisioning dispatch from PostgreSQL every
five seconds; scans use bounded keyset batches. Capacity-release wakeups coalesce
for 500 ms. There is no per-Runtime periodic scheduled-message chain or persisted
business next-run field.

## HTTP and UI

Player Runtime responses include an optional stable `waitingReason`, scoped to the
authorized player's own Runtime. They contain no host usage or allocation amounts.
Queued environments can be stopped while waiting.

Platform monitoring includes `capacity` with availability, a bounded Runner snapshot,
separate liveness/admission, Limits, reserved Budget, Allocatable, remaining budget,
Usage and observation time. Negative remaining values denote an over-budget node.
Unknown legacy limits remain null. Platform Runtime details include per-workload
amounts; ordinary competition staff do not receive these allocation details.
Monitoring refresh uses the existing 15-second page timer; pages never start samplers.
Global waiting counts use a maximum across duplicate Worker reporters, not a sum.

The frontend consumes generated OpenAPI types, keeps logic in features and uses
shared UI primitives plus stable Chinese/English keys.
