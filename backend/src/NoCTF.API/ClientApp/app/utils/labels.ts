import type { InjectionKey, Ref } from 'vue'
import { localeTag, translate } from './i18n'
import type {
  NoCtfapiEndpointsCompetitionsCompetitionResponse,
  NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse,
  NoCtfapiEndpointsNotificationsNotificationResponse,
  NoCtfapiEndpointsCompetitionsGameModeProtocol,
  NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol,
  NoCtfapiEndpointsTeamsTeamRegistrationStatusProtocol,
  NoCtfapiEndpointsRuntimeRuntimeStateProtocol,
  NoCtfapiEndpointsGameplayFactsGameplayFactKindProtocol,
  NoCtfapiEndpointsGameplayFactsGameplayFactStateProtocol,
  NoCtfapiEndpointsGameplayFactsGameplayFactResultProtocol,
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

/** 比赛客观行为类型。 */
export const GameplayFactKind = {
  FlagAttempt: 'FlagAttempt', BreakAttempt: 'BreakAttempt', FixAttempt: 'FixAttempt',
  HintUnlock: 'HintUnlock', ManualAdjustment: 'ManualAdjustment',
  AwdServiceTransition: 'AwdServiceTransition', KohControlObservation: 'KohControlObservation',
} as const

/** GameplayFact 当前处理状态。 */
export const GameplayFactState = {
  Pending: 'Pending', Queued: 'Queued', Processing: 'Processing', Completed: 'Completed', PlatformFailed: 'PlatformFailed',
} as const

/** GameplayFact 当前结果。 */
export const GameplayFactResult = {
  Correct: 'Correct', Wrong: 'Wrong', Duplicate: 'Duplicate', AttemptsExhausted: 'AttemptsExhausted', Rejected: 'Rejected',
  Unlocked: 'Unlocked', Applied: 'Applied', ServiceUp: 'ServiceUp', ServiceDown: 'ServiceDown',
  Controlled: 'Controlled', Uncontrolled: 'Uncontrolled',
} as const

/** 排行榜数据范围(NoCTF.Application.Scoring.Leaderboard.LeaderboardDataScope)。 */
export const LeaderboardDataScope = { Live: 'Live', Frozen: 'Frozen', Hidden: 'Hidden' } as const

export function gameModeLabel(mode?: NoCtfapiEndpointsCompetitionsGameModeProtocol | string | number): string {
  return ({ Ctf: 'CTF', Awd: 'AWD', Awdp: 'AWDP', Koh: 'KoH' } as Record<string, string>)[String(mode)] ?? translate('未知')
}

export function competitionStatusLabel(status?: NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol | string | number): string {
  return ({ Draft: translate('草稿'), Visible: translate('即将发布'), Published: translate('即将开始'), Running: translate('进行中'), Paused: translate('已暂停'), Finished: translate('已结束') } as Record<string, string>)[String(status)] ?? translate('未知')
}

export function teamRegistrationStatusLabel(status?: NoCtfapiEndpointsTeamsTeamRegistrationStatusProtocol | string | number): string {
  return ({ Pending: translate('待审核'), Approved: translate('已通过'), Rejected: translate('已拒绝') } as Record<string, string>)[String(status)] ?? translate('未知')
}

export function runtimeStateLabel(state?: NoCtfapiEndpointsRuntimeRuntimeStateProtocol | string | number): string {
  return ({ Queued: translate('排队中'), Provisioning: translate('部署中'), Running: translate('运行中'), Stopping: translate('停止中'), Stopped: translate('已停止'), Failed: translate('失败') } as Record<string, string>)[String(state)] ?? translate('未知')
}

export function gameplayFactKindLabel(kind?: NoCtfapiEndpointsGameplayFactsGameplayFactKindProtocol | string | number): string {
  return ({ FlagAttempt: 'Flag', BreakAttempt: 'Break', FixAttempt: 'Fix', HintUnlock: translate('提示解锁'), ManualAdjustment: translate('人工调分'), AwdServiceTransition: translate('AWD 服务状态'), KohControlObservation: translate('KoH 控制观测') } as Record<string, string>)[String(kind)] ?? translate('比赛事实')
}

export function gameplayFactStateLabel(state?: NoCtfapiEndpointsGameplayFactsGameplayFactStateProtocol | string | number): string {
  return ({ Pending: translate('待评测'), Queued: translate('排队中'), Processing: translate('评测中'), Completed: translate('已完成'), PlatformFailed: translate('平台故障') } as Record<string, string>)[String(state)] ?? translate('未知')
}

export function gameplayFactResultLabel(result?: NoCtfapiEndpointsGameplayFactsGameplayFactResultProtocol | string | number | null): string {
  if (result === null || result === undefined) return translate('评测中')
  return (
    { Correct: translate("正确"), Wrong: translate("错误"), Duplicate: translate("重复"), AttemptsExhausted: translate("次数耗尽"), Rejected: translate("已拒绝"), Unlocked: translate("已解锁"), Applied: translate("已应用"), ServiceUp: translate("服务正常"), ServiceDown: translate("服务异常"), Controlled: translate("已控制"), Uncontrolled: translate("未控制") } as Record<string, string>
  )[String(result)] ?? translate('未知')
}

/** 评测是否仍在进行中(需要继续轮询)。 */
export function isGameplayFactPending(state?: NoCtfapiEndpointsGameplayFactsGameplayFactStateProtocol | string | number): boolean {
  return state === 'Pending' || state === 'Queued' || state === 'Processing' || state === 0 || state === 1 || state === 2
}

export function formatDateTime(value?: string | null): string {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '—'
  return date.toLocaleString(localeTag(), { hour12: false })
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
  if (days > 0) parts.push(translate('{count} 天', { count: days }))
  if (hours > 0) parts.push(translate('{count} 小时', { count: hours }))
  if (minutes > 0 && days === 0) parts.push(translate('{count} 分', { count: minutes }))
  if (days === 0 && hours === 0) parts.push(translate('{count} 秒', { count: seconds }))
  return parts.join(' ')
}

/** 竞赛动态文案(NoCTF.Domain.Competitions.Events.CompetitionEventKind)。 */
export function competitionEventText(
  event: NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse,
): string {
  const actor = event.actorDisplayName ?? translate('系统')
  const team = event.teamDisplayName ?? translate('某队伍')
  const challenge = event.challengeTitle ?? translate('某题目')
  const templates: Record<string, string> = {
    CompetitionCreated: translate('竞赛已创建'), CompetitionUpdated: translate('竞赛信息已更新'), CompetitionLifecycleChanged: translate('竞赛生命周期变更'),
    LeaderboardVisibilityChanged: translate('排行榜可见性已变更'), ChallengeCreated: translate('题目「{challenge}」已加入竞赛', { challenge }), ChallengeUpdated: translate('题目「{challenge}」已更新', { challenge }),
    ChallengePublished: translate('题目「{challenge}」已发布', { challenge }), ChallengeDescriptionUpdated: translate('题目「{challenge}」已更新描述', { challenge }), ChallengeUnpublished: translate('题目「{challenge}」已下线', { challenge }), HintPublished: translate('题目「{challenge}」发布了新提示', { challenge }),
    HintUnlocked: translate('队伍「{team}」解锁了题目「{challenge}」的提示', { team, challenge }), TeamRegistered: translate('队伍「{team}」报名参赛', { team }), TeamRegistrationChanged: translate('队伍「{team}」的报名状态已变更', { team }),
    TeamMemberJoined: translate('队伍「{team}」加入了新成员', { team }), TeamBanned: translate('队伍「{team}」被封禁', { team }), TeamUnbanned: translate('队伍「{team}」已解除封禁', { team }), TeamBanCorrectionPublished: translate('队伍「{team}」的封禁纠正已发布', { team }), SubmissionReceived: translate('队伍「{team}」提交了题目「{challenge}」', { team, challenge }),
    GameplayFactAdjudicated: translate('队伍「{team}」在题目「{challenge}」的提交已评测', { team, challenge }), FirstBloodAwarded: translate('队伍「{team}」拿下了题目「{challenge}」的一血', { team, challenge }), SecondBloodAwarded: translate('队伍「{team}」拿下了题目「{challenge}」的二血', { team, challenge }),
    ThirdBloodAwarded: translate('队伍「{team}」拿下了题目「{challenge}」的三血', { team, challenge }), RuntimeCreated: translate('队伍「{team}」申请了题目「{challenge}」的环境', { team, challenge }), RuntimeStateChanged: translate('队伍「{team}」的题目「{challenge}」环境状态已变更', { team, challenge }),
    AnnouncementPublished: translate('官方发布了一条公告'), QuestionOpened: translate('{actor}提出了咨询', { actor }), QuestionReplied: translate('咨询已有回复'), QuestionStatusChanged: translate('咨询状态已变更'), QuestionPublished: translate('一条咨询已公开'),
  }
  return templates[String(event.kind)] ?? translate('发生了一条竞赛动态')
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

function notificationContentId(
  notification: NoCtfapiEndpointsNotificationsNotificationResponse,
  key: string,
): string | null {
  const value = notificationContent(notification)[key]
  return typeof value === 'string' && value.length > 0 ? value : null
}

export function notificationThreadRootId(
  notification: NoCtfapiEndpointsNotificationsNotificationResponse,
): string | null {
  return notificationContentId(notification, 'questionId') ?? notification.id ?? null
}

export function notificationTargetPath(
  notification: NoCtfapiEndpointsNotificationsNotificationResponse,
): string {
  const isQuestionActivity = notification.kind === 'QuestionOpened'
    || notification.kind === 'Message'
    || notification.kind === 'QuestionStatusChanged'
  const competitionId = notificationCompetitionId(notification)
    ?? (isQuestionActivity ? notification.relatedId ?? null : null)
  if (!competitionId)
    return '/notifications'

  const challengeId = notificationContentId(notification, 'competitionChallengeId')
  const questionId = notificationContentId(notification, 'questionId')
    ?? (notification.kind === 'QuestionOpened' ? notification.id ?? null : null)
  const gameplayFactId = notificationContentId(notification, 'gameplayFactId')
  switch (notification.kind) {
    case 'BloodAwarded':
    case 'ChallengePublished':
    case 'HintPublished':
      return challengeId
        ? `/competitions/${competitionId}/challenges/${challengeId}`
        : `/competitions/${competitionId}/challenges`
    case 'QuestionOpened':
    case 'Message':
    case 'QuestionStatusChanged':
      return questionId
        ? `/competitions/${competitionId}/questions?question=${questionId}`
        : `/competitions/${competitionId}/questions`
    case 'TeamBanned':
    case 'TeamBanCorrected':
      return `/competitions/${competitionId}/my/team#ban-appeal`
    case 'CheatIncidentDetected':
      return gameplayFactId
        ? `/admin/competitions/${competitionId}/cheats?incident=${gameplayFactId}`
        : `/admin/competitions/${competitionId}/cheats`
    case 'GameplayFactAdjudicated':
      return `/competitions/${competitionId}/my/submissions`
    case 'RuntimeStateChanged':
      return challengeId
        ? `/competitions/${competitionId}/challenges/${challengeId}`
        : `/competitions/${competitionId}/challenges`
    case 'TeamRegistrationChanged':
      return `/competitions/${competitionId}/my/team`
    case 'CompetitionAnnouncement':
      return `/notifications?notification=${notification.id ?? ''}`
    default:
      return `/competitions/${competitionId}/events?kind=${notification.kind}`
  }
}

export function notificationTitle(
  notification: NoCtfapiEndpointsNotificationsNotificationResponse,
): string {
  const payload = notificationContent(notification)
  if (notification.kind === 'CompetitionAnnouncement')
    return typeof payload.title === 'string' ? payload.title : translate("赛事公告")
  if (notification.kind === 'QuestionOpened' || notification.kind === 'Message' || notification.kind === 'QuestionStatusChanged')
    return typeof payload.title === 'string' ? payload.title : notificationText(notification)
  return notificationText(notification)
}

export function notificationBody(
  notification: NoCtfapiEndpointsNotificationsNotificationResponse,
): string | null {
  const payload = notificationContent(notification)
  for (const key of ['body', 'reason', 'detail', 'message']) {
    const value = payload[key]
    if (typeof value === 'string' && value.trim().length > 0)
      return value
  }
  return null
}

export function notificationActionLabel(
  notification: NoCtfapiEndpointsNotificationsNotificationResponse,
): string {
  switch (notification.kind) {
    case 'BloodAwarded':
    case 'ChallengePublished':
    case 'HintPublished':
    case 'RuntimeStateChanged': return translate("查看题目")
    case 'QuestionOpened':
    case 'Message':
    case 'QuestionStatusChanged': return translate("查看咨询")
    case 'TeamBanned':
    case 'TeamBanCorrected': return translate("查看封禁与申诉")
    case 'CheatIncidentDetected': return translate("查看作弊事件")
    case 'GameplayFactAdjudicated': return translate("查看提交")
    case 'TeamRegistrationChanged': return translate("查看我的队伍")
    default: return translate("查看比赛动态")
  }
}

/** 通知文案(NoCTF.Domain.Notifications.NotificationKind),content 为松散 JSON。 */
export function notificationText(
  notification: NoCtfapiEndpointsNotificationsNotificationResponse,
): string {
  const payload = notificationContent(notification)
  const title = typeof payload.competitionTitle === 'string' ? `「${payload.competitionTitle}」` : ''
  const team = typeof payload.teamName === 'string' ? `「${payload.teamName}」` : ''
  const challenge = typeof payload.challengeTitle === 'string' ? `「${payload.challengeTitle}」` : ''
  const announcementTitle = typeof payload.title === 'string' ? payload.title : translate("赛事公告")
  const announcementBody = typeof payload.body === 'string' ? payload.body : ''
  const announcement = announcementBody
    ? `${announcementTitle}：${announcementBody}`
    : announcementTitle
  const templates: Record<string, string> = {
    CompetitionLifecycleChanged: translate('竞赛{title}的生命周期已变更', { title }), TeamRegistrationChanged: translate('你的队伍{team}报名状态已变更', { team }), GameplayFactAdjudicated: translate('你在{challenge}的提交已完成评测', { challenge }),
    RuntimeStateChanged: translate('{challenge}的运行环境状态已变更', { challenge }), StartGateFailed: translate('竞赛{title}启动检查未通过', { title }), ManagementFailure: translate('竞赛{title}出现管理侧故障', { title }),
    BloodAwarded: translate('恭喜，你在{challenge}拿下了血榜名次', { challenge }), ChallengePublished: translate('竞赛{title}发布了新题目{challenge}', { title, challenge }), HintPublished: translate('{challenge}发布了新提示', { challenge }),
    TeamBanned: translate('你的队伍{team}已被封禁', { team }), QuestionOpened: translate('竞赛{title}有新的咨询', { title }), Message: translate('你的咨询已有新回复'),
    QuestionStatusChanged: translate("你的咨询状态已变更"), CheatIncidentDetected: translate("检测到疑似作弊行为"), TeamBanCorrected: translate("队伍封禁已被纠正"), DataExportReady: translate("数据导出已就绪"), DataExportFailed: translate("数据导出失败"),
    UserAccountLifecycleChanged: translate("用户账号状态已变更"), CompetitionAnnouncement: announcement,
  }
  return templates[String(notification.kind)] ?? translate('你有一条新通知')
}
