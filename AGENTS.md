# NoCTF repository instructions

## FastEndpoints

- Every HTTP endpoint must derive from a strongly typed FastEndpoints base such as `Endpoint<TRequest, TResponse>` or `EndpointWithoutRequest<TResponse>`.
- Implement endpoint behavior with `ExecuteAsync`; do not implement business endpoints with `HandleAsync`.
- Model alternative HTTP outcomes with `Microsoft.AspNetCore.Http.HttpResults.Results<T1, ..., Tn>`.
- Construct outcomes with `TypedResults` (`TypedResults.Ok`, `TypedResults.Accepted`, `TypedResults.NotFound`, `TypedResults.Problem`, and similar methods).
- Use concrete typed result types such as `Ok<T>`, `Accepted<T>`, `Created<T>`, `NotFound`, `NoContent`, `UnauthorizedHttpResult`, and `ProblemHttpResult` in the endpoint response declaration.
- Do not manually serialize endpoint responses, set status codes, or call `HttpResponse.Send*Async`/`WriteAsJsonAsync` from endpoint execution code.
- File-upload endpoints must bind files through a request DTO (`IFormFile`/`IReadOnlyList<IFormFile>`) and opt in with `AllowFileUploads()`. Do not read `HttpContext.Request.Body` from an endpoint. Use `FormFileSectionsAsync()` only when streaming is explicitly required for a large-file contract.
- Use domain/application enums for bounded concepts (for example submission kind and runtime provider). Convert to protocol text only at an explicit external boundary; do not pass provider/kind strings through ports or adapters.
- Do not use strings for bounded business concepts. Gameplay-fact failures, results, states and kinds, runtime states, provider kinds, and projection decisions must use enums or dedicated value objects. Strings are reserved for open text, opaque external identifiers, protected Flag content, and explicit serialization boundaries. `ManualAdjustment.Value` is the sole canonical signed Int32 text boundary.
- Never hand-edit EF Core migrations or the model snapshot. Create, remove, and update migrations only with `dotnet ef migrations ...`; generated migration files are tooling-owned.
- In AWDP, a Break achievement is produced by a correctly evaluated Flag submission; Fix is a separate archive submission and must not be modeled as another Flag action.
- Keep route binding, authentication/authorization metadata, rate limiting, and transport mapping in the endpoint. Put business rules in Application use cases.
- Each endpoint lives in its own file together with its request, response, and `Validator<TRequest>` types. A protocol model reused by multiple endpoints remains in the file of the endpoint that most closely owns the concept; do not create shared `*Models.cs`, `Dtos.cs`, or `CommonModels.cs` files. The sole protocol exception is the business-agnostic signed-keyset pagination primitives/codec, kept together in a `Pagination` feature folder rather than a generic models dumping file.
- Do not use LINQ query-expression syntax (`from`, `where`, `select`, `join`, and similar clauses). Use LINQ method syntax (`Where`, `Select`, `Join`, `GroupJoin`, `OrderBy`, and related methods) consistently, including EF Core queries.

## Project organization and EF Core

- Organize Domain, Application, and Infrastructure by business capability and nested feature folders, not by horizontal `Services`, `Ports`, `UseCaseAdapters`, or `Helpers` dumping grounds.
- Infrastructure types are named for the business responsibility they implement. Do not prefix every EF Core implementation with `Ef`; use a `Postgres` prefix only when distinguishing providers is meaningful.
- Prefer EF Core Data Annotations for keys, required fields, lengths, precision, columns, and indexes. Use Fluent ModelBuilder only where annotations cannot express PostgreSQL `jsonb`/`uuid[]`/GIN mapping, value conversion, a check constraint, or another provider-specific rule, and document why it is necessary.
- Tests use TUnit. NSubstitute is allowed for unit-test substitutes. PostgreSQL, Redis, and Wolverine integration behavior must be tested against real dependencies, normally through Testcontainers; do not use EF Core InMemory as proof of relational behavior.

