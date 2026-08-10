import type {
  NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionAccessCode,
  NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionFailureCode,
  NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionFailureResponse,
  NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionParticipantRoleCode,
} from '../api'
import { parseApiError } from '../utils/api-error'
import { translate } from '../utils/i18n'

type FailureCode = NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionFailureCode
type FailurePayload = NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionFailureResponse
type ParticipantRole = NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionParticipantRoleCode
type QuestionAccess = NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionAccessCode

const staticFailureMessages = {
  InvalidRequest: translate("咨询内容或请求参数无效，请检查后重试。"),
  SpamRejected: translate("咨询内容疑似重复或无效文本，请补充清晰的问题描述。"),
  CompetitionNotAcceptingQuestions: translate("当前比赛状态不允许创建咨询。"),
  TeamNotEligible: translate("当前队伍尚不具备发起咨询的资格。"),
  InvalidChallengeReference: translate("关联题目无效、未发布或不属于当前比赛。"),
  RevisionConflict: translate("咨询已被其他成员更新，请刷新后再发送。"),
  InvalidTransition: translate("当前咨询状态不允许执行此操作。"),
  QuestionClosed: translate("该咨询已关闭；如需继续沟通，请重新创建咨询。"),
} satisfies Partial<Record<FailureCode, string>>

export const competitionQuestionRoleLabel = {
  Asker: translate("选手"),
  Participant: translate("选手"),
  Handler: translate("工作人员"),
  Judge: translate("裁判"),
  ChallengeOwner: translate("题目所有者"),
  CompetitionManager: translate("比赛管理员"),
  PlatformAdministrator: translate("平台管理员"),
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
