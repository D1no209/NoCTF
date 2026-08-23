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
  InvalidRequest: '咨询内容或请求参数无效，请检查后重试。',
  SpamRejected: '咨询内容疑似重复或无效文本，请补充清晰的问题描述。',
  CompetitionNotAcceptingQuestions: '当前比赛状态不允许创建咨询。',
  TeamNotEligible: '当前队伍尚不具备发起咨询的资格。',
  InvalidChallengeReference: '关联题目无效、未发布或不属于当前比赛。',
  InvalidTransition: '当前咨询状态不允许执行此操作。',
  QuestionClosed: '该咨询已关闭；如需继续沟通，请重新创建咨询。',
} satisfies Partial<Record<FailureCode, string>>

export const competitionQuestionRoleLabel = {
  Asker: '选手',
  Participant: '选手',
  Handler: '工作人员',
  Judge: '裁判',
  ChallengeOwner: '题目所有者',
  CompetitionManager: '比赛管理员',
  PlatformAdministrator: '平台管理员',
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
    return translate('本队已有 {limit} 个活跃咨询，请先解决已有咨询。', { limit: limit ?? 5 })
  if (payload.code === 'ParticipantMessageLimitReached')
    return translate('工作人员回复前最多连续发送 {limit} 条消息，请等待回复。', { limit: limit ?? 3 })

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
