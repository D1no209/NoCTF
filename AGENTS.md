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
- Each endpoint lives in its own file together with its request, response, and `Validator<TRequest>` types. A protocol model reused by multiple endpoints remains in the file of the endpoint that most closely owns the concept; do not create shared `*Models.cs`, `Dtos.cs`, or `CommonModels.cs` files. Business-agnostic pagination contracts (`PaginationRequest`, `SearchRequest`, `ArrayResult<T>`, and the retained signed-cursor primitives) are kept together in the `Pagination` feature folder rather than a generic models dumping file.
- Do not use LINQ query-expression syntax (`from`, `where`, `select`, `join`, and similar clauses). Use LINQ method syntax (`Where`, `Select`, `Join`, `GroupJoin`, `OrderBy`, and related methods) consistently, including EF Core queries.

## Project organization and EF Core

- Organize Domain, Application, and Infrastructure by business capability and nested feature folders, not by horizontal `Services`, `Ports`, `UseCaseAdapters`, or `Helpers` dumping grounds.
- Infrastructure types are named for the business responsibility they implement. Do not prefix every EF Core implementation with `Ef`; use a `Postgres` prefix only when distinguishing providers is meaningful.
- Prefer EF Core Data Annotations for keys, required fields, lengths, precision, columns, and indexes. The common model targets EF Core relational providers and must not contain provider-specific column types, arrays, filtered indexes, collations, check constraints, raw SQL, or provider APIs. Provider registration and migrations live in a dedicated provider project.
- Tests use TUnit. NSubstitute is allowed for unit-test substitutes. PostgreSQL, Redis, and Wolverine integration behavior must be tested against real dependencies, normally through Testcontainers; do not use EF Core InMemory as proof of relational behavior.

## Data model baseline

- This repository does not preserve compatibility with the previous persistence model, API contracts, or migrations. Existing migrations may be removed and replaced with one EF-generated initial baseline; never hand-edit generated migrations or snapshots.
- `refresh_sessions` is not a business table. Refresh tokens are stateless JWTs with a fixed 30-day lifetime, the shared authentication signing key, a distinct refresh audience, and `token_type=refresh` plus `sub`, `jti`, `iat`, `exp`, and `token_version` claims. Refresh rotation, replay detection, and per-device server-side revocation are intentionally not implemented. Logout only clears the HttpOnly cookie; `User.TokenVersion` remains the global administrative/password-change invalidation mechanism.
- Competition mode configuration is a typed TPH aggregate with ordinary relational columns and ordered child tables. Persisted schema-version JSON is forbidden.
- Competition collaborators use the `competition_collaborators` association with one role per `(CompetitionId, UserId)`; `OwnerId` remains the sole owner source of truth.
- Lifecycle and leaderboard-visibility transitions are immutable `competition_events` TPH leaves with typed columns and ordered child tables. Event payload JSON is forbidden.
- Team membership uses `team_members`. `CaptainId` must reference a member of the same team.
- `team_invitations` is not a separate entity/table. A team owns one unique, plaintext, cryptographically random 32-character `InvitationToken`. Possession of the token directly joins the team. Captains/administrators may rotate it, immediately invalidating the previous value; there are no invited-user, invitation-status, or invitation-expiry states.
- `Challenge` is a reusable global question-bank template for exactly one `GameMode`. It owns typed TPH Runtime/Checker/Flag-injection definitions and relational collections. It must not contain competition identity, ordering/publication, scoring, hints, `RuntimeProvider`, or `RunnerPool`.
- `CompetitionChallenge` is the per-competition challenge instance with its own id and `CompetitionId`/template `ChallengeId` links. It owns typed mode-specific rules and relational hints. Its mode must match the referenced Challenge. Template metadata and definition edits are live for future starts/resets. Every Runtime reset creates a new Runtime UUID; RuntimeInstance stores neither a generation/replacement chain nor a challenge-definition snapshot/version.
- CTF progression is opt-in per competition. Its badge catalog is independent of the saved graph; graph nodes are sealed TPH challenge/badge leaves and predecessor edges are relational. Challenge completion comes from current correct FlagAttempt/FixAttempt facts; badges carry no score. A saved graph applies immediately and may relock solved challenges without removing results or scores.
- Template-level static flags may use `ChallengeId`; competition-specific flags use `CompetitionChallengeId`. The two scopes are mutually exclusive. Gameplay facts always use `CompetitionChallengeId`. Competition and practice Runtime instances use `CompetitionChallengeId`; `RuntimePurpose.TemplateTest` instances instead use `ChallengeId`, and the two Runtime scopes are mutually exclusive.
- There is no `runtime_operations` or `runtime_artifacts` business table. Asynchronous state belongs to the corresponding `RuntimeInstance`, `GameplayFact`, or `ChallengeFlag`; NATS JetStream owns at-least-once delivery and consumers must be idempotent.
- Docker Container and Compose Runtime public access must use the challenge service's own Docker port mapping with host port `0` (Docker-assigned random port). Do not add or use an HAProxy/ingress proxy for Docker Runtime exposure.
- A deployment selects exactly one active container Runtime provider (`Docker` or `Kubernetes`) and its runner pool in platform configuration. Challenge and Competition data never select either value.

