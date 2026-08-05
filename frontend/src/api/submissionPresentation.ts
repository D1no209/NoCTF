import type {
  NoCtfapiEndpointsSubmissionsFlagSubmissionAcceptedResponse,
  NoCtfapiEndpointsSubmissionsSubmissionListItemResponse,
  NoCtfapiEndpointsSubmissionsSubmissionListResponse,
  NoCtfapiEndpointsSubmissionsSubmissionStatusResponse,
  NoCtfDomainSubmissionsScoringFailureCode,
  NoCtfDomainSubmissionsScoringResult,
  NoCtfDomainSubmissionsSubmissionEvaluationState,
  NoCtfDomainSubmissionsSubmissionKind,
} from './generated/types.gen'

export const SubmissionKind = {
  Flag: 0,
  Break: 1,
  Fix: 2,
} as const satisfies Record<string, NoCtfDomainSubmissionsSubmissionKind>

export const SubmissionEvaluationState = {
  Pending: 0,
  Queued: 1,
  Processing: 2,
  Completed: 3,
  PlatformFailed: 4,
} as const satisfies Record<string, NoCtfDomainSubmissionsSubmissionEvaluationState>

export const ScoringResult = {
  Correct: 0,
  Wrong: 1,
  Duplicate: 2,
  AttemptsExhausted: 3,
  PlatformFailed: 4,
  Rejected: 5,
} as const satisfies Record<string, NoCtfDomainSubmissionsScoringResult>

export const ScoringFailureCode = {
  FlagNotSupported: 0,
  FixNotSupported: 1,
  BreakAttemptsExhausted: 2,
  FixAttemptsExhausted: 3,
  BreakRequired: 4,
  ArchiveValidationUnavailable: 5,
  FixArchiveMissing: 6,
  FixArchiveLengthMismatch: 7,
  FixArchiveContentTypeMismatch: 8,
  FixArchiveHashMismatch: 9,
  StorageTimeout: 10,
  StorageUnavailable: 11,
  CheckerPlatformError: 12,
  SelfAttackRejected: 13,
  DuplicateAttack: 14,
  DuplicateAchievement: 15,
  UnknownTeamIdentifier: 16,
  InvalidObservation: 17,
  ProducerTimeout: 18,
  ProducerUnavailable: 19,
  AmbiguousFlagMatch: 20,
  FlagExpired: 21,
  RoundOutOfRange: 22,
  HardeningActive: 23,
  AwdpFixFailed: 24,
  AwdpPatchFailed: 25,
  AwdpPatchTimeout: 26,
  AwdpServiceDown: 27,
  AwdpViolation: 28,
} as const satisfies Record<string, NoCtfDomainSubmissionsScoringFailureCode>

export const SubmissionOutcome = {
  Evaluating: 0,
  Correct: 1,
  Wrong: 2,
  Duplicate: 3,
  AttemptsExhausted: 4,
  PlatformFailed: 5,
  Rejected: 6,
} as const

export type PublicSubmissionKind
  = typeof SubmissionKind[keyof typeof SubmissionKind]
export type PublicSubmissionEvaluationState
  = typeof SubmissionEvaluationState[keyof typeof SubmissionEvaluationState]
export type PublicScoringResult
  = typeof ScoringResult[keyof typeof ScoringResult]
export type PublicScoringFailureCode
  = typeof ScoringFailureCode[keyof typeof ScoringFailureCode]
export type PublicSubmissionOutcome
  = typeof SubmissionOutcome[keyof typeof SubmissionOutcome]

export type SubmissionMessageKey
  = | 'challenges.correctFlag'
    | 'challenges.incorrectFlag'
    | 'challenges.duplicateFlag'
    | 'challenges.attemptsExhausted'
    | 'challenges.platformEvaluationFailed'
    | 'challenges.submissionRejected'
    | 'challenges.flagNotSupported'
    | 'challenges.selfAttackRejected'
    | 'challenges.unknownFlagTarget'
    | 'challenges.ambiguousFlag'
    | 'challenges.flagExpired'
    | 'challenges.roundOutOfRange'
    | 'challenges.hardeningActive'

export interface PublicSubmissionListItem {
  id: string
  competitionId: string
  competitionChallengeId: string
  teamId: string
  submittedByUserId: string
  kind: PublicSubmissionKind
  evaluationState: PublicSubmissionEvaluationState
  result: PublicScoringResult | null
  failureCode: PublicScoringFailureCode | null
  receivedAt: string
  processingVersion: number
}

