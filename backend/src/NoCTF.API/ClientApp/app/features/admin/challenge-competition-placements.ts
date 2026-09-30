import type { NoCtfapiEndpointsChallengesChallengeSummaryResponse, NoCtfapiEndpointsCompetitionsCompetitionResponse, NoCtfapiEndpointsCompetitionsGameModeProtocol } from '../../api'
import { adminCompetitionPath } from '../../utils/app-routes'

export type ManagedPlacementCompetition = NoCtfapiEndpointsCompetitionsCompetitionResponse & { id: string }
export interface ChallengeCompetitionPlacement {
  competition: ManagedPlacementCompetition
  instances: NoCtfapiEndpointsChallengesChallengeSummaryResponse[]
  nextOrder: number
}

export function writablePlacementCompetitions(items: NoCtfapiEndpointsCompetitionsCompetitionResponse[]): ManagedPlacementCompetition[] {
  return items.filter((item): item is ManagedPlacementCompetition => !!item.id && !item.deletedAt
    && (item.administrationRole === 'Owner' || item.administrationRole === 'Manager'))
}

export function projectChallengePlacements(
  competition: ManagedPlacementCompetition,
  challenges: NoCtfapiEndpointsChallengesChallengeSummaryResponse[],
  challengeId: string,
): ChallengeCompetitionPlacement {
  const active = challenges.filter(challenge => !challenge.deletedAt)
  return {
    competition,
    instances: active.filter(challenge => challenge.challengeId === challengeId),
    nextOrder: active.reduce((order, challenge) => Math.max(order, challenge.order ?? 0), 0) + 1,
  }
}

export function availablePlacementCompetitions(items: ChallengeCompetitionPlacement[], mode: NoCtfapiEndpointsCompetitionsGameModeProtocol) {
  return items.filter(item => item.competition.mode === mode && item.instances.length === 0)
}

export function placementManagementPath(item: ChallengeCompetitionPlacement): string {
  const instanceId = item.instances[0]?.id
  return `${adminCompetitionPath(item.competition.id)}/challenges${instanceId ? `/${encodeURIComponent(instanceId)}` : ''}`
}
