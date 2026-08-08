import type { InjectionKey, Ref } from 'vue'
import type {
  NoCtfapiEndpointsCompetitionsCompetitionResponse,
  NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse,
  NoCtfapiEndpointsNotificationsNotificationResponse,
  NoCtfapiEndpointsCompetitionsGameModeProtocol,
  NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol,
  NoCtfapiEndpointsTeamsTeamRegistrationStatusProtocol,
  NoCtfapiEndpointsRuntimeRuntimeStateProtocol,
  NoCtfapiEndpointsSubmissionsSubmissionKindProtocol,
  NoCtfapiEndpointsSubmissionsSubmissionEvaluationStateProtocol,
  NoCtfapiEndpointsSubmissionsScoringResultProtocol,
  NoCtfapiEndpointsCompetitionsEventsCompetitionEventKindProtocol,
  NoCtfapiEndpointsNotificationsNotificationKindProtocol,
} from '~/api'

/** 竞赛上下文:由 pages/competitions/[id].vue provide,子路由 inject。 */
export interface CompetitionContext {
  competition: Ref<NoCtfapiEndpointsCompetitionsCompetitionResponse | null>
  loading: Ref<boolean>
  error: Ref<string | null>
  refresh: () => Promise<void>
}

export const competitionContextKey: InjectionKey<CompetitionContext> = Symbol('competition-context')

/** 竞赛模式(NoCTF.Domain.Competitions.GameMode)。 */
export const GameMode = { Ctf: 'Ctf', Awd: 'Awd', Awdp: 'Awdp', Koh: 'Koh' } as const

/** 竞赛生命周期(NoCTF.Domain.Competitions.CompetitionStatus)。 */
export const CompetitionStatus = {
  Draft: 'Draft', Visible: 'Visible', Published: 'Published', Running: 'Running', Paused: 'Paused', Finished: 'Finished',
} as const

/** 队伍报名状态(NoCTF.Domain.Teams.TeamRegistrationStatus)。 */
export const TeamRegistrationStatus = { Pending: 'Pending', Approved: 'Approved', Rejected: 'Rejected' } as const

/** 运行时状态(NoCTF.Domain.Runtime.RuntimeState)。 */
export const RuntimeState = {
  Queued: 'Queued', Provisioning: 'Provisioning', Running: 'Running', Stopping: 'Stopping', Stopped: 'Stopped', Failed: 'Failed',
} as const

/** 提交类型(NoCTF.Domain.Submissions.SubmissionKind)。 */
export const SubmissionKind = { Flag: 'Flag', Break: 'Break', Fix: 'Fix' } as const

/** 提交评测状态(NoCTF.Domain.Submissions.SubmissionEvaluationState)。 */
export const EvaluationState = {
  Pending: 'Pending', Queued: 'Queued', Processing: 'Processing', Completed: 'Completed', PlatformFailed: 'PlatformFailed',
} as const

/** 评测结果(NoCTF.Domain.Submissions.ScoringResult)。 */
export const ScoringResult = {
  Correct: 'Correct', Wrong: 'Wrong', Duplicate: 'Duplicate', AttemptsExhausted: 'AttemptsExhausted', PlatformFailed: 'PlatformFailed', Rejected: 'Rejected',
} as const

/** 排行榜数据范围(NoCTF.Application.Scoring.Leaderboard.LeaderboardDataScope)。 */
export const LeaderboardDataScope = { Live: 'Live', Frozen: 'Frozen', Hidden: 'Hidden' } as const

export function gameModeLabel(mode?: NoCtfapiEndpointsCompetitionsGameModeProtocol | string | number): string {
  return ({ Ctf: 'CTF', Awd: 'AWD', Awdp: 'AWDP', Koh: 'KoH' } as Record<string, string>)[String(mode)] ?? '未知'
}