## Persistence baseline (2026 model)

- Business aggregates may use TPH tables, explicit association tables, active-slot/identity tables, and ordered value-object child tables. Do not lock the architecture to a fixed table count. `data_exports` is not a business table; exports are authorized synchronous streaming responses.
- `email_verification_settings`, both legacy token tables, question/entry tables, hint tables, published-port tables, maintenance schedules, and user-account lifecycle-audit tables are deleted. Do not add compatibility `DbSet`s or migrations.
- `files` owns immutable object metadata. Business records keep only a `FileId`; replacements upload a new File and enqueue `CleanupFile` for an unreferenced old File.
- `notifications` is one append-only message with dynamic audience (`CompetitionCollaborators`, `CompetitionParticipants`, `TeamMembers`, `PlatformAdministrators`). A question root has null `ThreadRootId`; replies and status events carry the root notification id in `ThreadRootId`. `ReplyToId` is optional non-unique reply context and does not define membership. Question roots and announcements are notification kinds, not extra tables.
- `GameplayFact` is the single current competition-behavior record. It owns the objective action, processing state, one current result and stable failure code; there is no Submission or ScoringEvent table, adjudication version, processing claim, callback hash, or historical result row.
- `GameplayFactKind.ManualAdjustment` stores the nonzero canonical signed Int32 delta (`-10` or `25`) in `Value`; no additional-id, score, or delta column is allowed. Patch uploads, hints and AWD rounds are referenced through the `(ReferenceKind, ReferenceId)` pair.
- Mutable aggregates use application-generated `Guid ConcurrencyStamp` optimistic concurrency tokens. Collection invariants use ordinary unique constraints and serializable transactions; bounded idempotent retries are allowed, while external side effects are not retried automatically.
- Periodic scheduler and Runner resource-domain ownership use NATS KV leases with CAS fencing. Scheduling is rebuilt from relational facts after takeover; no database advisory/table/row locks or persisted next-run protocol is allowed.
- Wolverine uses NATS JetStream only. Database Message Store, Inbox, Outbox and scheduled-message polling are forbidden. Business code publishes through `IPostCommitMessagePublisher` after commit; pending database states must be redispatchable to cover the commit/publish crash window.
- Production Wolverine handlers use checked-in static generated code under `NoCTF.Host/Internal/Generated/WolverineHandlers`. Wolverine/JasperFx `codegen write` generates these adapters from the actual handler and DI graph; `backend/scripts/Generate-WolverineHandlers.ps1` is only a local invocation wrapper, while Docker invokes the CLI directly. This is separate from the Roslyn `NoCTF.Modeling.Generators` incremental generator for domain hierarchies. Regenerate after changing handler signatures or dependencies. Service-location fallback is forbidden.
- Leaderboards are event-driven: scoring/eligibility/visibility changes invalidate cache immediately, each Worker receiving JetStream events coalesces competition ids for 500 ms, and a durable handler performs a full relational projection before atomically replacing cache and notifying SignalR. There is no `LeaderboardDirty` column or periodic dirty scan; cache loss must rebuild from relational facts.

## Product scope

- The only game modes are CTF, AWD, AWDP, and KoH. Penetration is ordinary CTF content, not a game mode, and multi-stage or static-container-fleet challenge types are out of scope.
- Production roles are `Api`, `Worker`, and `Runner`. `NoCTF.Host` is the only executable process and may run any non-empty role combination; it defaults to all three roles. `NoCTF.API`, `NoCTF.Worker`, and `NoCTF.Runner` are class-library feature modules and must not regain executable entry points. Role changes take effect only after restart. Durable business work uses Wolverine over NATS JetStream; PostgreSQL stores business facts only.

## Frontend (ClientApp)

- The SPA lives in `backend/src/NoCTF.API/ClientApp` (Nuxt 4, Bun, `ssr: false`). See `ClientApp/AGENTS.md` for the conventions.
- The frontend separates feature controllers/composition (`features/`), rendering views (`components/views/`), shared UI primitives (`components/ui/`), and stable bilingual catalogs (`locales/`). Pages and layouts are thin feature entry points. Follow `ClientApp/AGENTS.md` and run its architecture audit when changing frontend code.
