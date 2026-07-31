import type {
  NoCtfapiEndpointsCompetitionsGetLeaderboardEndpointResponse,
  NoCtfApplicationScoringLeaderboardLeaderboardBloodRank,
  NoCtfApplicationScoringLeaderboardLeaderboardBloodSummary,
  NoCtfApplicationScoringLeaderboardLeaderboardEntry,
  NoCtfApplicationScoringLeaderboardLeaderboardResponse,
} from '@/api/generated/types.gen'

const bloodLabelKeys = {
  1: 'scoreboard.firstBlood',
  2: 'scoreboard.secondBlood',
  3: 'scoreboard.thirdBlood',
} as const satisfies Record<NoCtfApplicationScoringLeaderboardLeaderboardBloodRank, string>

export interface LeaderboardBloodRow {
  key: string
  bloodRank: NoCtfApplicationScoringLeaderboardLeaderboardBloodRank
  labelKey: typeof bloodLabelKeys[NoCtfApplicationScoringLeaderboardLeaderboardBloodRank]
  teamId: string
  teamName: string
  slotKey: string
  occurredAt: string
}

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

export function leaderboardBloodRows(
  bloods: NoCtfApplicationScoringLeaderboardLeaderboardBloodSummary[],
): LeaderboardBloodRow[] {
  return bloods.flatMap((blood) => {
    if (!blood.bloodRank || !blood.teamId || !blood.slotKey || !blood.occurredAt)
      return []

    return [{
      key: `${blood.slotKey}:${blood.bloodRank}:${blood.teamId}`,
      bloodRank: blood.bloodRank,
      labelKey: bloodLabelKeys[blood.bloodRank],
      teamId: blood.teamId,
      teamName: blood.teamName?.trim() || blood.teamId,
      slotKey: blood.slotKey,
      occurredAt: blood.occurredAt,
    }]
  })
}
