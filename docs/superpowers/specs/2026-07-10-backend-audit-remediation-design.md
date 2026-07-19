# Backend Audit Remediation Design

## Objective

Resolve every backend issue identified in the 2026-07-10 repository audit while preserving NoCTF's plugin architecture and keeping existing public HTTP routes and response fields backward compatible.

Internal contracts, database migrations, indexes, concurrency tokens, deployment manifests, and plugin hosting may change where required. The implementation must leave the repository buildable, testable, and deployable without relying on single-process behavior.

## Architectural Direction

NoCTF will retain its existing API, Application, Infrastructure, PluginBase, plugin, API-hosted Channel consumer, and Runner boundaries. Background execution will become plugin-driven and host-aware:

- API hosts endpoints, authentication, authorization, SignalR, public queries, and command admission.
- API-hosted Channel consumer loads the same plugin modules and hosts competition engines, durable jobs, and instance maintenance.
- Runner remains the only production container-orchestration boundary.
- Plugins register game modes, challenge handlers, scoring contributors, background engines, runtime configuration providers, and maintenance services through stable abstractions.
- API-hosted Channel consumer discovers plugin background capabilities instead of hard-coding individual plugins.

All competition engines also acquire a PostgreSQL advisory lease before processing a competition. This protects rolling deployments and accidental API-hosted Channel consumer scaling. Non-relational unit tests use a process-local keyed lock with equivalent mutual-exclusion semantics.

## Scoring Ownership and Rebuilds

Scoring rebuilds become contributor-based. Each challenge-scoring implementation declares whether it owns a challenge and which score keys it rebuilds.

- The built-in CTF contributor rebuilds only ordinary CTF challenges that do not have a custom submission/scoring owner.
- Penetration registers a contributor that rebuilds stage and penetration blood-bonus events from `PenetrationFlagId` submissions.
- Competition-wide rebuild orchestration invokes all registered contributors inside a competition-scoped advisory lock.
- Existing public rebuild endpoints remain unchanged.
- Existing scoring signals and events retain idempotency keys; insert races are converted into successful idempotent reads instead of request failures.

Accepted submissions commit their canonical submission and scoring transaction before returning success. Leaderboard projection, Redis refresh, and SignalR delivery are written as durable post-commit jobs. Retrying a solved flag therefore returns the existing accepted outcome rather than presenting a false failure caused by a notification outage.

## Plugin Engines and Runtime Configuration

AWD, AWDP, and KoH engines move from API-hosted services to API-hosted Channel consumer-hosted plugin background engines. A shared plugin loader is used by both hosts, and deployment output places plugin assemblies where both hosts can discover them.

AWD registers a challenge runtime configuration provider. It converts the persisted Challenge container fields and orchestration specification into a `ContainerConfig`, injects the round flag, and preserves the existing `IContainerManager` boundary. Static challenges without runtime configuration are rejected during competition validation rather than silently skipped.

KoH no longer treats a container ID or Compose project name as a network host. Runner contracts return and persist:

- runtime kind (`container` or `compose`),
- provider type,
- container or project identifier,
- orchestration namespace,
- internal host and port,
- public host, published ports, and entry URL.

KoH polling uses the internal endpoint. Cleanup selects `DestroyContainerAsync` or `ComposeDownAsync` from the persisted runtime kind and metadata. Existing GameBox records remain readable; maintenance attempts to enrich legacy records and reports records that cannot be safely inferred.

## Concurrency and Durable Work

Penetration instance transitions acquire a keyed PostgreSQL advisory lock for `(competition, team, challenge)` and use a database concurrency token. Only short state-claim and state-finalization transactions hold database locks; Runner calls remain outside database transactions. Conflicting requests receive the existing `instance_busy` behavior.

Background tasks gain renewable leases with an owner token. A API-hosted Channel consumer renews a running task periodically and only that owner may complete or fail it. Expired tasks are recoverable, while a slow healthy task is not executed twice.

Competition engines, score emission, Redis leaderboard publication, and initialization operations are made idempotent at database boundaries. Unique conflicts are handled as competing successful executions, not generic failures.

## Storage

Challenge attachments and patch templates persist stable object keys in new nullable columns. Existing URL fields remain in public DTOs for compatibility.

- New uploads write the object key and generate a fresh signed URL for each response.
- Reads prefer the key and generate a current URL.
- Legacy URL-only records continue to work; a migration extracts keys where the local/S3 URL format is unambiguous.
- Deletion of teams, competitions, submissions, and templates schedules object cleanup.
- Local storage resolves every path under its configured root for download, upload, and delete and rejects traversal.

## Authentication, Public Data, and Transport Security

