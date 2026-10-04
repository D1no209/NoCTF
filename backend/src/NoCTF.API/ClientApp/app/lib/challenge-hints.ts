import type { NoCTFAPIEndpointsChallengesParticipantChallengeHintResponse, NoCTFAPIEndpointsCompetitionsEventsCompetitionEventKindProtocol, NoCTFAPIEndpointsGameplayFactsGameplayFactStatusResponse } from '../api/models'

export function readableHintContent(hint: NoCTFAPIEndpointsChallengesParticipantChallengeHintResponse): string | null {
  return hint.isUnlocked === true && typeof hint.content === 'string' ? hint.content : null
}

export function hintUnlockState(fact: NoCTFAPIEndpointsGameplayFactsGameplayFactStatusResponse): 'pending' | 'unlocked' | 'failed' {
  if (fact.state === 'Pending' || fact.state === 'Queued' || fact.state === 'Processing') return 'pending'
  return fact.state === 'Completed' && fact.result === 'Unlocked' ? 'unlocked' : 'failed'
}

export function affectsChallengeHints(kind: NoCTFAPIEndpointsCompetitionsEventsCompetitionEventKindProtocol): boolean {
  return kind === 'HintPublished' || kind === 'HintUnlocked' || kind === 'ChallengeUpdated'
}
