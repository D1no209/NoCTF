import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'
import type { NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogResponse } from '../app/api'
import {
  controlScreenChallenges,
  controlScreenPublicEntries,
  controlScreenSolveFeed,
  reconcileControlScreenSolves,
} from '../app/utils/control-screen'
import { normalizeScoreboardSnapshot } from '../app/utils/scoreboard-board'

const competitionId = '00000000-0000-0000-0000-000000000010'
const webId = '00000000-0000-0000-0000-000000000001'
const pwnId = '00000000-0000-0000-0000-000000000002'
const catalog: NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogResponse = {
  competitionId,
  revision: '1',
  items: [
    { id: webId, title: 'web-100', direction: 'WEB', category: 'WEB', order: 1, published: true },
    { id: pwnId, title: 'pwn-200', direction: 'PWN', category: 'PWN', order: 2, published: true },
  ],
}
// Wire format: slots, achievements and members are referenced by position.
const snapshot = normalizeScoreboardSnapshot({
  competitionId: 'competition-1',
  mode: 'Ctf',
  version: '1',
  challengeCatalogRevision: '1',
  generatedAt: '2026-08-12T12:10:00Z',
  currentRoundId: null,
  rounds: [],
  challenges: [
    { competitionChallengeId: webId, currentScore: 444 },
    { competitionChallengeId: pwnId, currentScore: 333 },
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
      members: [{ userId: 'alice', displayName: 'Alice', earnedPoints: 500 }],
      achievements: [{ challenge: 0, kind: 'Solve', member: 0, occurredAt: '2026-08-12T12:05:00Z' }],
      challengeTotals: [],
      slots: [{
        challenge: 0,
        round: null,
        scoreState: 'Provisional',
        netPoints: 500,
        offenseState: 'Succeeded',
        defenseState: 'None',
        activity: [{ kind: 'Solve', successfulCount: 1, attemptCount: 1 }],
        award: 'FirstBlood',
        awardPoints: 50,
      }],
    },
    {
      rank: 1,
      teamId: 'team-junior',
      teamName: 'Beta',
      trackKey: 'junior',
      rankingState: 'Eligible',
      totalScore: 1000,
      members: [{ userId: 'bob', displayName: 'Bob', earnedPoints: 1000 }],
      achievements: [{ challenge: 1, kind: 'Solve', member: 0, occurredAt: '2026-08-12T12:06:00Z' }],
      challengeTotals: [],
      slots: [{
        challenge: 1,
        round: null,
        scoreState: 'Provisional',
        netPoints: 1000,
        offenseState: 'Succeeded',
        defenseState: 'None',
        activity: [{ kind: 'Solve', successfulCount: 1, attemptCount: 1 }],
        award: 'SecondBlood',
        awardPoints: 25,
      }],
    },
    {
      rank: 1,
      teamId: 'team-staff',
      teamName: 'Internal',
      trackKey: 'staff',
      rankingState: 'Eligible',
      totalScore: 1500,
      members: [],
      achievements: [],
      challengeTotals: [],
      slots: [],
    },
  ],
})
const schema = snapshot.layout

describe('CTF control screen projection', () => {
  test('normalizes direction labels in the control screen model', () => {
    expect(controlScreenChallenges(catalog, schema, controlScreenPublicEntries(snapshot)).map(challenge => challenge.direction)).toEqual(['Web', 'Pwn'])
  })
  test('aggregates every non-internal track and excludes internal data', () => {
    const entries = controlScreenPublicEntries(snapshot)
    expect(entries.map(entry => entry.teamName)).toEqual(['Alpha', 'Beta'])

    const challenges = controlScreenChallenges(catalog, schema, entries, snapshot.challenges)
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
    expect(feed.map(item => item.key)).toEqual([`team-junior:${pwnId}`, `team-open:${webId}`])
    expect(feed.map(item => item.score)).toEqual([1000, 500])
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
    const page = await sourceFile(
      new URL('../app/pages/competitions/[id]/live.vue', import.meta.url),
    ).text()
    const shell = await sourceFile(
      new URL('../app/pages/admin/competitions/[id].vue', import.meta.url),
    ).text()
    const scene = await sourceFile(
      new URL('../app/lib/live-city-3d.ts', import.meta.url),
    ).text()
    const oldScreenExists = await sourceFile(
      new URL('../app/pages/competitions/[id]/screen.vue', import.meta.url),
    ).exists()

    expect(page).not.toContain('alias:')
    expect(page).toContain('useScoreboardMatrix(competitionId)')
    expect(page).toContain('scoreboardUpdated: () => void refreshLatest()')
    expect(page).toContain('refreshTimer = setInterval(() => void refreshLatest(), 15_000)')
    expect(page).toContain('reconcileControlScreenSolves')
    expect(page).toContain('celebrationQueue')
    expect(page).toContain('LiveCityScene')
    expect(page).toContain('controlScreenPublicEntries')
    expect(page).toContain('const allSolves = computed(')
    expect(page).toContain('const solveFeed = computed(() => allSolves.value.slice(0, 10))')
    expect(page).toContain('for (const solve of allSolves.value)')
    expect(page).toContain('reconcileCelebrations(allSolves.value)')
    expect(page).toContain("entry.rank ?? $t('common.label.symbol')")
    expect(page).toContain('scoreboardRankingStateLabel(entry.rankingState)')
    expect(page).not.toContain('rankClass(index + 1)')
    expect(page).toContain("t('competitions.label.publicTracks')")
    expect(page).not.toContain('selectedTrackKey')
    expect(page).toContain("competition.value.mode !== 'Ctf'")
    expect(page).not.toContain('$fetch(')
    expect(page).not.toContain('/api/v1')
    expect(oldScreenExists).toBe(false)
    expect(shell).not.toContain('/screen')
    expect(shell).toContain("competition.value?.mode === 'Awdp'")
    expect(shell).toContain("label: translate(\"administration.label.controlScreen\")")
    expect(shell).toContain("label: translate(\"leaderboard.ctf.liveTitle\")")
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
    expect(scene).toContain('return themeColor(token, this.container)')
    expect(scene).toContain('makeFacadeTextures(variant, solved, this.container)')
    expect(scene).toContain("this.color('--live-city-locked')")
    expect(scene).toContain("this.color('--live-city-solved')")
    expect(scene).toContain("this.color('--live-city-grid-major')")
    expect(page).toContain('--live-city-background: oklch(')
    expect(page).toContain('--live-city-locked: oklch(')
    expect(page).toContain('--live-city-solved: oklch(')
    expect(page).toContain('--primary: var(--live-blue)')
  })
})
