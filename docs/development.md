# Development Guide

This guide explains how to set up a local development environment for NoCTF.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Bun](https://bun.sh/) for frontend install, development, and production builds
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for PostgreSQL, Redis, and container features)

Optional but recommended:

- [EF Core CLI tools](https://learn.microsoft.com/en-us/ef/core/cli/dotnet): `dotnet tool install --global dotnet-ef`

## Backend Development

The backend is an ASP.NET Core application using FastEndpoints. In development mode it can proxy the frontend Vite dev server automatically.

1. Start the required infrastructure services (PostgreSQL, Redis, MinIO, and Runner when testing container-backed modes) with Docker Compose:

```bash
cd deploy && docker compose up -d postgres redis minio runner
```

2. Ensure your local environment is configured. The backend reads from `appsettings.Development.json` and environment variables. At minimum you need a connection string and JWT secret. Example:

```bash
export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=noctf;Username=noctf;Password=change_me_strong_password"
export ConnectionStrings__Redis="localhost:6379"
export JwtSettings__Secret="change_me_at_least_32_chars_long_secret_key"
```

> On Windows PowerShell use `$env:ConnectionStrings__DefaultConnection = "..."`

3. Run the API:

```bash
dotnet run --project backend/src/NoCTF.API
```

The API will start on `http://localhost:5000` (or the port configured in launchSettings). In development mode, ASP.NET Core proxies the SPA to the Vite dev server at `http://localhost:5173`.

4. Open the app in your browser:

```
http://localhost:5000
```

### Database Migrations

Add a new migration from the repo root:

```bash
dotnet ef migrations add MyMigrationName \
  --project backend/src/NoCTF.Infrastructure \
  --startup-project backend/src/NoCTF.API
```

Apply migrations:

```bash
dotnet ef database update \
  --project backend/src/NoCTF.Infrastructure \
  --startup-project backend/src/NoCTF.API
```

## Frontend Development

The frontend is a Vue 3 single-page application built with Vite.

1. Install dependencies:

```bash
cd frontend && bun install
```

2. Start the Vite dev server:

```bash
bun run dev
```

The dev server runs at `http://localhost:5173`.

Vite proxies API and SignalR requests to the real local backend by default. If the backend is unreachable, the dev session automatically falls back to the mock route table (`src/mocks/mock-data.json`) after a failed request plus an `/api/health` probe; watch for the `[noctf-mock]` console message. Set `VITE_ENABLE_MOCKS=true` to force mock routes on, or `VITE_ENABLE_MOCKS=false` to disable mocks entirely — keep mocks disabled (or unset with the backend running) while validating login or management APIs against the backend, since the mock login token is not accepted by real protected APIs.

If the backend is running with `dotnet run`, you can also access the frontend through the backend URL (`http://localhost:5000`) because of the SPA proxy. The API project is configured with `SpaProxyLaunchCommand=bun run dev`; you can also start `bun run dev` yourself before opening the backend URL.

Production publish uses the checked-in `bun.lock` to build the SPA and copy `frontend/dist` into the API `wwwroot`.

### Generate the OpenAPI Client

The frontend uses a typed API client generated from the backend OpenAPI spec.

1. Make sure the backend is running locally.

2. Fetch the backend OpenAPI artifact:

```bash
cd frontend && bun run fetch-openapi
```

3. Generate the client:

```bash
cd frontend && bun run generate-api
```

This runs `openapi-ts` against `backend/artifacts/openapi/swagger.json` and creates/updates the client code in the frontend source tree.

## Running Tests

NoCTF includes backend unit and integration tests in `backend/tests/NoCTF.Tests`.

Run all tests:

```bash
dotnet test backend/tests/NoCTF.Tests
```

Run with verbosity:

```bash
dotnet test backend/tests/NoCTF.Tests --logger "console;verbosity=detailed"
```

## Project Layout for Developers

```
backend/src/
  NoCTF.API/           # Web layer (endpoints, SignalR, auth, middleware)
  NoCTF.Application/   # Services, DTOs, event handlers
  NoCTF.Core/          # Entities, enums, domain constants
  NoCTF.Infrastructure/# DbContext, migrations, storage, tenanting
  NoCTF.PluginBase/    # Plugin contracts
  NoCTF.Container.Docker/  # Docker orchestration
  NoCTF.Runner.Client/ # HTTP client and contracts for runner calls
  NoCTF.Runner/        # Runtime boundary that owns Docker access
  NoCTF.Worker/        # Background task processor
  NoCTF.Plugins.CTF/   # CTF plugin
  NoCTF.Plugins.AWD/   # AWD plugin
  NoCTF.Plugins.AWDP/  # AWDP plugin
  NoCTF.Plugins.KoH/   # KoH plugin
  NoCTF.Plugins.QQBot/ # Optional QQ notification plugin

frontend/
  src/                 # Vue 3 application source
  package.json
  vite.config.ts
  openapi-ts.config.ts # Configuration for API generation
```

## Useful Tips

- Use `dotnet watch run --project backend/src/NoCTF.API` for hot reload during backend development.
- The backend health endpoint is a quick way to verify everything is wired up: `curl http://localhost:5000/api/health`
- Swagger UI is available at `http://localhost:5000/swagger` when running in development mode.
- If you change an endpoint DTO, regenerate the OpenAPI client so the frontend types stay in sync.
- Keep QQBot platform code in C#. The separately deployed outbound NoneBot agent is under `integrations/qqbot`; never develop it inside the read-only reference checkout at `/workspace/QQBOT`.
