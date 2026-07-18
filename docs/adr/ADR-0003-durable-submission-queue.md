# ADR-0003: Durable asynchronous submissions

Status: accepted

HTTP accepts and records a submission using the trusted `ReceivedAt`, returns `202` with a submission ID, and sends only identifiers to Wolverine. Workers resolve the permanent event and append one idempotent outcome. Completion is published through Redis/SignalR without submitted contents. Wolverine retries transient failures three times with cooldown and moves exhausted messages to a retryable error queue.
