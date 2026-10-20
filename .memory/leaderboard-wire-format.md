---
name: leaderboard-wire-format
description: GET /leaderboard is the only matrix resource; slots/achievements/totals reference challenges, rounds and members by position
type: decision
---

**Why**: Every publication made every viewer download the full leaderboard. Measured on realistic data, most bytes were
duplicates: AWDP/AWD repeated the same settlement entry (GUID id, actor, times, earned/deducted/net) in every later round,
AWD stored scores three times (awdComponents, breakdown, entries), people appeared in actors + memberContributions +
achievements, `challengeBenefits` repeated every CTF slot's score, and `/leaderboard/schema` repeated each round GUID
per challenge. The user asked to drop the schema resource, keep userId, and remove all duplication.

**How to apply**:
- `/leaderboard/schema` no longer exists. The response carries `mode`, `rounds[]` (visible window), `challenges[]`
  (published, matrix order, `currentScore`) and `roundWindowStart/End`, `latestRound`; coherence is only
  `challengeCatalogRevision === catalog.revision`.
- Slots are `{challenge, round?, scoreState, netPoints, offenseState, defenseState, activity[], award, awardPoints}`;
  `challenge`/`round` are positions in `challenges`/`rounds`. Achievements use `challenge` + `member` (position in the
  team's `members`, the only place a userId/displayName appears).
- `challengeTotals` replaces challengeScores + challengeBenefits: always sent for AWD/AWDP (competition-wide totals);
  for CTF/KoH only when a write-up was unlocked or gross/net differs from the slot. Absent row => gross = net = slot net.
- Per-kind points, entries, AWD components and actors live only in slot detail, now routed by
  `teams/{teamId}/challenges/{competitionChallengeId}?roundId=` (column indexes differ between audience views).
- The frontend resolves positions once in `utils/scoreboard-board.ts` (`normalizeScoreboardSnapshot`); `board.schema`
  is the derived `ScoreboardLayout`. Never read raw wire positions elsewhere.
- Internal `ScoreboardProjection` is unchanged; my-team standing still returns the full benefit list.

**Related**: [[perf-db-statement-overhead]], backend/src/NoCTF.API/Endpoints/Competitions/GetLeaderboardEndpoint.cs
