import { describe, expect, test } from 'bun:test'
import type {
  NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogResponse,
  NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse,
  NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse,
} from '../app/api'
import {
  controlScreenChallenges,
  controlScreenPublicEntries,
  controlScreenSolveFeed,
  reconcileControlScreenSolves,
} from '../app/utils/control-screen'

const competitionId = '00000000-0000-0000-0000-000000000010'
const webId = '00000000-0000-0000-0000-000000000001'
const pwnId = '00000000-0000-0000-0000-000000000002'
const catalog: NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogResponse = {
  competitionId,
  revision: 1,
  items: [
    { id: webId, title: 'web-100', direction: 'WEB', category: 'WEB', order: 1, published: true },
    { id: pwnId, title: 'pwn-200', direction: 'PWN', category: 'PWN', order: 2, published: true },
  ],
}
const schema: NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse = {
  competitionId,
  mode: 'Ctf',
  revision: 1,
  challengeCatalogRevision: 1,
  rounds: [],
  columns: [
    { index: 0, competitionChallengeId: webId, roundId: null },
    { index: 1, competitionChallengeId: pwnId, roundId: null },
  ],
}
const snapshot: NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse = {
  competitionId: 'competition-1',
  version: 1,
  schemaRevision: 1,
  generatedAt: '2026-08-12T12:10:00Z',
  currentRoundId: null,
  currentChallengeScores: [
    { competitionChallengeId: webId, score: 444, breakScore: null, fixScore: null },
    { competitionChallengeId: pwnId, score: 333, breakScore: null, fixScore: null },
  ],
  tracks: [
    { key: 'open', name: 'Open', visibleOnLeaderboard: true, isInternal: false },
    { key: 'junior', name: 'Junior', visibleOnLeaderboard: false, isInternal: false },
    { key: 'staff', name: 'Staff', visibleOnLeaderboard: true, isInternal: true },
  ],
  teams: [
    {
      rank: 1,
      teamId: 'team-open',
      teamName: 'Alpha',
      trackKey: 'open',
      rankingState: 'Eligible',
      totalScore: 500,
      slots: [{
        columnIndex: 0,
        scoreState: 'Provisional',
        earnedPoints: 500,
        deductedPoints: 0,
        netPoints: 500,
        entryCount: 1,
        breakdown: [{ kind: 'Solve', successfulCount: 1, attemptCount: 1, earnedPoints: 500, deductedPoints: 0, netPoints: 500 }],
        entries: [{
          id: '00000000-0000-0000-0000-000000000101', kind: 'Solve', outcome: 'Succeeded',
          actorIndex: 0, targetTeamId: null, occurredAt: '2026-08-12T12:05:00Z', settledAt: null,
          earnedPoints: 500, deductedPoints: 0, netPoints: 500, award: 'FirstBlood', awardPoints: 50,
        }],
      }],
    },
    {
      rank: 1,
      teamId: 'team-junior',
      teamName: 'Beta',
      trackKey: 'junior',
      rankingState: 'Eligible',
      totalScore: 1000,
      slots: [{
        columnIndex: 1,
        scoreState: 'Provisional',
        earnedPoints: 1000,
        deductedPoints: 0,
        netPoints: 1000,
        entryCount: 1,
        breakdown: [{ kind: 'Solve', successfulCount: 1, attemptCount: 1, earnedPoints: 1000, deductedPoints: 0, netPoints: 1000 }],
        entries: [{
          id: '00000000-0000-0000-0000-000000000102', kind: 'Solve', outcome: 'Succeeded',
          actorIndex: 1, targetTeamId: null, occurredAt: '2026-08-12T12:06:00Z', settledAt: null,
          earnedPoints: 1000, deductedPoints: 0, netPoints: 1000, award: 'SecondBlood', awardPoints: 25,
        }],
      }],
    },
    {
      rank: 1,
      teamId: 'team-staff',
      teamName: 'Internal',
      trackKey: 'staff',
      rankingState: 'Eligible',
      totalScore: 1500,
      slots: [],
    },
  ],
}

