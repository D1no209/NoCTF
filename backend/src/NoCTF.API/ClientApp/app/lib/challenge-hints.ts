import type {
  NoCtfapiEndpointsChallengesParticipantChallengeHintResponse,
  NoCtfapiEndpointsCompetitionsEventsCompetitionEventKindProtocol,
  NoCtfapiEndpointsGameplayFactsGameplayFactStatusResponse,
} from '../api'

export function readableHintContent(hint: NoCtfapiEndpointsChallengesParticipantChallengeHintResponse): string | null {
  return hint.isUnlocked === true && typeof hint.content === 'string' ? hint.content : null
}

export function hintUnlockState(fact: NoCtfapiEndpointsGameplayFactsGameplayFactStatusResponse): 'pending' | 'unlocked' | 'failed' {
  if (fact.state === 'Pending' || fact.state === 'Queued' || fact.state === 'Processing') return 'pending'
  return fact.state === 'Completed' && fact.result === 'Unlocked' ? 'unlocked' : 'failed'
}

export function affectsChallengeHints(kind: NoCtfapiEndpointsCompetitionsEventsCompetitionEventKindProtocol): boolean {
  return kind === 'HintPublished' || kind === 'HintUnlocked' || kind === 'ChallengeUpdated'
}
