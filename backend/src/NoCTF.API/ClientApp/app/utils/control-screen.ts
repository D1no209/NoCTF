import type {
  NoCtfapiEndpointsCompetitionsLeaderboardBloodRankProtocol,
  NoCtfapiEndpointsCompetitionsLeaderboardEntryResponse,
  NoCtfapiEndpointsCompetitionsLeaderboardProtocolResponse,
} from '~/api'

type LeaderboardEntry = NoCtfapiEndpointsCompetitionsLeaderboardEntryResponse

function normalizeChallengeKey(value?: string | null): string {
  return (value ?? '').replace(/[^0-9a-f]/gi, '').toLowerCase()
}

export interface ControlScreenChallenge {
  competitionChallengeId: string
  title: string
  direction: string
  currentScore: number
  solveCount: number
  completionPercent: number
  towerHeightPercent: number
}

export interface ControlScreenSolve {
  key: string
  teamId: string
  teamName: string
  competitionChallengeId: string
  challengeTitle: string
  direction: string
  score: number
  bloodRank: NoCtfapiEndpointsCompetitionsLeaderboardBloodRankProtocol | null
  solvedAt: string
}

export function controlScreenEntries(
  leaderboard: NoCtfapiEndpointsCompetitionsLeaderboardProtocolResponse | null,
  trackKey: string,
): LeaderboardEntry[] {
  const entries = leaderboard?.entries ?? []
  return trackKey ? entries.filter(entry => entry.trackKey === trackKey) : entries
}

export function controlScreenChallenges(
  leaderboard: NoCtfapiEndpointsCompetitionsLeaderboardProtocolResponse | null,
  entries: readonly LeaderboardEntry[],
): ControlScreenChallenge[] {
  const challenges = leaderboard?.challenges ?? []
  const teamCount = entries.length
  const maximumScore = Math.max(1, ...challenges.map(challenge => challenge.currentScore ?? 0))

  return challenges.map((challenge) => {
    const challengeId = challenge.competitionChallengeId ?? ''
    const challengeKey = normalizeChallengeKey(challengeId)
    const solveCount = entries.reduce((count, entry) => {
      const solved = (entry.cells ?? []).some(cell =>
        normalizeChallengeKey(cell.competitionChallengeId) === challengeKey
        && Boolean(cell.solvedAt))
      return count + (solved ? 1 : 0)
    }, 0)
    const score = challenge.currentScore ?? 0
    return {
      competitionChallengeId: challengeId,
      title: challenge.title ?? '',
      direction: challenge.direction ?? '',
      currentScore: score,
      solveCount,
      completionPercent: teamCount ? Math.round(solveCount / teamCount * 100) : 0,
      towerHeightPercent: 30 + Math.round(score / maximumScore * 60),
    }
  })
}

export function controlScreenSolveFeed(
  leaderboard: NoCtfapiEndpointsCompetitionsLeaderboardProtocolResponse | null,
  entries: readonly LeaderboardEntry[],
): ControlScreenSolve[] {
  const challenges = new Map(
    (leaderboard?.challenges ?? []).map(challenge => [
      normalizeChallengeKey(challenge.competitionChallengeId),
      challenge,
    ]),
  )
  const solves: ControlScreenSolve[] = []

  for (const entry of entries) {
    if (!entry.teamId || !entry.teamName) continue
    for (const cell of entry.cells ?? []) {
      if (!cell.competitionChallengeId || !cell.solvedAt) continue
      const challenge = challenges.get(normalizeChallengeKey(cell.competitionChallengeId))
      solves.push({
        key: `${entry.teamId}:${normalizeChallengeKey(cell.competitionChallengeId)}:${cell.solvedAt}`,
        teamId: entry.teamId,
        teamName: entry.teamName,
        competitionChallengeId: cell.competitionChallengeId,
        challengeTitle: challenge?.title ?? '',
        direction: challenge?.direction ?? '',
        score: cell.score ?? 0,
        bloodRank: cell.bloodRank ?? null,
        solvedAt: cell.solvedAt,
      })
    }
  }

  return solves.sort((left, right) => right.solvedAt.localeCompare(left.solvedAt))
}
