# Projection and capacity experiments — 2026-10-13

These are isolated Release component experiments against disposable real dependencies.
They do not measure HTTP QPS or maximum platform capacity. Raw output and machine
metadata are under `backend/artifacts/performance/optimization-next-20261013/` (ignored).
The host is Windows 11 build 26220, Intel i9-14900HX, 32 logical processors, 64 GiB RAM,
Turbo power plan, Docker 29.8.2. Exact runtime/SDK, revision, dirty state and commands
are retained in `environment.json` and each experiment's JSON.

## AWD full history projection

The full scoring projection previously materialized complete competition, round,
flag-issue and checker-fact graphs. The reader now selects only scoring fields and
ordered evidence needed by the existing projector. It still reads all required
history, applies observation cutoffs, and performs a complete relational projection.

The frozen reader from `75f8790b` is the baseline. Both readers use the same database
and RepeatableRead transaction, with 3 warmup pairs and 10 measured pairs in alternating
order. The corpus has 24 teams, 4 questions, 24 rounds, 96 rules, 9,216 flag issues,
3,072 captures, 4,608 checks and 36,096 flag-evidence children.

| Metric | Full entity baseline | Narrow projection |
| --- | ---: | ---: |
| Median elapsed | 859.55 ms | 555.93 ms |
| Observed elapsed range | 812.90–1,020.54 ms | 522.22–622.45 ms |
| Median allocated bytes | 127,041,248 | 93,127,456 |
| SQL commands | 14 | 14 |
| Returned rows | 55,601 | 55,416 |
| Returned cells | 1,060,915 | 417,014 |
| Scoring output rows | 2,016 | 2,016 |

Elapsed and allocation samples have no reader interceptor. SQL/row/cell diagnostics
come from separate equivalent reads after timing. Cells count returned row width,
not network bytes or database rows scanned. Allocations are process-wide and include
possible framework/background activity. Both readers use warm PostgreSQL buffers,
connection pools and EF query caches; this does not establish a production latency SLO.

`AwdScoreProjectionReaderPersistenceTests` verifies exact canonical scoring equivalence
at historical cutoffs, during a pause, with persisted/preview rule changes, and with
deleted questions/scoring windows. It also rejects sensitive non-scoring columns in
the optimized SQL. Raw samples: `awd/awd-full-history-projection.json` and CSV files.

```powershell
$env:NOCTF_REQUIRE_DOCKER_INTEGRATION = 'true'
$env:NOCTF_AWD_PROJECTION_MEASUREMENTS = 'backend/artifacts/performance/optimization-next-20261013/awd'
dotnet backend/tests/NoCTF.Tests/bin/Release/net10.0/NoCTF.Tests.dll --treenode-filter '/*/*/AwdScoreProjectionReaderPersistenceTests/*' --maximum-parallel-tests 1 --report-trx --results-directory backend/artifacts/performance/optimization-next-20261013/awd-tests --no-ansi --progress off
```

## Shared leaderboard rebuilds

Four independent NATS connections, Redis-backed cache providers and process-local
lock instances model four nodes in one test process. PostgreSQL contains 250 facts;
16 readers start together after invalidation. The same cache reader runs with the
cluster computation coordinator disabled/enabled in three alternating repetitions.

| Repetition | Uncoordinated projections | Coordinated projections | Coordinated ready responses |
| --- | ---: | ---: | ---: |
| 1 | 4 | 1 | 16/16 |
| 2 | 16 | 1 | 16/16 |
| 3 | 16 | 1 | 16/16 |

These are computation counts, not latency or throughput measurements. Concurrent
publication fences can reject an uncoordinated result and cause another local reader
to rebuild; the count can therefore exceed the node count. With the shared lease,
all readers returned the same complete version. Four invalidation-triggered Worker
messages also produced one projection. Explicit refresh after a database change
without invalidation still recomputed and exposed the new score.

Real NATS tests revoke a lease, acquire a successor, verify that old ownership cannot
publish or release the successor, and exercise waiting cancellation and an unrelated
competition. Fake-clock tests cover periodic renewal failure and simultaneous timer/
publication renewal without CAS races. The existing publication fence remains the
authority for accepted versions; lease confirmation and publication are not one
atomic transaction. Wolverine adapters were regenerated using `codegen write`.

Validation: 20 leaderboard persistence tests, 2 lease-renewal tests and 8 event-merge
tests passed. Raw counts: `leaderboard/leaderboard-rebuild-contention.json`.
