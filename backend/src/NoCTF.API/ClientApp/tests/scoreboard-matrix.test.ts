import { describe, expect, test } from 'bun:test'
import type {
  NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse,
  NoCtfapiEndpointsCompetitionsScoreboardTeamResponse,
} from '../app/api'
import {
  latestSettledScore,
  scoreboardBreakdown,
  scoreboardColumnsForChallenge,
  scoreboardSlot,
  scoreboardTeamSolveCount,
} from '../app/utils/scoreboard'

const composable = await Bun.file(
  new URL('../app/composables/useScoreboardMatrix.ts', import.meta.url),
).text()

describe('normalized scoreboard matrix', () => {
  test('loads catalog, schema and snapshot concurrently with stale-response fencing', () => {
    expect(composable).toContain('await Promise.all([')
    expect(composable).toContain('getScoreboardChallengeCatalogEndpoint')
    expect(composable).toContain('getScoreboardSchemaEndpoint')
    expect(composable).toContain('getLeaderboardEndpoint')
    expect(composable).toContain('const requestGeneration = ++generation')
    expect(composable).toContain('requestGeneration !== generation')
    expect(composable).toContain('(incoming.version ?? 0) >= (snapshot.value?.version ?? 0)')
  })

  test('uses compact realtime versions and a trailing refresh instead of applying hub arithmetic', () => {
    expect(composable).toContain('challengeCatalogRevision')
    expect(composable).toContain('schemaRevision')
    expect(composable).toContain('scoreboardUpdated: (raw) =>')
    expect(composable).toContain('createTrailingRefresh')
    expect(composable).toContain('void trailingRefresh()')
    expect(composable).not.toContain('totalScore.value +=')
  })

  test('keeps accepted state while processing and retries the authoritative snapshot', () => {
    expect(composable).toContain("snapshotResult?.response?.status === 202")
    expect(composable).toContain('scheduleProcessingRetry()')
    expect(composable).toContain('if (snapshotResult?.data)')
    expect(composable).not.toContain('snapshot.value = null')
  })

  test('reads sparse slots and never predicts a pending round score', () => {
    const team: NoCtfapiEndpointsCompetitionsScoreboardTeamResponse = {
      teamId: crypto.randomUUID(),
      teamName: 'Alpha',
      trackKey: 'default',
      rankingState: 'Eligible',
      totalScore: 300,
      slots: [
        {
          columnIndex: 0,
          scoreState: 'Settled',
          netPoints: 300,
          entryCount: 1,
          breakdown: [{
            kind: 'Solve',
            successfulCount: 1,
            attemptCount: 1,
            earnedPoints: 300,
            deductedPoints: 0,
            netPoints: 300,
          }],
        },
        {
          columnIndex: 1,
          scoreState: 'Pending',
          netPoints: null,
          entryCount: 2,
          breakdown: [{
            kind: 'Attack',
            successfulCount: 1,
            attemptCount: 2,
            earnedPoints: 0,
            deductedPoints: 0,
            netPoints: 0,
          }],
        },
      ],
    }

    expect(scoreboardSlot(team, 0)?.netPoints).toBe(300)
    expect(scoreboardSlot(team, 99)).toBeNull()
    expect(scoreboardBreakdown(scoreboardSlot(team, 1), 'Attack')?.attemptCount).toBe(2)
    expect(scoreboardTeamSolveCount(team)).toBe(1)
    expect(latestSettledScore(team, [{ index: 0 }, { index: 1 }])).toBe(300)
  })

  test('keeps challenge-major column order from the server schema', () => {
    const challengeA = crypto.randomUUID()
    const challengeB = crypto.randomUUID()
    const schema: NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse = {
      competitionId: crypto.randomUUID(),
      mode: 'Awdp',
      revision: 1,
      challengeCatalogRevision: 1,
      columns: [
        { index: 0, competitionChallengeId: challengeA },
        { index: 1, competitionChallengeId: challengeA },
        { index: 2, competitionChallengeId: challengeB },
      ],
    }

    expect(scoreboardColumnsForChallenge(schema, challengeA).map(column => column.index))
      .toEqual([0, 1])
  })
})
