import { afterAll, beforeEach, describe, expect, test } from 'bun:test'
import { client } from '../src/api/generated/client.gen'
import { ApiError, submissionApi } from '../src/api/noctf'
import {
  getSubmissionOutcome,
  isSubmissionTerminal,
  ScoringFailureCode,
  ScoringResult,
  solvedCompetitionChallengeIds,
  SubmissionEvaluationState,
  SubmissionKind,
  submissionMessageKey,
  SubmissionOutcome,
  toAcceptedFlagSubmission,
  toPublicSubmissionPage,
  toPublicSubmissionStatus,
} from '../src/api/submissionPresentation'

const apiBaseUrl = 'https://api.noctf.test'
const competitionId = '11111111-1111-1111-1111-111111111111'
const competitionChallengeId = '22222222-2222-2222-2222-222222222222'
const templateChallengeId = '33333333-3333-3333-3333-333333333333'
const submissionId = '44444444-4444-4444-4444-444444444444'
const teamId = '55555555-5555-5555-5555-555555555555'
const userId = '66666666-6666-6666-6666-666666666666'
const originalClientConfig = client.getConfig()
const requests: Request[] = []

const status = {
  submissionId,
  competitionId,
  teamId,
  competitionChallengeId,
  kind: 0,
  evaluationState: 3,
  result: 0,
  failureCode: null,
  receivedAt: '2026-08-01T00:00:00Z',
  evaluationUpdatedAt: '2026-08-01T00:00:01Z',
  processingVersion: 1,
} as const

const firstListItem = {
  id: submissionId,
  competitionId,
  competitionChallengeId,
  teamId,
  submittedByUserId: userId,
  kind: 0,
  evaluationState: 3,
  result: 0,
  failureCode: null,
  receivedAt: '2026-08-01T00:00:00Z',
  processingVersion: 1,
} as const

let listMode: 'two-pages' | 'repeated-cursor' = 'two-pages'
let denyStatus = false

const contractFetch: typeof fetch = async (input, init) => {
  const normalizedInput = typeof input === 'string' && input.startsWith('/')
    ? new URL(input, apiBaseUrl)
    : input
  const request = new Request(normalizedInput, init)
  requests.push(request)

  const url = new URL(request.url)
  const submissionsPath = `/api/v1/competitions/${competitionId}/submissions`
  const statusPath = `${submissionsPath}/${submissionId}`
  const submitPath
    = `/api/v1/competitions/${competitionId}/challenges/${competitionChallengeId}/flag-submissions`

  if (url.pathname === submitPath && request.method === 'POST') {
    return Response.json({
      submissionId,
      statusUrl: statusPath,
      submissions: null,
    }, { status: 202 })
  }

  if (url.pathname === statusPath) {
    if (denyStatus)
      return Response.json({}, { status: 403 })
    return Response.json(status)
  }

  if (url.pathname === submissionsPath) {
    const cursor = url.searchParams.get('cursor')
    if (!cursor) {
      return Response.json({
        items: [firstListItem],
        nextCursor: 'opaque+cursor/==',
      })
    }

    return Response.json({
      items: [],
      nextCursor: listMode === 'repeated-cursor' ? 'opaque+cursor/==' : null,
    })
  }

  return Response.json({}, { status: 404 })
}

beforeEach(() => {
  requests.length = 0
  listMode = 'two-pages'
  denyStatus = false
  client.setConfig({
    baseUrl: apiBaseUrl,
    fetch: contractFetch,
  })
})

afterAll(() => {
  client.setConfig(originalClientConfig)
})