describe('CTF control screen projection', () => {
  test('normalizes direction labels in the control screen model', () => {
    expect(controlScreenChallenges(catalog, schema, controlScreenPublicEntries(snapshot)).map(challenge => challenge.direction)).toEqual(['Web', 'Pwn'])
  })
  test('aggregates every non-internal track and excludes internal data', () => {
    const entries = controlScreenPublicEntries(snapshot)
    expect(entries.map(entry => entry.teamName)).toEqual(['Alpha', 'Beta'])

    const challenges = controlScreenChallenges(catalog, schema, entries, snapshot.currentChallengeScores)
    expect(challenges.map(challenge => ({ title: challenge.title, solves: challenge.solveCount })))
      .toEqual([{ title: 'web-100', solves: 1 }, { title: 'pwn-200', solves: 1 }])
    expect(challenges[0]?.completionPercent).toBe(50)
    expect(challenges.map(challenge => challenge.currentScore)).toEqual([444, 333])

    const feed = controlScreenSolveFeed(catalog, schema, entries)
    expect(feed).toHaveLength(2)
    expect(feed.map(item => item.teamName)).not.toContain('Internal')
  })

  test('orders the live feed by solve time and normalizes challenge ids', () => {
    const feed = controlScreenSolveFeed(catalog, schema, controlScreenPublicEntries(snapshot))
    expect(feed.map(item => item.teamName)).toEqual(['Beta', 'Alpha'])
    expect(feed.map(item => item.challengeTitle)).toEqual(['pwn-200', 'web-100'])
    expect(feed.map(item => item.awardPoints)).toEqual([25, 50])
  })

  test('baselines historical solves and queues every new solve in occurrence order', () => {
    const initial = controlScreenSolveFeed(catalog, schema, controlScreenPublicEntries(snapshot))
    const baseline = reconcileControlScreenSolves(null, initial)
    expect(baseline.newSolves).toEqual([])

    const next = [
      { ...initial[0]!, key: 'third', solvedAt: '2026-08-12T12:08:00Z', teamName: 'Gamma' },
      { ...initial[0]!, key: 'second', solvedAt: '2026-08-12T12:07:00Z', teamName: 'Delta' },
      ...initial,
    ]
    const reconciled = reconcileControlScreenSolves(baseline.seenKeys, next)
    expect(reconciled.newSolves.map(item => item.teamName)).toEqual(['Delta', 'Gamma'])
    expect(reconcileControlScreenSolves(reconciled.seenKeys, next).newSolves).toEqual([])
  })

  test('keeps challenge telemetry wired to the responsive city camera', async () => {
    const page = await Bun.file(
      new URL('../app/pages/competitions/[id]/live.vue', import.meta.url),
    ).text()
    const shell = await Bun.file(
      new URL('../app/pages/admin/competitions/[id].vue', import.meta.url),
    ).text()
    const scene = await Bun.file(
      new URL('../app/lib/live-city-3d.ts', import.meta.url),
    ).text()
    const oldScreenExists = await Bun.file(
      new URL('../app/pages/competitions/[id]/screen.vue', import.meta.url),
    ).exists()

    expect(page).toContain('definePageMeta({ layout: false })')
    expect(page).toContain('useScoreboardMatrix(competitionId)')
    expect(page).toContain('scoreboardUpdated: () => void refreshLatest()')
    expect(page).toContain('refreshTimer = setInterval(() => void refreshLatest(), 15_000)')
    expect(page).toContain('reconcileControlScreenSolves')
    expect(page).toContain('celebrationQueue')
    expect(page).toContain('LiveCityScene')
    expect(page).toContain('controlScreenPublicEntries')
    expect(page).toContain("entry.rank ?? '—'")
    expect(page).toContain('scoreboardRankingStateLabel(entry.rankingState)')
    expect(page).not.toContain('rankClass(index + 1)')
    expect(page).toContain("t('全部公开赛道')")
    expect(page).not.toContain('selectedTrackKey')
    expect(page).toContain("competition.value.mode !== 'Ctf'")
    expect(page).not.toContain('$fetch(')
    expect(page).not.toContain('/api/v1')
    expect(oldScreenExists).toBe(false)
    expect(shell).not.toContain('/screen')
    expect(shell).toContain("competition.value?.mode === 'Awdp'")
    expect(shell).toContain('label: translate("中控大屏")')
    expect(shell).toContain('label: translate("3D 大屏")')
    expect(scene).toContain('fitLiveCityFrame(this.worldRadius, this.worldHeight,')
    expect(scene).toContain("window.addEventListener('resize', this.resize)")
    expect(scene).toContain("window.removeEventListener('resize', this.resize)")
    expect(scene).toContain('this.labelResizeObserver.disconnect()')
    expect(page).toContain('height: 100dvh')
    expect(page).not.toContain('min-height: 75rem')
    expect(scene).toContain("els.labelPts.textContent = `${state.score} pts`")
    expect(scene).toContain('els.labelSolves.textContent = state.solvesText')
    expect(page).toContain('bloodToneOrder[left.tone] - bloodToneOrder[right.tone]')
    expect(page).toContain('points: solve.awardPoints')
    expect(scene).toContain('`${blood.label} ${blood.teamName} +${blood.points} pts`')
    expect(page).toContain('const sameChallengeUpdates = activeChallengeId')
    expect(page).toContain('if (sameChallengeUpdates.length) focusSolve(sameChallengeUpdates.at(-1)!)')
    expect(scene).toContain('minHeight: 6, maxHeight: 24')
    expect(scene).toContain('minHeight: 4, maxHeight: 18')
    expect(scene).toContain('liveCityBuildingHeight(state.score, maxScore, count)')
    expect(scene).toContain('const baseWidth = 6.8 + rand() * 3.2')
    expect(scene).toContain('const baseDepth = 6.8 + rand() * 3.2')
    expect(scene).toContain('const backdropOpacity = this.focusing ? 0.08 : 0.96')
    expect(scene).toContain('depthWrite: false')
  })
})
