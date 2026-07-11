# Backend Architecture and Performance Remediation Plan

## Phase 1: Restore valid host composition

1. Extract API/Worker service registration into testable composition helpers.
2. Remove unconditional local-storage directory initialization in S3 mode.
3. Add dedicated Local URL signing configuration and Docker Compose parity.
4. Preserve Penetration public endpoints during Worker synchronization.
5. Add API/Worker/Runner composition smoke tests with scope validation.
6. Fix clean project plugin build/copy dependencies.

## Phase 2: Make plugins host-aware

1. Add additive `PluginHostRole` and host-aware module interface.
2. Move plugin loader/load context to a shared host-neutral assembly.
3. Split built-in module registration into common, API, and Worker capabilities.
4. Add lightweight challenge scoring-owner metadata and remove submission-handler discovery from rebuilds.
5. Load the same plugin catalog from API and Worker outputs.
6. Move AWD/AWDP/KoH engines out of API and into Worker.
7. Add Redis Stream Worker notifier and API relay with consumer-group recovery.

## Phase 3: Repair scoring hot paths

1. Add batch score-signal/event persistence primitives with idempotent conflict handling.
2. Remove CTF/Penetration incremental-then-rebuild duplication.
3. Convert rebuild deletion to set-based operations and batch solve/signal reads.
4. Batch AWD current-round check/attack reads and scoring writes.
5. Batch AWDP round rows/events in one transaction and recover `RoundScoring`.
6. Poll KoH agents concurrently with bounded fan-out and one leaderboard refresh.
7. Add PostgreSQL correctness and query-count regression tests.

## Phase 4: Optimize public and administrative reads

1. Replace Redis per-team hash reads with multi-field reads.
2. Refactor team detail into target SQL aggregates and conditional member loading.
3. Bound and version AWDP snapshot caching; aggregate timeline in SQL.
4. Pre-serialize SSE snapshots and emit heartbeats when unchanged.
5. Replace repeated challenge/hint scans with maps/lookups.
6. Batch storage reference checks.
7. Add bounded/keyset pagination where response growth is unbounded.

## Phase 5: Runtime correctness and capacity

1. Add hot-query indexes and migration cleanup.
2. Add distributed generic instance transition locking.
3. Make Penetration counter updates atomic and dynamic flag lookup indexed.
4. Make background-task recovery conditional and task claiming use PostgreSQL skip-locked semantics.
5. Add bounded Worker pools by task class.
6. Add Runner operation IDs, receipts, and concurrency admission.
7. Bound Kubernetes/Docker output and use independent cleanup cancellation.
8. Replace fire-and-forget log broadcast with a bounded channel.
9. Dispose/reuse Docker, S3, HTTP request, and response resources.

## Phase 6: Acceptance

1. Run locked restore, Release build, analyzers, format verification, and all tests.
2. Run PostgreSQL and Redis integration suites plus query-count/performance regression tests.
3. Verify migrations from an empty PostgreSQL database and check model drift.
4. Regenerate OpenAPI/client and build the frontend.
5. Validate Compose/Kubernetes manifests and build API/Worker/Runner Docker images.
6. Review the complete diff for plugin/API compatibility, unintended secrets, and unbounded paths.
7. Commit changes by logical phase; do not push or create a PR unless explicitly requested.