## Data model baseline

- This repository does not preserve compatibility with the previous persistence model, API contracts, or migrations. Existing migrations may be removed and replaced with one EF-generated initial baseline; never hand-edit generated migrations or snapshots.
- `refresh_sessions` is not a business table. Refresh tokens are stateless JWTs with a fixed 30-day lifetime, the shared authentication signing key, a distinct refresh audience, and `token_type=refresh` plus `sub`, `jti`, `iat`, `exp`, and `token_version` claims. Refresh rotation, replay detection, and per-device server-side revocation are intentionally not implemented. Logout only clears the HttpOnly cookie; `User.TokenVersion` remains the global administrative/password-change invalidation mechanism.
- `competition_configurations` is not a separate entity/table. Competition mode, configuration JSON, and update time are columns on `competitions`; common values use ordinary columns and mode-specific values use `jsonb`.
- Competition collaborators are stored directly on `competitions` as the mutually exclusive PostgreSQL UUID arrays `ManagerIds`, `JudgeIds`, and `ObserverIds`. Do not create a collaborator entity or table. `OwnerId` is the sole owner source of truth.
- Lifecycle and leaderboard-visibility transitions are immutable `competition_events` with typed JSON payloads. There are no lifecycle-audit or leaderboard-visibility-audit tables, child collections, or `DbSet`s.
- Team membership is stored directly on `teams` as a duplicate-free PostgreSQL UUID array `MemberIds`. `CaptainId` is the sole captain source of truth and must be present in `MemberIds`. Do not create a `TeamMember` entity/table or attach ordering semantics to the array.
- `team_invitations` is not a separate entity/table. A team owns one unique, plaintext, cryptographically random 32-character `InvitationToken`. Possession of the token directly joins the team. Captains/administrators may rotate it, immediately invalidating the previous value; there are no invited-user, invitation-status, or invitation-expiry states.
- `Challenge` is a reusable global question-bank template for exactly one `GameMode`. It owns the statement, attachments, plaintext template flags, and provider-neutral Runtime/Checker/Flag-injection definition JSON. It must not contain competition identity, ordering/publication, scoring, hints, `RuntimeProvider`, or `RunnerPool`.
- `CompetitionChallenge` is the per-competition challenge instance with its own id and `CompetitionId`/template `ChallengeId` links. It owns competition ordering/publication, rules JSON, and hints (including content, cost, and publication time). Challenge scores come exclusively from the mode-specific rules JSON; there is no separate base-score field. Its mode must match the referenced Challenge. Template metadata and definition edits are live for future starts/resets. Every Runtime reset creates a new Runtime UUID; RuntimeInstance stores neither a generation/replacement chain nor a challenge-definition snapshot/version.
- Template-level static flags may use `ChallengeId`; competition-specific flags use `CompetitionChallengeId`. The two scopes are mutually exclusive. Gameplay facts always use `CompetitionChallengeId`. Competition and practice Runtime instances use `CompetitionChallengeId`; `RuntimePurpose.TemplateTest` instances instead use `ChallengeId`, and the two Runtime scopes are mutually exclusive.
- There is no `runtime_operations` or `runtime_artifacts` business table. Asynchronous state belongs to the corresponding `RuntimeInstance`, `GameplayFact`, or `ChallengeFlag`, while Wolverine owns durable message delivery.
- Docker Container and Compose Runtime public access must use the challenge service's own Docker port mapping with host port `0` (Docker-assigned random port). Do not add or use an HAProxy/ingress proxy for Docker Runtime exposure.
- A deployment selects exactly one active container Runtime provider (`Docker` or `Kubernetes`) and its runner pool in platform configuration. Challenge and Competition data never select either value.

## Persistence baseline (2026 model)

