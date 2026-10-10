import type { NoCtfapiEndpointsChallengesChallengeSummaryResponse } from '../api'

export function nextCompetitionChallengeOrder(challenges: NoCtfapiEndpointsChallengesChallengeSummaryResponse[]): number {
  return challenges.reduce((order, challenge) => Math.max(order, challenge.order ?? 0), 0) + 1
}
