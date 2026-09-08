import { sourceFile } from './support/feature-source'
import { expect, test } from 'bun:test'
import { scoreboardTeamAchievements } from '../app/utils/scoreboard'
import type { NoCtfapiEndpointsCompetitionsScoreboardTeamResponse } from '../app/api'

test('only successful challenge metadata selects solved rows, not scores or truncated slots', () => {
  const team: NoCtfapiEndpointsCompetitionsScoreboardTeamResponse = {
    totalScore: 9999,
    slots: [],
    achievements: [{ competitionChallengeId: 'solved', kind: 'Solve', displayName: 'Alice', occurredAt: '2026-09-08T01:00:00Z' }],
  }
  expect(scoreboardTeamAchievements(team, 'solved', 'Ctf')[0]?.displayName).toBe('Alice')
  expect(scoreboardTeamAchievements(team, 'adjusted-only', 'Ctf')).toEqual([])
  expect(scoreboardTeamAchievements({ totalScore: 9999 }, 'solved', 'Ctf')).toEqual([])
})

test('AWDP keeps successful attack and defense attribution outside the displayed round window', () => {
  const team: NoCtfapiEndpointsCompetitionsScoreboardTeamResponse = { achievements: [
    { competitionChallengeId: 'pwn', kind: 'Defense', displayName: 'Bob', occurredAt: '2026-09-08T02:00:00Z' },
    { competitionChallengeId: 'pwn', kind: 'Attack', displayName: 'Alice', occurredAt: '2026-09-08T01:00:00Z' },
  ] }
  const result = scoreboardTeamAchievements(team, 'pwn', 'Awdp')
  expect(result.map(x => x.displayName)).toEqual(['Alice', 'Bob'])
  expect(result.map(x => x.kind)).toEqual(['Attack', 'Defense'])
})

test('team detail scrolls within its opaque dialog and presents solver and time columns', async () => {
  const source = await sourceFile(new URL('../app/features/leaderboard/ScoreboardTeamDetailDialog.vue', import.meta.url)).text()
  expect(source).toContain('<DialogContent class="flex max-h-[calc(100dvh-2rem)]')
  expect(source).toContain('overflow-y-auto overscroll-contain')
  expect(source).toContain('row.achievements.length > 0')
  expect(source).toContain("$t('ui.solvedBy')")
  expect(source).toContain("$t('ui.solvedAt')")
  expect(source).not.toContain('DialogScrollContent')
})