export function competitionStatusLabel(status?: NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol | string | number): string {
  return ({ Draft: '草稿', Visible: '即将发布', Published: '即将开始', Running: '进行中', Paused: '已暂停', Finished: '已结束' } as Record<string, string>)[String(status)] ?? '未知'
}

export function teamRegistrationStatusLabel(status?: NoCtfapiEndpointsTeamsTeamRegistrationStatusProtocol | string | number): string {
  return ({ Pending: '待审核', Approved: '已通过', Rejected: '已拒绝' } as Record<string, string>)[String(status)] ?? '未知'
}

export function runtimeStateLabel(state?: NoCtfapiEndpointsRuntimeRuntimeStateProtocol | string | number): string {
  return ({ Queued: '排队中', Provisioning: '部署中', Running: '运行中', Stopping: '停止中', Stopped: '已停止', Failed: '失败' } as Record<string, string>)[String(state)] ?? '未知'
}

export function submissionKindLabel(kind?: NoCtfapiEndpointsSubmissionsSubmissionKindProtocol | string | number): string {
  return ({ Flag: 'Flag', Break: 'Break', Fix: 'Fix' } as Record<string, string>)[String(kind)] ?? '提交'
}

export function evaluationStateLabel(state?: NoCtfapiEndpointsSubmissionsSubmissionEvaluationStateProtocol | string | number): string {
  return ({ Pending: '待评测', Queued: '排队中', Processing: '评测中', Completed: '已完成', PlatformFailed: '平台故障' } as Record<string, string>)[String(state)] ?? '未知'
}

export function scoringResultLabel(result?: NoCtfapiEndpointsSubmissionsScoringResultProtocol | string | number | null): string {
  if (result === null || result === undefined) return '评测中'
  return (
    { Correct: '正确', Wrong: '错误', Duplicate: '重复提交', AttemptsExhausted: '次数耗尽', PlatformFailed: '平台故障', Rejected: '已拒绝' } as Record<string, string>
  )[String(result)] ?? '未知'
}

/** 评测是否仍在进行中(需要继续轮询)。 */
export function isEvaluationPending(state?: NoCtfapiEndpointsSubmissionsSubmissionEvaluationStateProtocol | string | number): boolean {
  return state === 'Pending' || state === 'Queued' || state === 'Processing' || state === 0 || state === 1 || state === 2
}

export function formatDateTime(value?: string | null): string {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '—'
  return date.toLocaleString('zh-CN', { hour12: false })
}

