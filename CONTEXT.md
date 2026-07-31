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
  publication, base score, rules, revision, and hints; it never selects a Runtime provider or Runner
  pool.
- **Submission** is the persisted participant input and processing state. It never stores a score,
  score delta, or rank.
- **Scoring event** is a persisted evaluation fact derived from a Submission or trusted system
  observation. It also never stores a score, score delta, or rank.
- **Leaderboard projection** recomputes scores from PostgreSQL facts and current configuration.
  Redis stores a replaceable snapshot and projection state; PostgreSQL remains the source of truth.
- **Team ban** is a moderation fact, not a Submission. Changing it advances the competition's
  leaderboard revision.
- **Fix attempt** is consumed only by a team-controlled failed validation. Platform, Runner, storage,
  and checker failures do not consume an attempt.
- **Runtime instance** is the durable scheduling and lifecycle fact for one concrete generation. Its
  provider receipt is opaque outside the owning provider boundary.
- **Checker** is a trusted, administrator-controlled one-shot workload with a minimum-permission
  internal JWT. Challenge service input and network traffic remain untrusted.
- **Bot user** is a non-interactive platform user. Organizer Bots use ordinary access JWTs and the
  same resource permissions as other users; there is no repository-specific identity protocol.

## Architecture vocabulary

- Production has three independent processes: `NoCTF.API`, horizontally scalable `NoCTF.Worker`,
  and one or more `NoCTF.Runner` nodes.
- PostgreSQL is the only business source of truth and also stores Wolverine inboxes, outboxes,
  schedules, queues, and dead letters.
- Business work is durable Wolverine messaging. In-process Channels, fire-and-forget tasks, and a
  supported single-process hosting mode are not part of the target architecture.
- Domain does not depend on EF Core, HTTP, Redis, Wolverine, or provider SDKs. Application use cases
  expose capability-oriented interfaces; Infrastructure implements them.
- CTF, AWD, AWDP, and KoH are built-in modes. Penetration is ordinary CTF content, not a mode, and
  dynamic game-mode plugins are not a target-architecture seam.
- A deployment selects one active container provider (`Docker` or `Kubernetes`) and its Runner pool.
  Challenge and Competition data never select either value.
- Docker Runtime public access uses the challenge service's declared TCP port mapping with requested
  host port `0`. No platform HAProxy or ingress proxy is inserted for Docker Runtime exposure.
- Frontend HTTP calls consume the generated OpenAPI client and types; deployment base URLs are
  resolved centrally rather than hardcoded by features.
