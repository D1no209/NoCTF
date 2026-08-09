import type {
  NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionAccessCode,
  NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionFailureCode,
  NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionFailureResponse,
  NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionParticipantRoleCode,
} from '../api'
import { parseApiError } from '../utils/api-error'

type FailureCode = NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionFailureCode
type FailurePayload = NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionFailureResponse
type ParticipantRole = NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionParticipantRoleCode
type QuestionAccess = NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionAccessCode

const staticFailureMessages = {
  InvalidRequest: '咨询内容或请求参数无效，请检查后重试。',
  SpamRejected: '咨询内容疑似重复或无效文本，请补充清晰的问题描述。',
  CompetitionNotAcceptingQuestions: '当前比赛状态不允许创建咨询。',
  TeamNotEligible: '当前队伍尚不具备发起咨询的资格。',
  InvalidChallengeReference: '关联题目无效、未发布或不属于当前比赛。',
  RevisionConflict: '咨询已被其他成员更新，请刷新后再发送。',
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
    return `本队已有 ${limit ?? 5} 个活跃咨询，请先解决已有咨询。`
  if (payload.code === 'ParticipantMessageLimitReached')
    return `工作人员回复前最多连续发送 ${limit ?? 3} 条消息，请等待回复。`

  return staticFailureMessages[payload.code] ?? fallback
}

export function competitionQuestionUnreadCount(
  revision: number | undefined,
  seenRevision: number | undefined,
  lastActorRole: ParticipantRole | undefined,
  access: QuestionAccess | undefined,
): number {
  const current = Math.max(0, revision ?? 0)
  if (seenRevision !== undefined)
    return Math.max(0, current - seenRevision)

  return access === 'Asker' && isCompetitionQuestionHandlerRole(lastActorRole) ? 1 : 0
}

function asFailurePayload(error: unknown): FailurePayload | null {
  if (!error || typeof error !== 'object') return null
  const payload = error as Partial<FailurePayload>
  return typeof payload.code === 'string' ? payload as FailurePayload : null
}
