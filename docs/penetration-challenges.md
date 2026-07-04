# Penetration Challenges

Penetration Challenge is a CTF/Jeopardy challenge type, not a new attack-defense game mode. Each approved team receives its own authorized target range for a single challenge. Teams attack only their own range, collect stage flags, and submit those flags for per-stage score events.

It is not AWD, AWDP, or KoH:

- no team-to-team attacks
- no service availability score
- no patch upload or FixScript workflow
- no occupation or control interval score
- no shared target range between teams

## Current Support

The MVP uses Docker Compose through the existing Docker runner/container abstraction. Kubernetes orchestration for Penetration Challenge is not implemented yet, even though the repository contains general Kubernetes deployment manifests.

## Authoring Flow

1. Open the challenge bank.
2. Create a challenge with `TypeId = Penetration`.
3. Configure the topology JSON in the Penetration topology editor.
4. Bind the template into a CTF competition.
5. Adjust the competition copy of the topology if needed.
6. Approve teams.
7. Players start their own instance from the challenge modal and submit stage flags.

The challenge bank stores reusable assets and topology defaults. The competition challenge copy stores the deployed topology, stage scores, flag prefix, hints, and runtime reset policy for that competition.

## Topology JSON

The topology document has four main sections:

```json
{
  "name": "Web Pivot Range",
  "description": "Entry web service with an internal database.",
  "entryConfig": { "scheme": "http" },
  "config": {
    "allowReset": true,
    "instanceMode": "team",
    "maxResetCount": 3,
    "visibleEntryAfterStart": true,
    "instanceTtlSeconds": 7200,
    "actionCooldownSeconds": 5
  },
  "nodes": [],
  "flags": []
}
```

### Nodes

Each node becomes a Docker Compose service. Exactly one or more nodes can be marked as entry, but the first entry node is used as the player-facing address.

```json
{
  "name": "web",
  "role": "entry",
  "image": "registry.example.com/noctf/range-web:latest",
  "ports": [80],
  "isEntry": true,
  "isInternal": false,
  "environment": {
    "APP_ENV": "production"
  },
  "dependsOn": ["db"],
  "displayOrder": 1
}
```

Internal nodes can expose container ports for in-range communication, but they must not publish host ports such as `"5432:5432"`. The Compose builder only publishes the entry service to a random host port.

Forbidden directives include privileged mode, host networking, Docker socket mounts, devices, extra hosts, and host namespace options.

### Stage Flags

Each stage has its own score. Dynamic flags are generated per team, per instance, and per stage.

```json
{
  "stage": 1,
  "name": "Initial Access",
  "score": 100,
  "isDynamic": true,
  "nodeName": "web",
  "injectionType": "EnvironmentVariable",
  "injectionKey": "NOCTF_FLAG_STAGE1",
  "visible": true,
  "hintAfterSolved": "Look for the internal service."
}
```

Static flags are also supported:

```json
{
  "stage": 2,
  "name": "Privilege Escalation",
  "score": 200,
  "isDynamic": false,
  "nodeName": "worker",
  "valueSecret": "rooted",
  "visible": true
}
```

Submitted flags may be either the raw stored content or the formatted value using the competition challenge flag prefix, for example `flag{rooted}`.

## Example: Web + DB

```json
{
  "name": "Web DB Range",
  "entryConfig": { "scheme": "http" },
  "config": {
    "allowReset": true,
    "instanceMode": "team",
    "maxResetCount": 3,
    "visibleEntryAfterStart": true,
    "instanceTtlSeconds": 7200,
    "actionCooldownSeconds": 5
  },
  "nodes": [
    {
      "name": "web",
      "role": "entry",
      "image": "registry.example.com/noctf/web-db-web:latest",
      "ports": [80],
      "isEntry": true,
      "isInternal": false,
      "dependsOn": ["db"],
      "displayOrder": 1
    },
    {
      "name": "db",
      "role": "internal",
      "image": "postgres:16-alpine",
      "ports": [5432],
      "isEntry": false,
      "isInternal": true,
      "displayOrder": 2
    }
  ],
  "flags": [
    {
      "stage": 1,
      "name": "Web foothold",
      "score": 100,
      "isDynamic": true,
      "nodeName": "web",
      "injectionKey": "NOCTF_STAGE1",
      "visible": true
    },
    {
      "stage": 2,
      "name": "Database access",
      "score": 200,
      "isDynamic": true,
      "nodeName": "db",
      "injectionKey": "NOCTF_STAGE2",
      "visible": true
    }
  ]
}
```

## Player Scope

Players are authorized to access only:

- their team's displayed entry address
- services reachable from inside their own challenge range
- files, APIs, and services intentionally included in that range

Players are not authorized to attack other teams, the platform, the Docker host, the runner, or public targets outside the challenge statement.

## Security Notes

- Dynamic flag plaintext is injected into containers but is not stored in submissions, audit logs, or competition logs.
- Wrong submissions are stored as hash and length metadata.
- Old dynamic flags are deactivated when an instance is stopped, destroyed, or regenerated.
- Each team/challenge has at most one active Penetration instance.
- Reset regenerates dynamic flags and restarts the team's range.
