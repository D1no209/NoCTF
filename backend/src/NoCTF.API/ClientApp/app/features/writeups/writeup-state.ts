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
export function writeUpPublicationVersion(value?: WriteUp | null, preview = latestWriteUpVersion(value)): WriteUpVersion | null {
  if (!preview?.id || preview.id === value?.publishedVersionId) return null
  if (preview.state === 'Draft') return value?.source === 'Official' && value.draft?.id === preview.id ? preview : null
  if (preview.state === 'Submitted') return value?.submitted?.id === preview.id ? preview : null
  return preview.state === 'Approved' ? preview : null
}
export function writeUpPublicationLabel(version?: WriteUpVersion | null): MessageKey {
  return version?.state === 'Draft' ? 'challengeWriteUp.publishDraft'
    : version?.state === 'Approved' ? 'challengeWriteUp.republishVersion' : 'challengeWriteUp.publishSubmitted'
}
export function needsWriteUpConfirmation(quote?: WriteUpQuote | null): boolean {
  return !!quote && !quote.isFree && !quote.isUnlocked
}
export function challengeWriteUpPath(competitionId: string, challengeId: string, writeUpId?: string): string {
  const base = `/competitions/${encodeURIComponent(competitionId)}/challenge-writeups/${encodeURIComponent(challengeId)}`
  return writeUpId ? `${base}/${encodeURIComponent(writeUpId)}` : base
}
