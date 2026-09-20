# Runtime capacity contract

Runner admission uses the latest observed free resources of the whole execution
domain. A running Runtime's declared limit is not a lifetime capacity debt.

## Admission calculation

For every fresh observation, Runner calculates:

```text
CPU free       = total CPU × (1 - current usage ratio)
CPU headroom   = total CPU × (1 - CpuHighRatio)
memory free    = MemAvailable
memory headroom= MemTotal × MemoryLowRatio
PID free       = PID capacity - PID used
PID headroom   = PID capacity × (1 - PidsHighRatio)

admission available = max(0, free - headroom - startup reservations)
```

The defaults retain 10% CPU, 10% memory and 10% PID headroom. Observations older
than 15 seconds, recent OOM kills, provider pressure, sustained CPU pressure and
memory/PID pressure block new starts. Recovery still requires the configured healthy
sample window.

Docker and Libvirt use the observed host/cgroup execution domain. They do not use
`Runner:Capacity` as a scheduling ceiling. Kubernetes uses Node Allocatable and the
Metrics API. Kubernetes PID usage remains unknown when the platform cannot observe
it; admission then relies on PIDPressure and each workload's enforced PID limit
instead of inventing a numeric PID balance.

## Startup reservations

Every new workload atomically reserves its complete declared hard limit while it is
starting. A Runtime entering `Running` records `completedAt`, but its startup
reservation remains until Runner publishes the first observation whose timestamp is
at or after `completedAt`. This closes the sampling race between provider creation and
host metrics.

Provisioning failure, cancellation and confirmed resource removal release the
reservation idempotently. Primary and auxiliary work use the same resource formula.
They retain separate startup concurrency limits (`MainStartupConcurrency=2` and
`AuxiliaryConcurrency=1` by default); no fixed Checker/Patch resource reserve exists.

`RuntimeResourceLimits` remain provider hard limits. Kubernetes Requests equal Limits
for new work. `capacity_allocations.budget` remains only as rolling-upgrade evidence
for allocations written by older versions; schema 3 admission uses `limit` as the
startup reservation.

## Redis schema and recovery

Capacity registration schema 3 stores observed total/free resources, safety
headroom, startup reservations, final admission availability, observation state and
primary/auxiliary startup counts. Claim hashes retain ownership and declared limits.
Claims with `starting=0` do not reduce admission availability.

The Worker accepts schema 2 and schema 3 during rolling upgrades. Schema 2 keeps its
legacy budget behavior until that Runner upgrades. A schema 3 Runner pauses admission,
owns the execution domain, compares PostgreSQL allocations with provider resources and
rebuilds Redis before becoming Ready:

- `Provisioning` restores a full startup reservation.
- `Running` and `Stopping` restore ownership without a startup reservation.
- `Failed` resources retain ownership until cleanup.
- absent or stale Redis claims are removed during reconciliation.

Existing provider resources are never recreated or stopped by the capacity upgrade.
Queued Runtime rows remain PostgreSQL facts and are retried by the Singular Agent.

## Monitoring contract

Platform monitoring exposes:

- `observedTotal`
- `observedAvailable`
- `safetyHeadroom`
- `startupReserved`
- `admissionAvailable`
- `declaredLimits` (observation only; not admission debt)
- the raw observation, including OOM/provider pressure
- `startingPrimary` and `startingAuxiliary`

Waiting reasons distinguish actual CPU, memory and PID shortages, startup concurrency,
stale observation, node pressure, provider unavailability and reconciliation.

## Verification

Run the Release verification and the isolated capacity suite:

```powershell
backend/scripts/Verify-Backend.ps1 -Configuration Release
backend/scripts/Measure-CoreCapacity.ps1
```

The production-equivalent fixture models a 4 CPU / approximately 8 GiB node with
three idle Runtime claims at 0.5 CPU / 256 MiB / 128 PID each. The fourth Runtime must
be admitted, while 64 concurrent claims remain atomic and never produce negative
availability.
