import { describe, expect, test } from 'bun:test'
import {
  asLeaderboardSnapshot,
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
})
