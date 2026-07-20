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
- Do not use strings for bounded business concepts. Submission errors, scoring reasons, runtime states, attempt kinds, event kinds, provider kinds, and projection decisions must use enums or dedicated value objects. Strings are reserved for open text (team names, user messages, MIME types, object keys, opaque external identifiers, and protected Flag content) or explicit serialization boundaries.
- Never hand-edit EF Core migrations or the model snapshot. Create, remove, and update migrations only with `dotnet ef migrations ...`; generated migration files are tooling-owned.
- In AWDP, a Break achievement is produced by a correctly evaluated Flag submission; Fix is a separate archive submission and must not be modeled as another Flag action.
- Keep route binding, authentication/authorization metadata, rate limiting, and transport mapping in the endpoint. Put business rules in Application use cases.
- Each endpoint lives in its own file. Endpoint-specific request/response models may share that file; models reused by multiple endpoints belong in a sibling `*Models.cs` file.
- Do not use LINQ query-expression syntax (`from`, `where`, `select`, `join`, and similar clauses). Use LINQ method syntax (`Where`, `Select`, `Join`, `GroupJoin`, `OrderBy`, and related methods) consistently, including EF Core queries.

## Data model baseline

- This repository does not preserve compatibility with the previous persistence model, API contracts, or migrations. Existing migrations may be removed and replaced with one EF-generated initial baseline; never hand-edit generated migrations or snapshots.
- `refresh_sessions` is not a business table. Refresh tokens are stateless JWTs with a fixed 30-day lifetime, the shared authentication signing key, a distinct refresh audience, and `token_type=refresh` plus `sub`, `jti`, `iat`, `exp`, and `token_version` claims. Refresh rotation, replay detection, and per-device server-side revocation are intentionally not implemented. Logout only clears the HttpOnly cookie; `User.TokenVersion` remains the global administrative/password-change invalidation mechanism.
- `competition_configurations` is not a separate entity/table. Competition mode, configuration JSON, revision, and update time are columns on `competitions`; common values use ordinary columns and mode-specific values use `jsonb`.
- Collaborators and lifecycle audits belong to the `Competition` aggregate as owned dependents. They may use PostgreSQL child tables, but must not be exposed or operated as independent application roots or `DbSet`s.
- Team members belong to the `Team` aggregate as an ordered owned collection. `MemberOrder` is unique per team, starts at zero, and the member at order zero is the captain. Do not maintain a second `CaptainId` or role source of truth; captain transfer must atomically reorder members.
- `team_invitations` is not a separate entity/table. A team owns one unique, plaintext, cryptographically random 32-character `InvitationToken`. Possession of the token directly joins the team. Captains/administrators may rotate it, immediately invalidating the previous value; there are no invited-user, invitation-status, or invitation-expiry states.
- `Challenge` is a reusable global question-bank template. It must not contain competition identity, competition ordering/publication, scoring, hints, or mode-specific competition configuration. Template attachments are owned by the template and remain competition-independent.
- `CompetitionChallenge` is the per-competition challenge instance with its own id and `CompetitionId`/template `ChallengeId` links. It owns competition ordering/publication, `BaseScore`, revision, mode configuration JSON, and hints (including content, cost, and publication time). Template edits are live for all references; no snapshot copy is introduced.
- Flags, submissions, scoring events, challenge instances, and runtime operations use `CompetitionChallengeId` as their business challenge identity. A template `ChallengeId` alone is insufficient for competition-scoped behavior.