export interface PublicSubmissionStatus {
  submissionId: string
  competitionId: string
  teamId: string
  competitionChallengeId: string
  kind: PublicSubmissionKind
  evaluationState: PublicSubmissionEvaluationState
  result: PublicScoringResult | null
  failureCode: PublicScoringFailureCode | null
  receivedAt: string
  evaluationUpdatedAt: string
  processingVersion: number
}

export interface PublicSubmissionPage {
  items: PublicSubmissionListItem[]
  nextCursor: string | null
}

export interface AcceptedFlagSubmission {
  submissionId: string
  statusUrl: string
}

const submissionKinds = new Set<number>(Object.values(SubmissionKind))
const evaluationStates = new Set<number>(Object.values(SubmissionEvaluationState))
const scoringResults = new Set<number>(Object.values(ScoringResult))
const scoringFailureCodes = new Set<number>(Object.values(ScoringFailureCode))

function requireString(value: string | undefined, field: string) {
  if (typeof value !== 'string' || value.length === 0)
    throw new TypeError(`Submission response is missing ${field}.`)
  return value
}

function requireNonNegativeInteger(value: number | undefined, field: string) {
  if (!Number.isSafeInteger(value) || (value ?? -1) < 0)
    throw new TypeError(`Submission response has an invalid ${field}.`)
  return value as number
}

function requireEnum<T extends number>(
  value: T | undefined,
  values: ReadonlySet<number>,
  field: string,
): T {
  if (value === undefined)
    throw new TypeError(`Submission response is missing ${field}.`)
  if (!values.has(value))
    throw new TypeError(`Submission response has an unsupported ${field}.`)
  return value
}

function requireNullableEnum<T extends number>(
  value: T | null | undefined,
  values: ReadonlySet<number>,
  field: string,
): T | null {
  if (value === undefined)
    throw new TypeError(`Submission response is missing ${field}.`)
  if (value !== null && !values.has(value))
    throw new TypeError(`Submission response has an unsupported ${field}.`)
  return value
}

function requirePublicScoringFailureCode(
  value: NoCtfDomainSubmissionsScoringFailureCode | null | undefined,
): PublicScoringFailureCode | null {
  return requireNullableEnum(
    value,
    scoringFailureCodes,
    'failureCode',
  ) as PublicScoringFailureCode | null
}

function commonSubmissionFields(
  value: NoCtfapiEndpointsSubmissionsSubmissionListItemResponse
    | NoCtfapiEndpointsSubmissionsSubmissionStatusResponse,
) {
  return {
    competitionId: requireString(value.competitionId, 'competitionId'),
    competitionChallengeId: requireString(
      value.competitionChallengeId,
      'competitionChallengeId',
    ),
    teamId: requireString(value.teamId, 'teamId'),
    kind: requireEnum(value.kind, submissionKinds, 'kind'),
    evaluationState: requireEnum(
      value.evaluationState,
      evaluationStates,
      'evaluationState',
    ),
    result: requireNullableEnum(value.result, scoringResults, 'result'),
    failureCode: requirePublicScoringFailureCode(value.failureCode),
    receivedAt: requireString(value.receivedAt, 'receivedAt'),
    processingVersion: requireNonNegativeInteger(
      value.processingVersion,
      'processingVersion',
    ),
  }
}

export function toPublicSubmissionListItem(
  value: NoCtfapiEndpointsSubmissionsSubmissionListItemResponse,
): PublicSubmissionListItem {
  return {
    id: requireString(value.id, 'id'),
    ...commonSubmissionFields(value),
    submittedByUserId: requireString(
      value.submittedByUserId,
      'submittedByUserId',
    ),
  }
}

export function toPublicSubmissionStatus(
  value: NoCtfapiEndpointsSubmissionsSubmissionStatusResponse,
): PublicSubmissionStatus {
  return {
    submissionId: requireString(value.submissionId, 'submissionId'),
    ...commonSubmissionFields(value),
    evaluationUpdatedAt: requireString(
      value.evaluationUpdatedAt,
      'evaluationUpdatedAt',
    ),
  }
}

export function toPublicSubmissionPage(
  value: NoCtfapiEndpointsSubmissionsSubmissionListResponse,
): PublicSubmissionPage {
  if (!Array.isArray(value.items))
    throw new TypeError('Submission list response is missing items.')
  if (value.nextCursor === undefined)
    throw new TypeError('Submission list response is missing nextCursor.')
  if (value.nextCursor !== null && (typeof value.nextCursor !== 'string' || value.nextCursor.length === 0))
    throw new TypeError('Submission list response has an invalid nextCursor.')

  return {
    items: value.items.map(toPublicSubmissionListItem),
    nextCursor: value.nextCursor,
  }
}

