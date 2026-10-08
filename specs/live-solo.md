# LiveSolo implementation and acceptance

Approved scope: an independent fifth game mode with Team-based participants, parallel Matches, first valid Flag by durable Round admission sequence, and no ordinary challenge points or blood awards. Existing CTF, AWD, AWDP and KoH behavior must remain unchanged.

Baseline: `a84a281f` on main, confirmed pushed to origin/main. Baseline solution build, 1411 non-integration tests, 43 writeup tests, 746 frontend tests, typecheck and architecture audit passed. One isolated-cluster capacity test was configured to skip. Work proceeds on `codex/livesolo`; validated stages receive local atomic commits.

## Product rules

- Single- and double-elimination brackets support byes, unresolved sources, automatic advancement, and the conditional double-elimination reset final. Only actual defeats count towards elimination.
- Default BO3 (first to 2); BO1/BO5 and first to N are configurable, with stage overrides. Countdown defaults to 5 seconds, question release interval to 180 seconds, Round limit to 900 seconds.
- Both teams receive identical Round question instances at the same actual release time. Earlier questions remain valid. Selection and Flag input belong to each member and are preserved when a new question opens.
- Rosters are locked per Match. Every roster member must publish a screen before the judge starts the countdown. Loss of sharing alerts the judge but does not automatically pause or forfeit.
- Deadline handling drains previously admitted submissions before voiding a Round. Voids do not add wins or consume an effective BO round. Replays use fresh Round/question/runtime scopes and new suitable question groups; insufficient reserves wait for the judge.
- A team may hold only one active Match slot. Match and competition pauses use the union of overlapping pause intervals.
- Dynamic Flags, Runtime instances, attachment allocations and download evidence are isolated by RoundQuestion execution scope. Previous attempts, template tests and other Match/round scopes cannot supply evidence.
- CTF FlagSubmission templates can be copied independently with canonical-source provenance. PatchVerification is not silently converted. Scheduling excludes questions previously exposed to either team or any roster member in this competition.
- Ordinary static Flags are allowed. Once a question is publicly obtainable, it is barred from new Matches that have not started. Existing active Matches are not forced to replace it.
- Formal Runtime exposure requires infrastructure isolation, normally WsrxOnly. Prewarming does not grant contestant access. Container public ports are forbidden for WsrxOnly; deployment/network capability failures prevent formal starts.
- LiveSolo has no normal challenge score, blood rewards, paid hints or writeup benefit discounts. Free hints release to both sides together. Post-event writeup submission/review/reading reuses existing capabilities.

## Architecture

Business directories: Domain/LiveSolo, Application/LiveSolo, GameModes/LiveSolo, Infrastructure/LiveSolo, API/Endpoints/LiveSolo, Worker/LiveSolo, ClientApp/features/live-solo. Views render only; generic video, selection and bracket interactions belong to shared UI.

Reuse Competition, Team, identity and staff authorization, Challenge/CompetitionChallenge, protected Flag matching, File storage, GameplayFact, Runtime provisioning/admission/providers, ConcurrencyStamp, serializable transactions, Wolverine/NATS, NATS KV leases, FusionCache, SignalR transport and notifications. Submission association records do not become another scoring/submission authority.

Shared changes are restricted to mode registration/mapping/composition, neutral optional Runtime ExecutionScopeId, strategy dispatch for scoped resource access/evaluation/state projection, and explicit typed events/references. Old modes do not reference LiveSolo implementations or any media SDK. Media SDK/protocol details are restricted to media adapters.

Relational records own brackets/match slots/active team slots, locked rosters, rounds and pause intervals, RoundQuestion releases, question groups/reserves, ordered GameplayFact associations, Runtime bindings, source provenance/exposure, media generations and recordings. No business configuration JSON, Runtime definition snapshots, database Inbox, Redis business locks or persisted next-run protocol.

Formal submissions atomically allocate one monotonically increasing sequence shared across all questions and both sides in a Round. Evaluation and adjudication resolve the earliest valid sequence; later evaluation completion never steals a win. Winner selection, Match wins and advancement commit consistently. Durable redispatchable states cover lost post-commit wakeups. Stale schedule messages validate current Round and timeline revision.

