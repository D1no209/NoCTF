import type {
  NoCtfapiEndpointsChallengesWriteUpsChallengeWriteUpResponse,
  NoCtfapiEndpointsChallengesWriteUpsChallengeWriteUpVersionResponse,
  NoCtfapiEndpointsChallengesWriteUpsChallengeWriteUpContentResponse,
  NoCtfApplicationChallengesWriteUpsWriteUpAccessView,
  NoCtfApplicationChallengesWriteUpsChallengeWriteUpQuote,
} from '~/api'
import type { MessageKey } from '~/locales/en'

export type WriteUp = NoCtfapiEndpointsChallengesWriteUpsChallengeWriteUpResponse
export type WriteUpVersion = NoCtfapiEndpointsChallengesWriteUpsChallengeWriteUpVersionResponse
export type WriteUpContent = NoCtfapiEndpointsChallengesWriteUpsChallengeWriteUpContentResponse
export type WriteUpAccess = NoCtfApplicationChallengesWriteUpsWriteUpAccessView
export type WriteUpQuote = NoCtfApplicationChallengesWriteUpsChallengeWriteUpQuote

export function writeUpStatusKey(value?: WriteUp | null): MessageKey {
  if (value?.publishedVersionId && value.submitted?.state === 'Submitted') return 'challengeWriteUp.publishedPending'
  if (value?.publishedVersionId && value.submitted?.state === 'Rejected') return 'challengeWriteUp.publishedRejected'
  if (value?.publishedVersionId) return 'challengeWriteUp.published'
  if (value?.draft) return 'challengeWriteUp.draft'
  if (value?.submitted?.state === 'Rejected') return 'challengeWriteUp.rejected'
  if (value?.versions?.some(x => x.state === 'Approved')) return 'challengeWriteUp.withdrawn'
  if (value?.submitted) return 'challengeWriteUp.submitted'
  return 'challengeWriteUp.notSubmitted'
}
export function latestWriteUpVersion(value?: WriteUp | null): WriteUpVersion | null {
  return value?.draft ?? value?.submitted ?? value?.published ?? value?.versions?.[0] ?? null
}
export function needsWriteUpConfirmation(quote?: WriteUpQuote | null): boolean {
  return !!quote && !quote.isFree && !quote.isUnlocked
}
export function challengeWriteUpPath(competitionId: string, challengeId: string, writeUpId?: string): string {
  const base = `/competitions/${encodeURIComponent(competitionId)}/challenge-writeups/${encodeURIComponent(challengeId)}`
  return writeUpId ? `${base}/${encodeURIComponent(writeUpId)}` : base
}
