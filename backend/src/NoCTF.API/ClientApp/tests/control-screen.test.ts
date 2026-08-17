import { describe, expect, test } from 'bun:test'
import type { NoCtfapiEndpointsCompetitionsLeaderboardProtocolResponse } from '../app/api'
import {
  controlScreenChallenges,
  controlScreenPublicEntries,
  controlScreenSolveFeed,
  reconcileControlScreenSolves,
} from '../app/utils/control-screen'

const leaderboard: NoCtfapiEndpointsCompetitionsLeaderboardProtocolResponse = {
  competitionId: 'competition-1',
  challenges: [
    { competitionChallengeId: '00000000-0000-0000-0000-000000000001', title: 'web-100', direction: 'WEB', currentScore: 500 },
    { competitionChallengeId: '00000000-0000-0000-0000-000000000002', title: 'pwn-200', direction: 'PWN', currentScore: 1000 },
  ],
  tracks: [
    { key: 'open', name: 'Open', visibleOnLeaderboard: true, isInternal: false },
    { key: 'junior', name: 'Junior', visibleOnLeaderboard: false, isInternal: false },
    { key: 'staff', name: 'Staff', visibleOnLeaderboard: true, isInternal: true },
  ],
  entries: [
    {
      rank: 1,
      teamId: 'team-open',
      teamName: 'Alpha',
      trackKey: 'open',
      score: 500,
      solveCount: 1,
      cells: [{
        competitionChallengeId: '00000000000000000000000000000001',
        score: 500,
        solvedAt: '2026-08-12T12:05:00Z',
        bloodRank: 'First',
      }],
    },
    {
      rank: 1,
      teamId: 'team-junior',
      teamName: 'Beta',
      trackKey: 'junior',
      score: 1000,
      solveCount: 1,
      cells: [{
        competitionChallengeId: '00000000-0000-0000-0000-000000000002',
        score: 1000,
        solvedAt: '2026-08-12T12:06:00Z',
        bloodRank: 'Second',
      }],
    },
    {
      rank: 1,
      teamId: 'team-staff',
      teamName: 'Internal',
      trackKey: 'staff',
      score: 1500,
      solveCount: 2,
      cells: [],
    },
  ],
}

describe('CTF control screen projection', () => {
  test('aggregates every non-internal track and excludes internal data', () => {
    const entries = controlScreenPublicEntries(leaderboard)
    expect(entries.map(entry => entry.teamName)).toEqual(['Alpha', 'Beta'])

    const challenges = controlScreenChallenges(leaderboard, entries)
    expect(challenges.map(challenge => ({ title: challenge.title, solves: challenge.solveCount })))
      .toEqual([{ title: 'web-100', solves: 1 }, { title: 'pwn-200', solves: 1 }])
    expect(challenges[0]?.completionPercent).toBe(50)

    const feed = controlScreenSolveFeed(leaderboard, entries)
    expect(feed).toHaveLength(2)
    expect(feed.map(item => item.teamName)).not.toContain('Internal')
  })

  test('orders the live feed by solve time and normalizes challenge ids', () => {
    const feed = controlScreenSolveFeed(leaderboard, controlScreenPublicEntries(leaderboard))
    expect(feed.map(item => item.teamName)).toEqual(['Beta', 'Alpha'])
    expect(feed.map(item => item.challengeTitle)).toEqual(['pwn-200', 'web-100'])
  })

  test('baselines historical solves and queues every new solve in occurrence order', () => {
    const initial = controlScreenSolveFeed(leaderboard, controlScreenPublicEntries(leaderboard))
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

  test('ships only the collaborator 3D screen with an elevated overview camera', async () => {
    const page = await Bun.file(
      new URL('../app/pages/competitions/[id]/live.vue', import.meta.url),
    ).text()
    const shell = await Bun.file(
      new URL('../app/pages/competitions/[id].vue', import.meta.url),
    ).text()
    const scene = await Bun.file(
      new URL('../app/lib/live-city-3d.ts', import.meta.url),
    ).text()
    const oldScreenExists = await Bun.file(
      new URL('../app/pages/competitions/[id]/screen.vue', import.meta.url),
    ).exists()

    expect(page).toContain('definePageMeta({ layout: false })')
    expect(page).toContain('getLeaderboardEndpoint({ path: { competitionId } })')
    expect(page).toContain('leaderboardRefreshed: () => void refreshLatest()')
    expect(page).toContain('refreshTimer = setInterval(() => void refreshLatest(), 15_000)')
    expect(page).toContain('reconcileControlScreenSolves')
    expect(page).toContain('celebrationQueue')
    expect(page).toContain('LiveCityScene')
    expect(page).toContain('controlScreenPublicEntries')
    expect(page).toContain("t('全部公开赛道')")
    expect(page).not.toContain('selectedTrackKey')
    expect(page).toContain("competition.value.mode !== 'Ctf'")
    expect(page).not.toContain('$fetch(')
    expect(page).not.toContain('/api/v1')
    expect(oldScreenExists).toBe(false)
    expect(shell).not.toContain('/screen')
    expect(shell).not.toContain('label: translate("中控大屏")')
    expect(shell).toContain('label: translate("3D 大屏")')
    expect(shell).toContain('competition.value?.mode === \'Ctf\'')
    expect(scene).toContain('radius: this.citySpan * 1.08 + 34')
    expect(scene).toContain('height: this.citySpan * 0.82 + 26')
    expect(scene).toContain('minHeight: 6, maxHeight: 24')
    expect(scene).toContain('minHeight: 4, maxHeight: 18')
  })
})
