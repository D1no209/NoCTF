# NoCTF backend architecture

The backend is a .NET 10 solution with explicit process seams:

```text
Domain
  -> Application
     -> GameModes / Integrations.QQBot
        -> Infrastructure / Runtime.Docker / Runtime.Kubernetes
           -> API / Runner
```

`NoCTF.Domain` contains only entities, values, and enums. `NoCTF.Application` owns use cases and business ports. `NoCTF.GameModes` contains pure, stateless rules and strict versioned JSON configuration. `NoCTF.Infrastructure` is the EF Core/Redis adapter and hosts bounded in-process Channel API-hosted Channel consumers. Runtime projects are concrete Docker/Kubernetes adapters.

## Submission and scoring

Flag and Fix endpoints validate admission using the server `ReceivedAt`, persist a Submission, and return `202 Accepted` with a submission ID. API-hosted Channel API-hosted Channel consumers create one score-free ScoringEvent and publish a status that contains no submitted Flag or archive contents.

Each competition stores accepted Submission facts and current score-free ScoringEvents in PostgreSQL. Ban/unban is relational moderation state plus audit and schedules an in-process rebuild; it never mutates submission history. Redis stores only rebuildable leaderboard responses and notification envelopes.

## Operations

Run `dotnet run --project src/NoCTF.API -- --migrate-only` against an empty PostgreSQL database. The command applies the single EF baseline. Seed the administrator separately. Runner operations require `X-Runner-Key` configured as `Runtime:Runner:ApiKey`.

The former dynamic-plugin design is superseded by [ADR-0001](adr/ADR-0001-compile-time-game-modes.md). Stream ownership and queue semantics are recorded in [ADR-0002](adr/ADR-0002-dual-event-streams.md) and [ADR-0003](adr/ADR-0003-durable-submission-queue.md).
