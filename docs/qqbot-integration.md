# QQBot Integration

NoCTF's QQ broadcast support is an optional platform plugin. It does not participate in scoring, solve validation, blood-rank calculation, or punishment decisions. The platform records final business events, renders a safe message, and exposes a signed outbound-agent API. A separately deployed NoneBot/Milky agent long-polls that API and sends the message to an explicitly allowlisted QQ group.

The reference project at `E:\SourceCode\QQBOT` is read-only. NoCTF does not reference its source tree, install into it during build, or require it to run on the same server.

## Architecture and data flow

```text
committed competition result
  -> C# notification outbox + background task (PostgreSQL)
  -> QQBot dispatcher checks global, competition, event, and group policy
  -> safe template renders text/mention_all segments
  -> durable delivery row
  <- Python agent long-polls over HTTPS with ECDSA request signatures
  -> NoneBot Milky send_group_message
  <- ACK or classified failure; delivery log remains auditable
```

`NoCTF.Plugins.QQBot` is optional: if its assembly is absent, the five game-mode/challenge plugins still load and the platform continues with a no-op notification outbox. A disconnected agent or QQ account never rolls back competition work.

PostgreSQL is authoritative for events, deliveries, leases, attempts, templates, and audit state. Redis is used only for signed-request nonce replay protection and short group/competition cooldowns. The agent uses a local SQLite journal to distinguish an acknowledged send from an ambiguous transport outcome.

## Supported events and trigger points

| Event | Platform trigger |
|---|---|
| Competition start | A persisted status transition to `Running`, including create-in-running-state and later update |
| Challenge publication | A challenge is first bound into a competition that is already running |
| Hint publication | Hint content is newly added to a challenge in a running competition |
| First/second/third blood | The final rank produced by CTF or Penetration submission processing; the plugin never recalculates rank |
| Team penalty | A formal team ban transition from unbanned to banned; suspicious incidents alone do not broadcast |
| Announcement | An authorized administrator/organizer submits a test or formal notification |

The current challenge and hint models do not have separate draft/visibility publication fields. The integration therefore hooks only the existing observable transitions above and does not invent publication semantics.

## Management and permissions

The global page is `/admin/qqbot`; only the `Admin` role can enable the plugin, register/rotate an agent public key, authorize synchronized groups, and view platform-versus-QQ connectivity.

The competition page is `/admin/competitions/{id}/qqbot`. `Admin` and an organizer who passes the existing competition permission check can configure that competition, event switches, templates, announcements, and logs. Only an `Admin` can enumerate globally authorized groups and make the initial binding; organizers can manage existing bindings for competitions they control. All state-changing endpoints use the platform's existing authentication, authorization, rate limiting/CSRF-equivalent API protections, and audit mechanism.

Global enablement never opts competitions in. Each competition separately enables messages, manual announcements, post-finish policy, event types, templates, and one or more authorized groups. `@all` is off by default.

## Agent authentication and network boundary

The agent initiates every connection, so the BOT and platform may be on unrelated networks. No inbound port is required on the BOT host. `NOCTF_PLATFORM_URL` must be an absolute HTTPS URL without embedded credentials; redirects are rejected.

Generate an ECDSA P-256 key on the BOT host:

```bash
python integrations/qqbot/generate_agent_key.py /run/secrets/noctf_qqbot_agent.pem
```

Paste only the emitted public key into the global management page. The private key stays on the BOT host. Each request signs this canonical value with ECDSA/SHA-256:

```text
NOCTF-QQBOT-V1
HTTP_METHOD
/api/integrations/qqbot/v1/path
unix_timestamp
nonce
sha256(raw_request_body)
```

The platform accepts a 60-second clock window, stores each nonce in Redis with `SET NX`, and supports a bounded previous-public-key overlap for rotation. Tokens, private keys, and authentication headers are never returned to the browser or stored in delivery logs.

Agent endpoints are versioned under `/api/integrations/qqbot/v1`: heartbeat, group synchronization, delivery lease, ACK, and failure. Request bodies are limited to 128 KiB and the API has a dedicated per-agent/IP rate policy.

## Message safety, delivery, and retry

Templates use a fixed event-specific variable whitelist. They do not support property traversal, expressions, includes, scripts, environment access, file access, or network access. The C# renderer normalizes control characters, limits text length/newlines, and emits only `text` plus optional `mention_all` Milky segments. The Python agent rejects every other segment shape before calling Milky.

Each event and group delivery has a stable idempotency key and a unique database constraint. Leasing uses a PostgreSQL transaction with `FOR UPDATE SKIP LOCKED`, making concurrent platform/agent instances safe. Recoverable failures have bounded exponential backoff; permanent protocol, authorization, group, template, and ambiguous-outcome failures stop automatically. An expired final lease is marked `delivery_outcome_unknown` instead of being silently stranded.

Before sending, the agent records `sending` in SQLite. A successful Milky response is recorded locally before the platform ACK. If the process or network fails after the call may have reached QQ, a later lease is reported as `delivery_outcome_unknown` and is not automatically resent. An administrator can make an explicit, audited retry decision.

## Platform deployment

Apply the EF Core migration and deploy both API and Worker with the QQBot plugin assembly present:

```bash
dotnet ef database update --project backend/src/NoCTF.Infrastructure --startup-project backend/src/NoCTF.API
```

Set `QqBot__PublicBaseUrl` to the public competition origin used in rendered links, for example `https://ctf.example.com`. This value is not the BOT address. Configure the global and competition policies through the WEB administration pages; no BOT credential belongs in platform environment variables.

## BOT-agent deployment

Install `integrations/qqbot/requirements.txt` in the BOT's own isolated image or virtual environment, mount `integrations/qqbot/noctf_broadcast` as a NoneBot plugin, and set the variables documented in `integrations/qqbot/.env.example`. The plugin registers no incoming-message matcher and does not intercept or change existing plugins.

Both sides must authorize a group:

1. Put the group number in `NOCTF_QQBOT_GROUP_ALLOWLIST` on the agent.
2. After group synchronization, authorize the group globally in NoCTF.
3. Bind it to the intended competition and events.

For a test environment, restrict both allowlists to a single operator-approved test group supplied outside version control. Start with a test notification from the competition page. Do not use another group without explicit approval.

## Operations and rollback

The UI separately reports whether the platform has received a recent signed heartbeat and whether Milky reports a logged-in QQ account. Delivery logs can be filtered by competition, event, status, and time; only eligible failures can be retried.

To stop broadcasting immediately, disable the global switch or the competition switch. To roll back code, stop/remove the agent plugin first, disable the platform plugin, deploy the previous application version, and migrate the database to the migration immediately before `AddQqBotBroadcastPlugin`. The core competition tables and game-mode rules are not changed by the integration.

## Verification

Before release, run the backend Release suite with real PostgreSQL/Redis, analyzer and format checks, migration drift/chain checks, OpenAPI regeneration, frontend lint/build, dependency scans, Compose/Kubernetes validation, and verify the reference QQBOT directory hash remains unchanged. Python syntax can be checked without importing or writing into the reference project:

```bash
python -c "import ast, pathlib; ast.parse(pathlib.Path('integrations/qqbot/noctf_broadcast/__init__.py').read_text(encoding='utf-8'))"
```
