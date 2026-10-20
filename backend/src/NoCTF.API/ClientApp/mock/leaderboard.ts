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
    const kinds = (item: (typeof progress)[number]) => [
      ...(item.solve ? ['Solve'] : []), ...(item.attack ? ['Attack'] : []), ...(item.defense ? ['Defense'] : []),
    ]
    return model('CompetitionsScoreboardTeamResponse', {
      teamId: team.id, teamName: team.name, trackKey: team.trackKey, rankingState: team.isBanned ? 'Banned' : 'Eligible',
      totalScore, attackScore, defenseScore, globalAdjustmentCount: 0, theoryScore: 0, hasTheorySubmission: false,
      // Slots, achievements and totals reference challenges by their position in `challenges`.
      slots: progress.map((item, ci) => model('CompetitionsScoreboardSlotResponse', {
        challenge: ci, round: null, scoreState: 'Settled',
        offenseState: item.solve || item.attack ? 'Succeeded' : 'None',
        defenseState: item.defense ? 'Succeeded' : 'None',
        netPoints: kinds(item).length * 100,
        activity: competition.mode === 'Awdp'
          ? [
              ...(item.attack ? [{ kind: 'Attack', successfulCount: 1, attemptCount: 1 }] : []),
              ...(item.defense ? [{ kind: 'Defense', successfulCount: 1, attemptCount: 1 }] : []),
            ]
          : item.solve ? [{ kind: 'Solve', successfulCount: 1, attemptCount: 1 }] : [],
        award: null,
        awardPoints: 0,
      })),
      challengeTotals: competition.mode !== 'Awdp' ? [] : progress.flatMap((item, ci) => item.attack || item.defense
        ? [{ challenge: ci, grossPoints: kinds(item).length * 100, netPoints: kinds(item).length * 100,
            attackScore: item.attack ? 100 : 0, defenseScore: item.defense ? 100 : 0, writeUp: null }]
        : []),
      members: [{ userId: team.captainId, displayName: own ? 'Mock Player' : team.name, earnedPoints: totalScore }],
      achievements: progress.flatMap((item, ci) => kinds(item).map(kind => ({ challenge: ci, kind, member: 0, occurredAt: date(-1) }))),
    })
  })
  const bloodRanks = ['FirstBlood', 'SecondBlood', 'ThirdBlood'] as const
  for (let challengeIndex = 0; challengeIndex < challenges.length; challengeIndex++) {
    let awarded = 0
    for (const row of rows) {
      const slot = row.slots.find((item: Data) => item.challenge === challengeIndex)
      if (!slot?.activity.some((item: Data) => item.kind === 'Solve') || awarded >= bloodRanks.length) continue
      slot.award = bloodRanks[awarded]
      slot.awardPoints = 10 - awarded * 3
      awarded += 1
    }
  }
  rows.sort((a, b) => b.totalScore - a.totalScore).forEach((row, index) => { row.rank = index + 1 })
  return model('CompetitionsScoreboardSnapshotResponse', {
    competitionId, mode: competition.mode, version: String(state.facts.length + 1), challengeCatalogRevision: 'mock-1', generatedAt: now(),
    currentRoundId: null, rounds: [], roundWindowStart: null, roundWindowEnd: null, latestRound: null,
    visibility: 'Normal', dataScope: 'Live', dataAsOf: now(), theoryEnabled: false,
    tracksEnabled: competition.tracksEnabled ?? true, tracks: [{ key: 'open', name: '公开赛道 / Open', isInternal: false, visibleOnLeaderboard: true, isViewerTrack: true }],
    challenges: challenges.map(c => ({ competitionChallengeId: c.id, currentScore: 100 })), teams: rows,
  })
}

/** Rebuilds the team-standing benefit list from a mock leaderboard row (gross = net without a total). */
export function mockChallengeBenefits(snapshot: Data | undefined, row: Data | undefined): Data[] {
  if (!snapshot || !row) return []
  const challengeId = (index: number) => snapshot.challenges[index]?.competitionChallengeId
  const totals = new Map<number, Data>(row.challengeTotals.map((total: Data) => [total.challenge, total]))
  return row.slots.filter((slot: Data) => slot.netPoints !== null || totals.has(slot.challenge)).map((slot: Data) => {
    const total = totals.get(slot.challenge)
    return {
      competitionChallengeId: challengeId(slot.challenge),
      grossPoints: total?.grossPoints ?? Math.max(0, slot.netPoints ?? 0),
      writeUpDeductionPoints: total?.writeUp?.deductionPoints ?? 0,
      writeUpDeductionPercent: total?.writeUp?.deductionPercent ?? null,
      writeUpUnlockedAt: total?.writeUp?.unlockedAt ?? null,
      netPoints: total?.netPoints ?? slot.netPoints ?? 0,
    }
  })
}

export function leaderboardRead(state: MockState, suffix: string, competitionId: string): Data | undefined {
  const challenges = state.challenges.filter(c => c.competitionId === competitionId && !c.deletedAt)
  if (suffix === '') return leaderboard(state, competitionId)
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
