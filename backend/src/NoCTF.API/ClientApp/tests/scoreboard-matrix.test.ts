import { describe, expect, test } from 'bun:test'
import type {
  NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse,
  NoCtfapiEndpointsCompetitionsScoreboardTeamResponse,
} from '../app/api'
import {
  latestSettledScore,
  scoreboardBreakdown,
  scoreboardColumnsForChallenge,
  scoreboardEntryKindLabel,
  scoreboardEntryOutcomeLabel,
  scoreboardRankingStateLabel,
  scoreboardSlot,
  scoreboardTeamSolveCount,
} from '../app/utils/scoreboard'
import {
  isCoherentScoreboardBundle,
  shouldRestoreRequestedRoundWindow,
} from '../app/utils/scoreboard-coherence'

const composable = await Bun.file(
  new URL('../app/composables/useScoreboardMatrix.ts', import.meta.url),
).text()
const leaderboardPage = await Bun.file(
  new URL('../app/pages/competitions/[id]/leaderboard.vue', import.meta.url),
).text()

describe('normalized scoreboard matrix', () => {
  test('loads catalog, schema and snapshot concurrently with stale-response fencing', () => {
    expect(composable).toContain('await Promise.all([')
    expect(composable).toContain('getScoreboardChallengeCatalogEndpoint')
    expect(composable).toContain('getScoreboardSchemaEndpoint')
    expect(composable).toContain('getLeaderboardEndpoint')
    expect(composable).toContain('const requestGeneration = ++generation')
    expect(composable).toContain('requestGeneration !== generation')
    expect(composable).toContain('candidateSnapshot = incoming')
    expect(composable).not.toContain('(incoming.version ?? 0) >= (snapshot.value?.version ?? 0)')
  })

  test('uses compact realtime versions and a trailing refresh instead of applying hub arithmetic', () => {
    expect(composable).toContain('challengeCatalogRevision')
    expect(composable).toContain('schemaRevision')
    expect(composable).toContain('scoreboardUpdated: (raw) =>')
    expect(composable).toContain('createTrailingRefresh')
    expect(composable).toContain('void queueRefresh({')
    expect(composable).toContain('catalog: wantsCatalog')
    expect(composable).toContain('schema: wantsSchema')
    expect(composable).not.toContain('void refresh({ catalog: wantsCatalog')
    expect(composable).not.toContain('totalScore.value +=')
  })

  test('keeps accepted state while processing and retries the authoritative snapshot', () => {
    expect(composable).toContain("snapshotResult?.response?.status === 202")
    expect(composable).toContain('scheduleProcessingRetry()')
    expect(composable).toContain('if (snapshotResult?.data)')
    expect(composable).not.toContain('snapshot.value = null')
  })

  test('pages bounded AWDP round windows and keeps slot details on the selected window', () => {
    expect(composable).toContain('const requestedEndingRound = ref<number | null>(null)')
    expect(composable).toContain('query: { endingRound }')
    expect(composable).toContain('await selectRoundWindow(windowStart - 1)')
    expect(composable).toContain('const nextEnd = Math.min(latestRound, windowEnd + 50)')
    expect(composable).toContain('await selectRoundWindow(nextEnd >= latestRound ? null : nextEnd)')
    expect(composable).toContain('const detailEndingRound = computed')
    expect(leaderboardPage).toContain('endingRound: board.detailEndingRound.value')
    expect(leaderboardPage).toContain("$t('较早轮次')")
    expect(leaderboardPage).toContain("$t('较新轮次')")
    expect(leaderboardPage).toContain("$t('返回最新轮次')")
  })

  test('accepts only a revision-coherent catalog, schema and snapshot bundle', () => {
    const competitionId = crypto.randomUUID()
    const catalog = { competitionId, revision: '9007199254740993', items: [] }
    const schema = {
      competitionId,
      mode: 'Ctf' as const,
      revision: '9007199254740995',
      challengeCatalogRevision: '9007199254740993',
      rounds: [],
      columns: [],
    }
    const snapshot = {
      competitionId,
      version: '638914000000000001',
      schemaRevision: '9007199254740995',
      generatedAt: new Date().toISOString(),
      actors: [],
      teams: [],
    }

    expect(isCoherentScoreboardBundle(catalog, schema, snapshot)).toBeTrue()
    expect(isCoherentScoreboardBundle({ ...catalog, revision: '9007199254740994' }, schema, snapshot)).toBeFalse()
    expect(isCoherentScoreboardBundle(catalog, { ...schema, revision: '9007199254740996' }, snapshot)).toBeFalse()
    expect(composable).toContain('scheduleCoherenceRetry()')
    expect(composable).toContain('catalog.value = candidateCatalog')
    expect(composable).toContain('schema.value = candidateSchema')
    expect(composable).toContain('snapshot.value = candidateSnapshot')
  })

  test('keeps a selected round window while an incoherent bundle is retried', () => {
    expect(shouldRestoreRequestedRoundWindow('accepted')).toBeFalse()
    expect(shouldRestoreRequestedRoundWindow('retrying')).toBeFalse()
    expect(shouldRestoreRequestedRoundWindow('superseded')).toBeFalse()
    expect(shouldRestoreRequestedRoundWindow('failed')).toBeTrue()
    expect(composable).toContain("shouldRestoreRequestedRoundWindow(outcome)")
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
      revision: '1',
      challengeCatalogRevision: '1',
      columns: [
        { index: 0, competitionChallengeId: challengeA },
        { index: 1, competitionChallengeId: challengeA },
        { index: 2, competitionChallengeId: challengeB },
      ],
    }

    expect(scoreboardColumnsForChallenge(schema, challengeA).map(column => column.index))
      .toEqual([0, 1])
  })

  test('keeps page-local detail actors stable while appending cursor pages', () => {
    expect(leaderboardPage).toContain('const pageActors = new Map((page.actors ?? [])')
    expect(leaderboardPage).toContain('actorNames.set(entry.id, displayName)')
    expect(leaderboardPage).toContain('detailActorNames.value.get(entry.id)')
    expect(leaderboardPage).not.toContain('board.actorsByIndex.value.get(entry.actorIndex)')
  })

  test('renders generated scoreboard protocol values through localized labels', () => {
    expect(scoreboardRankingStateLabel('Banned')).toBe('已封禁')
    expect(scoreboardRankingStateLabel('Disqualified')).toBe('已取消资格')
    expect(scoreboardEntryKindLabel('Attack')).toBe('攻击')
    expect(scoreboardEntryKindLabel('ManualAdjustment')).toBe('人工调分')
    expect(scoreboardEntryOutcomeLabel('Succeeded')).toBe('成功')
    expect(scoreboardEntryOutcomeLabel('Rejected')).toBe('已拒绝')
    expect(leaderboardPage).toContain('scoreboardRankingStateLabel(team.rankingState)')
    expect(leaderboardPage).toContain('scoreboardEntryKindLabel(entry.kind)')
    expect(leaderboardPage).toContain('scoreboardEntryOutcomeLabel(entry.outcome)')
    expect(leaderboardPage).not.toContain('{{ team.rankingState }}')
    expect(leaderboardPage).not.toContain('{{ entry.kind }} · {{ entry.outcome }}')
  })
})
