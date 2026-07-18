# NoCTF backend architecture

The backend is a .NET 10 solution with explicit process seams:

```text
Domain
  -> Application
     -> GameModes / Integrations.QQBot
        -> Infrastructure / Runtime.Docker / Runtime.Kubernetes
           -> API / Worker / Runner
```

`NoCTF.Domain` contains only anemic entities, values, and enums. `NoCTF.Application` owns use cases and business ports. `NoCTF.GameModes` contains pure, stateless rules and strict versioned JSON configuration. `NoCTF.Infrastructure` is the EF Core/Marten/Wolverine/Redis adapter. Runtime projects are concrete Docker/Kubernetes adapters. Hosts contain transport and composition only.

## Submission and scoring

Flag and Fix endpoints validate admission using the server `ReceivedAt`, append a permanent input, and return `202 Accepted` with a submission ID. Wolverine workers resolve the stream event, append one typed outcome, and publish a status that contains no submitted Flag or archive contents. The status endpoint is the reconnect fallback.

Each competition has a permanent Submission Stream and a disposable Scoring Stream. Scoring rules replay the permanent inputs into a staging stream. After catching the submission high-water mark, a Marten checkpoint selects the new stream and the leaderboard projection. Ban/unban is relational moderation state plus audit and schedules a rebuild; it never mutates submission history. Redis stores only leaderboard snapshots and notification envelopes.

## Operations

Run `dotnet run --project src/NoCTF.API -- --migrate-only` against an empty PostgreSQL database. The command applies the EF baseline and Marten schema. The Wolverine durable message schema is applied by the Wolverine host at startup before workers process messages. Seed the administrator separately. Runner operations require `X-Runner-Key` configured as `Runtime:Runner:ApiKey`.

The former dynamic-plugin design is superseded by [ADR-0001](adr/ADR-0001-compile-time-game-modes.md). Stream ownership and queue semantics are recorded in [ADR-0002](adr/ADR-0002-dual-event-streams.md) and [ADR-0003](adr/ADR-0003-durable-submission-queue.md).
