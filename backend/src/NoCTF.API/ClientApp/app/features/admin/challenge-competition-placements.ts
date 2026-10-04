import type { NoCTFAPIEndpointsChallengesChallengeSummaryResponse, NoCTFAPIEndpointsCompetitionsCompetitionResponse, NoCTFAPIEndpointsCompetitionsGameModeProtocol } from '../../api/models'
import { adminCompetitionPath } from '../../utils/app-routes'

export type ManagedPlacementCompetition = NoCTFAPIEndpointsCompetitionsCompetitionResponse & { id: string }
export interface ChallengeCompetitionPlacement {
  competition: ManagedPlacementCompetition
  instances: NoCTFAPIEndpointsChallengesChallengeSummaryResponse[]
  nextOrder: number
}

export function writablePlacementCompetitions(items: NoCTFAPIEndpointsCompetitionsCompetitionResponse[]): ManagedPlacementCompetition[] {
  return items.filter((item): item is ManagedPlacementCompetition => !!item.id && !item.deletedAt
    && (item.administrationRole === 'Owner' || item.administrationRole === 'Manager'))
}

export function projectChallengePlacements(
  competition: ManagedPlacementCompetition,
  challenges: NoCTFAPIEndpointsChallengesChallengeSummaryResponse[],
  challengeId: string,
): ChallengeCompetitionPlacement {
  const active = challenges.filter(challenge => !challenge.deletedAt)
  return {
    competition,
    instances: active.filter(challenge => challenge.challengeId === challengeId),
    nextOrder: active.reduce((order, challenge) => Math.max(order, challenge.order ?? 0), 0) + 1,
  }
}

export function availablePlacementCompetitions(items: ChallengeCompetitionPlacement[], mode: NoCTFAPIEndpointsCompetitionsGameModeProtocol) {
  return items.filter(item => item.competition.mode === mode && item.instances.length === 0)
}

export function placementManagementPath(item: ChallengeCompetitionPlacement): string {
  const instanceId = item.instances[0]?.id
  return `${adminCompetitionPath(item.competition.id)}/challenges${instanceId ? `/${encodeURIComponent(instanceId)}` : ''}`
}
