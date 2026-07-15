# Game Modes

NoCTF supports four competition formats. This document explains how each mode works from a player and organizer perspective.

## CTF (Jeopardy)

The classic Jeopardy-style capture the flag format. Challenges are categorized (for example, Web, Crypto, Pwn, Reverse) and each challenge has a hidden flag.

### Dynamic Scoring

Challenge point values decrease as more teams solve them. The formula is:

```
value = ((minimum - initial) / decay^2) * solves^2 + initial
```

The result is clamped to `[MinimumPoints, InitialPoints]`.

- `InitialPoints` — the starting value when no one has solved the challenge
- `MinimumPoints` — the lowest value the challenge can reach
- `DecayFactor` — controls how quickly the value drops

### First Blood

The first team to solve a challenge receives a first-blood highlight on the leaderboard. The event is recorded permanently in the score history.

### Flag Validation

When a team submits a flag:

1. The server checks if the team already solved this challenge
2. The submitted flag is compared with the expected flag using a constant-time algorithm to prevent timing attacks
3. If correct, a `ScoreEvent` is created and the team's total score updates
4. Dynamic scores are recalculated for all prior solvers and correction events are written

### Leaderboard

The leaderboard ranks teams by total score. Ties are broken by solve time.

### Penetration Challenge Type

Penetration Challenge is available inside CTF/Jeopardy competitions as `TypeId = Penetration`. It is not AWD, AWDP, KoH, or a team-to-team mode.

For each Penetration challenge:

- each approved team starts its own authorized Docker Compose range
- the range can contain multiple services and internal nodes
- the first entry node is published to a random host port
- dynamic flags are generated per team, per instance, and per stage
- each stage flag has its own score
- submitting one stage does not automatically solve the whole challenge
- the challenge is considered fully solved only after all visible stages are solved

Players may attack only their own displayed entry service and services reachable inside their assigned range. See [Penetration Challenges](penetration-challenges.md) for authoring and operations details.

## AWD (Attack with Defense)

Teams run identical vulnerable services (game boxes) and must attack opponents while defending their own.

### Round Engine

`AwdRoundEngine` is a background service that drives the competition:

- Polls every 5 seconds for active AWD competitions
- Advances to the next round when the current round duration elapses
- Default round duration is 300 seconds (5 minutes)
- Default total rounds is 10 (configurable per competition)

### Flag Generation and Rotation

At the start of each round:

1. `AwdFlagService` ensures flags exist for every team, challenge, and round
2. Flags for the current round are injected into each team's game box container
3. Old flags expire after a configurable validity window (default 2 rounds)

### Checker Containers

Each challenge can define a checker Docker image. `AwdCheckerService` runs the checker against every game box each round to determine service health.

### Attack / Defense / Service Scoring

At the end of each round, `AwdScoreEngine` calculates scores:

- **Service online**: `+ServiceOnlinePoints` (default 100) for each healthy service
- **Service down**: `-ServiceDownPenalty` (default 50) for each failed checker
- **Been attacked**: `-BeenAttackedPenalty` (default 50) for each challenge that was successfully exploited by any opponent this round

### Flag Submission Rules

When a team submits a flag:

- The flag must exist for the current competition
- Self-attacks are rejected
- Duplicate attacks (same attacker, victim, challenge, and round) are rejected
- Flags older than the validity window are rejected

Successful attacks award `AttackPoints` (default 50) immediately.

## AWDP (Attack with Defense and Patch)

AWDP is an independent plugin model based on Break, Fix, round settlement, and attempt limits. It is not an AWD sub-mode and it does not use an initial score pool.

### Core Flow

For each AWDP challenge, a team works through the plugin operation card:

1. Create a challenge instance
2. Break: submit the challenge flag
3. Fix: request defense and upload a FixScript archive
4. The platform validates the FixScript in an isolated runner
5. Future round settlement awards attack and defense score deltas from the recorded Break/Fix state

AWDP container-related operations, including creating or changing the instance and requesting defense, use a 30-second cooldown. This does not change instance lifetime, runner/check timeouts, or round duration.

