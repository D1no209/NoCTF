import { describe, expect, test } from 'bun:test'
import {
  asLeaderboardSnapshot,
  leaderboardBloodRows,
  leaderboardRows,
} from '../src/components/game/leaderboardPresentation'

describe('leaderboard contract adaptation', () => {
  test('uses the generated score and solve count fields', () => {
    expect(leaderboardRows([{
      teamId: 'team-1',
      teamName: 'NoCTF',
      score: 1200,
      solveCount: 7,
    }])).toEqual([{
      teamId: 'team-1',
      teamName: 'NoCTF',
      score: 1200,
      solveCount: 7,
      totalScore: 1200,
      solvedCount: 7,
    }])
  })

  test('does not replace a snapshot with the accepted processing response', () => {
    expect(asLeaderboardSnapshot({
      competitionId: 'competition-1',
      state: 0,
      targetRevision: 4,
      statusUrl: '/api/v1/competitions/competition-1/leaderboard',
    })).toBeUndefined()
  })

  test('preserves the strongly typed first, second, and third blood summaries', () => {
    const snapshot = asLeaderboardSnapshot({
      competitionId: 'competition-1',
      generatedAt: '2026-07-31T00:00:00Z',
      entries: [],
      bloods: [
        {
          slotKey: 'challenge:one',
          slotKind: 0,
          bloodRank: 1,
          teamId: 'team-1',
          teamName: 'First',
          occurredAt: '2026-07-31T00:00:01Z',
        },
        {
          slotKey: 'challenge:one',
          slotKind: 0,
          bloodRank: 2,
          teamId: 'team-2',
          teamName: 'Second',
          occurredAt: '2026-07-31T00:00:02Z',
        },
        {
          slotKey: 'challenge:one',
          slotKind: 0,
          bloodRank: 3,
          teamId: 'team-3',
          teamName: 'Third',
          occurredAt: '2026-07-31T00:00:03Z',
        },
      ],
    })

    expect(snapshot?.bloods?.map(blood => blood.bloodRank)).toEqual([1, 2, 3])
    expect(leaderboardBloodRows(snapshot?.bloods ?? [])).toEqual([
      expect.objectContaining({
        bloodRank: 1,
        labelKey: 'scoreboard.firstBlood',
        teamName: 'First',
        slotKey: 'challenge:one',
        occurredAt: '2026-07-31T00:00:01Z',
      }),
      expect.objectContaining({
        bloodRank: 2,
        labelKey: 'scoreboard.secondBlood',
        teamName: 'Second',
        slotKey: 'challenge:one',
        occurredAt: '2026-07-31T00:00:02Z',
      }),
      expect.objectContaining({
        bloodRank: 3,
        labelKey: 'scoreboard.thirdBlood',
        teamName: 'Third',
        slotKey: 'challenge:one',
        occurredAt: '2026-07-31T00:00:03Z',
      }),
    ])
  })
})
