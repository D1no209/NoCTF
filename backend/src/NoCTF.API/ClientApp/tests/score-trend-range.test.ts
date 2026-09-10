import { describe, expect, test } from 'bun:test'
import { scoreTrendTimeRange } from '../app/features/leaderboard/score-trend-range'

describe('score trend time range', () => {
  test('expands to every data point and leaves visible space at both edges', () => {
    const range = scoreTrendTimeRange([
      {
        teamId: 'team-1',
        teamName: 'Team 1',
        points: [
          { at: '2026-09-10T10:00:00Z', score: 10 },
          { at: '2026-09-10T14:00:00Z', score: 20 },
        ],
      },
    ], '2026-09-10T11:00:00Z', '2026-09-10T13:00:00Z')

    expect(range.start).toBe(new Date('2026-09-10T10:00:00Z').getTime())
    expect(range.end).toBe(new Date('2026-09-10T14:00:00Z').getTime())
    expect(range.axisMin).toBeLessThan(range.start)
    expect(range.axisMax).toBeGreaterThan(range.end)
  })

  test('uses a stable non-zero range when timestamps coincide', () => {
    const instant = '2026-09-10T10:00:00Z'
    const range = scoreTrendTimeRange([], instant, instant)

    expect(range.end - range.start).toBe(60_000)
    expect(range.axisMax).toBeGreaterThan(range.end)
  })
})
