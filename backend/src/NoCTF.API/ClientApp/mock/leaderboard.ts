import type { MockState } from './data/fixtures'
import { date, id, model, now, type Data } from './schema'

export function leaderboard(state: MockState, competitionId: string) {
  const competition = state.competitions.find(c => c.id === competitionId)!
  const challenges = state.challenges.filter(c => c.competitionId === competitionId && c.isPublished && !c.deletedAt)
  const teams = state.teams.filter(t => t.competitionId === competitionId && t.registrationStatus === 'Approved')
  const rows = teams.map((team, ti) => {
    const own = team.memberIds.includes(id(1, 3))
    const progress = challenges.map((challenge, ci) => {
      const simulated = ci < Math.max(1, challenges.length - ti)
      const solve = competition.mode !== 'Awdp' && (own
        ? state.facts.some(f => f.teamId === team.id && f.competitionChallengeId === challenge.id && f.result === 'Correct')
        : simulated)
      const attack = competition.mode === 'Awdp' && (own ? ci === 0 || ci === 2 : simulated)
      const defense = competition.mode === 'Awdp' && (own ? ci === 1 || ci === 2 : ci < Math.max(1, challenges.length - ti - 1))
      return { challenge, solve, attack, defense }
    })
    const attackScore = progress.filter(item => item.solve || item.attack).length * 100
    const defenseScore = progress.filter(item => item.defense).length * 100
    const totalScore = attackScore + defenseScore
    return model('CompetitionsScoreboardTeamResponse', {
      teamId: team.id, teamName: team.name, trackKey: team.trackKey, rankingState: team.isBanned ? 'Banned' : 'Eligible',
      totalScore, attackScore, defenseScore,
      slots: progress.map((item, ci) => {
        const entries = [
          ...(item.solve ? ['Solve'] : []),
          ...(item.attack ? ['Attack'] : []),
          ...(item.defense ? ['Defense'] : []),
        ].map((kind, index) => model('CompetitionsScoreboardEntryResponse', {
          id: id(20 + ti, ci * 3 + index + 1), kind, outcome: 'Succeeded', occurredAt: date(-1), settledAt: date(-1),
          actorIndex: own ? 2 : null, earnedPoints: 100, deductedPoints: 0, netPoints: 100,
          award: null,
          awardPoints: 0,
        }))
        const breakdown = competition.mode === 'Awdp'
          ? [
              model('CompetitionsScoreboardBreakdownResponse', { kind: 'Attack', successfulCount: item.attack ? 1 : 0, attemptCount: item.attack ? 1 : 0, earnedPoints: item.attack ? 100 : 0, deductedPoints: 0, netPoints: item.attack ? 100 : 0 }),
              model('CompetitionsScoreboardBreakdownResponse', { kind: 'Defense', successfulCount: item.defense ? 1 : 0, attemptCount: item.defense ? 1 : 0, earnedPoints: item.defense ? 100 : 0, deductedPoints: 0, netPoints: item.defense ? 100 : 0 }),
            ]
          : [model('CompetitionsScoreboardBreakdownResponse', { kind: 'Solve', successfulCount: item.solve ? 1 : 0, attemptCount: item.solve ? 1 : 0, earnedPoints: item.solve ? 100 : 0, deductedPoints: 0, netPoints: item.solve ? 100 : 0 })]
        return model('CompetitionsScoreboardSlotResponse', {
          columnIndex: ci, scoreState: 'Settled',
          offenseState: item.solve || item.attack ? 'Succeeded' : 'None',
          defenseState: item.defense ? 'Succeeded' : 'None',
          earnedPoints: entries.length * 100, deductedPoints: 0, netPoints: entries.length * 100,
          entryCount: entries.length, breakdown, entries,
        })
      }),
      challengeScores: progress.filter(item => item.solve || item.attack || item.defense).map(item => ({
        competitionChallengeId: item.challenge.id,
        attackScore: item.solve || item.attack ? 100 : 0,
        defenseScore: item.defense ? 100 : 0,
      })),
      memberContributions: [{ userId: team.captainId, displayName: own ? 'Mock Player' : team.name, earnedPoints: totalScore }],
      achievements: progress.flatMap(item => [
        ...(item.solve ? ['Solve'] : []), ...(item.attack ? ['Attack'] : []), ...(item.defense ? ['Defense'] : []),
      ].map(kind => ({ competitionChallengeId: item.challenge.id, kind, userId: team.captainId, displayName: own ? 'Mock Player' : team.name, occurredAt: date(-1) }))),
    })
  })
  const bloodRanks = ['FirstBlood', 'SecondBlood', 'ThirdBlood'] as const
  for (let challengeIndex = 0; challengeIndex < challenges.length; challengeIndex++) {
    let awarded = 0
    for (const row of rows) {
      const solve = row.slots[challengeIndex]?.entries.find((entry: Data) => entry.kind === 'Solve')
      if (!solve || awarded >= bloodRanks.length) continue
      solve.award = bloodRanks[awarded]
      solve.awardPoints = 10 - awarded * 3
      awarded += 1
    }
  }
  rows.sort((a, b) => b.totalScore - a.totalScore).forEach((row, index) => { row.rank = index + 1 })
  return model('CompetitionsScoreboardSnapshotResponse', {
    competitionId, version: String(state.facts.length + 1), schemaRevision: 'mock-1', generatedAt: now(),
    visibility: 'Normal', dataScope: 'Live', tracksEnabled: competition.tracksEnabled ?? true, tracks: [{ key: 'open', name: '公开赛道 / Open', isInternal: false, visibleOnLeaderboard: true, isViewerTrack: true }],
    actors: state.users.map((user, index) => ({ index, userId: user.userId, displayName: user.userName })), teams: rows,
    currentChallengeScores: challenges.map(c => ({ competitionChallengeId: c.id, score: 100, breakScore: 100, fixScore: 100 })),
  })
}

export function leaderboardRead(state: MockState, suffix: string, competitionId: string): Data | undefined {
  const competition = state.competitions.find(c => c.id === competitionId)!
  const challenges = state.challenges.filter(c => c.competitionId === competitionId && !c.deletedAt)
  if (suffix === '') return leaderboard(state, competitionId)
  if (suffix === '/schema') return model('CompetitionsScoreboardSchemaResponse', {
    competitionId, mode: competition.mode, revision: 'mock-1', challengeCatalogRevision: 'mock-1',
    columns: challenges.map((c, index) => ({ index, competitionChallengeId: c.id, roundId: null })),
  })
  if (suffix === '/challenges') return {
    competitionId, revision: 'mock-1', items: challenges.map((c, index) => ({ id: c.id, title: c.title, direction: c.direction, directionIcon: c.directionIcon, category: c.direction, order: index, published: c.isPublished })),
  }
  if (suffix === '/trends') return {
    competitionId, version: String(state.facts.length + 1), generatedAt: now(), dataAsOf: now(),
    teams: leaderboard(state, competitionId).teams.map((team: Data) => ({
      teamId: team.teamId, teamName: team.teamName, trackKey: team.trackKey,
      points: Array.from({ length: 9 }, (_, i) => ({ at: date(-4 + i / 2), score: Math.floor(team.totalScore * i / 8) })),
    })),
  }
}
