import { reviewChallengeWriteUp, submitChallengeWriteUp } from '~/api'
import type { WriteUp, WriteUpVersion } from './writeup-state'

export function writeUpActionTarget(root?: WriteUp | null, version?: WriteUpVersion | null) {
  if (!root?.id || !root.competitionChallengeId || !root.concurrencyStamp || !version?.id) return null
  return { writeUpId: root.id, challengeId: root.competitionChallengeId, expectedStamp: root.concurrencyStamp,
    versionId: version.id, versionStamp: version.concurrencyStamp, number: version.number ?? 1, state: version.state,
    source: root.source, challengeTitle: root.challengeTitle, authorName: root.authorName }
}
export type WriteUpActionTarget = NonNullable<ReturnType<typeof writeUpActionTarget>>

export function matchesWriteUpTarget(root: WriteUp | null, version: WriteUpVersion | null | undefined, target: WriteUpActionTarget) {
  return root?.id === target.writeUpId && root.concurrencyStamp === target.expectedStamp
    && version?.id === target.versionId && version.concurrencyStamp === target.versionStamp && version.state === target.state
}

/** Seal official drafts before publishing, always retaining the version confirmed by the user. */
export async function publishWriteUpVersion(competitionId: string, target: WriteUpActionTarget, onSubmitted: (root: WriteUp) => void) {
  let expectedStamp = target.expectedStamp
  if (target.state === 'Draft') {
    if (target.source !== 'Official') throw { code: 'Conflict', messageKey: 'challengeWriteUp.publicationChanged' }
    const result = await submitChallengeWriteUp({ path: { competitionId, competitionChallengeId: target.challengeId },
      body: { official: true, expectedStamp } })
    if (result.error || !result.data) throw result.error
    if (result.data.id !== target.writeUpId || result.data.submitted?.id !== target.versionId
      || result.data.submitted.state !== 'Submitted' || !result.data.concurrencyStamp) throw { code: 'Conflict', messageKey: 'challengeWriteUp.publicationChanged' }
    onSubmitted(result.data)
    expectedStamp = result.data.concurrencyStamp
  }
  const result = await reviewChallengeWriteUp({ path: { competitionId, competitionChallengeId: target.challengeId, writeUpId: target.writeUpId },
    body: { action: 'Publish', versionId: target.versionId, expectedStamp } })
  if (result.error || !result.data) throw result.error
  return result.data
}
