import type { InjectionKey, Ref } from 'vue'
import type {
  NoCtfapiEndpointsCompetitionsCompetitionResponse,
  NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse,
  NoCtfapiEndpointsNotificationsNotificationResponse,
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
export const GameMode = { Ctf: 0, Awd: 1, Awdp: 2, Koh: 3 } as const

/** 竞赛生命周期(NoCTF.Domain.Competitions.CompetitionStatus)。 */
export const CompetitionStatus = {
  Draft: 0,
  Visible: 1,
  Published: 2,
  Running: 3,
  Paused: 4,
  Finished: 5,
} as const

/** 队伍报名状态(NoCTF.Domain.Teams.TeamRegistrationStatus)。 */
export const TeamRegistrationStatus = { Pending: 0, Approved: 1, Rejected: 2 } as const

/** 运行时状态(NoCTF.Domain.Runtime.RuntimeState)。 */
export const RuntimeState = {
  Queued: 0,
  Provisioning: 1,
  Running: 2,
  Stopping: 3,
  Stopped: 4,
  Failed: 5,
} as const

/** 提交类型(NoCTF.Domain.Submissions.SubmissionKind)。 */
export const SubmissionKind = { Flag: 0, Break: 1, Fix: 2 } as const

/** 提交评测状态(NoCTF.Domain.Submissions.SubmissionEvaluationState)。 */
export const EvaluationState = {
  Pending: 0,
  Queued: 1,
  Processing: 2,
  Completed: 3,
  PlatformFailed: 4,
} as const

/** 评测结果(NoCTF.Domain.Submissions.ScoringResult)。 */
export const ScoringResult = {
  Correct: 0,
  Wrong: 1,
  Duplicate: 2,
  AttemptsExhausted: 3,
  PlatformFailed: 4,
  Rejected: 5,
} as const

/** 排行榜数据范围(NoCTF.Application.Scoring.Leaderboard.LeaderboardDataScope)。 */
export const LeaderboardDataScope = { Live: 0, Frozen: 1, Hidden: 2 } as const

export function gameModeLabel(mode?: number): string {
  return (['CTF', 'AWD', 'AWDP', 'KoH'] as const)[mode ?? -1] ?? '未知'
}

export function competitionStatusLabel(status?: number): string {
  return (
    {
      0: '草稿',
      1: '即将发布',
      2: '即将开始',
      3: '进行中',
      4: '已暂停',
      5: '已结束',
    } as Record<number, string>
  )[status ?? -1] ?? '未知'
}

export function teamRegistrationStatusLabel(status?: number): string {
  return ({ 0: '待审核', 1: '已通过', 2: '已拒绝' } as Record<number, string>)[status ?? -1] ?? '未知'
}

export function runtimeStateLabel(state?: number): string {
  return (
    {
      0: '排队中',
      1: '部署中',
      2: '运行中',
      3: '停止中',
      4: '已停止',
      5: '失败',
    } as Record<number, string>
  )[state ?? -1] ?? '未知'
}

export function submissionKindLabel(kind?: number): string {
  return ({ 0: 'Flag', 1: 'Break', 2: 'Fix' } as Record<number, string>)[kind ?? -1] ?? '提交'
}

export function evaluationStateLabel(state?: number): string {
  return (
    {
      0: '待评测',
      1: '排队中',
      2: '评测中',
      3: '已完成',
      4: '平台故障',
    } as Record<number, string>
  )[state ?? -1] ?? '未知'
}

export function scoringResultLabel(result?: number | null): string {
  if (result === null || result === undefined) return '评测中'
  return (
    {
      0: '正确',
      1: '错误',
      2: '重复提交',
      3: '次数耗尽',
      4: '平台故障',
      5: '已拒绝',
    } as Record<number, string>
  )[result] ?? '未知'
}

/** 评测是否仍在进行中(需要继续轮询)。 */
export function isEvaluationPending(state?: number): boolean {
  return state === 0 || state === 1 || state === 2
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
  const templates: Record<number, string> = {
    0: '竞赛已创建',
    1: '竞赛信息已更新',
    3: '竞赛生命周期变更',
    4: '排行榜可见性已变更',
    5: `${challenge}已加入竞赛`,
    6: `${challenge}已更新`,
    7: `${challenge}已发布`,
    8: `${challenge}已下线`,
    10: `${challenge}发布了新提示`,
    11: `${team}解锁了${challenge}的提示`,
    12: `${team}报名参赛`,
    13: `${team}的报名状态已变更`,
    16: `${team}加入了新成员`,
    19: `${team}被封禁`,
    20: `${team}已解除封禁`,
    21: `${team}提交了${challenge}`,
    22: `${team}在${challenge}的提交已评测`,
    24: `${team}拿下了${challenge}的一血`,
    25: `${team}拿下了${challenge}的二血`,
    26: `${team}拿下了${challenge}的三血`,
    27: `${team}申请了${challenge}的环境`,
    28: `${team}的${challenge}环境状态已变更`,
    32: '官方发布了一条公告',
    33: `${actor}提出了咨询`,
    34: `咨询已有回复`,
    35: '咨询状态已变更',
    36: '一条咨询已公开',
  }
  return templates[event.kind ?? -1] ?? '发生了一条竞赛动态'
}

/** 通知文案(NoCTF.Domain.Notifications.NotificationKind),payload 为松散 JSON。 */
export function notificationText(
  notification: NoCtfapiEndpointsNotificationsNotificationResponse,
): string {
  const payload = (notification.payload ?? {}) as Record<string, unknown>
  const title = typeof payload.competitionTitle === 'string' ? `「${payload.competitionTitle}」` : ''
  const team = typeof payload.teamName === 'string' ? `「${payload.teamName}」` : ''
  const challenge = typeof payload.challengeTitle === 'string' ? `「${payload.challengeTitle}」` : ''
  const templates: Record<number, string> = {
    0: `竞赛${title}的生命周期已变更`,
    1: `你的队伍${team}报名状态已变更`,
    2: `你在${challenge}的提交已完成评测`,
    3: `${challenge}的运行环境状态已变更`,
    4: `竞赛${title}启动检查未通过`,
    5: `竞赛${title}出现管理侧故障`,
    6: `恭喜,你在${challenge}拿下了血榜名次`,
    7: `竞赛${title}发布了新题目${challenge}`,
    8: `${challenge}发布了新提示`,
    9: `你的队伍${team}已被封禁`,
    10: `竞赛${title}有新的咨询`,
    11: `你的咨询已有新回复`,
    12: '你的咨询状态已变更',
    13: '检测到疑似作弊行为',
    14: '队伍封禁已被纠正',
    15: '数据导出已就绪',
    16: '数据导出失败',
  }
  return templates[notification.kind ?? -1] ?? '你有一条新通知'
}
