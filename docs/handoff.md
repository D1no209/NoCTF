# NoCTF Handoff

This handoff is the fastest path for a new collaborator to understand the current project, run it, and make changes without breaking the plugin-style architecture.

## 2026-07-18 Documentation deployment privacy

- Tracked documentation uses operator-supplied placeholders for public origins, host paths, test-group identifiers, and deployment credentials.
- Environment-specific production details remain outside version control. Examples use reserved documentation addresses or generic domains only.

## 2026-07-18 Audit client IP normalization

- Audit writers now normalize IPv4-mapped IPv6 values such as `::ffff:192.0.2.10` to canonical IPv4 before persistence.
- The audit-log query also normalizes previously stored mapped addresses when returning them, so historical rows display consistently without a data migration.
- Native IPv6 addresses are preserved; forwarded-header trust and proxy selection rules are unchanged.

## 2026-07-18 Admin challenge taxonomy readability

- The challenge-library table keeps the existing badge treatment for runtime type and direction, but both labels now use the same readable text size as the challenge title instead of the previous 10-pixel rendering.

## 2026-07-18 QQ Bot competition event configuration

- Competition QQ Bot event switches now map exactly to the backend `QqBotEventType` enum: competition start, challenge publish, hint publish, first/second/third blood, team penalty, and announcement.
- Saving a competition configuration now sends only the update contract and always supplies each binding's event list.
- The QQ Bot administration service updates existing event rules and group bindings in place instead of deleting and recreating rows that share unique competition/event or competition/group keys. Stable row IDs are retained and repeat saves no longer collide with those unique indexes.

## 2026-07-18 Login identifier input rendering

- The missing login identifier control was caused by Vue I18n interpreting the unescaped `@` in the localized example address as linked-message syntax. That render exception replaced the field with comment nodes while leaving the password field visible.
- Both localized placeholders now encode the literal at-sign with Vue I18n message syntax and render as `name@example.com or johndoe` (localized conjunction in Chinese).
- The unrelated shared `Input` component changes from the initial diagnosis were reverted. The field remains the existing native text control with `autocomplete="username"`.

## 2026-07-18 Challenge type/direction separation

- Challenge templates and competition-bound challenges now store `TypeId` (runtime type such as CTF, AWD, or AWDP) separately from `Direction` (for example WEB, PWN, MISC, or AI).
- The admin challenge form offers mode-aware direction choices: AWD uses WEB/PWN, AWDP uses WEB/PWN/AI, and CTF/KoH expose the established common direction list.
- Public challenge cards, leaderboard direction aggregation, AWDP screen categories, and QQ Bot `problem_category` messages now read `Direction`; runtime dispatch continues to use `TypeId` unchanged.
- Migration `20260718025047_AddChallengeDirections` adds the two non-null columns. It preserves exact legacy direction values when `TypeId` is one of the established directions; rows whose direction cannot be proven remain `Uncategorized`.

## Project Snapshot

NoCTF is a multi-mode competition platform for CTF, AWD, AWDP, and KoH events.

- Backend: .NET 8, FastEndpoints, SignalR, EF Core, PostgreSQL, Redis.
- Frontend: Vue 3, TypeScript, Vite, Bun, Tailwind CSS, shadcn-vue style components.
- Runtime: Docker Compose for PostgreSQL, Redis, MinIO, API, worker, and runner.
- Architecture rule: keep game-mode and challenge-type behavior plugin-oriented. Put shared contracts in `NoCTF.PluginBase`, shared domain in `NoCTF.Core`, application services in `NoCTF.Application`, and mode-specific behavior in `NoCTF.Plugins.*`.

Current maintainer expectation: after code changes, rebuild both frontend and backend, then verify the relevant browser flow before handing work back.

## First 15 Minutes

1. Read this file first.
2. Read [Architecture](architecture.md) for the plugin and tenancy model.
3. Read [Development](development.md) for local setup details.
4. Run the stack and health check:

```powershell
docker compose -f deploy/docker-compose.yml up -d --build backend worker runner
Invoke-RestMethod http://127.0.0.1/api/health
```

5. Start or reuse the frontend dev server:

```powershell
cd frontend
bun install
bun run dev
```

6. Open `http://127.0.0.1:5173`.

Default admin account:

```text
Email: admin@noctf.local
Password: Admin@123456
```

Normal player accounts are usually created through registration or `POST /api/auth/register`.

## Repository Map

```text
backend/src/NoCTF.API
  FastEndpoints, auth, SignalR hubs, middleware, admin/player endpoints.

backend/src/NoCTF.Application
  Shared application services, leaderboard, scoring, background task abstractions.

backend/src/NoCTF.Core
  Entities, enums, constants, tenant-scoped domain model.

backend/src/NoCTF.Infrastructure
  EF Core DbContext, migrations, tenant context, storage/data seeding.

backend/src/NoCTF.PluginBase
  Contracts that plugins and infrastructure share.

backend/src/NoCTF.Container.Docker
  Docker Engine container manager/provider implementation.

backend/src/NoCTF.Runner
  Runtime boundary for Docker operations.

backend/src/NoCTF.Worker
  Background jobs such as expired instance cleanup.

backend/src/NoCTF.Plugins.CTF
backend/src/NoCTF.Plugins.AWD
backend/src/NoCTF.Plugins.AWDP
backend/src/NoCTF.Plugins.KoH
  Game-mode-specific logic.

backend/src/NoCTF.Plugins.Penetration
  CTF/Jeopardy Penetration challenge type: per-team Docker Compose ranges, staged flags, dynamic flag injection, and stage scoring.

frontend/src
  Vue application, routes, layouts, admin/player pages, API wrappers.

deploy/docker-compose.yml
  Local integrated stack.
```

## Running The Project

### Integrated Docker Stack

Use this when you want the production-like path where the backend serves the built SPA at `http://127.0.0.1/`.

```powershell
docker compose -f deploy/docker-compose.yml up -d --build
Invoke-RestMethod http://127.0.0.1/api/health
```

Services:

- `postgres`: PostgreSQL on `5432`
- `redis`: Redis on `6379`
- `minio`: S3-compatible storage on `9000`, console on `9001`
- `runner`: Docker operation boundary
- `worker`: background worker
- `backend`: API and built SPA on host port `80`

### Frontend Dev Server

The frontend dev server runs at `http://127.0.0.1:5173`.

```powershell
cd frontend
bun run dev
```

Vite proxies `/api` and `/hubs` to `NOCTF_API_TARGET`. If the variable is unset, it defaults to `http://127.0.0.1`, which matches the Docker Compose backend on port `80`.

When using a locally run API on port `5000`, start Vite like this:

```powershell
$env:NOCTF_API_TARGET = "http://127.0.0.1:5000"
bun run dev
```

### Backend Dev Server

```powershell
dotnet run --project backend/src/NoCTF.API --urls "http://127.0.0.1:5000"
```

The API health endpoint is:

```text
http://127.0.0.1:5000/api/health
```

Swagger is available in development mode at:

```text
http://127.0.0.1:5000/swagger
```

## Environment Notes

Important environment variables:

| Variable | Purpose |
| --- | --- |
| `POSTGRES_PASSWORD` | PostgreSQL password. |
| `JWT_SECRET` | Must be at least 32 characters. |
| `SEED_ADMIN_EMAIL` | Initial admin email when no admin exists. |
| `SEED_ADMIN_PASSWORD` | Initial admin password when no admin exists. |
| `DOCKER_HOST` | Docker socket/host for container operations. |
| `NOCTF_PUBLIC_HOST` | Host/IP shown to players for dynamic challenge instances. |
| `NOCTF_API_TARGET` | Frontend dev proxy target for `/api` and `/hubs`. |

If Docker image pulls or GitHub pushes need the local proxy, use the maintainer's common PowerShell proxy pattern:

```powershell
$env:HTTP_PROXY = "http://127.0.0.1:7890"
$env:HTTPS_PROXY = "http://127.0.0.1:7890"
```

For Docker Compose build args inside containers, `deploy/.env` uses:

```text
HTTP_PROXY=http://host.docker.internal:7890
HTTPS_PROXY=http://host.docker.internal:7890
ALL_PROXY=http://host.docker.internal:7890
```

For player-visible dynamic challenge addresses, set `NOCTF_PUBLIC_HOST` to a LAN-reachable IP or DNS name. If left empty, the API derives the host from the incoming request.

## Core Product Flows

### Admin Competition Flow

1. Login as admin.
2. Open `/admin/competitions`.
3. Create or edit a competition.
4. Use the competition detail page to configure mode/scoring/team settings.
5. Add challenges from the challenge bank into the competition.
6. Configure per-competition challenge scoring, decay, hints, flag prefix, and first-blood options where applicable.
7. Review team registrations and approve teams unless auto-approval is enabled.
8. Monitor logs, submissions, containers, and health pages.

### Challenge Bank Flow

The admin-side `Challenges` page is the challenge bank. It should own reusable challenge basics:

- name
- description
- direction/category
- attachment
- container image
- checker image when the mode needs one
- exposed container port
- challenge deployment type
- flag content or dynamic flag variable name

Competition-specific values belong in competition challenge management, not the bank:

- score
- decay profile
- difficulty factor
- first-blood reward toggle
- hint publication for a specific competition
- flag prefix for that competition

### AWDP Challenge Authoring

AWDP is implemented as its own plugin and should stay separate from AWD. The current AWDP validation model is:

- A participant uploads a FixScript archive.
- The platform audits the archive, runs the configured FixScript entry in an isolated side runner, recreates the team instance with patch metadata, then runs one challenge-author `check` container.
- There is no separate AWDP EXP phase. For AWDP, `CheckerConfig.Image`, `CheckerConfig.Command`, and `CheckerConfig.TimeoutSeconds` mean the check container image, command, and timeout.
- The check container receives `TARGET_HOST`, `TARGET_PORT`, `TEAM_ID`, `PATCH_URL`, `PATCH_FILE_NAME`, and `FIX_ENTRY`.
- Check exit codes map as `0` fix success, `1` exploit still succeeds, `2` bad/rule-violating patch, `3` service or interaction error, and timeout as service error.
- Participant UI only exposes defense success, exploit-success defense abnormal, or service-error defense abnormal. Internal bad-patch detail remains operator-facing.

AWDP challenge-bank assets can include a downloadable patch template:

- `ChallengeTemplate.PatchTemplateUrl` and `Challenge.PatchTemplateUrl`
- upload endpoint: `POST /api/admin/challenges/{id}/patch-template`
- participant download button appears in the challenge modal when `patchTemplateUrl` exists

Reference authoring templates live under:

```text
backend/src/NoCTF.Plugins.AWDP/Templates/basic-web
backend/src/NoCTF.Plugins.AWDP/Templates/standard-web
```

### Player Flow

1. Register or login.
2. Create or join a team.
3. Register a team for a competition.
4. Wait for approval unless the competition has auto-approval enabled.
5. Enter the competition.
6. Filter challenges by direction or show all.
7. Open a challenge card and solve/submit.
8. For dynamic containers, create an instance, copy the displayed `host:port`, extend/destroy as needed.
9. For AWDP, request defense and upload a FixScript only after defense is enabled. AWD uses its own team-to-team attack and service-defense dashboard.

### Penetration Challenge Flow

Penetration Challenge is a CTF challenge type, not AWD/AWDP/KoH.

Admin flow:

1. Create a challenge-bank template with `TypeId = Penetration`.
2. Configure the topology JSON on the template.
3. Bind it to a CTF competition.
4. Optionally edit the competition-specific topology from the competition challenge panel.
5. Monitor team instances through the Penetration admin APIs.