export function formatBytes(bytes?: number | null): string {
  if (bytes === null || bytes === undefined) return ''
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`
}

/** 将剩余毫秒格式化为「Xd Xh Xm Xs」倒计时文本。 */
export function formatDuration(ms: number): string {
  if (ms <= 0) return '0s'
  const total = Math.floor(ms / 1000)
  const days = Math.floor(total / 86400)
  const hours = Math.floor((total % 86400) / 3600)
  const minutes = Math.floor((total % 3600) / 60)
  const seconds = total % 60
  const parts: string[] = []
  if (days > 0) parts.push(`${days} 天`)
  if (hours > 0) parts.push(`${hours} 小时`)
  if (minutes > 0 && days === 0) parts.push(`${minutes} 分`)
  if (days === 0 && hours === 0) parts.push(`${seconds} 秒`)
  return parts.join(' ')
}

/** 竞赛动态文案(NoCTF.Domain.Competitions.Events.CompetitionEventKind)。 */
export function competitionEventText(
  event: NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse,
): string {
  const actor = event.actorDisplayName ?? '系统'
  const team = event.teamDisplayName ? `队伍「${event.teamDisplayName}」` : '某队伍'
  const challenge = event.challengeTitle ? `题目「${event.challengeTitle}」` : '某题目'
  const templates: Record<string, string> = {
    CompetitionCreated: '竞赛已创建', CompetitionUpdated: '竞赛信息已更新', CompetitionLifecycleChanged: '竞赛生命周期变更',
    LeaderboardVisibilityChanged: '排行榜可见性已变更', ChallengeCreated: `${challenge}已加入竞赛`, ChallengeUpdated: `${challenge}已更新`,
    ChallengePublished: `${challenge}已发布`, ChallengeUnpublished: `${challenge}已下线`, HintPublished: `${challenge}发布了新提示`,
    HintUnlocked: `${team}解锁了${challenge}的提示`, TeamRegistered: `${team}报名参赛`, TeamRegistrationChanged: `${team}的报名状态已变更`,
    TeamMemberJoined: `${team}加入了新成员`, TeamBanned: `${team}被封禁`, TeamUnbanned: `${team}已解除封禁`, SubmissionReceived: `${team}提交了${challenge}`,
    SubmissionEvaluated: `${team}在${challenge}的提交已评测`, FirstBloodAwarded: `${team}拿下了${challenge}的一血`, SecondBloodAwarded: `${team}拿下了${challenge}的二血`,
    ThirdBloodAwarded: `${team}拿下了${challenge}的三血`, RuntimeCreated: `${team}申请了${challenge}的环境`, RuntimeStateChanged: `${team}的${challenge}环境状态已变更`,
    AnnouncementPublished: '官方发布了一条公告', QuestionOpened: `${actor}提出了咨询`, QuestionReplied: '咨询已有回复', QuestionStatusChanged: '咨询状态已变更', QuestionPublished: '一条咨询已公开',
  }
  return templates[String(event.kind)] ?? '发生了一条竞赛动态'
}

function notificationContent(
  notification: NoCtfapiEndpointsNotificationsNotificationResponse,
): Record<string, unknown> {
  return notification.content && typeof notification.content === 'object'
    ? notification.content as Record<string, unknown>
    : {}
}

export function notificationCompetitionId(
  notification: NoCtfapiEndpointsNotificationsNotificationResponse,
): string | null {
  const content = notificationContent(notification)
  return typeof content.competitionId === 'string' ? content.competitionId : null
}

/** 通知文案(NoCTF.Domain.Notifications.NotificationKind),content 为松散 JSON。 */
export function notificationText(
  notification: NoCtfapiEndpointsNotificationsNotificationResponse,
): string {
  const payload = notificationContent(notification)
  const title = typeof payload.competitionTitle === 'string' ? `「${payload.competitionTitle}」` : ''
  const team = typeof payload.teamName === 'string' ? `「${payload.teamName}」` : ''
  const challenge = typeof payload.challengeTitle === 'string' ? `「${payload.challengeTitle}」` : ''
  const templates: Record<string, string> = {
    CompetitionLifecycleChanged: `竞赛${title}的生命周期已变更`, TeamRegistrationChanged: `你的队伍${team}报名状态已变更`, SubmissionEvaluated: `你在${challenge}的提交已完成评测`,
    RuntimeStateChanged: `${challenge}的运行环境状态已变更`, StartGateFailed: `竞赛${title}启动检查未通过`, ManagementFailure: `竞赛${title}出现管理侧故障`,
    BloodAwarded: `恭喜,你在${challenge}拿下了血榜名次`, ChallengePublished: `竞赛${title}发布了新题目${challenge}`, HintPublished: `${challenge}发布了新提示`,
    TeamBanned: `你的队伍${team}已被封禁`, CompetitionQuestionOpened: `竞赛${title}有新的咨询`, CompetitionQuestionReplied: '你的咨询已有新回复',
    CompetitionQuestionStatusChanged: '你的咨询状态已变更', CheatIncidentDetected: '检测到疑似作弊行为', TeamBanCorrected: '队伍封禁已被纠正', DataExportReady: '数据导出已就绪', DataExportFailed: '数据导出失败',
    UserAccountLifecycleChanged: '用户账号状态已变更',
  }
  return templates[String(notification.kind)] ?? '你有一条新通知'
}
