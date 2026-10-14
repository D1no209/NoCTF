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

## Staff Webhook capture

An ordinary reply previously read every consultation's history and every incident in
the competition before capturing its event. Existing baselines now scope ordinary
question/incident changes to affected items. Initial capture, subscription rebuilds,
metadata changes, ban/appeal operations and explicit resync retain full projection.
A relational catch-up check preserves updates committed in another context or through
bulk fact changes. Closed historical tombstones cannot force permanent full capture.
The source mutation, projection and event sequence still commit together.

The equivalent retained full-capture path is the reference, not a historical binary.
Each corpus has 32 replies per consultation and half as many incidents as consultations.
The experiment captures one participant reply, rolls back the transaction, alternates
variants for 3 warmup and 10 measured pairs, and compares canonical event contents.

| Consultations | Variant | Median elapsed (observed range) | Median allocated bytes | Read rows / cells |
| ---: | --- | ---: | ---: | ---: |
| 32 | Full capture | 10.96 ms (10.29–16.66) | 2,485,876 | 1,138 / 11,962 |
| 32 | Scoped capture | 10.36 ms (9.90–11.32) | 1,459,660 | 41 / 396 |
| 512 | Full capture | 137.96 ms (127.43–176.59) | 26,817,852 | 18,058 / 190,162 |
| 512 | Scoped capture | 24.93 ms (23.49–36.56) | 1,465,440 | 41 / 396 |

Full/scoped reads issue 10/9 SQL commands. Small-corpus elapsed times are close;
the large-corpus result supports lower history transfer, materialization and capture
cost. The catch-up EXISTS still examines existing competition work items, and the
competition stream remains sequential. This is not constant total database work.
Timing excludes transaction creation, source preparation, SaveChanges/commit and
delivery. Allocation counters are process-wide. SQL diagnostics run separately from
timing; both variants receive the same subscription probe and pending mutation.

Six PostgreSQL correctness tests cover full/scoped event equivalence and commit,
unaffected pending items, first capture, cross-context metadata edits, bulk incident
changes/removal, recovery from tombstones, and nullable platform-question roots/replies.
The nullable cases exposed and fixed an existing `.Value` access on reply notifications
that legitimately carry no question target. Raw samples: `staff-v3/staff-webhook-capture.json`.

```powershell
$env:NOCTF_REQUIRE_DOCKER_INTEGRATION = 'true'
$env:NOCTF_STAFF_CAPTURE_MEASUREMENTS = 'backend/artifacts/performance/optimization-next-20261013/staff-v3'
dotnet backend/tests/NoCTF.Tests/bin/Release/net10.0/NoCTF.Tests.dll --treenode-filter '/*/*/StaffWebhookCaptureScopeTests/*' --maximum-parallel-tests 1 --report-trx --results-directory backend/artifacts/performance/optimization-next-20261013/staff-tests-v3 --no-ansi --progress off
```

## Rejected capacity partition experiment

A candidate replaced the global process gate and EF concurrency row with one per
resource domain. Under the real PostgreSQL Serializable workload, domain predicates
still encountered serialization conflicts. A small relation/index page can share SSI
predicate locks across logically distinct domains; partitioning the logical key alone
does not eliminate those conflicts. Predicate-lock sharing is a plausible contributor,
not a captured execution-plan/lock diagnosis; the rejection rests on completion,
retry, CPU and latency measurements.

The workload uses 128 claims, concurrency 16 and four stable NATS domain owners.
With the original three-attempt policy, the candidate completed only 78–82 claims per
measured pass. After narrowing additional reads and adding bounded jittered retries
(eight complete attempts), it completed 122–127, still short of 128. The latter had
260–272 retries per pass, observed per-pass P95 request latencies of 531.54–640.98 ms
versus 222.71–258.49 ms for the full-reader global reference, and 1,812.50–2,281.25 ms
of client CPU versus 671.88–953.12 ms. All five reference passes completed 128/128.

This candidate is rejected. Failed raw runs remain under `capacity/`, `capacity-v2/`
and their test-result directories; incomplete batches are not reported as speedups.
The final implementation retains global coordination and reduces the reservation
read shape while fixing stale-context and ownership-handoff correctness. The discarded
coordination-table migration is removed through EF tooling.