- Remove the obsolete `Microsoft.AspNetCore.Identity` 2.2 package and use the supported .NET 8 shared framework implementation.
- Upgrade vulnerable transitive dependencies and replace floating package versions with exact versions.
- Add an SDK pin and locked restore inputs; pin Bun and production image tags to supported versions.
- Reject known placeholder secrets and weak Runner, JWT, database, storage, and bootstrap credentials outside Development.
- Seed logic may create the first administrator but never promotes an existing user by matching email.
- Configure trusted forwarded headers before authentication and rate limiting.
- Add production HSTS and TLS-enabled Ingress configuration.
- Expose Swagger only in Development or when explicitly enabled.
- Split process liveness from dependency readiness and update Kubernetes probes accordingly.
- Public leaderboard detail keeps team-level aggregates but omits member IDs, names, and per-member solve history for anonymous users. Managers and the team's own members may receive the existing member data.
- Health endpoints return stable status codes and generic public descriptions rather than raw exception messages.

## Real-Time and Query Load

The AWDP screen uses one shared snapshot producer per active competition. A cached snapshot is broadcast to SSE or SignalR consumers; individual clients do not execute database rebuilds. Anonymous connection and snapshot endpoints receive concurrency and rate limits. Public error messages are mapped to safe status text and never include checker stdout/stderr.

Leaderboard trend and detail queries use server-side filtering and bounded result sets. Penetration rate-limit counting is performed in SQL with a supporting composite index rather than loading historical submissions into memory.

## Data Integrity and Validation

Migrations will:

1. remove or report orphaned rows deterministically,
2. add stable storage-key and runtime-metadata columns,
3. add concurrency and lease-owner columns,
4. add missing composite indexes,
5. add foreign keys with explicit cascade or restrict behavior where ownership is unambiguous.

Configuration validation enforces competition time order, non-empty bounded names, valid point ranges, positive bounded round/check/poll durations, and bounded penalty and timeout settings. Orchestration JSON parse failures become explicit validation errors instead of silently falling back to defaults. Docker commands preserve structured argument arrays rather than splitting quoted commands on spaces.

## Deployment Hardening

Kubernetes manifests will use non-root, read-only filesystems, dropped capabilities, seccomp, explicit service-account-token settings, and separate liveness/readiness probes for API and API-hosted Channel consumer where applicable. Secret manifests contain no usable shared credentials. TLS and forwarded-header settings are documented and validated.

Database migration execution is separated from horizontally scaled API startup through an explicit migration job or deployment step. Automatic API migration remains available only as an opt-in compatibility setting.

Plugin assemblies remain fully trusted code. External plugins require an allow-list plus a configured SHA-256 digest; built-in plugins are matched against the deployment manifest. This does not attempt an in-process security sandbox, which would provide a misleading trust boundary.

## Error Handling and Compatibility

Public routes and existing response fields are preserved. New fields are additive. Legacy database records are read through compatibility adapters until migrated.

External Runner and storage failures produce durable retryable states. Logs and audit records pass through centralized redaction before persistence or public broadcasting. User-visible errors use stable codes and do not contain internal hosts, paths, environment variables, command output, or exception text.

## Testing and Acceptance

The existing unit suite remains green. New coverage includes:

- PostgreSQL integration tests for scoring rebuild ownership, advisory leases, idempotency races, foreign keys, tenant isolation, task lease renewal, and Penetration transitions;
- API authorization tests for every anonymous and protected endpoint, including public leaderboard redaction;
- AWD lifecycle tests using challenge runtime configuration;
- KoH container and Compose metadata, polling, and cleanup contract tests;
- storage key migration, URL refresh, deletion, and traversal tests;
- shared AWDP snapshot tests proving database work does not scale with client count;
- deployment configuration tests for placeholder secrets, TLS, forwarded headers, and probe separation;
- package-vulnerability, deprecated-package, migration-drift, analyzer, formatting, build, and test checks in CI.

Completion requires a clean Release build, all tests passing, no known vulnerable runtime packages reported by `dotnet list package --vulnerable --include-transitive`, no pending model changes, and a clean working tree aside from the intentional remediation commits.

## Delivery Sequence

1. Dependency and build reproducibility baseline.
2. Scoring ownership and durable post-commit work.
3. Plugin background hosting and engine leases.
4. Runner metadata, AWD runtime configuration, and KoH lifecycle.
5. Penetration and task concurrency.
6. Stable storage references and deletion cleanup.
7. Authentication, public data, rate limiting, and real-time load controls.
8. Database integrity and validation migrations.
9. Kubernetes and CI hardening.
10. Full verification and compatibility review.
