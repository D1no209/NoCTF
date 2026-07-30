# NoCTF backend context

## Domain glossary

- **Competition** is a time-bounded event with one immutable compile-time `GameMode`.
- **Team ban** is a relational moderation ruling. It is never a submission event; changing it dirties the scoring projection.
- **Submission stream** is the permanent, append-only record of every scoring input and outcome. A plaintext Flag lives only in this stream.
- **Scoring stream** is disposable derived state. It is rebuilt from the submission stream and current JSON configuration.
- **Leaderboard projection** is the authoritative EF Core read model; Redis is only a cache and SignalR backplane.
- **Fix attempt** is consumed only by a team-controlled failed validation. Platform, Runner, storage, and checker failures do not consume an attempt.
- **Runtime receipt** is an opaque durable reference to a Docker or Kubernetes resource.
- **Challenge template** is a reusable question definition for exactly one Game Mode. It owns the statement, attachments, static Flags, provider-neutral Runtime definition, Checker definition, and dynamic Flag injection contract.
- **Competition challenge** is one competition's use of a Challenge template. It owns ordering, publication, scoring and admission rules, and Hints, but never infrastructure-provider details.
- **Checker** is a one-shot job attached to a Runtime's internal network. It updates its execution status through the platform Internal API; process failure and timeout are checker execution states, not challenge results.
- **Bot user** is a platform User created by an administrator for non-interactive automation. It cannot authenticate with a password, but its issued JWTs and competition or Challenge permissions use the same authorization model as every other User.

## Architecture vocabulary

Each project is a module with a small interface at a deliberate seam. Application ports are deep use-case interfaces: adapters hide EF, EF Core, native Channel, Redis, Docker, and Kubernetes details. Tests cross the same interface as production callers. There are no dynamic plugin seams for built-in modes; the remaining adapters are real variations (storage, runtime provider, messaging, and notification transport).

See `docs/architecture.md` and `docs/adr/` for the accepted architecture decisions. The historical dynamic-plugin documents under `docs/superpowers/specs/` are superseded.