export function toAcceptedFlagSubmission(
  value: NoCtfapiEndpointsSubmissionsFlagSubmissionAcceptedResponse,
): AcceptedFlagSubmission {
  if (value.submissions !== undefined && value.submissions !== null)
    throw new TypeError('Flag submission response unexpectedly contains a batch result.')

  const submissionId = value.submissionId ?? undefined
  const statusUrl = value.statusUrl ?? undefined
  if (typeof submissionId !== 'string' || submissionId.length === 0)
    throw new TypeError('Flag submission response is missing submissionId.')
  if (typeof statusUrl !== 'string' || statusUrl.length === 0)
    throw new TypeError('Flag submission response is missing statusUrl.')

  return { submissionId, statusUrl }
}

export function isSubmissionTerminal(
  submission: Pick<PublicSubmissionStatus, 'evaluationState'>,
) {
  return submission.evaluationState === SubmissionEvaluationState.Completed
    || submission.evaluationState === SubmissionEvaluationState.PlatformFailed
}

export function getSubmissionOutcome(
  submission: Pick<PublicSubmissionStatus, 'evaluationState' | 'result'>,
): PublicSubmissionOutcome {
  if (!isSubmissionTerminal(submission))
    return SubmissionOutcome.Evaluating
  if (submission.evaluationState === SubmissionEvaluationState.PlatformFailed)
    return SubmissionOutcome.PlatformFailed

  switch (submission.result) {
    case ScoringResult.Correct:
      return SubmissionOutcome.Correct
    case ScoringResult.Wrong:
      return SubmissionOutcome.Wrong
    case ScoringResult.Duplicate:
      return SubmissionOutcome.Duplicate
    case ScoringResult.AttemptsExhausted:
      return SubmissionOutcome.AttemptsExhausted
    case ScoringResult.PlatformFailed:
      return SubmissionOutcome.PlatformFailed
    case ScoringResult.Rejected:
      return SubmissionOutcome.Rejected
    default:
      throw new TypeError('Completed submission response is missing result.')
  }
}

export function submissionMessageKey(
  submission: Pick<
    PublicSubmissionStatus,
    'evaluationState' | 'result' | 'failureCode'
  >,
): SubmissionMessageKey {
  const outcome = getSubmissionOutcome(submission)

  switch (outcome) {
    case SubmissionOutcome.Correct:
      return 'challenges.correctFlag'
    case SubmissionOutcome.Wrong:
      return 'challenges.incorrectFlag'
    case SubmissionOutcome.Duplicate:
      return 'challenges.duplicateFlag'
    case SubmissionOutcome.AttemptsExhausted:
      return 'challenges.attemptsExhausted'
    case SubmissionOutcome.PlatformFailed:
      return 'challenges.platformEvaluationFailed'
    case SubmissionOutcome.Rejected:
      switch (submission.failureCode) {
        case ScoringFailureCode.FlagNotSupported:
          return 'challenges.flagNotSupported'
        case ScoringFailureCode.SelfAttackRejected:
          return 'challenges.selfAttackRejected'
        case ScoringFailureCode.UnknownTeamIdentifier:
          return 'challenges.unknownFlagTarget'
        case ScoringFailureCode.AmbiguousFlagMatch:
          return 'challenges.ambiguousFlag'
        case ScoringFailureCode.FlagExpired:
          return 'challenges.flagExpired'
        case ScoringFailureCode.RoundOutOfRange:
          return 'challenges.roundOutOfRange'
        case ScoringFailureCode.HardeningActive:
          return 'challenges.hardeningActive'
        default:
          return 'challenges.submissionRejected'
      }
    default:
      throw new TypeError('Submission is still being evaluated.')
  }
}

export function solvedCompetitionChallengeIds(
  submissions: readonly PublicSubmissionListItem[],
) {
  return new Set(
    submissions
      .filter(submission =>
        submission.evaluationState === SubmissionEvaluationState.Completed
        && submission.result === ScoringResult.Correct
        && (
          submission.kind === SubmissionKind.Flag
          || submission.kind === SubmissionKind.Break
        ),
      )
      .map(submission => submission.competitionChallengeId),
  )
}
