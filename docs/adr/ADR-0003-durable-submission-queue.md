# ADR-0003: Single-process asynchronous submissions

Status: accepted (current phase)

HTTP accepts and records a submission using the trusted `ReceivedAt`, returns `202` with a submission ID, and sends only identifiers to a bounded in-process Channel. API-hosted consumers resolve the submission and append one idempotent `ScoringEvent`. Completion is published through Redis/SignalR without submitted contents. Consumers retry transient failures in-process; durable outbox, dead-lettering and crash recovery are intentionally deferred. The backend therefore runs as a single replica.
