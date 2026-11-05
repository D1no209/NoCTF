import type { NoCtfapiEndpointsChallengesChallengeSummaryResponse, NoCtfapiEndpointsCompetitionsCompetitionResponse, NoCtfapiEndpointsCompetitionsGameModeProtocol } from '../../api'
import { adminCompetitionPath } from '../../utils/app-routes'
import { nextCompetitionChallengeOrder } from '../../lib/competition-challenge-order'
import type { NoCtfapiEndpointsAdministrationChallengeBankChallengeCapabilityProtocol as Capability } from '../../api'

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
    nextOrder: nextCompetitionChallengeOrder(challenges),
  }
}

export function supportsCompetitionCapabilities(mode: NoCtfapiEndpointsCompetitionsGameModeProtocol | undefined, capabilities: readonly Capability[]): boolean {
  switch (mode) {
    case 'Ctf': return capabilities.includes('Break')
    case 'Awd': return capabilities.includes('Break') && capabilities.includes('Check')
    case 'Awdp': return capabilities.includes('Break') && capabilities.includes('Fix') && capabilities.includes('Check')
    case 'Koh': return capabilities.includes('Control')
    case 'LiveSolo': return capabilities.includes('Break') || capabilities.includes('Fix') && capabilities.includes('Check')
    default: return false
  }
}

export function availablePlacementCompetitions(items: ChallengeCompetitionPlacement[], capabilities: readonly Capability[], includeFinished = false) {
  return items.filter(item => supportsCompetitionCapabilities(item.competition.mode, capabilities)
    && (includeFinished || item.competition.status !== 'Finished') && item.instances.length === 0)
}

export function placementManagementPath(item: ChallengeCompetitionPlacement): string {
  const instanceId = item.instances[0]?.id
  return `${adminCompetitionPath(item.competition.id)}/challenges${instanceId ? `/${encodeURIComponent(instanceId)}` : ''}`
}
