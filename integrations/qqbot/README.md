# NoCTF QQ BOT

群聊命令和权限说明见 [`docs/bot-usage.md`](../../docs/bot-usage.md)。

`NoCTF.Bot` is a standalone .NET 10 worker. Its provider-neutral Core consumes general public
NoCTF HTTP APIs and the competition SignalR Hub; the separately compiled Milky provider owns QQ
HTTP/WebSocket details. The platform has no BOT/provider configuration or delivery responsibility.

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

The bundled deployment uses the Milky provider. Other frameworks implement `IChatProvider` in an
independent adapter project; see `docs/bot-provider-development.md`.

## Configuration

Copy the names from `.env.example` into the host Secret/environment manager. Never commit the
values. `NoCtf__BaseUrl` and `NoCtf__PublicBaseUrl` must be HTTPS origins. Milky may use HTTP only on
the isolated internal network.

`Bot__Provider=milky` and `Bot__MasterUserId=<QQ>` are required. `Bot__AllowedGroupIds__0`, `__1`,
and so on are optional; groups outside the list are ignored.

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
4. In an allowlisted test group, have the configured master run `enable`.
5. Have the group owner/admin run `bind <competitionId>` and verify ordinary members can use
   status/challenges/rank/team/link but cannot change configuration.
6. Verify master-added admin is limited to this group, disable/re-enable retains configuration, and
   master revoke clears it.
7. Run the announcement body, lifecycle, blood, AWDP, freeze, blackout, reconnect, restart and JWT-revocation cases
   from `docs/qqbot-jwt.md`.

## Acceptance

- Run continuously for at least 24 hours.
- Event-to-group-message P95 is below five seconds.
- Staff/private information leakage is zero.
- Restart preserves pending outbound records, authorization and bindings.
- Duplicate SignalR notifications do not create duplicate logical messages.
- Frozen and hidden leaderboard behavior matches the web application.
- Invalid credentials and network failures do not create request loops.
- CPU and memory remain stable.
- UniQsign and Milky have no public listener and are absent from platform deployment.
- No Secret appears in source, image layers, configuration committed to Git, or logs.

## Rollback

Stop `NoCTF.Bot`, revoke its JWT, then stop Lagrange.Milky and UniQsign. Remove the test QQ account
from the group. Preserve the SQLite database and sanitized logs for diagnosis. Rollback never edits
competition, team, score or leaderboard data.
