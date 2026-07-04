# Architecture

This document describes the core architecture of NoCTF, including the plugin system, game mode implementations, container abstraction, real-time communication, multi-tenancy, and security model.

## Plugin System

NoCTF uses a plugin-based architecture to keep game modes and challenge types extensible. Each plugin is a separate .NET assembly that registers its own services during startup.

### Core Interfaces

All plugins implement interfaces defined in `NoCTF.PluginBase`:

| Interface | Purpose |
|-----------|---------|
| `IGameMode` | Handles competition initialization, round ticks, and flag submission processing |
| `IChallengeType` | Validates flags and provides container configuration for a challenge type |
| `IContainerManager` | High-level abstraction for creating, destroying, and running containers |
| `IContainerProvider<TClient, TMetadata>` | Low-level provider abstraction (for example, Docker) |
| `IStorageProvider` | Upload, download, delete, and generate URLs for files |
| `IPluginModule` | Entry point for a plugin to register services |

Plugins are currently loaded eagerly in `Program.cs`:

```csharp
new CtfModule().ConfigureServices(builder.Services);
new AwdModule().ConfigureServices(builder.Services);
new AwdpModule().ConfigureServices(builder.Services);
new KohModule().ConfigureServices(builder.Services);
```

Future versions may load plugins dynamically via `AssemblyLoadContext` so new game modes can be added without recompiling the API.

## Game Mode Designs

### CTF (Jeopardy)

- **Dynamic scoring**: Uses the CTFd-style formula `((min - initial) / decay^2) * solves^2 + initial`, clamped to `[MinimumPoints, InitialPoints]`
- **First blood tracking**: The first team to solve a challenge gets a visual highlight and a permanent record
- **Duplicate solve prevention**: A team can only solve each challenge once
- **Timing-safe validation**: Flag comparison uses `CryptographicOperations.FixedTimeEquals` to prevent timing attacks

### AWD (Attack with Defense)

- **Round engine**: `AwdRoundEngine` is a background service that polls every 5 seconds and advances rounds automatically
- **Flag generation and rotation**: `AwdFlagService` generates unique flags per team, per challenge, per round, then injects them into running containers
- **Checker system**: `AwdCheckerService` runs health-check containers against each game box every round
- **Scoring**: `AwdScoreEngine` awards service uptime points, applies down penalties, and subtracts "been attacked" penalties per round
- **Attack validation**: `AwdGameMode` prevents self-attacks, duplicate attacks on the same victim/round, and expired flags

### AWDP (Attack with Defense and Patch)

- **Independent plugin**: AWDP registers its own `AwdpGameMode`, `AwdpRoundEngine`, `AwdpScoreEngine`, `AwdpPatchService`, and `AwdpModeProvider`; it is not an AWD sub-mode
- **Break + Fix state model**: Break flag submissions and FixScript submissions update AWDP team/challenge state, but do not award full challenge points immediately
- **Attempt limits**: `AwdpGameMode` enforces maximum Break attempts, and `AwdpPatchService` enforces maximum Fix attempts
- **Round settlement**: `AwdpRoundEngine` advances AWDP rounds, then `AwdpScoreEngine` writes per-team, per-challenge `AwdpRoundScore` rows and `awdp-round` score events
- **No starting score**: AWDP totals are the sum of round score deltas; there is no initial score pool
- **FixScript validation**: Teams upload `.zip`, `.tar.gz`, or `.tgz` archives. The configured entry script, for example `fix.sh`, runs in an isolated side container before one challenge-author-provided `check` container validates service health, exploitability, and patch rules
- **Outcome separation**: `FixFailed`, `FixServiceError`, `FixScriptError`, `FixTimeout`, `AuditFailed`, and `FixRuleViolation` are tracked separately. `FixFailed` does not create a penalty by default; service and violation penalties are controlled by AWDP configuration

### KoH (King of the Hill)

- Does not use flag submissions
- `KohGameMode` initializes one hill container per challenge (stored as `AwdGameBox` with `TeamId = Guid.Empty`)
- `KohPollEngine` polls each hill container's agent `/status` endpoint every 5 seconds
- `KohScoreEngine` awards control points to whichever team currently holds the hill
- Territory control timing and scores are persisted per challenge

## Container Abstraction

NoCTF separates container orchestration into two layers so providers can be swapped without touching game mode code.

### `IContainerManager`

The high-level manager used by plugins:

```csharp
Task<ContainerInstance> CreateContainerAsync(ContainerConfig config, ...);
Task DestroyContainerAsync(ContainerInstance container, ...);
Task<ContainerRunResult> RunContainerAsync(ContainerConfig config, ...);
```

### `IContainerProvider<TClient, TMetadata>`

The low-level provider interface. The only built-in implementation today is `DockerProvider` + `DockerManager`, which wraps the Docker Engine API.

Container configuration includes image, command, environment variables, port mappings, labels, network, and optional TTL for auto-cleanup.

## Real-Time Communication

SignalR powers every live screen in NoCTF. The Redis backplane ensures messages scale across multiple API replicas.

| Hub | Route | Audience | Purpose |
|-----|-------|----------|---------|
| `LeaderboardHub` | `/hubs/leaderboard` | All authenticated users | Live leaderboard rank and score updates |
| `GameHub` | `/hubs/game` | All authenticated users | Game events (solves, round starts, challenge updates) |
| `MonitorHub` | `/hubs/monitor` | Admins and organizers | Submission logs, container events, system alerts |

All hubs require `?competitionId=xxx` as a query string parameter. Clients join a SignalR group named `Competition_{competitionId}` so updates are scoped to a single competition.

JWT tokens for SignalR WebSocket connections can be passed via the `access_token` query parameter.

## Multi-Tenancy

Every competition is a fully isolated tenant.

- **Tenant resolution**: `TenantResolutionMiddleware` reads the `competition_id` claim from the JWT, or derives the competition id from `/api/competitions/{id}` routes after authentication. Client-provided `X-Competition-Id` is not trusted.
- **Tenant context**: `ITenantContext` stores the current `CompetitionId` for the request scope
- **Global query filters**: EF Core automatically applies a filter on `CompetitionId` to every tenant-scoped entity, ensuring cross-competition data leaks are impossible
- **Admin bypass**: Admin queries can call `.IgnoreQueryFilters()` when global operations are required (for example, the round engine)

## Security Highlights

- **Timing-safe flag comparison**: `FlagValidator.IsMatch` uses `CryptographicOperations.FixedTimeEquals`
- **Audit logging**: Sensitive endpoints implement `IAuditableEndpoint` and are processed by `AuditLogPostProcessor`. Only whitelisted fields are logged; others are redacted
- **RBAC + resource permissions**: JWT tokens carry role claims (`Admin`, `Organizer`, `User`). Fine-grained permissions are enforced by `CompetitionPermissionService` and `TeamPermissionService`
- **Sensitive field redaction**: API responses strip internal fields (for example, `FlagSecret`) before serialization
- **SignalR authorization**: Hubs use `[Authorize]` or `[Authorize(Roles = "Admin,Organizer")]` to restrict access