Player flow:

1. Open the Penetration challenge modal.
2. Start the team's own instance.
3. Use only the displayed entry address and in-range services.
4. Submit stage flags; each visible stage is scored independently.
5. Reset or destroy the range as allowed by the challenge config.

Penetration ranges are rendered as Compose and run through the configured Runner. Docker Runner executes Compose directly; Kubernetes Runner translates the supported Compose subset into per-instance Kubernetes resources.

## Dynamic Containers And Flags

Dynamic challenge instances are managed through:

```text
GET    /api/competitions/{id}/challenges/{challengeId}/instance
POST   /api/competitions/{id}/challenges/{challengeId}/instance
DELETE /api/competitions/{id}/challenges/{challengeId}/instance
POST   /api/competitions/{id}/challenges/{challengeId}/instance/extend
```

Current lifecycle rules:

- one active instance per team/challenge
- 2 hour default TTL
- 30 minute extension
- 5 second cooldown between create/destroy/extend operations for CTF dynamic instances
- 30 second container-operation cooldown for AWDP create/destroy/extend and defense requests
- expired instances are cleaned by the worker

Dynamic flag environment variable:

```text
NOCTF_FLAG_UUID
```

Challenge authors can override the environment variable name in the challenge form. If left empty, the platform uses `NOCTF_FLAG_UUID`.

Static flags are composed from a competition-level flag prefix and the stored flag content. Dynamic container flags use a generated UUID value passed through the configured environment variable.

## Scoring And Leaderboard Notes

The CTF scoring path currently supports:

- dynamic score decay
- multiple decay modes
- difficulty factor
- minimum score
- first three blood rewards
- direction/category score breakdown
- solved-state display for players
- suspected cheating information for cross-team dynamic flag submissions
- competition-scoped bans

Leaderboard expectations:

- approved registered teams should appear even with zero score
- banned teams should not inflate solve counts
- challenge solve counts should exclude banned teams
- score timeline should be stepwise, showing sudden score changes over time
- challenge cells should show special notation for first-blood bonuses

## Audit And Logs

There are two important log concepts:

- system/admin logs for platform health and operations
- competition logs scoped to one competition

Competition logs should include events such as:

- team registration
- team approval/rejection
- container create/destroy/extend/cleanup
- flag submissions
- suspicious dynamic-flag submissions
- admin ban/unban actions
- warnings and mode-specific exceptions

When adding endpoints that mutate meaningful state, check whether the endpoint should implement `IAuditableEndpoint` or explicitly write a competition log through `CompetitionLogWriter`.

## Frontend Conventions

- Keep existing Vue 3 + TypeScript + Tailwind patterns.
- Prefer existing components under `frontend/src/components`.
- Use `lucide-vue-next` icons where suitable.
- Do not introduce new UI libraries for small visual changes.
- Maintain i18n in both `frontend/src/locales/zh-CN.json` and `frontend/src/locales/en.json`.
- Avoid landing-page style layouts for operational admin screens.
- For player competition screens, verify both desktop and mobile viewports.

## Backend Conventions

- Endpoints live under `backend/src/NoCTF.API/Endpoints`.
- Tenant-scoped entities are protected by EF Core query filters. Use `.IgnoreQueryFilters()` only when intentionally crossing competition boundaries.
- Admin/global reads often need `.IgnoreQueryFilters()`.
- Player endpoints should derive the current user from JWT claims and verify team/competition membership.
- Keep mode-specific behavior in plugin projects.
- Add migrations for schema changes under `NoCTF.Infrastructure`.
- If endpoint DTOs change and frontend generated types rely on them, regenerate the OpenAPI client.

## Verification Checklist

Run these before handing work back:

```powershell
cd frontend
bun run build
```

```powershell
dotnet build backend/NoCTF.slnx
```

For backend behavior changes:

```powershell
dotnet test backend/tests/NoCTF.Tests
```

For integrated validation:

```powershell
docker compose -f deploy/docker-compose.yml up -d --build backend worker runner
Invoke-RestMethod http://127.0.0.1/api/health
```

For frontend/player/admin flows:

- use the in-app browser or Edge
- login with the seeded admin account
- test the real UI, not only direct API calls
- check console errors and failed network requests
- test mobile width when layout changes are involved

## Current Work Context

The active working branch is `main`. Use `git status --short` and `git log --oneline -n 15` for the current HEAD instead of relying on a fixed hash; do not reset or clean unrelated user work.

The current work covers plugin-host fail-fast behavior, score and task idempotency, PostgreSQL execution leases, storage CAS replacement and cleanup outbox, Redis leaderboard versioning, API query/rate-limit/security hardening, Docker/Kubernetes runtime durability, game-engine recovery, team lifecycle invariants, safe identity migration, deployment manifests, CI, and expanded integration tests. Plugin boundaries and public API compatibility remain required.

Latest verification status:

- Release solution build: 0 warnings, 0 errors.
- Complete Release suite with real PostgreSQL 16 and Redis 7: 490/490 passed. The EF InMemory test helper now reuses option-compatible service providers without suppressing the production warning; the notification payload boundary, email-verification flow, Admin email-settings, and explicit SMTP transport-mode regressions are included in this count.
- `SubmissionMutationGuard` lease, transaction, cancellation, rollback, and release paths were manually reviewed for CTF, Penetration, AWDP, and AWD. AWD rollback remains available after mutation-token cancellation.
- Analyzer and whitespace checks passed; the EF migration model has no pending changes.
- An empty PostgreSQL database passed full upgrade, latest-migration rollback/reapply, full downgrade to `0`, and full re-upgrade.
- OpenAPI was fetched from a running API and regenerated idempotently. Contract changes are the expected `before`/`limit` pagination on AWDP patch submissions, authenticated notification inbox endpoints, email-verification request/response contracts, and the three Admin email-settings endpoints; generator template changes follow the security upgrade to `@hey-api/openapi-ts` 0.97.3.
- Frontend frozen install, all 8 tracked tests, type-check, and production build passed. NuGet vulnerability/deprecation scans and `bun audit` are clean. The repository-wide ESLint run still reports the pre-existing generated/UI-scaffold baseline; the newly added notification and email-verification views pass targeted lint and no unrelated bulk formatting was applied.
- Compose parsing and kubeconform strict passed (47 valid resources, 0 invalid/errors/skipped). Local `kubectl apply --dry-run=client` could not perform API discovery because no cluster is configured at `localhost:8080`; this is not a manifest validation failure.
- API and Worker publish outputs contain the five required game/challenge plugin assemblies plus the optional QQBot assembly. Temporary PostgreSQL/Redis validation containers were removed.
- Frontend authentication now refreshes active sessions two minutes before access-token expiry, coalesces concurrent refresh attempts, retries transient refresh failures without discarding a still-valid token, and updates route guards, API requests, and SignalR token callbacks from the renewed session. The backend's existing absolute session limit and unauthorized-response behavior remain unchanged. Focused auth tests, frontend type-check/production build, and a real login-refresh-admin API sequence pass.
- Vite mock interception is opt-in through `VITE_ENABLE_MOCKS=true`. It must remain disabled for normal full-stack development: the mock login JWT is intentionally not accepted by the real backend, and mixing it with real admin endpoints causes an immediate 401 and session clear. A Playwright full-stack login-to-admin regression check now reaches `/admin/competitions` with the real admin identity and a retained session.
- The global QQ Bot workspace, per-competition QQ Bot delivery center, Infrastructure workspace, and their admin navigation entries now use the existing Vue i18n layer. English and Simplified Chinese locale trees have matching keys; backend-provided names, connection types, diagnostic values, warnings, and safe error summaries remain unmodified runtime data.
- Root `DESIGN.md` and `.impeccable/design.json` now document the actual Pixel Industrial implementation and the contract for future developer-curated visual themes. Users may select only registered theme identifiers; custom CSS, token payloads, remote fonts, backgrounds, imports, and theme-authored behavior are explicitly outside the design boundary. The document preserves the existing Competition Control Room principles while replacing the stale Inter, violet, rounded-card token set with the shipped Fusion Pixel, grayscale, square-control, and solid-offset-shadow system.
- Pixel Industrial retains Fusion Pixel across the complete interface, including dense UI copy, forms, tables, descriptions, and captions. Readability is improved without changing the mosaic typeface: Tailwind `text-xs` and `text-sm` are raised to `0.8rem` and `0.9rem`, line height is `1.5`, and pixel glyphs must not render below the `0.8rem` floor.
- The authenticated user notification inbox persists an allowlisted view of confirmed competition events for competition owners, collaborators, and members of approved teams. `ICompetitionNotificationOutbox` is now a failure-isolating composite over notification sinks, so the built-in inbox and the optional QQ Bot sink coexist without either registration replacing the other. The inbox API is `GET /api/notifications`, `POST /api/notifications/{id}/read`, and `POST /api/notifications/read-all`; all reads and mutations are scoped from the JWT user claim. Migration `AddUserNotificationInbox` adds only the user-scoped notification table and its idempotency/read indexes.
- The navigation bell is now an authenticated notification center instead of an inert control. It polls the user-scoped inbox every 30 seconds, refreshes when opened, exposes an accessible unread count, supports per-item and bulk read state, and links competition events back to their competition. English and Simplified Chinese copy covers all confirmed event types and uses a safe generic fallback for future types; unknown payload fields are never rendered by that fallback.
- The notification audit found and fixed two release issues: allowlisted inbox strings are bounded before serialization so an oversized public Hint cannot overflow the 4096-character notification payload column and roll back the Hint update; frontend dependencies now use `@hey-api/openapi-ts` 0.97.3 and Vite 7.3.6 with patched `esbuild` 0.28.1 and `js-yaml` 4.3.0 overrides. `bun audit` reports no vulnerabilities, and the regenerated OpenAPI difference is limited to the three notification endpoints and their request/response schemas plus expected generator-template updates.
- Competition and team deletion now persist successful runtime cleanup before issuing relational bulk artifact deletes. This prevents EF Core from retaining already-deleted runtime entities and raising `DbUpdateConcurrencyException` during the final save; cleanup failure semantics and the rule that database state is preserved when a runtime resource cannot be destroyed remain unchanged.
- Optional email verification is implemented behind its effective `Enabled` setting. New accounts are pending only when enabled; existing accounts are backfilled as verified, raw verification tokens are never stored, resend responses do not reveal account existence, and login rejects pending accounts. Registration and pending-login flows route to the localized `/verify-email` workspace, which consumes the token through a POST body, removes it from the browser URL, supports resend cooldown feedback, and returns verified users to login. SMTP remains disabled until values are supplied through deployment configuration or the Admin-only settings page; never commit the SMTP password.
- System administrators now have an **Email verification** navigation entry at `/admin/email-verification`. Its Admin-only API persists verification policy and SMTP settings in the singleton `EmailVerificationSettings` row, returns only a password-configured flag, retains the existing password when the field is blank, audits changes with password redaction, rate-limits test delivery, and sends tests only to the signed-in administrator. Environment variables remain bootstrap defaults; a persisted row takes precedence. The SMTP password is AES-GCM protected with a purpose-specific key derived from `JwtSettings:Secret`, so JWT-secret rotation requires re-entering that SMTP password.
- SMTP delivery uses explicit transport security: TLS-enabled port 465 connects with implicit TLS, while other TLS-enabled ports require STARTTLS. This avoids the `System.Net.Mail` port-465 timeout and preserves certificate validation, bounded timeout, authentication, cancellation, and plaintext-message behavior.
- The expanded Admin navigation now uses an adaptive, route-local sidebar layout: full-size navigation rows are distributed across the available height, while the compact account/action footer reserves room for every route. Supported desktop viewports are filled without an unnecessary nested scrollbar; genuinely short and mobile viewports retain the shared sidebar overflow behavior.
- Registration trims surrounding username whitespace, rejects remaining Unicode whitespace, and relies on the existing normalized unique username index for case-insensitive uniqueness. Login accepts either email or username through the backward-compatible login request field, matching both identifiers case-insensitively; the localized login form reflects the combined identifier semantics.
- Admin challenge management now exposes independent challenge-type, attachment-state, and asset-type filters alongside title search. The existing TanStack table pagination is surfaced with localized previous/next controls and filtered-result counts, so large reusable challenge banks remain bounded to one page while filters reset safely to the first page.
- Challenge-bank rows now show a container mode only when the existing deployment type is `DynamicContainer` or `StaticContainer`; attachment-only and no-asset templates display no container instead of inheriting the entity's `SingleImage` default. The sensitive action is labeled **Reveal Flag**, opens a loading/result dialog, and immediately issues the existing audited reveal request with an explicit empty JSON body required by the unsafe-request boundary. Empty and failed responses are visible instead of leaving a blank dialog.
- Competition settings no longer expose the legacy competition-level score-decay controls or preview; score decay remains a per-deployed-challenge setting. The remaining identity, participation, and schedule controls are grouped into explicit sections. Admin/Organizer competition logs now return the exact submitted flag only from newly persisted submission-log metadata and display it in the authorized competition log workspace; historical redacted submissions cannot be reconstructed. The log panel uses page flow instead of an internal fixed-height scroll area. Realtime system-log redaction now masks concrete assignments, bearer credentials, and flag payloads without collapsing harmless EF schema identifiers such as `TokenVersion` into `[REDACTED]`. The plugin inventory is sourced from the loader's module catalog, includes QQBot and Penetration, and reports the core engine separately; CTF, AWDP, and Penetration modules are versioned `1.0.1` for their submission-log metadata change.
- Competition challenge management is now a direction-filterable card grid. Creating a deployed challenge and editing an existing one use focused routes (`/admin/competitions/:id/challenges/new` and `/admin/competitions/:id/challenges/:challengeId/edit`) with explicit Save/Cancel/Back navigation to the challenge list. Per-challenge scoring, decay preview, direction, Markdown statement, hints, blood-bonus switch, and AWDP overrides remain on those focused pages. The bind contract adds only an optional `direction`; omitted values still inherit the reusable template direction, preserving older clients.
- OpenAPI was regenerated from the local Release API after these Admin changes. The only contract additions are optional `direction` on the existing bind-competition-challenge request and optional `submittedFlag` on the Admin/Organizer competition-log response; generated frontend types contain the same two expected fields.
- The competition QQ Bot workspace translates the platform warning codes `global_plugin_disabled`, `no_online_agent`, and `no_bound_group` in both English and Simplified Chinese. Configuration and template-preview warnings use the same allowlisted presentation helper; unknown future codes remain visible as safe plain text rather than being silently discarded.
- Competition challenge directions now remain editable after a template is bound, and migration `BackfillChallengeDirectionsFromTemplates` repairs only legacy `UNCATEGORIZED` competition challenges from their linked template's allowlisted direction. This fixes the public challenge-direction filter without inferring a direction from the runtime type or overwriting an explicitly configured competition value. The Admin update contract adds an optional `direction` field so older clients remain compatible.
- Competition **Operations** and the mode-specific **AWDP screen** entry now live in the competition navigation between team review and cheat information instead of floating beside scoreboard controls. The navigation, operations workspace, scoreboard actions, penetration runtime controls, and QQ Bot delivery workspace have matching English and Simplified Chinese copy.
- The per-competition QQ Bot delivery workspace is reorganized into delivery controls, message-content options, bound groups, and event rules, with compact actions and safe non-empty Select sentinels. Both operation workspaces stay mounted and switch visibility explicitly, avoiding the previous large-panel tab patch failure; the challenge-link flag now hydrates from its own persisted field.
- The Admin-only user-role endpoint now accepts the existing `Admin` enum value instead of rejecting it after the UI submits the selection. Role changes still increment `TokenVersion` and publish the cache update, so the promoted account must sign in again before receiving administrator claims. A focused endpoint test covers promotion and session revocation. Freshly seeded bootstrap administrators are explicitly email-verified because migrations run before the seed step on an empty production database; startup also repairs the same deployment-order gap for an existing unverified account that exactly matches the configured seed administrator. This prevents the trusted bootstrap administrator from being locked out when verification is enabled without weakening verification for ordinary accounts. The frontend document title is `noctf`, and the Vite favicon has been replaced by the existing pixel-brand dinosaur motif.