describe('generated public submission contract', () => {
  test('submits one flag with the competition challenge identity and an exact body', async () => {
    const accepted = await submissionApi.submitFlag(
      competitionId,
      competitionChallengeId,
      'flag{one}',
    )

    expect(accepted).toEqual({
      submissionId,
      statusUrl: `/api/v1/competitions/${competitionId}/submissions/${submissionId}`,
    })
    expect(templateChallengeId).not.toBe(competitionChallengeId)
    expect(requests).toHaveLength(1)
    expect(requests[0]!.method).toBe('POST')
    expect(new URL(requests[0]!.url).pathname)
      .toBe(`/api/v1/competitions/${competitionId}/challenges/${competitionChallengeId}/flag-submissions`)
    expect(await requests[0]!.json()).toEqual({ flag: 'flag{one}' })
  })

  test('rejects fake synchronous results and incomplete single-flag acceptances', () => {
    expect(() => toAcceptedFlagSubmission({ correct: true } as never))
      .toThrow('Flag submission response is missing submissionId.')
    expect(() => toAcceptedFlagSubmission({
      submissionId,
      statusUrl: null,
      submissions: null,
    })).toThrow('Flag submission response is missing statusUrl.')
  })

  test('gets status with competition and submission ids instead of fetching statusUrl', async () => {
    await expect(
      submissionApi.getStatus(competitionId, submissionId),
    )
      .resolves
      .toEqual(status)

    expect(requests).toHaveLength(1)
    expect(new URL(requests[0]!.url).pathname)
      .toBe(`/api/v1/competitions/${competitionId}/submissions/${submissionId}`)
  })

  test('maps every bounded enum and rejects unsupported or incomplete status values', () => {
    expect(SubmissionKind).toEqual({ Flag: 0, Break: 1, Fix: 2 })
    expect(SubmissionEvaluationState).toEqual({
      Pending: 0,
      Queued: 1,
      Processing: 2,
      Completed: 3,
      PlatformFailed: 4,
    })
    expect(ScoringResult).toEqual({
      Correct: 0,
      Wrong: 1,
      Duplicate: 2,
      AttemptsExhausted: 3,
      PlatformFailed: 4,
      Rejected: 5,
    })
    expect(ScoringFailureCode).toEqual({
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
    })

    for (const failureCode of Object.values(ScoringFailureCode)) {
      expect(toPublicSubmissionStatus({
        ...status,
        evaluationState: 4,
        result: null,
        failureCode,
      }).failureCode).toBe(failureCode)
    }

    expect(() => toPublicSubmissionStatus({ ...status, kind: 9 as never }))
      .toThrow('Submission response has an unsupported kind.')
    expect(() => toPublicSubmissionStatus({ ...status, submissionId: undefined }))
      .toThrow('Submission response is missing submissionId.')
    expect(() => toPublicSubmissionStatus({ ...status, result: undefined }))
      .toThrow('Submission response is missing result.')
    expect(() => toPublicSubmissionStatus({ ...status, failureCode: 29 }))
      .toThrow('Submission response has an unsupported failureCode.')
    expect(() => toPublicSubmissionPage({ nextCursor: null }))
      .toThrow('Submission list response is missing items.')
    expect(() => toPublicSubmissionPage({ items: [firstListItem] }))
      .toThrow('Submission list response is missing nextCursor.')
  })

  test('reduces evaluation states and scoring results without treating 202 as solved', () => {
    for (const evaluationState of [
      SubmissionEvaluationState.Pending,
      SubmissionEvaluationState.Queued,
      SubmissionEvaluationState.Processing,
    ]) {
      const pending = toPublicSubmissionStatus({
        ...status,
        evaluationState,
        result: null,
      })
      expect(isSubmissionTerminal(pending)).toBe(false)
      expect(getSubmissionOutcome(pending)).toBe(SubmissionOutcome.Evaluating)
    }

    const outcomes = [
      [ScoringResult.Correct, SubmissionOutcome.Correct],
      [ScoringResult.Wrong, SubmissionOutcome.Wrong],
      [ScoringResult.Duplicate, SubmissionOutcome.Duplicate],
      [ScoringResult.AttemptsExhausted, SubmissionOutcome.AttemptsExhausted],
      [ScoringResult.PlatformFailed, SubmissionOutcome.PlatformFailed],
      [ScoringResult.Rejected, SubmissionOutcome.Rejected],
    ] as const

    for (const [result, outcome] of outcomes) {
      const completed = toPublicSubmissionStatus({ ...status, result })
      expect(isSubmissionTerminal(completed)).toBe(true)
      expect(getSubmissionOutcome(completed)).toBe(outcome)
    }

    const platformFailed = toPublicSubmissionStatus({
      ...status,
      evaluationState: SubmissionEvaluationState.PlatformFailed,
      result: null,
      failureCode: ScoringFailureCode.CheckerPlatformError,
    })
    expect(isSubmissionTerminal(platformFailed)).toBe(true)
    expect(getSubmissionOutcome(platformFailed)).toBe(SubmissionOutcome.PlatformFailed)

    expect(submissionMessageKey(toPublicSubmissionStatus({
      ...status,
      result: ScoringResult.Rejected,
      failureCode: ScoringFailureCode.SelfAttackRejected,
    }))).toBe('challenges.selfAttackRejected')
  })

  test('walks every opaque cursor page and passes the returned cursor verbatim', async () => {
    await expect(submissionApi.listAll(competitionId)).resolves.toEqual([firstListItem])

    expect(requests).toHaveLength(2)
    expect(new URL(requests[0]!.url).searchParams.get('limit')).toBe('200')
    expect(new URL(requests[0]!.url).searchParams.has('cursor')).toBe(false)
    expect(new URL(requests[1]!.url).searchParams.get('cursor')).toBe('opaque+cursor/==')
  })

  test('guards a repeated cursor instead of looping forever', async () => {
    listMode = 'repeated-cursor'

    await expect(
      submissionApi.listAll(competitionId),
    )
      .rejects
      .toThrow('Submission list returned a repeated cursor.')
    expect(requests).toHaveLength(2)
  })

  test('derives solved competition challenges only from completed correct flag or break attempts', () => {
    const ids = solvedCompetitionChallengeIds([
      firstListItem,
      { ...firstListItem, id: 'break', competitionChallengeId: 'break-challenge', kind: 1 },
      { ...firstListItem, id: 'fix', competitionChallengeId: 'fix-challenge', kind: 2 },
      { ...firstListItem, id: 'wrong', competitionChallengeId: 'wrong-challenge', result: 1 },
      { ...firstListItem, id: 'queued', competitionChallengeId: 'queued-challenge', evaluationState: 1, result: null },
    ])

    expect([...ids]).toEqual([competitionChallengeId, 'break-challenge'])
  })

  test('preserves generated authorization failures as ApiError', async () => {
    denyStatus = true

    await expect(
      submissionApi.getStatus(competitionId, submissionId),
    )
      .rejects
      .toBeInstanceOf(ApiError)
    try {
      await submissionApi.getStatus(competitionId, submissionId)
    }
    catch (error) {
      expect(error).toBeInstanceOf(ApiError)
      expect((error as ApiError).status).toBe(403)
    }
  })
})
