---
name: perf-db-statement-overhead
description: Under k6 load the PostgreSQL bottleneck is statement count (connection reset + per-request round trips), not query execution time
type: lesson
---

**Why**: The 200 VU baseline (perf/results/20261019122442-8ea3ee) showed 3.08M statements but only ~82 s total execution
time, while the PostgreSQL container used 2-8 cores. 2.7M of those statements were Npgsql's connection-reset sequence
(CLOSE ALL, SET SESSION AUTHORIZATION DEFAULT, RESET ALL, UNLISTEN *, pg_advisory_unlock_all, DISCARD SEQUENCES,
DISCARD TEMP), which runs once per pooled-connection checkout because auto-prepare prevents plain DISCARD ALL.
The host itself was mostly idle (thread-time trace dominated by idle waits).

**How to apply**:
- Keep `NoResetOnClose = true` in `PostgreSqlPersistence`; it is safe only because the app uses no raw SQL, SET,
  LISTEN, temp tables or advisory locks. Re-check before adding any session-state feature.
- When optimizing, count round trips per request (pg_stat_statements calls) before tuning individual queries.
- Map queryIds to SQL offline with `perf/Inspect-Database.ps1 -RunId <id> -QueryFile <sql>` against pg_stat_statements.
- The other big amplifier is fan-out per publish/event (every client re-downloads the scoreboard; every
  Team event was pushed to every connection). Check `hub_events` and response size before tuning SQL.
- Flag intake must keep the command receipt inside a transaction (`TransactionalRequestReplay.Store` guard);
  removing it broke every idempotent submit. Keep the transaction late (write + commit only).
- k6 `data_received` counts decompressed bytes; use `scoreboard_compressed_responses` to check compression.
- Each perf run keeps a Host copy (`perf/.local/<id>/application`), raw traces and Docker volumes. E: filled up
  after ~80 runs, which made every stage ~1.5x slower and broke trace analysis. Check free disk before
  trusting a slower run; prune `.local/*/application`, `*.nettrace`/`*.etlx` and old `noctf-perf-*` volumes.

**Related**: perf/README.md, perf/optimistic-implementation-status.md
