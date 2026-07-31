import type {
  NoCtfapiEndpointsCompetitionsGetLeaderboardEndpointResponse,
  NoCtfApplicationScoringLeaderboardLeaderboardEntry,
  NoCtfApplicationScoringLeaderboardLeaderboardResponse,
} from '@/api/generated/types.gen'

export function asLeaderboardSnapshot(
  value: NoCtfapiEndpointsCompetitionsGetLeaderboardEndpointResponse,
): NoCtfApplicationScoringLeaderboardLeaderboardResponse | undefined {
  return 'entries' in value ? value : undefined
}

export function leaderboardRows(
  entries: NoCtfApplicationScoringLeaderboardLeaderboardEntry[],
) {
  return entries.map(entry => ({
    ...entry,
    totalScore: entry.score ?? 0,
    solvedCount: entry.solveCount ?? 0,
  }))
}