Break success and Fix success are state changes. They do not grant the whole challenge score immediately.

Challenge authors can attach a patch-template archive to an AWDP challenge. When `patchTemplateUrl` exists, players can download it from the challenge modal and use it as the starter FixScript package.

### Attempt Limits

AWDP tracks attempts per team and challenge:

- `maxAttackAttempts` limits Break flag submissions
- `maxDefenseAttempts` limits FixScript submissions
- By default, a team cannot keep submitting Break attempts after `BreakSuccess`
- By default, a team cannot keep submitting Fix attempts after `FixSuccess`

When attempts are exhausted, the backend rejects the request and the frontend disables the corresponding operation.

### FixScript Validation

`AwdpPatchService` validates every FixScript archive:

1. **Archive audit**: The archive must be `.zip`, `.tar.gz`, or `.tgz`, must use safe paths, and must contain the configured entry script, such as `fix.sh`
2. **Side runner**: A temporary container downloads and extracts the archive, then runs the configured FixScript entry
3. **Recreate game box**: The team's container is recreated with the accepted FixScript environment
4. **Check run**: The challenge author's check container is executed against the patched container with both target and patch archive metadata

AWDP uses one check container. It does not run a separate EXP container phase. The admin challenge fields `CheckerConfig.Image`, `CheckerConfig.Command`, and `CheckerConfig.TimeoutSeconds` are interpreted as the AWDP check container image, check command, and timeout.

The check container receives:

- `TARGET_HOST`
- `TARGET_PORT`
- `TEAM_ID`
- `PATCH_URL`
- `PATCH_FILE_NAME`
- `FIX_ENTRY`

The validation result is classified precisely:

- Check exit `0`: `FixSuccess`, meaning the service works and the vulnerability is fixed
- Check exit `1`: `FixFailed`, meaning EXP exploit succeeded and the vulnerability still exists
- Check exit `2`: `FixRuleViolation`, meaning the patch is bad or violates rules; players see this as service error
- Check exit `3`: `FixServiceError`, meaning the service is unavailable or interaction failed
- Check timeout: `FixServiceError`, shown to players as service error
- FixScript execution failed: `FixScriptError`
- FixScript timed out: `FixTimeout`
- Archive audit failed: `AuditFailed`

If validation rejects a patched container, the platform destroys it and restores the original challenge image so the team's service can continue.

### Round Scoring

`AwdpRoundEngine` drives AWDP rounds, and `AwdpScoreEngine` calculates score deltas for every team and challenge at settlement time:

- `BreakSuccess` earns the configured attack score for that round
- `FixSuccess` earns the configured defense score for that round
- `FixFailed` does not earn defense points and does not create a penalty by default
- `ServiceError` creates a penalty only when AWDP service penalties are enabled
- `AuditFailed`, `FixScriptError`, `FixTimeout`, and `FixRuleViolation` create violation penalties only when AWDP violation penalties are enabled

Total score is the sum of round deltas:

```
totalScore = sum(roundScoreDelta)
```

AWDP does not use:

- Initial team scores
- One-shot full challenge scoring for Break or Fix
- AWD-style live mutual attacks
- Default penalties for failed Fix attempts or exhausted attempts

## KoH (King of the Hill)

Teams compete to control a shared "hill" service. Holding control earns points over time.

### Agent Polling

Each KoH challenge defines an agent container that exposes a `/status` endpoint. `KohPollEngine` polls every hill container every 5 seconds.

The agent response includes an `identifier` field that identifies the controlling team (either a team GUID or team name).

### Control Timing

`KohScoreEngine` tracks:

- Which team controls each hill at any moment
- How long that control has been held
- Points to award per control interval

### Score Accumulation

Teams earn `ControlPointsPerInterval` (default 10 points) for each hill they control every polling interval. Control can flip back and forth between teams, so scores reflect total time under control.

### No Flag Submissions

KoH does not use the flag submission system at all. The only way to score is through continuous control of the hill services.
