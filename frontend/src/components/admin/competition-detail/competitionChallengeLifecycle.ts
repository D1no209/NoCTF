import type {
  AdminDeleteCompetitionChallengeData,
  AdminRestoreCompetitionChallengeData,
  NoCtfapiEndpointsAdministrationChallengesCreateChallengeRequest,
  NoCtfapiEndpointsAdministrationChallengesUpdateChallengeRequest,
} from '@/api/generated/types.gen'
import type { CompetitionChallenge } from '@/api/noctf'

const uuidPattern
  = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const emptyUuid = '00000000-0000-0000-0000-000000000000'

export interface CompetitionChallengeDraft {
  stableId: string
  challengeId: string
  baseScore: number
  order: number
  isPublished: boolean
}

export type CompetitionChallengeLifecycleAction = 'delete' | 'restore'

export interface CompetitionChallengeLifecycleRequest {
  action: CompetitionChallengeLifecycleAction
  query:
    & AdminDeleteCompetitionChallengeData['query']
    & AdminRestoreCompetitionChallengeData['query']
}

export interface CompetitionChallengeLifecycleMutation
  extends CompetitionChallengeLifecycleRequest {
  competitionChallengeId: string
}

function hasValidNumbers(draft: CompetitionChallengeDraft) {
  return Number.isSafeInteger(draft.baseScore)
    && draft.baseScore >= 0
    && Number.isSafeInteger(draft.order)
    && draft.order >= 0
}

export function isValidOptionalCompetitionChallengeId(value: string) {
  const id = value.trim()
  return !id || (uuidPattern.test(id) && id.toLowerCase() !== emptyUuid)
}

export function isAvailableCompetitionChallengeTemplate(
  challengeId: string,
  templates: Array<{ id?: string | null }>,
) {
  return Boolean(challengeId && templates.some(template => template.id === challengeId))
}

export function isDeletedCompetitionChallenge(challenge: CompetitionChallenge) {
  return Boolean(challenge.deletedAt)
}

export function buildCompetitionChallengeCreateRequest(
  draft: CompetitionChallengeDraft,
): NoCtfapiEndpointsAdministrationChallengesCreateChallengeRequest | null {
  const stableId = draft.stableId.trim()
  const challengeId = draft.challengeId.trim()
  if (!challengeId || !isValidOptionalCompetitionChallengeId(stableId) || !hasValidNumbers(draft))
    return null

  return {
    ...(stableId ? { id: stableId } : {}),
    challengeId,
    baseScore: draft.baseScore,
    order: draft.order,
  }
}

export function buildCompetitionChallengeUpdateRequest(
  challenge: CompetitionChallenge,
  draft: CompetitionChallengeDraft,
): NoCtfapiEndpointsAdministrationChallengesUpdateChallengeRequest | null {
  if (
    challenge.deletedAt
    || typeof challenge.revision !== 'number'
    || !Number.isSafeInteger(challenge.revision)
    || challenge.revision < 0
    || !hasValidNumbers(draft)
  ) {
    return null
  }

  return {
    baseScore: draft.baseScore,
    order: draft.order,
    isPublished: draft.isPublished,
    expectedRevision: challenge.revision,
  }
}

export function buildCompetitionChallengeLifecycleRequest(
  challenge: CompetitionChallenge,
  action: CompetitionChallengeLifecycleAction,
): CompetitionChallengeLifecycleRequest | null {
  const revision = challenge.revision
  if (
    typeof revision !== 'number'
    || !Number.isSafeInteger(revision)
    || revision < 0
  ) {
    return null
  }

  const state = challenge.deletedAt === null
    ? 'active'
    : typeof challenge.deletedAt === 'string' && challenge.deletedAt.trim()
      ? 'deleted'
      : null
  if (
    state === null
    || (action === 'delete' && state !== 'active')
    || (action === 'restore' && state !== 'deleted')
  ) {
    return null
  }

  return {
    action,
    query: { expectedRevision: revision },
  }
}

export function buildCompetitionChallengeLifecycleMutation(
  challenges: CompetitionChallenge[] | undefined,
  competitionChallengeId: string,
  action: CompetitionChallengeLifecycleAction,
): CompetitionChallengeLifecycleMutation | null {
  const matches = challenges?.filter(
    challenge => challenge.id === competitionChallengeId,
  )
  const challenge = matches?.[0]
  if (!challenge || matches.length !== 1)
    return null

  const request = buildCompetitionChallengeLifecycleRequest(challenge, action)
  return request
    ? { competitionChallengeId, ...request }
    : null
}

export function nextCompetitionChallengeOrder(challenges: CompetitionChallenge[]) {
  return challenges
    .filter(challenge =>
      !isDeletedCompetitionChallenge(challenge)
      && typeof challenge.order === 'number'
      && Number.isSafeInteger(challenge.order),
    )
    .reduce((highest, challenge) => Math.max(highest, challenge.order ?? -1), -1) + 1
}
