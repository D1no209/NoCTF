# Backend Architecture and Performance Remediation Design

## Objective

Repair the architecture, correctness, and performance defects found in the 2026-07-12 backend audit while preserving public HTTP routes, response fields, the plugin design pattern, and existing deployment entry points.

Internal plugin contracts may gain additive host-role capabilities. Database migrations, indexes, worker composition, Runner contracts, caches, and internal service boundaries may change. Existing external plugins implementing only `IPluginModule` must continue to load with their current behavior.

## Confirmed Failure Baseline

The remediation starts from verified defects rather than speculative cleanup:

- `NoCTF.Worker` cannot build its service provider because Penetration submission services require an API-only notification dependency.
- Kubernetes S3 deployments attempt to create `/app/uploads` on a read-only root filesystem.
- API hosts AWD, AWDP, and KoH engines while Worker hard-codes only selected plugin services.
- AWDP and AWD round scoring execute database work per team/challenge cell.
- KoH polls agents serially and rebuilds the whole leaderboard once per hill.
- anonymous AWDP snapshot cache entries are never evicted.
- Runner create/compose commands are not idempotent across network ambiguity.

## Architectural Direction

### Host-aware plugin composition

Plugin discovery becomes shared by API and Worker. A new additive host-aware plugin interface receives an `Api` or `Worker` role. Legacy modules continue through the existing `ConfigureServices(IServiceCollection)` method.

- API registers HTTP-facing game modes, challenge handlers, feature providers, and submission admission services.
- Worker registers round engines, durable job handlers, scoring contributors required for rebuilds, and instance maintenance capabilities.
- Runner remains the only production container orchestration boundary.
- built-in plugin build dependencies are explicit and plugin assemblies are copied to both API and Worker outputs from clean project builds.

Submission-handler enumeration will no longer determine score-rebuild ownership. Plugins register lightweight scoring-owner metadata, which removes the `rebuilder -> submission handler -> rebuilder` cycle and allows Worker to load only background-safe capabilities.

### Durable cross-host notifications

Worker-side engines must not reference API SignalR types. Worker publishes typed notifications to a Redis Stream. API replicas consume the stream through one consumer group; a message is delivered to one relay, forwarded through the existing SignalR Redis backplane, and acknowledged only after successful forwarding. Pending entries are reclaimed after a bounded idle period.

API request-path notifications continue to use the direct notifier. Redis stream messages contain stable type identifiers and JSON payloads, and malformed messages are acknowledged after logging so they cannot poison the stream.

### Explicit host composition

API, Worker, and Runner composition is moved into testable registration methods. Every host validates scopes and service construction during startup. Production API and Worker require `Runner:BaseUrl`; in-process Docker remains an explicit Development-only compatibility option.

## Scoring and Database Performance

### CTF and Penetration

Accepted submissions record the canonical submission and score signal once. They do not first execute per-team incremental scoring and then delete that work during a rebuild.

Challenge rebuilds:

- acquire the existing competition-scoped rebuild lock;
- delete owned projection rows with set-based `ExecuteDeleteAsync`;
- load solves and signals in bounded set queries;
- create all replacement events in memory;
- save once per bounded batch;
- invoke plugin rebuild contributors without discovering full submission handlers.

Rebuild failure continues to enqueue a durable job. Multiple requests for the same competition/challenge use a deterministic task idempotency key so bursts coalesce rather than creating an unbounded queue.

### AWD and AWDP rounds

Round engines load competition, teams, challenges, states, checks, attacks, existing round rows, and existing event keys once. They calculate missing facts in memory and persist them in one transaction, split only when parameter or batch limits require it.

- AWD removes per-cell check and attack queries and emits a batch of scoring facts.
- AWDP uses `AwdpConfigResolver.Resolve(competition, challenge)` on already-loaded entities, creates round rows and score events together, and recovers a round left in `RoundScoring`.
- unique constraints remain the final idempotency boundary.

### KoH

Agent HTTP requests use a short configurable timeout, dispose requests/responses, and run with bounded concurrency. Database control updates remain serialized through one scoped context, but all network results are collected first. A competition tick refreshes and broadcasts the leaderboard at most once.

