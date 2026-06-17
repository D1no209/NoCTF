# API Reference

This document covers the NoCTF REST API, authentication, and SignalR hubs.

## Swagger / OpenAPI

When the backend is running, Swagger UI is available at:

```
http://localhost:8080/swagger
```

The OpenAPI spec can be downloaded from `/swagger/v1/swagger.json`.

## Authentication

NoCTF uses JWT Bearer tokens.

### Obtain a Token

```http
POST /api/auth/login
Content-Type: application/json

{
  "username": "your_username",
  "password": "your_password"
}
```

The response contains an `accessToken` string.

### Use the Token

Include the token in the `Authorization` header for all protected endpoints:

```http
Authorization: Bearer <your-access-token>
```

## SignalR Hubs

NoCTF exposes three SignalR hubs for real-time communication. All hubs require authentication.

### Competition Scoping

Every hub connection must include `?competitionId=<guid>` as a query string parameter. The server uses this to route messages to the correct competition group.

For WebSocket connections, pass the JWT token via the `access_token` query parameter:

```
/hubs/leaderboard?competitionId=xxx&access_token=<your-token>
```

### LeaderboardHub

- **Route**: `/hubs/leaderboard`
- **Audience**: All authenticated users
- **Purpose**: Live leaderboard updates

Clients receive:

- `LeaderboardSnapshot` — full leaderboard payload (rank, team name, total score, solve count)
- `LeaderboardDelta` — incremental updates when a single score changes

### GameHub

- **Route**: `/hubs/game`
- **Audience**: All authenticated users
- **Purpose**: General game notifications

Clients receive:

- `SubmissionSolved` — a team solved a challenge
- `FirstBlood` — first solve of a challenge
- `RoundStarted` — a new AWD/AWDP round began
- `CompetitionStateChanged` — competition started, paused, or ended

### MonitorHub

- **Route**: `/hubs/monitor`
- **Audience**: Admins and organizers only (`Admin` or `Organizer` role required)
- **Purpose**: Organizer monitoring stream

Clients receive:

- `AttackLog` — live AWD attack events
- `ContainerEvent` — container creation/destruction notifications
- `SystemAlert` — backend health or error alerts
- `LogEntry` — real-time log stream from the backend

## Common Endpoints

### Health Check

```http
GET /api/health
```

Response:

```json
{
  "status": "Healthy",
  "checks": [
    { "name": "postgresql", "status": "Healthy", "description": null },
    { "name": "redis", "status": "Healthy", "description": null },
    { "name": "docker", "status": "Healthy", "description": null }
  ]
}
```

Returns `503` if any required dependency is unhealthy.

### Static Files

Uploaded challenge files and attachments are served from:

```
GET /api/files/<file-name>
```

This route is public and does not require authentication.

## Error Responses

NoCTF uses standard HTTP status codes:

- `400` — Bad request (validation error)
- `401` — Unauthorized (missing or invalid JWT)
- `403` — Forbidden (insufficient permissions)
- `404` — Not found
- `409` — Conflict (for example, duplicate solve)
- `500` — Internal server error

Validation error responses include a list of failed fields and messages.
