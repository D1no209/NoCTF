import type {
  NoCtfApplicationScoringLeaderboardLeaderboardDataScope,
  NoCtfDomainCompetitionsCompetitionLeaderboardVisibility,
} from './generated/types.gen'

export const leaderboardVisibility = {
  normal: 0,
  frozen: 1,
  blackout: 2,
} as const satisfies Record<string, NoCtfDomainCompetitionsCompetitionLeaderboardVisibility>

export const leaderboardDataScope = {
  live: 0,
  frozen: 1,
  hidden: 2,
} as const satisfies Record<string, NoCtfApplicationScoringLeaderboardLeaderboardDataScope>

export function leaderboardVisibilityLabelKey(
  value: NoCtfDomainCompetitionsCompetitionLeaderboardVisibility | undefined,
) {
  return [
    'leaderboardVisibility.normal',
    'leaderboardVisibility.frozen',
    'leaderboardVisibility.blackout',
  ][value ?? -1] ?? 'leaderboardVisibility.unknown'
}