QQ broadcast integration is now implemented as an optional sixth platform plugin plus a separately deployed outbound NoneBot/Milky agent. Platform code is C#; the Python agent lives under `integrations/qqbot` and the read-only reference checkout at `E:\SourceCode\QQBOT` is never modified. Configuration is global-policy plus explicit per-competition opt-in and group/event binding. PostgreSQL owns the durable outbox, leases, ACK/failure history, idempotency, and audit; Redis owns only replay protection and short cooldowns. See [`qqbot-integration.md`](qqbot-integration.md) for deployment, security, triggers, permissions, test-group restrictions, and rollback.

The QQBot agent requirements pass `pip-audit`, and an ephemeral Python 3.12 container successfully imported the plugin and built the allowlisted Milky `mention_all`/`text` message segments. No live QQ message is sent during automated verification; operators must provide an explicitly approved test group outside version control for manual smoke tests.

The production NoneBot/Milky agent must be registered against the operator-provided HTTPS platform origin and locally allowlist only explicitly approved groups. Milky 1.2 requires the `no_cache` field on `get_group_list`; the agent supplies `false` explicitly so group synchronization works with the deployed Lagrange implementation instead of failing request deserialization. Existing QQBOT matchers remain untouched.

The authoritative continuation instructions, architecture reading map, four critical request flows, collaborator protocol, exact risk list, validation caveats, and required execution order are in [`HANDOFF_PROMPT.md`](../HANDOFF_PROMPT.md). A new collaborator should follow its 20–30 minute quick-start sequence before changing the working tree.