- The only business tables are `users`, `competitions`, `competition_events`, `teams`, `challenges`, `challenge_attachments`, `competition_challenges`, `challenge_flags`, `runtime_instances`, `patch_uploads`, `account_tokens`, `platform_settings`, `notifications`, `gameplay_facts`, and `files`. `data_exports` is not a business table; exports are authorized synchronous streaming responses.
- `email_verification_settings`, both legacy token tables, question/entry tables, hint tables, published-port tables, maintenance schedules, and user-account lifecycle-audit tables are deleted. Do not add compatibility `DbSet`s or migrations.
- `files` owns immutable object metadata. Business records keep only a `FileId`; replacements upload a new File and enqueue `CleanupFile` for an unreferenced old File.
- `notifications` is one append-only message with dynamic audience (`CompetitionCollaborators`, `CompetitionParticipants`, `TeamMembers`, `PlatformAdministrators`). A question root has null `ThreadRootId`; replies and status events carry the root notification id in `ThreadRootId`. `ReplyToId` is optional non-unique reply context and does not define membership. Question roots and announcements are notification kinds, not extra tables.
- `GameplayFact` is the single current competition-behavior record. It owns the objective action, processing state, one current result and stable failure code; there is no Submission or ScoringEvent table, adjudication version, processing claim, callback hash, or historical result row.
- `GameplayFactKind.ManualAdjustment` stores the nonzero canonical signed Int32 delta (`-10` or `25`) in `Value`; no additional-id, score, or delta column is allowed. Patch uploads, hints and AWD rounds are referenced through the `(ReferenceKind, ReferenceId)` pair.
- Persisted `Revision`, `*Revision`, `ExpectedRevision`, `ConcurrencyVersion`, `CriticalSectionVersion`, and `ProcessingVersion` protocols are forbidden. Mutable business records use last-write-wins while immutable records, business unique constraints, idempotency keys, and Wolverine Inbox/Outbox retain their own invariants. `User.TokenVersion`, JSON `schemaVersion`, event payload versions, and leaderboard protocol versions are not concurrency tokens and remain valid.
- Wolverine periodic scheduling uses one cluster Singular Agent that rebuilds in-memory schedules from PostgreSQL facts after startup/failover, skips missed ticks, and only dispatches durable messages. Do not persist business next-run fields or use Wolverine Scheduled Messages as the periodic clock. All Workers share one PostgreSQL persistence/transport; readiness must fail when leader/agent ownership cannot be established.
- Single-consumer work uses named durable PostgreSQL competing-consumer endpoints. Multi-subscriber work uses explicit fan-out to independent named Sticky PostgreSQL endpoints with `IdAndDestination`. Never enable `MultipleHandlerBehavior.Separated` globally. A required Sticky destination that is absent or resolves to a local queue is a startup error. Business writes and sends use EF transactional outbox; consumers use durable inbox.
- Leaderboards are event-driven: scoring/eligibility/visibility changes invalidate cache immediately, a Singular Agent coalesces competition ids for 500 ms, and a durable handler performs a full PostgreSQL projection before atomically replacing cache and notifying SignalR. There is no `LeaderboardDirty` column or periodic dirty scan; cache loss must rebuild from PostgreSQL.

## Product scope

- The only game modes are CTF, AWD, AWDP, and KoH. Penetration is ordinary CTF content, not a game mode, and multi-stage or static-container-fleet challenge types are out of scope.
- Production roles are `Api`, `Worker`, and `Runner`. They may run in the legacy `NoCTF.API`, `NoCTF.Worker`, and `NoCTF.Runner` processes or in any non-empty combination through `NoCTF.Host`; the unified host defaults to all three roles. Role changes take effect only after restart or rolling deployment. Durable business work must still use Wolverine/PostgreSQL queues in combined-process deployments.

## Frontend (ClientApp)

- The SPA lives in `backend/src/NoCTF.API/ClientApp` (Nuxt 4, Bun, `ssr: false`). See `ClientApp/AGENTS.md` for the conventions.
- It is currently a minimal skeleton without a UI framework, pages, or an API SDK; frontend features are rebuilt on top of this base as needed.
