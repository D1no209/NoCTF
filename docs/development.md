# Development Guide

This guide explains how to set up a local development environment for NoCTF.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 20+](https://nodejs.org/)
- [pnpm](https://pnpm.io/installation)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for PostgreSQL, Redis, and container features)

Optional but recommended:

- [EF Core CLI tools](https://learn.microsoft.com/en-us/ef/core/cli/dotnet): `dotnet tool install --global dotnet-ef`

## Backend Development

The backend is an ASP.NET Core application using FastEndpoints. In development mode it can proxy the frontend Vite dev server automatically.

1. Start the required infrastructure services (PostgreSQL and Redis) with Docker Compose:

```bash
cd deploy && docker compose up -d postgres redis
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

The API will start on `http://localhost:5000` (or the port configured in launchSettings). In development mode, `spa.proxy.json` proxies frontend requests to the Vite dev server at `http://localhost:5173`.

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
cd frontend && pnpm install
```

2. Start the Vite dev server:

```bash
pnpm dev
```

The dev server runs at `http://localhost:5173`.

If the backend is running with `dotnet run`, you can also access the frontend through the backend URL (`http://localhost:5000`) because of the SPA proxy.

### Generate the OpenAPI Client

The frontend uses a typed API client generated from the backend OpenAPI spec.

1. Make sure the backend is running locally.

2. Generate the client:

```bash
cd frontend && pnpm generate-api
```

This runs `openapi-ts` and creates/updates the client code in the frontend source tree.

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
  NoCTF.Plugins.CTF/   # CTF plugin
  NoCTF.Plugins.AWD/   # AWD plugin
  NoCTF.Plugins.AWDP/  # AWDP plugin
  NoCTF.Plugins.KoH/   # KoH plugin

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
