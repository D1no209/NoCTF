import { describe, expect, test } from 'bun:test'
import type { NoCtfapiEndpointsCompetitionsLeaderboardProtocolResponse } from '../app/api'
import {
  controlScreenChallenges,
  controlScreenEntries,
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
    { key: 'open', name: 'Open', visibleOnLeaderboard: true },
    { key: 'junior', name: 'Junior', visibleOnLeaderboard: true },
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
  ],
}

describe('CTF control screen projection', () => {
  test('keeps rankings and solve data isolated by selected track', () => {
    const entries = controlScreenEntries(leaderboard, 'open')
    expect(entries.map(entry => entry.teamName)).toEqual(['Alpha'])

    const challenges = controlScreenChallenges(leaderboard, entries)
    expect(challenges.map(challenge => ({ title: challenge.title, solves: challenge.solveCount })))
      .toEqual([{ title: 'web-100', solves: 1 }, { title: 'pwn-200', solves: 0 }])
    expect(challenges[0]?.completionPercent).toBe(100)

    const feed = controlScreenSolveFeed(leaderboard, entries)
    expect(feed).toHaveLength(1)
    expect(feed[0]).toMatchObject({ teamName: 'Alpha', challengeTitle: 'web-100', bloodRank: 'First' })
  })

  test('orders the live feed by solve time and normalizes challenge ids', () => {
    const feed = controlScreenSolveFeed(leaderboard, controlScreenEntries(leaderboard, ''))
    expect(feed.map(item => item.teamName)).toEqual(['Beta', 'Alpha'])
    expect(feed.map(item => item.challengeTitle)).toEqual(['pwn-200', 'web-100'])
  })

  test('baselines historical solves and queues every new solve in occurrence order', () => {
    const initial = controlScreenSolveFeed(leaderboard, controlScreenEntries(leaderboard, ''))
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

  test('ships as a dedicated CTF screen using generated SDK and realtime invalidation', async () => {
    const page = await Bun.file(
      new URL('../app/pages/competitions/[id]/screen.vue', import.meta.url),
    ).text()
    const shell = await Bun.file(
      new URL('../app/pages/competitions/[id].vue', import.meta.url),
    ).text()

    expect(page).toContain('definePageMeta({ layout: false })')
    expect(page).toContain('getLeaderboardEndpoint({ path: { competitionId } })')
    expect(page).toContain('leaderboardRefreshed: () => void refreshLatest()')
    expect(page).toContain('refreshTimer = setInterval(() => void refreshLatest(), 15_000)')
    expect(page).toContain('reconcileControlScreenSolves')
    expect(page).toContain('celebrationQueue')
    expect(page).toContain('control-screen-impact-ring')
    expect(page).toContain("competition.value.mode !== 'Ctf'")
    expect(page).not.toContain('$fetch(')
    expect(page).not.toContain('/api/v1')
    expect(shell).toContain('label: translate("中控大屏")')
    expect(shell).toContain('competition.value?.mode === \'Ctf\'')
  })
})
