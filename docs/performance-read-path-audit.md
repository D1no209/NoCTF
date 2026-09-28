# Read-path performance evidence (2026-09-24)

This report is a regression baseline, not a claim of production improvement. It contains no credentials, Flag values, user identifiers, or request payloads.

## Production baseline

- Running image: `noctf-host:0.2.1-alpha.39`; Linux x86-64 host with 7.8 GiB RAM. Requests below were read-only HTTP GETs from the host to the Host container's private network address after warm-up; the bearer credential is deliberately omitted.
- Admin challenge bank, 10 items: eight time-to-first-byte samples in seconds: `0.738, 0.673, 0.678, 0.646, 0.636, 0.638, 0.637, 0.650`.
- Admin challenge detail: ten warm samples ranged from `0.728` to `0.802` seconds. Empty platform active-Runtime page: eight samples `0.082, 0.081, 0.083, 0.079, 0.081, 0.086, 0.082, 0.084` seconds.
- Other sampled warm paths: `/auth/me` about 2 ms; admin competition list about 5 ms; competition-challenge list about 25–30 ms but about 60 KB for one response; admin GameplayFact list about 7 ms; platform users about 7 ms; notifications about 12 ms. Notification and question lists had slower first-use requests but fast subsequent requests, so they are not classified as steady-state bottlenecks.
- The Host was healthy, with low idle CPU and ample available memory. These observations do not establish a historical pre-refactor latency distribution.

The following additional endpoints were sampled three times each after initial warm-up, with one representative production competition and an administrator session. Values are approximate warm private-network TTFB; three samples are for triage, not a p95 estimate.

| Read path | Warm TTFB | Decision |
| --- | ---: | --- |
| Admin competition list / detail | 5 / 22–26 ms | Retain current query |
| Competition-challenge list | 25–30 ms, 60 KB | Switch to summary contract for payload and scale |
| Admin GameplayFact / Runtime list | 7 / 16–19 ms | Retain current query |
| Permission candidates / platform users | 5–8 / 7 ms | Retain current query |
| Notification list / feed | 12 / 6–8 ms | Retain; first notification read was 185 ms |
| Team list / question list | 7 / 8 ms | Retain; first question read was 304 ms |
| Empty platform active-Runtime list | 79–86 ms | Remove empty-page follow-up queries |

The distinction between first-use and warm service time prevents changing fast steady-state paths merely because their first EF query compilation was slower.

## Reproducible EF comparison

`ChallengeReadPerformanceTests.Summary_page_is_measured_against_the_full_graph_projection` uses a PostgreSQL Testcontainer with 55 synthetic CTF templates. Each definition has a Container Runtime and two entries in each ordered child collection. After warm-up, ten alternating request-scoped EF reads compared the alpha.39-equivalent full graph projection with the new summary projection, and single-query detail with split-query detail. The full raw CSV is generated at `backend/tests/NoCTF.Tests/bin/Release/net10.0/TestResults/performance/challenge-read-current.csv` when run locally.

| Iteration | Full list ms | Summary list ms | Single-query detail ms | Split detail ms |
| ---: | ---: | ---: | ---: | ---: |
| 1 | 48.182 | 4.095 | 455.333 | 11.484 |
| 2 | 56.322 | 3.788 | 453.213 | 10.419 |
| 3 | 47.000 | 3.619 | 454.609 | 10.843 |
| 4 | 36.526 | 3.751 | 473.841 | 10.811 |
| 5 | 23.772 | 3.452 | 457.112 | 10.181 |
| 6 | 24.955 | 2.804 | 448.885 | 10.744 |
| 7 | 31.621 | 2.999 | 459.255 | 9.933 |
| 8 | 24.965 | 2.878 | 454.993 | 10.507 |
| 9 | 25.293 | 2.966 | 450.370 | 8.839 |
| 10 | 24.078 | 3.228 | 448.772 | 9.429 |

The candidate shapes are substantially faster in this fixture, but this is a component comparison, not an equivalent production Host/image before-and-after result. PostgreSQL and host versions differ from production. Deployment acceptance must repeat the same production read-only request sequence and separately measure real Flag latency; no synthetic Flag submissions should be sent to production.

## Production after cutover

`noctf-host:0.2.1-alpha.40` was deployed after a fresh database/upload backup. Each comparison below uses ten warm, sequential, authenticated GETs from the same server to the Host container private address. The first two post-restart requests were excluded from warm measurements and are retained here as cold/first-use evidence.

| Path | alpha.39 warm p95 | alpha.40 warm p95 | Response bytes before → after |
| --- | ---: | ---: | ---: |
| Admin challenge bank, 10 items | 714 ms | 5.8 ms | 11,375 → 2,803 |
| Admin challenge detail | 791 ms | 9.9 ms | 1,359 → 1,359 |
| Empty platform active-Runtime page | 82.6 ms | 6.9 ms | 22 → 22 |

Post-restart first-use samples were 37 ms for the challenge bank, 236 ms for challenge detail and 206 ms for the empty Runtime page. The competition-challenge list fell from about 60 KB to 16 KB and warmed at about 4–5 ms. Auth, competition list, GameplayFact list and notification list remained in their previous warm ranges. Read-only API totals before and after the switch matched: 55 challenge templates, 90 users, 464 GameplayFacts and 340 competition Runtime records. The Host was healthy and `/health/ready` returned 200.

No new production Flag submissions were observed immediately after the cutover. Thus a production p95 for Flag accepted-to-visible latency is **not yet established**; the isolated CTF/AWD/AWDP/KoH E2E suites and the transactional/concurrency integration suite passed, but their single-flow correctness results must not be presented as a p95 latency improvement.

The candidate image ID was `sha256:5dcaad4bd47718e7a55a9b4b2cf7a5e0e253e2e3c0ba76dfeca0f716349ab5eb`. Before cutover, the server stored a PostgreSQL custom-format dump and upload archive under `/opt/noctf-migration/pre-alpha40-20260924.dump` and `/opt/noctf-migration/pre-alpha40-uploads-20260924.tgz`; both were hash-checked and readable. PostgreSQL, Redis, NATS and the upload bind mount were not recreated. The prior `alpha.39` image and `.env.pre-alpha40` remain available for rollback.

Verification before release: 1,194 non-integration .NET tests; 272 container integration tests passed with six documented environment-dependent skips; all four full E2E suites passed; 562 Bun tests, typecheck, architecture audit, Nuxt generation and Docker image build passed; EF reported no pending model changes. Production browser inspection confirmed challenge-bank pagination, challenge detail and the competition challenge list rendered without console errors. The first attempt to rerun the entire .NET suite concurrently with frontend work had eight Docker/PostgreSQL timeout or scheduling failures; all eight passed when rerun alone, and the final isolated integration run was green. This environmental variance is retained rather than hidden.
