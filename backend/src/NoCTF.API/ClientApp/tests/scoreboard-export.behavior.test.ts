import { describe, expect, test } from 'bun:test'
import { buildScoreboardCsv } from '../app/features/leaderboard/scoreboard-export'
import { normalizeScoreboardSnapshot } from '../app/utils/scoreboard-board'

function board(mode: 'Ctf' | 'Koh') {
  return normalizeScoreboardSnapshot({
    mode,
    challenges: ['current', 'settled', 'pending', 'zero', 'missing'].map(competitionChallengeId => ({ competitionChallengeId })),
    teams: [{
      teamId: 'team', teamName: 'Team,"一"', totalScore: 461, theoryScore: 10,
      slots: [
        { challenge: 0, scoreState: 'Provisional', netPoints: 286 },
        { challenge: 1, scoreState: 'Settled', netPoints: 165 },
        { challenge: 2, scoreState: 'Pending', netPoints: 999 },
        { challenge: 3, scoreState: 'Provisional', netPoints: 0 },
      ],
    }],
  })
}

describe('leaderboard CSV export', () => {
  test.each(['Ctf', 'Koh'] as const)('includes current %s points alongside totals and leaves unavailable cells empty', mode => {
    const snapshot = board(mode)
    const csv = buildScoreboardCsv(
      ['Rank', 'Team', 'Total', 'Theory', 'Current', 'Settled', 'Pending', 'Zero', 'Missing'],
      snapshot.teams, snapshot.layout.columns, mode,
      team => [1, team.teamName, team.totalScore, team.theoryScore],
    )
    expect(csv).toBe('\uFEFF"Rank","Team","Total","Theory","Current","Settled","Pending","Zero","Missing"\r\n'
      + '"1","Team,""一""","461","10","286","165","","0",""')
  })

  test.each(['Awd', 'Awdp'] as const)('exports only settled %s round points', mode => {
    const snapshot = normalizeScoreboardSnapshot({
      mode,
      challenges: [{ competitionChallengeId: 'challenge' }, { competitionChallengeId: 'other' }],
      rounds: [{ id: 'first', state: 'Settled' }, { id: 'second', state: 'Running' }],
      teams: [{ teamId: 'team', slots: [
        { challenge: 0, round: 0, scoreState: 'Settled', netPoints: 165 },
        { challenge: 0, round: 1, scoreState: 'Provisional', netPoints: 286 },
        { challenge: 1, round: 0, scoreState: 'Settled', netPoints: 0 },
        { challenge: 1, round: 1, scoreState: 'Pending', netPoints: 999 },
      ] }],
    })
    expect(buildScoreboardCsv(['Team', 'First', 'Second', 'Other first', 'Other second'], snapshot.teams, snapshot.layout.columns, mode, team => [team.teamId]))
      .toBe('\uFEFF"Team","First","Second","Other first","Other second"\r\n"team","165","","0",""')
  })

  test('preserves filtered row and column order, negative scores, and track cells', () => {
    const snapshot = board('Ctf')
    const team = snapshot.teams[0]!
    team.slots[1]!.netPoints = -5
    const csv = buildScoreboardCsv(['Team', 'Track', 'B', 'A'], [team], [snapshot.layout.columns[1]!, snapshot.layout.columns[0]!], 'Ctf', item => [item.teamId, '2026'])
    expect(csv).toBe('\uFEFF"Team","Track","B","A"\r\n"team","2026","-5","286"')
    expect(buildScoreboardCsv(['Team'], [], [], 'Ctf', () => [])).toBe('\uFEFF"Team"')
  })
})
