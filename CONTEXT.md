# NoCTF project context

The authoritative product and engineering specifications start at
[`docs/README.md`](docs/README.md). This file is a compact vocabulary guide, not
an independent source of requirements.

## Domain glossary

- **Competition** is a time-bounded event with one `GameMode` that is immutable after creation.
- **Challenge template** is a reusable global question definition for exactly one game mode. It owns
  the statement, attachments, template flags, and provider-neutral Runtime/Checker/Flag-injection
  definitions.
- **Competition challenge** is one competition's use of a challenge template. It owns ordering,
  publication, typed mode-specific rules, and ordered hints; it never selects a Runtime provider or
  Runner pool.
- **Gameplay fact** is the single persisted objective behavior and current adjudication. Player,
  administrator, AWD-service and KoH-observation facts share one row with one current result; old
  adjudications and processing versions are not retained.
- **Manual adjustment** is a completed administrator-created GameplayFact whose `Value` is a nonzero
  canonical signed Int32. It never owns a score or delta column.
- **Leaderboard projection** fully recomputes scores from relational GameplayFacts and current
  configuration. A NATS invalidation is coalesced for 500 ms and atomically replaces the stable
  snapshot in the named FusionCache; Redis is only replaceable L2/backplane infrastructure.
- **Leaderboard matrix** is the public projection of competition challenges as columns and teams as
  ranked rows. Each sparse cell is one team's current score contribution for one competition challenge
  plus its solve time, solver, and optional CTF blood rank; an absent cell has no public result.
- **Manual score adjustment** always targets one team and one competition challenge. It changes that
  leaderboard cell and the team's total; there is no competition-wide or unscoped team adjustment.
- **Team ban** is a moderation fact, not a GameplayFact. Changing it publishes a leaderboard invalidation.
- **Fix attempt** is consumed only by a team-controlled failed validation. Platform, Runner, storage,
  and checker failures do not consume an attempt.
- **Runtime instance** is the durable scheduling and lifecycle fact for one concrete generation. Its
  provider receipt is a typed Container, Compose, or OVA receipt persisted through a TPH one-to-one.
- **Checker** is a trusted, administrator-controlled one-shot workload with a minimum-permission
  internal JWT. Challenge service input and network traffic remain untrusted.
- **Bot user** is a non-interactive platform user. Organizer Bots use ordinary access JWTs and the
  same resource permissions as other users; there is no repository-specific identity protocol.

## Architecture vocabulary

- Production has three roles: `Api`, `Worker`, and `Runner`. `NoCTF.Host.dll` is the only process
  entry point and may run any non-empty role combination; Worker and Runner roles remain horizontally scalable.
- A relational database is the business source of truth. The current production provider is PostgreSQL;
  the common model has no Npgsql dependency. Wolverine transport, consumers, schedules and dead letters
  live only in NATS JetStream.
- Business work is durable Wolverine messaging even when roles share a process. In-process Channels
  and fire-and-forget tasks are not part of the target architecture.
- Domain does not depend on EF Core, HTTP, Redis, Wolverine, or provider SDKs. Application use cases
  expose capability-oriented interfaces; Infrastructure implements them.
- Competition, Challenge, CompetitionChallenge, GameplayFact, RuntimeInstance, ChallengeFlag,
  CompetitionEvent, Notification and receipts use stable-discriminator TPH hierarchies. Collections are
  relational child/association tables; structural value objects use EF Complex Types and ordinary columns.
- CTF, AWD, AWDP, and KoH are built-in modes. Penetration is ordinary CTF content, not a mode, and
  dynamic game-mode plugins are not a target-architecture seam.
- A deployment selects one active container provider (`Docker` or `Kubernetes`) and its Runner pool.
  Challenge and Competition data never select either value.
- Docker Runtime public access uses the challenge service's declared TCP port mapping with requested
  host port `0`. No platform HAProxy or ingress proxy is inserted for Docker Runtime exposure.
- Frontend HTTP calls consume the generated OpenAPI client and types; deployment base URLs are
  resolved centrally rather than hardcoded by features.

## Persistence vocabulary

- `competition_events` is the append-only typed TPH audit stream for lifecycle, leaderboard visibility,
  runtime, gameplay and other competition events. There is no payload JSON or Kind/payload dual source.
- `platform_settings` is the singleton `id=1` row for platform identity, logo FileId, email
  verification, password reset, and SMTP settings, guarded by an application-generated concurrency stamp.
- `account_tokens` combines email-verification and password-reset tokens with a `Kind` enum.
- `files` is immutable metadata shared by avatars, posters, attachments, patches, and exports.
- `notifications` is the durable dynamic-audience feed and linear question thread. A competition
  management announcement uses `SourceId=sender UserId`, `TargetId=CompetitionId`, and
  `TargetType=CompetitionCollaborators` by default.
