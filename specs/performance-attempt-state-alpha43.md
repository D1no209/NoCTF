# Attempt-state read performance investigation

## Reproduction and environment

- Starting production image: `noctf-host:0.2.1-alpha.42`, digest `sha256:3d5b04d374ffbcaed70a95cd8b284f71782207b620abd06278a8c4f891a21729`.
- Repository starting commit: `5531ee0f1dd881e3eda8e478f40ff515ee62f404`; the worktree was already heavily dirty (about 700 entries) before this investigation. Existing changes were not reset or overwritten.
- Local measurement: Windows 11 x64, Intel Core i9-14900HX, .NET SDK 10.0.401, Docker Engine 29.7.2, Release `net10.0` test assembly, PostgreSQL 17.10 Testcontainer. These are component measurements, not production-equivalent HTTP measurements.
- Run: `dotnet backend/tests/NoCTF.Tests/bin/Release/net10.0/NoCTF.Tests.dll --treenode-filter '/*/*/CompetitionPracticeModePersistenceTests/Challenge_attempt_state_uses_fewer_commands_than_full_admission' --maximum-parallel-tests 1 --minimum-expected-tests 1`.
- Raw per-iteration timings: ignored `backend/tests/NoCTF.Tests/bin/Release/net10.0/TestResults/performance/challenge-attempt-state-current.csv` (50 sequential warm samples; full admission and narrow read alternate on the same seeded CTF practice fixture).

## Evidence and ownership

- The previous challenge-detail attempt-state use case called the same `LoadAdmissionAsync` as Flag intake. That materializes competition configuration, challenge rules and definition child graphs and, for CTF practice, checks Runtime state even though the detail response only needs limits, accepted count and solved state.
- In the 50-sample component run, full admission p50/p95 was **27.0/34.6 ms**; narrow attempt-state read p50/p95 was **3.2/3.5 ms**. A command-count interceptor also verifies the narrow path uses fewer EF reader commands, at most four on this CTF fixture. The experiment identifies redundant database read/materialization work in the detail path; it does not establish a production HTTP improvement by itself.
- A read-only Prometheus query on the `alpha.42` production host returned approximately 101 challenge-detail requests in the preceding hour and a histogram-estimated p95 of 105 ms. The window includes cold and warm requests and is not a matched before/after workload.
- Flag intake and its transaction recheck still use the full admission snapshot. `AdmissionLoad` is now specific to submission intake; `ChallengeAttemptStateRead` measures the detail path separately.

## Validation and remaining limits

- PostgreSQL integration tests verify the CTF official/practice window, AWDP break-attempt fallback limit, and relative command count. Unit endpoint tests verify the authorized team identity is reused. These do not replace the full four-mode and concurrent-write suites.
- Runtime dispatch now exposes stage durations for successful dispatch paths. Its previous p95 had only about nine production samples; no dispatch behavior was changed on that evidence.
- Cold event/schema/catalog spikes were not repeatably attributed to a dominant cost. No query-compile, startup prewarm or global EF behavior change was made.
- `alpha.43` passed the Release build, 1,195 non-integration tests, 275 container integration tests (six external-environment skips), and all four Full E2E modes; one AWDP E2E attempt failed with a transient `PlatformFailed` Fix and passed when rerun in isolation. The first container-integration run exposed an inaccurate test outbox that published before commit; after the test substitute was corrected, the full suite passed.
- The `alpha.43` image was deployed from `/opt/noctf`, but the production Compose project is also managed from `/opt/noctf-v2`, which later recreated the Host from its own `alpha.42` configuration. The active PostgreSQL and upload bind mounts belong to `/opt/noctf-v2`. The worktree and image validation did not change the EF model or API success contract.
- Production natural traffic showed `ChallengeAttemptStateRead` p95 around 5 ms over 50 samples, but the full challenge-detail histogram still showed p95 around 96 ms in a mixed-version hour. This is not an equivalent warm before/after comparison and does not demonstrate the 30 ms target; further route-stage diagnosis is warranted before claiming an end-to-end speedup.
- A separate malformed polymorphic JSON POST issue was fixed in `alpha.44`: missing `mode`/`kind` now returns HTTP 400 rather than an unhandled 500, while unknown discriminator and valid current payload behavior stay strict. `alpha.44` was deployed after a fresh stopped-Host PostgreSQL/upload/config backup, now retained at `/opt/noctf/data/backups/pre-alpha44-20260925T0357Z`. The then-active `/opt/noctf-v2` deployment was subsequently moved to the sole canonical `/opt/noctf` path without altering its bind-mounted data.
- No synthetic Flag writes were made against production. The backup is root-only on the same host for cutover rollback, not an off-site disaster-recovery copy.
