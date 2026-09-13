# Leaderboard projection performance audit — 2026-09-13

## Symptom and scope

Leaderboard rebuilds consume disproportionate CPU and allocate heavily as the number of teams,
challenges, rounds, and summarized gameplay facts grows. This audit covers the deterministic
in-process projection algorithms and the projection orchestration path. It does not change scoring,
ranking, tie-breaking, blood awards, audience visibility, or the 50-round scoreboard window.

Database execution plans and production process traces are outside this local component benchmark.
They remain separate follow-up measurements because a BenchmarkDotNet run cannot establish remote
PostgreSQL, Redis, NATS, or SignalR latency ownership.

## Reproduction

Baseline commit: `f661da9ae` (benchmark corpus committed with the pre-optimization implementation).

Environment:

- Windows 11 Professional Workstations Insider Preview, build 26220
- Intel Core i9-14900HX, 24 physical / 32 logical cores
- 64 GiB RAM
- .NET SDK 10.0.300; .NET runtime 10.0.8, x64 RyuJIT x86-64-v3
- BenchmarkDotNet 0.15.8
- Benchmark job: ShortRun, 3 warmups and 3 measured iterations, Concurrent Workstation GC
- Power plan during measurement: High performance (restored to Turbo afterward)
- Antivirus reported by BenchmarkDotNet: Kaspersky and Windows Defender

The deterministic corpus contains 12 challenges and 16/64 teams. It exercises:

- CTF dynamic scores, blood rewards, wrong submissions, hints, and manual adjustments;
- AWD 20-round attack, victim-defense, service transition, and aggregate-score paths;
- AWDP break/fix activation, break-gated fixes, penalties, 50-round normalized output, and a
  pause/resume lifecycle;
- KoH repeated control observations and manual adjustments.

Exact command:

```powershell
$env:HTTP_PROXY='http://127.0.0.1:7890'
$env:HTTPS_PROXY='http://127.0.0.1:7890'
dotnet run -c Release --no-build --project backend/benchmarks/NoCTF.Benchmarks -- `
  --filter *Combined* -j Short -e json markdown --allStats `
  --artifacts backend/artifacts/performance/leaderboard-2026-09-13/<run> `
  --wakeLock System
```

Raw local results are under the ignored directory
`backend/artifacts/performance/leaderboard-2026-09-13/`.

## Evidence

The table uses the 64-team mean from equivalent `baseline-valid` and `candidate-final-2` runs.
Allocation is managed allocation per complete `ProjectOutputs` operation.

| Mode | Baseline | Candidate | Time delta | Baseline allocation | Candidate allocation | Allocation delta |
|---|---:|---:|---:|---:|---:|---:|
| CTF | 5.921 ms | 3.457 ms | -41.6% | 10.32 MB | 9.23 MB | -10.6% |
| AWD | 183.549 ms | 73.501 ms | -60.0% | 80.79 MB | 72.02 MB | -10.9% |
| AWDP | 248.813 ms | 202.348 ms | -18.7% | 231.76 MB | 191.38 MB | -17.4% |
| KoH | 2.282 ms | 1.590 ms | -30.3% | 5.57 MB | 5.58 MB | effectively unchanged |

ShortRun confidence intervals are wide for the sub-millisecond and high-allocation cases. The large
AWD effect is also reproduced by the earlier candidate run (72.180 ms), while the baseline runs were
180.370 ms, 183.253 ms, 191.720 ms, and 183.549 ms. AWDP improved in two stages: 217.240 ms after
indexing/sweeping and 202.348 ms after compiling the lifecycle timeline once per projection.

## Dominant causes and fixes

1. **Repeated audience projection.** A published competition projected the complete administrator
   scoreboard, then ran the complete engine again for the participant view. When every challenge and
   every referenced fact/round/aggregate is participant-visible, both inputs are identical. The
   participant view now reuses the first projection only after checking that complete condition. A
   competition containing unpublished or excluded historical challenge data still follows the
   original isolated reprojection path.

2. **AWD service-state lookup was quadratic.** Every team/challenge/round row scanned the global
   service-transition list for its latest preceding value. Transitions are now indexed by
   `(TeamId, CompetitionChallengeId)` and resolved by binary search. Awarded attack membership is a
   hash lookup instead of an `All` scan, and per-team attack statistics are aggregated once.

3. **AWDP break-before-fix lookup was quadratic.** Every correct fix searched all correct facts for a
   preceding break. Facts are already deterministically ordered by `(OccurredAt, GameplayFactId)`;
   a single forward pass now tracks prior breaks and first activations with hash sets.

4. **AWDP settlement repeatedly rescanned activations.** Legacy totals previously counted and updated
   every preceding activation for every change round. A forward count plus reverse cumulative segment
   pass now calculates the same award in linear time after sorting. The normalized 50-round view uses
   a forward active set, while preserving the original output order and per-round entries.

5. **Configuration and lifecycle parsing repeated inside fact loops.** Effective challenge scoring
   settings are now resolved once per challenge. JSON serializer options are reused. Pause/resume
   transitions are sorted once into an immutable projection timeline instead of once per fact and
   once per round boundary.

6. **Per-team rescans.** Manual adjustments are grouped once for all teams, KoH observations are
   indexed by team, and CTF challenge metadata/current scores are dictionaries rather than repeated
   scans and JSON parses.

## Correctness validation

- Release solution build: passed with zero warnings and zero errors.
- Non-integration TUnit suite: 1,283 passed out of 1,284.
- The only failure is the pre-existing unrelated
  `HistoricalAdjudicationPreviewHttpTests.Cursor_is_bound_to_competition_user_and_challenge_filter_and_rejects_tampering`
  assertion because its fixture returns a null `NextCursor`.
- Existing projection tests cover deterministic replay, tracks, blood attribution, AWD attack and
  availability slots, AWDP accumulated activations, pause/resume boundaries, long-history windows,
  rejudging, manual adjustments, and KoH control.
- Benchmark setup replays every corpus and compares the complete legacy and normalized projections by
  serialized value before measurement.

Docker/Testcontainers integration tests were not run during this audit. No persistence model or
migration was changed.

## Residual risks and follow-ups

- AWDP remains allocation-heavy because its public detail model intentionally materializes per-team,
  per-challenge, per-round entries and entry allocations for up to 50 rounds. Reducing this further
  requires a protocol/storage representation decision, not a safe internal loop rewrite.
- `ProjectBundleAsync` still makes separate competition, team, challenge-template, lifecycle, fact,
  and actor queries. A PostgreSQL `EXPLAIN (ANALYZE, BUFFERS)` capture is required before changing the
  query shape or indexes.
- KoH's bounded fact reader contains a correlated prior-state lookup. Its cost depends on production
  cardinality and PostgreSQL plans and was therefore not changed without integration evidence.
- AWD round creation can enqueue multiple durable projection requests across challenges. Coalescing
  those requests safely across worker nodes needs a durable generation-aware design; process-local
  suppression would risk missing a required round refresh.
- Cache misses during the 500 ms invalidation merge window can still race a scheduled rebuild across
  nodes. The publication fence preserves correctness, but an application-level load test is needed to
  quantify redundant rebuilds before changing refresh semantics.
