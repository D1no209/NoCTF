# API Reference

This document covers the NoCTF REST API, authentication, and SignalR hubs.

## Swagger / OpenAPI

When the backend is running, Swagger UI is available at:

```
http://localhost/swagger
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

When email verification is enabled, registration returns `requiresEmailVerification: true` and login returns `403 email_not_verified` until the address is verified. Verification tokens are submitted in the request body rather than in an API URL:

```http
POST /api/auth/email-verification/verify
Content-Type: application/json

{ "token": "<token from the email link>" }
```

Clients can request another message with `POST /api/auth/email-verification/resend`. That endpoint always returns the same accepted response for unknown, already verified, cooling-down, and pending addresses to avoid account enumeration.

Administrators can manage the effective verification and SMTP settings through:

```text
GET  /api/admin/email-verification
PUT  /api/admin/email-verification
POST /api/admin/email-verification/test
```

These endpoints require the `Admin` role. The read response reports only whether an SMTP password is configured; it never returns the credential. A blank password on update preserves the current value. The test endpoint sends only to the authenticated administrator's own account email and is limited to three requests per ten minutes.

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

### User Notifications

Authenticated users receive an inbox derived from competition events that have already been accepted by the platform. Notifications are scoped to the current user; clients cannot request or mutate another user's inbox.

```http
GET /api/notifications?limit=20
POST /api/notifications/{id}/read
POST /api/notifications/read-all
```

The list response contains the newest notifications and the user's total unread count. The supported event types are `competition.started`, `challenge.published`, `hint.published`, `blood.first`, `blood.second`, `blood.third`, `team.penalized`, and `announcement`. Payloads contain only the event-specific public fields allowlisted by the server. Delivery to this inbox is independent of the optional QQ Bot plugin; one sink failing does not prevent the other sinks from accepting an event.

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

## Penetration Challenge Endpoints

Penetration Challenge is a CTF challenge type. These endpoints are thin API adapters over the challenge-type plugin registry; the API project does not reference the Penetration plugin directly.

### Player

All player endpoints require an authenticated user with an approved, unbanned team in the competition.

```http
GET /api/competitions/{id}/challenges/{challengeId}/penetration
```

Returns the player's authorized scope, topology metadata, stage solve state, and team instance state. It does not return plaintext flags.

```http
GET    /api/competitions/{id}/challenges/{challengeId}/penetration/instance
POST   /api/competitions/{id}/challenges/{challengeId}/penetration/instance/start
POST   /api/competitions/{id}/challenges/{challengeId}/penetration/instance/stop
POST   /api/competitions/{id}/challenges/{challengeId}/penetration/instance/reset
DELETE /api/competitions/{id}/challenges/{challengeId}/penetration/instance
```

Manages only the caller's own team instance.

```http
POST /api/competitions/{id}/challenges/{challengeId}/penetration/flags/submit
Content-Type: application/json

{
  "flag": "flag{...}"
}
```

Correct responses include `result = "accepted"` or `result = "already_solved"`. Wrong, expired-instance, missing-instance, and rate-limited submissions return a response body with `correct = false` and a result code. Submitted flag plaintext is not persisted in submissions or logs.

### Admin

Competition-scoped admin endpoints require `Admin` or `Organizer` plus `CanManageCompetitionAsync` for the competition.

```http
GET /api/admin/challenges/{templateId}/penetration-topology
PUT /api/admin/challenges/{templateId}/penetration-topology
```

Reads and updates the reusable challenge-bank topology for a Penetration template.

```http
GET /api/admin/competitions/{competitionId}/challenges/{challengeId}/penetration/topology
PUT /api/admin/competitions/{competitionId}/challenges/{challengeId}/penetration/topology
```

Reads and updates the competition-specific deployed topology. Updates are rejected while active instances exist.

```http
GET    /api/admin/competitions/{competitionId}/penetration/instances
GET    /api/admin/competitions/{competitionId}/penetration/instances/{instanceId}
POST   /api/admin/competitions/{competitionId}/penetration/instances/{instanceId}/reset
DELETE /api/admin/competitions/{competitionId}/penetration/instances/{instanceId}
```

Lists and manages team instances for a competition without exposing plaintext dynamic flags.
