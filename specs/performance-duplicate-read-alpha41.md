# alpha.41 repeated-read and latency audit

This report records observations, not an unmeasured speedup. It contains no
credentials, Flag values, user identifiers, SQL parameters or raw traces.

## Baseline and reproduction

- Source baseline: production `noctf-host:0.2.1-alpha.40`, image
  `sha256:5dcaad4bd47718e7a55a9b4b2cf7a5e0e253e2e3c0ba76dfeca0f716349ab5eb`.
  The repository was dirty before this work; the previous release was built
  from that worktree. Candidate builds use .NET SDK 10.0.300 on Windows x64;
  production is Linux x64, with PostgreSQL, Redis and NATS in Docker.
- The reported challenge detail returned HTTP 200 in 284 ms on the first
  authenticated public-network probe. Its `runtimes/current` request returned
  HTTP 404 in 1,705 ms. HTTP 404 is the intended empty-runtime contract; the
  delay before it is the defect.
- Eighteen warm, sequential, authenticated GETs from the production host to
  the private Host address returned HTTP 404, with TTFB p50 1,597 ms and p95
  1,678 ms (range 1,572–1,678 ms). Requests used `curl --max-time 10` and
  `%{http_code},%{time_starttransfer}`. The credential was not stored with
  the timing artifact. This is not a concurrent or cold-start distribution.
- The earlier [read-path audit](performance-read-path-audit.md) found warm
  notifications, users, GameplayFact lists and most competition reads fast;
  those paths are not changed merely to reduce their query count.

## Ownership and changes

- An absent player Runtime still caused `FindPlayerRuntimeAsync` to materialize
  full Competition configuration, challenge rules and template definition
  before returning null. Its read path now selects only eligibility, mode,
  status, practice state and the AWDP type invariants. The Runtime lookup
  follows only for an eligible scope. PostgreSQL integration tests cover both
  existing and absent Runtimes and assert at most two reader commands for the
  absent case. The write path retains its full, freshly validated scope.
- Public challenge detail previously repeated user/competition/team reads
  across audience, visibility and hints. One request-specific decision now
  carries visibility and team identity; hints project only hint fields and
  unlocked IDs. Non-KoH detail no longer calls the KoH access reader.
- The nonempty platform Runtime page now derives source team ID in its page
  query instead of rereading the same Runtime rows; an empty team-name set
  does not issue a query. Integration tests cap the reader-command count.
- Flag acceptance retains the transactional replay and admission recheck.
  Changing only a challenge-rules child does not update its aggregate root
  `ConcurrencyStamp`, so the transaction compares effective current admission
  rules with the preflight rules. It does not assume a root stamp fences child
  edits. The full definition comparison remains because the current model
  lacks a proven child-change token; dropping that graph without a replacement
  would change concurrent-edit behavior. Production Flag p95 remains unknown.
- The challenge page shares in-flight GameplayFact status requests by
  competition and fact ID. The history dialog is mounted only while open;
  SignalR still triggers a REST status read.

## Measurement and release gates

Prometheus/Grafana now collect low-cardinality route-template duration and
Flag-stage histograms. The first 15-minute live window had too few requests
for most routes to support a p95 comparison; a single slow request is not
treated as a distribution. During candidate validation, use the same private
GET sequence and compare cold and warm p50/p95/p99, HTTP status, bytes and
error rate. Write-bearing Flag load is restricted to isolated E2E/Testcontainers.
No public HTTP contract or EF model migration is intended.

## Production acceptance

The candidate was built from the preserved dirty worktree as
`noctf-host:0.2.1-alpha.41`, image
`sha256:fff60d7468d3977263d710a9aa84c73360773e8f3c10eaf25a0e3da0667c7afc`.
This follows the explicitly selected local-image release method used for
`alpha.40`; it is not a CI digest. The previous image remains available.

| Same private-network route and credential, 18 warm sequential GETs | alpha.40 | alpha.41 |
| --- | ---: | ---: |
| `runtimes/current` with no active Runtime | 404 | 404 |
| TTFB p50 | 1,597 ms | 6.4 ms |
| TTFB p95 | 1,678 ms | 12.9 ms |

The observed p95 reduction is about 99.2%. Both samples are small; p99 and
alpha.40 concurrency-8 distribution were not established. Forty bounded,
warm `alpha.41` GETs with eight simultaneous clients all returned 404; their
TTFB p50/p95/p99 were 18.5/43.6/48.8 ms. The challenge
detail still returned HTTP 200 with the same 1,004-byte payload and had a
58 ms first post-cutover authenticated sample.

Ten additional warm private-network GETs after startup returned p95 6.1 ms
for the admin challenge bank (10 items), 16.5 ms for the nonempty active
Runtime page (3 items), 17.8 ms for notifications, and 1.3 ms for the public
competition list. First-use notification and competition samples were slower
and excluded from these warm figures; these small samples do not prove that
all routes have improved.

Before and after the cutover the API reported 55 templates, 90 users and 3
active Runtimes. The cutover archive and live upload directory both contained
70 files. Fresh PostgreSQL, upload and stopped Redis/NATS directory archives
were validated under `/opt/noctf-migration/pre-alpha41-cutover-20260924*`.
The new Host was healthy without restarts at acceptance; NoCTF, PostgreSQL,
Redis, NATS, node and exporter Prometheus targets were up, including nine
JetStream consumer series. Grafana is available through the separately
authenticated HTTPS domain `noctf.grafana.fa1lsnow.com`; Prometheus and
`/metrics` remain private.

When observability was first enabled on the old `alpha.40` Host, it briefly
restarted twice while Redis-backed platform logging reported timeouts and
readiness was intermittently unhealthy. The cause was not proven. The new
`alpha.41` process remained healthy with zero restarts through the post-cutover
checks and the 40-request concurrency-8 probe; keep watching Redis timeouts,
request error rate and restart count rather than assuming this risk vanished.

Verification: Release solution build with zero warnings, 1,194 non-integration
tests, 273 container integration tests plus one isolated retry passed, six
environment-dependent skips, and full CTF/AWD/AWDP/KoH E2E passed. The one
initial integration failure was a transient missing Docker BusyBox image during
an image-pull test; its isolated rerun passed. All 564 Bun tests, typecheck,
architecture audit, Nuxt generation and EF pending-model-change check passed.
The OpenAPI file inside the old and new Host images had the same SHA-256 hash.
