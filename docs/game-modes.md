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

AWDP extends AWD with a patch phase. Teams can upload patches to fix vulnerabilities in their game boxes and earn defense points.

### Fix Phase and Break Phase

A typical AWDP competition has alternating phases, but the exact timing is configured per competition. The core mechanic is:

- Teams develop and upload a patch archive
- The platform validates the patch automatically
- If the patch passes, the team's game box is updated and defense points are awarded

### Patch Validation Sandbox

`AwdpPatchService` validates every patch in a sandbox:

1. **Sandbox run**: A temporary container runs `patch.sh` with the patch archive
2. **Recreate game box**: The team's container is recreated with `PATCH_URL` set so the patch applies on startup
3. **Checker run**: The challenge checker is executed against the patched container
4. **EXP run**: The challenge exploit container is executed against the patched container
5. **Outcome**:
   - `checker passed` AND `EXP failed` → patch is **Verified**, defense points awarded
   - Any other result → patch is **Rejected**

### Rollback

If a patch is rejected, the platform automatically destroys the patched container and restores the original challenge image so the team's service stays online.

### Scoring

- Attack points are awarded the same way as AWD
- Defense points are awarded when a patch is verified (default 100 points)

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
