import type {
  NoCtfapiEndpointsAdministrationCompetitionsCompetitionTrackFailureCodeProtocol,
  NoCtfapiEndpointsAdministrationCompetitionsCompetitionTrackFailureResponse,
  NoCtfapiEndpointsTeamsTeamRegistrationFailureCodeProtocol,
  NoCtfapiEndpointsTeamsTeamRegistrationFailureResponse,
} from '../api'
import { parseApiError } from '../utils/api-error'
import { translate } from '../utils/i18n'

const trackMessages = {
  CompetitionNotFound: '比赛不存在。',
  InvalidConfiguration: '赛道配置无效，请检查默认赛道、赛道键和功能组合。',
  ConfigurationLocked: '比赛已开始，赛道配置已冻结。',
  ConfigurationConflict: '赛道配置已被其他工作人员更新，请刷新后重试。',
  TrackInUse: '仍有队伍使用该赛道，不能删除。',
  TeamNotFound: '队伍不存在。',
  TrackNotFound: '所选赛道不存在。',
  TrackNotPublicSelectable: '所选赛道不允许参赛者自行选择。',
  AssignmentLocked: '比赛已开始，队伍赛道归属已冻结。',
  AssignmentConflict: '队伍信息已被更新，请刷新后重试。',
} satisfies Record<NoCtfapiEndpointsAdministrationCompetitionsCompetitionTrackFailureCodeProtocol, string>

const registrationMessages = {
  InvalidTeamName: '队伍名称无效。',
  CompetitionNotFound: '比赛不存在。',
  RegistrationClosed: '当前比赛阶段不允许创建队伍。',
  UserAlreadyRegistered: '你已经加入本场比赛的其他队伍。',
  TeamNameOrMembershipConflict: '队伍名称已存在，或你的队伍归属发生冲突。',
  TeamNotFound: '队伍不存在。',
  CompetitionFinished: '比赛已结束。',
  TeamLocked: '队伍当前已锁定。',
  TeamConflict: '队伍信息已被更新，请重试。',
  TeamReviewConflict: '队伍审核状态已发生变化。',
  CompetitionActive: '比赛进行中，不能执行此队伍操作。',
  TrackNotFound: '所选赛道不存在。',
  TrackNotPublicSelectable: '所选赛道不允许参赛者自行选择。',
  TrackInvitationRequired: '该赛道需要邀请码。',
  TrackInvitationInvalid: '赛道邀请码错误。',
} satisfies Record<NoCtfapiEndpointsTeamsTeamRegistrationFailureCodeProtocol, string>

export function competitionTrackErrorMessage(error: unknown, fallback: string): string {
  const payload = error as Partial<NoCtfapiEndpointsAdministrationCompetitionsCompetitionTrackFailureResponse> | null
  return payload?.code ? translate(trackMessages[payload.code]) : parseApiError(error, fallback).message
}

export function teamRegistrationErrorMessage(error: unknown, fallback: string): string {
  const payload = error as Partial<NoCtfapiEndpointsTeamsTeamRegistrationFailureResponse> | null
  return payload?.code ? translate(registrationMessages[payload.code]) : parseApiError(error, fallback).message
}
