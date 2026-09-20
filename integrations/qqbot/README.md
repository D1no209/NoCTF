# NoCTF QQ BOT

`NoCTF.Bot` is a standalone .NET 10 worker that connects directly to Lagrange.Milky. It consumes
only existing public NoCTF HTTP APIs and the competition SignalR Hub. It does not add platform
endpoints, enter the NoCTF durable business pipeline, or require a staff/participant identity.

The retired `noctf_broadcast` Python plugin used a removed private delivery protocol and has been
deleted. Do not restore `/api/integrations/qqbot/*`, public-key agents, platform-owned QQ group
bindings, or platform-owned QQ delivery state.

## Prerequisites

- .NET 10 runtime for `NoCTF.Bot`.
- A private Lagrange.Milky endpoint using an access token.
- UniQsign reachable only by Lagrange.Milky on the private network.
- A short-lived NoCTF Access JWT belonging to a `User`-role `Bot` account.
- Written authorization from the UniQsign author before a private deployment, as required by that
  project. This repository does not bundle or expose UniQsign.

Lagrange V2 uses Milky rather than the retired OneBot 11 implementation. The worker follows Milky's
HTTP `/api/*` and WebSocket `/event` contracts.

## Configuration

Copy the names from `.env.example` into the host Secret/environment manager. Never commit the
values. `NoCtf__BaseUrl` and `NoCtf__PublicBaseUrl` must be HTTPS origins. Milky may use HTTP only on
the isolated internal network.

`Bot__AllowedGroupIds__0`, `__1`, and so on are optional. When supplied, messages from other groups
are ignored even if the QQ account is present there.

## Build and run

```bash
dotnet publish backend/src/NoCTF.Bot/NoCTF.Bot.csproj -c Release -o /opt/noctf-bot/app
set -a
. /etc/noctf-bot.env
set +a
exec dotnet /opt/noctf-bot/app/NoCTF.Bot.dll
```

No Docker/Compose files are supplied here. Keep UniQsign, Milky, SQLite and the BOT process off the
public network. The only normally public service used by the worker is the configured NoCTF HTTPS
origin; SSH should be restricted to approved management addresses.

## First test

1. Start UniQsign privately and verify its authenticated health check.
2. Start Lagrange.Milky, complete QR login, and verify session recovery.
3. Start `NoCTF.Bot`; startup must log a validated `User`-role Bot identity and Milky implementation.
4. In an allowlisted test group, have an owner/admin run `/ctf subscribe <competitionId>`.
5. Verify ordinary members can use status/challenges/rank/team/link but cannot change configuration.
6. Run the publish, lifecycle, blood, freeze, blackout, reconnect, restart and JWT-revocation cases
   from `docs/qqbot-jwt.md`.

## Acceptance

- Run continuously for at least 24 hours.
- Event-to-group-message P95 is below five seconds.
- Staff/private information leakage is zero.
- Restart preserves pending outbound records and subscriptions.
- Duplicate SignalR notifications do not create duplicate logical messages.
- Frozen and hidden leaderboard behavior matches the web application.
- Invalid credentials and network failures do not create request loops.
- CPU and memory remain stable.
- UniQsign and Milky have no public listener.
- No Secret appears in source, image layers, configuration committed to Git, or logs.

## Rollback

Stop `NoCTF.Bot`, revoke its JWT, then stop Lagrange.Milky and UniQsign. Remove the test QQ account
from the group. Preserve the SQLite database and sanitized logs for diagnosis. Rollback never edits
competition, team, score or leaderboard data.