## Media

Independent self-hosted LiveKit SFU, Egress and media workers; separate Redis if required by the media service. NoCTF uses ILiveSoloMediaGateway. Every Match has an opaque room identity and generation. Screen tracks only by default; no microphone or system audio. The browser asks the user for screen capture permission each time.

Publishers cannot subscribe to opponents by default. An event may enable mutual viewing before play. Judges view raw screens live; public viewers receive server-delayed program media and state, normally 60 seconds. No public raw-room tokens or client-controlled asOf. Revocation uses server disconnection, room generations and periodic authorization audits; expiration/kicking alone does not stop an old self-hosted token rejoining.

Recording is optional and off by default. Delayed program buffering remains temporary when recording is off. Recording captures every roster screen, not just main views, with UTC mapping and Match/Round references. Defaults: 30-day retention; configurable quotas, staff-only access, dispute holds and redispatchable pending completion. Only after the entire event ends may a manager publish replay. Media failures alert but never change results automatically.

## Stages and acceptance

1. Registration, typed relational models/configuration and neutral shared contracts; architecture tests and regression baseline.
2. Source-safe template copying, groups, roster, common release, scoped access/Runtime and one complete first-valid-Flag Round.
3. Pure bracket generation for 2/3/5/8/16 teams, byes, advancement/reset finals, replay/corrections and fenced restartable scheduling.
4. Actual self-hosted media adapter, capture gate, delayed program, optional recording, revocation/room-generation integration tests.
5. Hall/preparation/play/judge/bracket/replay/screen UI, real browser checks, capacity measurements, documentation and complete regressions.

Required concurrency/security tests include multiple nodes/members/questions, deadline boundaries and pending earlier evaluations, retries/rollback/lost wakeups, overlapping pauses/takeover, unopen/other-team/other-Match/non-roster/old-Round access through ordinary endpoints, scope isolation on Runtime reset/Flag/downloads, upstream correction with started dependents, media revoked/old-generation authorization and delayed-media URL guessing.

Capacity acceptance runs recording off and on independently: 4 parallel Matches, 2 members per side, 16 published screens and 50 viewers. Judge video targets <=2s end-to-end; committed business-state push targets P95 <=500ms. Public media and state must never precede configured delay. Report measured CPU, memory, network, storage and bottlenecks instead of guaranteeing capacity on arbitrary hardware.

Run TUnit, PostgreSQL/NATS/required cache Testcontainers, HTTP/SignalR authorization and actual SFU integration. Mocks do not replace actual media acceptance. EF tooling owns migrations/snapshots; OpenAPI/SDK and affected Wolverine adapters are generated by repository tooling. Frontend uses bun test/typecheck/architecture audit and real desktop browser checks. Disabled or incomplete LiveSolo/media configuration permits drafts only, never formal play.

## Official references

- [Self-hosting](https://docs.livekit.io/transport/self-hosting/)
- [Token grants and revocation boundaries](https://docs.livekit.io/frontends/reference/tokens-grants/)
- [Independent Egress](https://docs.livekit.io/transport/self-hosting/egress/)
- [Browser display capture](https://developer.mozilla.org/en-US/docs/Web/API/MediaDevices/getDisplayMedia)

## Implementation ledger

Foundation: independent relational aggregates and typed TPH registration are implemented. Runtime has an opaque execution identity and scoped active-slot suffix; null preserves the original slot key. Ordinary live resource/scoring/admission paths reject scoped modes. Runtime proxy connections and probes fail closed for execution-scoped targets without qualified access, while ordinary targets retain prior behavior. Provider-neutral media and execution authorization ports are declared; formal Match operation and media delivery are subsequent stages.

Foundation checks: real PostgreSQL migration upgrade retained all four old discriminator values and verified active-team/round-sequence uniqueness with rollback; pause-union/configuration/runtime-key tests and dependency-boundary tests passed. Non-integration suite: 1423 passed, one isolated-cluster capacity test configured to skip. Frontend baseline: 746 passed, typecheck and architecture audit passed. EF migration/snapshot were generated exclusively by CLI and have no model drift. The new mode's full Match/media feature UI is not enabled by this foundation commit.