The successful engine timestamp is committed after the poll. A deterministic poll slot is used in signal idempotency keys so a partial retry cannot double award an interval.

## Query and Cache Design

- Redis leaderboard reads use one sorted-set command and one multi-field hash read.
- leaderboard team detail accepts `includeMembers`; anonymous calls never query users or build member history.
- team detail uses target-team aggregates and server-side solve counts/ranks instead of loading all submissions twice.
- expensive public insights receive short, version-aware caching.
- AWDP snapshots query only the current round rows; timeline data is grouped in SQL.
- snapshot cache storage is size bounded and uses a fixed set of striped locks.
- SSE stores one pre-serialized payload per snapshot version, emits only changes, and sends a lightweight heartbeat while idle.
- challenge DTO assembly uses dictionaries/lookups rather than repeated list scans.

## Runtime Correctness and Backpressure

- generic challenge instance transitions use the existing distributed execution lease, preventing concurrent orphan creation.
- Penetration solved counters use an atomic database update; flag lookup uses the indexed normalized hash.
- expired instance cleanup claims records conditionally and never clears a container identifier that changed after the claim.
- expired task recovery uses conditional set-based updates, preventing a renewal race.
- Worker concurrency is configurable and bounded; long patch jobs cannot block every scoring job.
- Runner has bounded operation concurrency/queueing and returns overload status rather than spawning unlimited work.
- Runner create/run/compose commands carry stable operation identifiers. Provider labels and a bounded receipt store make ambiguous retries return the original result.
- Docker failure cleanup uses an independent cleanup timeout.
- Kubernetes log capture and process output are bounded and marked when truncated.
- log streaming uses a bounded channel with a documented drop policy.

## Configuration and Storage

- Local storage creates its directory only when selected. S3 mode does not map or touch the local file route.
- Local URL signing has a dedicated secret shared by API and Worker and validated outside Development.
- API and Worker share typed instance-access configuration. Maintenance preserves an existing public host/URL when runtime status lacks a replacement.
- singleton Docker and S3 clients implement disposal and are reused for signing.

## Indexes and Migration

A new migration adds indexes aligned with audited hot queries:

- AWD flag lookup by competition, challenge, and flag hash;
- AWD victim/round attack lookup;
- latest AWD check by competition, round, team, challenge, and timestamp;
- AWDP patch lists by competition/team/submitted time;
- audit log timestamp keyset scans;
- a partial unique active KoH-control index;
- dynamic Penetration flag hash lookup;
- any lease/status columns needed for instance cleanup and Runner receipts.

Migration code cleans conflicting active KoH rows deterministically before adding the partial unique index.

## Compatibility

- no public route is removed or renamed;
- existing response fields retain their meaning;
- pagination additions are optional and defaults remain useful for existing clients;
- legacy plugins using only `IPluginModule` still register;
- new Runner operation IDs are additive and generated by callers when absent;
- legacy storage URL rows remain readable.

## Verification

Acceptance requires:

- API, Worker, and Runner composition smoke tests with scope validation;
- clean direct project builds proving both plugin host outputs are complete;
- real PostgreSQL integration tests for batch scoring, task recovery races, active KoH uniqueness, atomic Penetration counts, and instance transition leases;
- Redis integration tests for constant-command leaderboard reads, notification-stream claiming, and bounded stale-snapshot behavior;
- query-count tests demonstrating round scoring SQL count is bounded rather than proportional to team/challenge cells;
- concurrency tests for Runner operations, cleanup, Worker task pools, and container creation;
- Release build with zero warnings, full test suite, analyzer/format checks, migration drift, package audit, frontend/OpenAPI consistency, deployment validation, and Docker image builds.

## Delivery Phases

1. Host-startup and configuration P0 fixes.
2. Shared host-aware plugin composition and Worker engine ownership.
3. Scoring batch APIs and AWD/AWDP/CTF/KoH hot-path repair.
4. Query, cache, SSE, and index optimization.
5. Runtime idempotency, cleanup leases, backpressure, and bounded I/O.
6. Full integration, benchmark, deployment, and compatibility verification.

