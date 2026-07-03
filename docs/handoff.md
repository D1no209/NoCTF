# NoCTF Handoff

This handoff is the fastest path for a new collaborator to understand the current project, run it, and make changes without breaking the plugin-style architecture.

## Project Snapshot

NoCTF is a multi-mode competition platform for CTF, AWD, AWDP, and KoH events.

- Backend: .NET 8, FastEndpoints, SignalR, EF Core, PostgreSQL, Redis.
- Frontend: Vue 3, TypeScript, Vite, Bun, Tailwind CSS, shadcn-vue style components.
- Runtime: Docker Compose for PostgreSQL, Redis, MinIO, API, worker, and runner.
- Architecture rule: keep game-mode behavior plugin-oriented. Put shared contracts in `NoCTF.PluginBase`, shared domain in `NoCTF.Core`, application services in `NoCTF.Application`, and mode-specific behavior in `NoCTF.Plugins.*`.

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

### Player Flow

1. Register or login.
2. Create or join a team.
3. Register a team for a competition.
4. Wait for approval unless the competition has auto-approval enabled.
5. Enter the competition.
6. Filter challenges by direction or show all.
7. Open a challenge card and solve/submit.
8. For dynamic containers, create an instance, copy the displayed `host:port`, extend/destroy as needed.
9. For AWDP/AWD flows, request defense and upload patches only after defense is enabled.

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
- 5 second cooldown between create/destroy/extend operations
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

As of this handoff update, the active local branch is:

```text
codex/competition-row-list
```

Open PR:

```text
https://github.com/D1no209/NoCTF/pull/1
```

Recent focus areas:

- competition list UI is now a full-width square-row layout
- competition list API exposes approved non-banned team counts
- dynamic challenge instances show reachable `host:port` addresses
- instance lifecycle includes create, destroy, extend, cooldown, and TTL
- CTF scoring and leaderboard details have been expanded
- AWDP includes a basic challenge/checker template

## Common Pitfalls

- Do not put competition-specific score/decay settings in the challenge bank.
- Do not count banned teams in solve counts or dynamic score decay.
- Do not show `127.0.0.1` to players for challenge containers unless the player is on the same host and that is intentional.
- Do not API-test only when the reported bug is a browser freeze or UI loading issue.
- Do not bypass team approval checks for player competition actions.
- Do not move game-mode-specific logic into the API project unless it is orchestration glue.
- Do not forget i18n for new visible UI text.
- Do not leave old JWTs in local storage when testing auth; expired tokens can make admin pages appear broken.

## Useful Links

- [README](../README.md)
- [Architecture](architecture.md)
- [Development Guide](development.md)
- [Deployment Guide](deployment.md)
- [Game Modes](game-modes.md)
- [API Reference](api.md)
- [Quickstart Test Guide](quickstart-test.md)
