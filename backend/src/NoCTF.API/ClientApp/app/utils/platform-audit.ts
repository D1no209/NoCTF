import type {
  NoCtfapiEndpointsAdministrationPlatformPlatformAuditLogResponse,
  NoCtfapiEndpointsCompetitionsEventsCompetitionEventKindProtocol,
} from '~/api'
import { translate } from './i18n'

type AuditLog = NoCtfapiEndpointsAdministrationPlatformPlatformAuditLogResponse

const COMPETITION_STATUS_LABELS: Record<string, string> = {
  Draft: '草稿',
  Visible: '可见',
  Published: '已发布',
  Running: '进行中',
  Paused: '已暂停',
  Finished: '已结束',
}

const VISIBILITY_LABELS: Record<string, string> = {
  Normal: '正常',
  Frozen: '冻结',
  Blackout: '封榜',
}

const ACCOUNT_ACTION_LABELS: Record<string, string> = {
  Activated: '激活账户',
  Banned: '封禁账户',
  Disabled: '禁用账户',
  EmailVerified: '激活用户邮箱',
  EmailUnverified: '撤销用户邮箱激活',
  Anonymized: '匿名化账户',
  PhysicallyDeleted: '物理删除账户',
}

const LIFECYCLE_REASON_LABELS: Record<string, string> = {
  manual_make_visible: '公开竞赛',
  manual_publish: '发布竞赛',
  manual_start: '启动竞赛',
  manual_pause: '暂停竞赛',
  manual_resume: '恢复竞赛',
  manual_finish: '结束竞赛',
}

const EVENT_ACTION_LABELS: Partial<Record<NoCtfapiEndpointsCompetitionsEventsCompetitionEventKindProtocol, string>> = {
  CompetitionCreated: '创建竞赛',
  CompetitionUpdated: '更新竞赛',
  CompetitionDeleted: '删除竞赛',
  ChallengeCreated: '添加竞赛题目',
  ChallengeUpdated: '更新竞赛题目',
  ChallengePublished: '发布竞赛题目',
  ChallengeUnpublished: '下线竞赛题目',
  ChallengeDeleted: '删除竞赛题目',
  HintPublished: '发布题目提示',
  TeamRegistrationChanged: '审核队伍报名',
  TeamDeleted: '解散参赛队伍',
  TeamBanned: '封禁参赛队伍',
  TeamUnbanned: '解除队伍封禁',
  ProtectedGameplayFactValueAccessed: '查看受保护 Flag',
  CheatIncidentConfirmed: '确认作弊并封禁队伍',
  CheatIncidentDismissed: '驳回作弊事件',
  CheatIncidentSuperseded: '取代作弊事件处置',
  CheatIncidentCorrected: '纠正作弊事件处置',
  ProtectedCompetitionExportCreated: '创建受保护竞赛导出',
  TeamBanAppealUpheld: '维持队伍封禁',
  TeamBanAppealAccepted: '通过队伍申诉',
  TeamBanCorrectionPublished: '发布队伍封禁纠正',
  RuntimeForceTerminationRequested: '请求强制终止运行环境',
  RuntimeForceTerminationCompleted: '完成强制终止运行环境',
  RuntimeForceTerminationFailed: '强制终止运行环境失败',
  AnnouncementPublished: '发布竞赛公告',
  ChallengeDescriptionUpdated: '更新题目描述',
  TrackConfigurationUpdated: '更新赛道配置',
  TeamTrackChanged: '调整队伍赛道',
}

function withReason(action: string, reason: string | null | undefined): string {
  const normalized = reason?.trim()
  return normalized && !normalized.startsWith('manual_')
    ? translate('{action} · 原因：{reason}', { action: translate(action), reason: normalized })
    : translate(action)
}

function statusLabel(value: unknown): string {
  const key = value === null || value === undefined ? null : COMPETITION_STATUS_LABELS[String(value)]
  return key ? translate(key) : String(value ?? '-')
}

function visibilityLabel(value: unknown): string {
  const key = value === null || value === undefined ? null : VISIBILITY_LABELS[String(value)]
  return key ? translate(key) : String(value ?? '-')
}

export function platformAuditActionText(log: AuditLog): string {
  if (log.kind === 'CompetitionLifecycle') {
    const reasonAction = log.reason ? LIFECYCLE_REASON_LABELS[log.reason] : null
    if (reasonAction) return translate(reasonAction)
    if (log.fromCompetitionStatus !== null && log.fromCompetitionStatus !== undefined) {
      return translate('变更竞赛状态：{from} → {to}', {
        from: statusLabel(log.fromCompetitionStatus),
        to: statusLabel(log.toCompetitionStatus),
      })
    }
    return translate('变更竞赛生命周期')
  }

  if (log.kind === 'CompetitionLeaderboardVisibility') {
    return translate('调整排行榜可见性：{from} → {to}', {
      from: visibilityLabel(log.fromLeaderboardVisibility),
      to: visibilityLabel(log.toLeaderboardVisibility),
    })
  }

  if (log.kind === 'UserAccountLifecycle') {
    const action = log.userAccountAction ? ACCOUNT_ACTION_LABELS[String(log.userAccountAction)] : null
    return withReason(action ?? '变更账户状态', log.reason)
  }

  if (log.kind === 'CompetitionAdministration') {
    return withReason('强制级联删除竞赛', log.reason)
  }

  const action = log.competitionEventKind ? EVENT_ACTION_LABELS[log.competitionEventKind] : null
  return withReason(action ?? '执行竞赛管理操作', log.reason)
}
