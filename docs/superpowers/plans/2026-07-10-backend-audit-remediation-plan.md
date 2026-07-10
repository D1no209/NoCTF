# Backend Audit Remediation Implementation Plan

## Compatibility Rules

- Preserve public routes and existing response fields.
- Keep plugins as the unit of game-mode, challenge, scoring, and background capability registration.
- Prefer additive schema and internal contract changes.
- Keep legacy records readable while new writes use corrected formats.

## Phase 1: Dependency Baseline

1. Remove the obsolete ASP.NET Core Identity package.
2. Override vulnerable transitive packages with supported versions and verify the resolved graph.
3. replace floating package versions with exact versions.
4. Add `global.json` and NuGet lock files or locked restore configuration.
5. Add CI checks for vulnerable and deprecated runtime dependencies.

## Phase 2: Scoring Correctness

1. Add challenge scoring rebuild contributor abstractions.
2. Restrict ordinary CTF rebuilding to challenges without custom owners.
3. Implement Penetration stage and blood-bonus rebuild contribution.
4. Make score signal/event unique races idempotent.
5. Add cross-plugin and concurrent rebuild tests.

## Phase 3: Background Execution

1. Introduce plugin background capability registration shared by API and Worker.
2. Stop starting competition engines in the API host.
3. Load built-in plugin background capabilities in Worker without mode-specific dispatch logic.
4. Add PostgreSQL advisory competition leases.
5. Add task owner tokens and renewable leases.

## Phase 4: Runtime Lifecycle

1. Add an AWD runtime configuration provider based on persisted Challenge configuration.
2. Extend runtime metadata with kind, internal endpoint, provider, namespace, and deployment identity.
3. Persist and consume this metadata for KoH.
4. Select container or Compose cleanup from runtime kind.
5. Add lifecycle contract tests for Docker and Kubernetes representations.

## Phase 5: Concurrency, Storage, and Integrity

1. Serialize Penetration transitions with keyed advisory locks and concurrency tokens.
2. Persist attachment and patch-template object keys and generate URLs on read.
3. Harden local storage root containment.
4. Clean object storage during destructive lifecycle operations.
5. Add request/configuration bounds, indexes, and safe foreign keys.

## Phase 6: API and Operational Security

1. Reject known placeholder production secrets and stop seed-user promotion.
2. Configure forwarded headers, HSTS, conditional Swagger, and safe public health output.
3. Split liveness/readiness endpoints.
4. Redact anonymous leaderboard member details.
5. Replace per-SSE-client AWDP database polling with a shared snapshot cache and add rate/concurrency limits.
6. Bound expensive leaderboard and Penetration rate-limit queries.

## Phase 7: Deployment and Verification

1. Harden Kubernetes pod security contexts, TLS, probes, and secret templates.
2. Move migration execution out of default horizontally scaled API startup.
3. Pin CI/runtime tool versions and validate deployment configuration.
4. Run formatting, analyzers, Release build, complete tests, package audits, and migration drift checks.
5. Review the final diff for public API compatibility and plugin-boundary preservation.
