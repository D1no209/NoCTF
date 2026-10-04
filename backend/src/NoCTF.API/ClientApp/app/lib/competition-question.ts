import { dateTimestamp } from '../utils/date-value'
import type { UiMessage } from '../utils/i18n'
import type { NoCTFAPIEndpointsChallengesQuestionsCompetitionQuestionAccessCode, NoCTFAPIEndpointsChallengesQuestionsCompetitionQuestionFailureCode, NoCTFAPIEndpointsChallengesQuestionsCompetitionQuestionFailureResponse, NoCTFAPIEndpointsChallengesQuestionsCompetitionQuestionParticipantRoleCode, NoCTFAPIEndpointsChallengesQuestionsCompetitionQuestionResponse } from '../api/models'
import { parseApiError } from '../utils/api-error'
import { translate } from '../utils/i18n'

type FailureCode = NoCTFAPIEndpointsChallengesQuestionsCompetitionQuestionFailureCode
type FailurePayload = NoCTFAPIEndpointsChallengesQuestionsCompetitionQuestionFailureResponse
type ParticipantRole = NoCTFAPIEndpointsChallengesQuestionsCompetitionQuestionParticipantRoleCode
type QuestionAccess = NoCTFAPIEndpointsChallengesQuestionsCompetitionQuestionAccessCode
type CompetitionQuestion = NoCTFAPIEndpointsChallengesQuestionsCompetitionQuestionResponse

const staticFailureMessages = {
  InvalidRequest: "common.competitionQuestion.error.questionContentCheckInvalid",
  SpamRejected: "common.competitionQuestion.description.questionAppearsDuplicatedUnclear",
  CompetitionNotAcceptingQuestions: "common.competitionQuestion.validation.questionsOpenedFormat",
  TeamNotEligible: "common.competitionQuestion.description.teamEligibleOpenQuestion",
  InvalidChallengeReference: "common.competitionQuestion.error.linkedChallengeUnpublishedInvalid",
  InvalidTransition: "common.competitionQuestion.description.actionAllowedQuestionState",
  QuestionClosed: "common.competitionQuestion.description.questionClosedOpenNew",
} satisfies Partial<Record<FailureCode, string>>

export const competitionQuestionRoleLabel = {
  Asker: "common.label.participant",
  Participant: "common.label.participant",
  Handler: "common.label.staff",
  Judge: "common.label.judge",
  ChallengeOwner: "common.label.challengeOwner",
  CompetitionManager: "common.label.competitionManager",
  PlatformAdministrator: "notifications.label.platformAdministrator",
} satisfies Record<ParticipantRole, string>

export function isCompetitionQuestionHandlerRole(role?: ParticipantRole | null): boolean {
  return role !== undefined && role !== 'Asker' && role !== 'Participant'
}

export function competitionQuestionErrorMessage(
  error: unknown,
  fallback: string,
): UiMessage {
  const payload = asFailurePayload(error)
  if (!payload?.code)
    return parseApiError(error, fallback).displayMessage

  const limit = payload.limit ?? undefined
  if (payload.code === 'TeamActiveQuestionLimitReached')
    return translate("notifications.competitionQuestion.description.teamAlreadyActiveQuestions", { limit: limit ?? 5 })
  if (payload.code === 'ParticipantMessageLimitReached')
    return translate("notifications.competitionQuestion.description.sendConsecutiveMessagesStaff", { limit: limit ?? 3 })

  const message = staticFailureMessages[payload.code]
  return message ? translate(message) : fallback
}

export function competitionQuestionUnreadCount(
  updatedAt: Date | string | null | undefined,
  seenUpdatedAt: string | null | undefined,
  lastActorRole: ParticipantRole | null | undefined,
  access: QuestionAccess | null | undefined,
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

function questionTime(value: Date | string | null | undefined): number {
  const timestamp = dateTimestamp(value ?? '')
  return Number.isFinite(timestamp) ? timestamp : 0
}

function asFailurePayload(error: unknown): FailurePayload | null {
  if (!error || typeof error !== 'object') return null
  const payload = error as Partial<FailurePayload>
  return typeof payload.code === 'string' ? payload as FailurePayload : null
}