## Common Pitfalls

- Do not put competition-specific score/decay settings in the challenge bank.
- Do not count banned teams in solve counts or dynamic score decay.
- Do not show `127.0.0.1` to players for challenge containers unless the player is on the same host and that is intentional.
- Do not API-test only when the reported bug is a browser freeze or UI loading issue.
- Do not bypass team approval checks for player competition actions.
- Do not move game-mode-specific logic into the API project unless it is orchestration glue.
- Do not forget i18n for new visible UI text.
- Do not reintroduce a separate AWDP EXP container or expose internal bad-patch details to participants.
- Do not bypass the shared auth-session coordinator when adding authenticated frontend transports; API requests, route guards, and SignalR must observe the same renewed token.

## Useful Links

- [README](../README.md)
- [Architecture](architecture.md)
- [Development Guide](development.md)
- [Deployment Guide](deployment.md)
- [Game Modes](game-modes.md)
- [Penetration Challenges](penetration-challenges.md)
- [Penetration Operations](penetration-operations.md)
- [API Reference](api.md)
- [Quickstart Test Guide](quickstart-test.md)
# 2026-07-18 — reduce real-time EF command log noise

- The admin real-time log stream now drops routine `Information` events from `Microsoft.EntityFrameworkCore.Database.Command`, including the high-frequency QQ bot lease polling SQL.
- EF Core database warnings and errors remain visible, and application `Information` events continue to stream unchanged.
- The QQ bot polling cadence, lease transaction, retry, and acknowledgement behavior were not changed.
- Added focused logger tests covering both the EF command suppression and preservation of normal application information logs.
