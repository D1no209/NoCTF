# NoCTF backend context

## Domain glossary

- **Competition** is a time-bounded event with one immutable compile-time `GameMode`.
- **Team ban** is a relational moderation ruling. It is never a submission event; changing it dirties the scoring projection.
- **Submission stream** is the permanent, append-only record of every scoring input and outcome. A plaintext Flag lives only in this stream.
- **Scoring stream** is disposable derived state. It is rebuilt from the submission stream and current JSON configuration.
- **Leaderboard projection** is the authoritative Marten read model; Redis is only a cache and SignalR backplane.
- **Fix attempt** is consumed only by a team-controlled failed validation. Platform, Runner, storage, and checker failures do not consume an attempt.
- **Runtime receipt** is an opaque durable reference to a Docker or Kubernetes resource.

## Architecture vocabulary

Each project is a module with a small interface at a deliberate seam. Application ports are deep use-case interfaces: adapters hide EF, Marten, Wolverine, Redis, Docker, and Kubernetes details. Tests cross the same interface as production callers. There are no dynamic plugin seams for built-in modes; the remaining adapters are real variations (storage, runtime provider, messaging, and notification transport).

See `docs/architecture.md` and `docs/adr/` for the accepted architecture decisions. The historical dynamic-plugin documents under `docs/superpowers/specs/` are superseded.