## Final capacity read optimization

The accepted implementation retains the global local gate and original `Id=1` EF
ledger. It reads narrow reservations for all owners in the selected resource domain,
including a previous owner's pending allocations, and uses current allocation rows
plus a Runtime concurrency check when writing a tracked graph. The only generated
schema change is `20261013151905_IndexRuntimeCapacityResourceDomain`, adding the
allocation domain index. The upgrade/downgrade test preserves allocations and the
existing ledger stamp.

Both variants use the current schema/index and Serializable isolation. The reference
retains the `75f8790b` global claim algorithm. Each pass has 128 claims at concurrency
16, with 2 warmup pairs and 5 alternating measured pairs per domain count. The table
shows medians across the five passes; P95 is the median of each pass's request P95.

| Domains | Variant | Batch elapsed (observed range) | Request P95 | Client CPU | Allocated bytes |
| ---: | --- | ---: | ---: | ---: | ---: |
| 1 | Global reference | 2,081.29 ms (1,911.40–2,253.75) | 280.97 ms | 937.50 ms | 309,631,272 |
| 1 | Global narrow reads | 1,363.06 ms (1,272.61–1,393.30) | 179.74 ms | 750.00 ms | 256,039,848 |
| 4 | Global reference | 2,031.07 ms (1,799.37–2,141.21) | 289.83 ms | 828.13 ms | 262,133,760 |
| 4 | Global narrow reads | 1,390.25 ms (1,354.28–1,403.01) | 187.76 ms | 859.38 ms | 252,578,064 |

Every warmup, measured and diagnostic pass completed 128/128 with valid allocation
invariants and no exceptions. All measured samples had zero retries, so the broader
retry allowance did not contribute to their completion or timing. Four-domain client
CPU is slightly higher; these samples do not establish a CPU improvement there.
Separate concurrency-one diagnostics show returned cells of 322,752 → 37,504 for
one domain and 83,136 → 12,928 for four. Database CPU and network bytes are unmeasured.

The candidate permits eight complete transaction attempts with jittered backoff
(at most 1,001 ms scheduled delay plus I/O), versus three in the reference. Exhaustion
still fails. Caller-owned transactions propagate conflicts to their owner. Dispatch
and release message publication occurs after commit outside the retry boundary.
PostgreSQL regressions cover competing processes, old-owner reservations, identical
workload identities, stale scoped contexts, ambient rollback and publication failure.

Timing includes context creation, NATS registration reads, SQL, SaveChanges and commit;
it excludes seeding, owner refresh, verification and release. No provider containers
are created. Release wakeups are a no-op in both measured variants. Global serialization
remains a scaling limit, and no maximum platform throughput is claimed. Raw results:
`capacity-v3/capacity-domain-transactions.json`.

```powershell
$env:NOCTF_REQUIRE_DOCKER_INTEGRATION = 'true'
$env:NOCTF_CAPACITY_DOMAIN_MEASUREMENTS = 'backend/artifacts/performance/optimization-next-20261013/capacity-v3'
dotnet backend/tests/NoCTF.Tests/bin/Release/net10.0/NoCTF.Tests.dll --treenode-filter '/*/*/RuntimeCapacityDomainTests/*' --maximum-parallel-tests 1 --report-trx --results-directory backend/artifacts/performance/optimization-next-20261013/capacity-tests-v3 --no-ansi --progress off
```

## Final validation

The selected runs contain 100 distinct passed test cases, including the isolated
component measurements, real PostgreSQL/Redis/NATS behavior and architecture checks.
The final correctness repeat intentionally skips the capacity measurement already run
in isolation; its claim code was unchanged by the last release-publication boundary fix.
Release builds have zero warnings/errors. EF's read-only `has-pending-model-changes
--no-build` check reports no model changes. `validation-summary.json` lists the selected
TRX files and individual passed cases. Earlier failed runs remain available, including
the rejected partition experiment and corrected fixture/nullable-source failures.

TUnit reported its external-span cap in earlier fixture runs; no trace-completeness
claim is made. SQL row/cell evidence uses the separate explicit reader instrumentation,
and timed reads do not use that wrapper. These local experiments establish the listed
component behavior and distributions, not end-to-end production capacity.
