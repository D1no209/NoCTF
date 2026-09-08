import type {
  NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionAccessCode,
  NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionFailureCode,
  NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionFailureResponse,
  NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionParticipantRoleCode,
  NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionResponse,
} from '../api'
import { parseApiError } from '../utils/api-error'
import { translate } from '../utils/i18n'

type FailureCode = NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionFailureCode
type FailurePayload = NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionFailureResponse
type ParticipantRole = NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionParticipantRoleCode
type QuestionAccess = NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionAccessCode
type CompetitionQuestion = NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionResponse

const staticFailureMessages = {
  InvalidRequest: "ui.theQuestionContentOrRequestIsInvalidCheckItAnd",
  SpamRejected: "ui.theQuestionAppearsDuplicatedOrUnclearPleaseProvideAClearer",
  CompetitionNotAcceptingQuestions: "ui.questionsCannotBeOpenedInTheCurrentCompetitionState",
  TeamNotEligible: "ui.thisTeamIsNotEligibleToOpenAQuestion",
  InvalidChallengeReference: "ui.theLinkedChallengeIsInvalidUnpublishedOrBelongsToAnother",
  InvalidTransition: "ui.thisActionIsNotAllowedInTheCurrentQuestionState",
  QuestionClosed: "ui.thisQuestionIsClosedOpenANewQuestionToContinue",
} satisfies Partial<Record<FailureCode, string>>

export const competitionQuestionRoleLabel = {
  Asker: "ui.participant",
  Participant: "ui.participant",
  Handler: "ui.staff",
  Judge: "ui.judge",
  ChallengeOwner: "ui.challengeOwner",
  CompetitionManager: "ui.competitionManager",
  PlatformAdministrator: "ui.platformAdministrator",
} satisfies Record<ParticipantRole, string>

export function isCompetitionQuestionHandlerRole(role?: ParticipantRole): boolean {
  return role !== undefined && role !== 'Asker' && role !== 'Participant'
}

export function competitionQuestionErrorMessage(
  error: unknown,
  fallback: string,
): string {
  const payload = asFailurePayload(error)
  if (!payload?.code)
    return parseApiError(error, fallback).message

  const limit = payload.limit ?? undefined
  if (payload.code === 'TeamActiveQuestionLimitReached')
    return translate("ui.yourTeamAlreadyHasActiveQuestionsResolveAnExistingOne", { limit: limit ?? 5 })
  if (payload.code === 'ParticipantMessageLimitReached')
    return translate("ui.youCanSendUpToConsecutiveMessagesBeforeAStaff", { limit: limit ?? 3 })

  const message = staticFailureMessages[payload.code]
  return message ? translate(message) : fallback
}

export function competitionQuestionUnreadCount(
  updatedAt: string | undefined,
  seenUpdatedAt: string | undefined,
  lastActorRole: ParticipantRole | undefined,
  access: QuestionAccess | undefined,
): number {
  if (seenUpdatedAt !== undefined)
    return questionTime(updatedAt) > questionTime(seenUpdatedAt) ? 1 : 0

  return access === 'Asker' && isCompetitionQuestionHandlerRole(lastActorRole) ? 1 : 0
}

/**
 * Merge cursor pages and independently fetched thread details without
 * duplicating a question. UpdatedAt identifies the freshest projection and
 * keeps recently active threads at the top.
 */
export function mergeCompetitionQuestions(
  current: readonly CompetitionQuestion[],
  incoming: readonly CompetitionQuestion[],
): CompetitionQuestion[] {
  const byThreadRootId = new Map<string, CompetitionQuestion>()
  const withoutThreadRootId: CompetitionQuestion[] = []

  for (const question of [...current, ...incoming]) {
    if (!question.threadRootId) {
      withoutThreadRootId.push(question)
      continue
    }

    const existing = byThreadRootId.get(question.threadRootId)
    if (!existing || isFresherQuestion(question, existing))
      byThreadRootId.set(question.threadRootId, question)
  }

  return [...byThreadRootId.values(), ...withoutThreadRootId].sort((left, right) => {
    const timeDelta = questionTimestamp(right) - questionTimestamp(left)
    if (timeDelta !== 0) return timeDelta
    return (right.threadRootId ?? '').localeCompare(left.threadRootId ?? '')
  })
}

function isFresherQuestion(
  candidate: CompetitionQuestion,
  current: CompetitionQuestion,
): boolean {
  return questionTimestamp(candidate) > questionTimestamp(current)
}

function questionTimestamp(question: CompetitionQuestion): number {
  return questionTime(question.updatedAt)
}

function questionTime(value: string | undefined): number {
  const timestamp = Date.parse(value ?? '')
  return Number.isFinite(timestamp) ? timestamp : 0
}

function asFailurePayload(error: unknown): FailurePayload | null {
  if (!error || typeof error !== 'object') return null
  const payload = error as Partial<FailurePayload>
  return typeof payload.code === 'string' ? payload as FailurePayload : null
}
