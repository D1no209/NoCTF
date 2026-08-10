import { toast } from 'vue-sonner'

/** Protocol enum label maps. HTTP enums are PascalCase strings. */

export const GameModeLabel: Record<string, string> = {
  Ctf: 'CTF', Awd: 'AWD', Awdp: 'AWDP', Koh: 'KoH',
}

export const CompetitionStatusLabel: Record<string, string> = {
  Draft: '草稿', Visible: '可见', Published: '已发布', Running: '进行中', Paused: '已暂停', Finished: '已结束',
}

export const TeamRegistrationStatusLabel: Record<string, string> = {
  Pending: '待审批', Approved: '已通过', Rejected: '已拒绝',
}

export const TeamBanSourceLabel: Record<string, string> = {
  ManualModeration: '人工封禁', CheatIncident: '作弊事件',
}

export const TeamBanAppealStatusLabel: Record<string, string> = {
  Submitted: '待裁决', Upheld: '已维持', Accepted: '已接受',
}

export const GameplayFactKindLabel: Record<string, string> = {
  FlagAttempt: 'Flag', BreakAttempt: 'Break', FixAttempt: 'Fix', HintUnlock: '提示解锁',
  ManualAdjustment: '人工调分', AwdServiceTransition: 'AWD 服务状态', KohControlObservation: 'KoH 控制观测',
}

export const GameplayFactStateLabel: Record<string, string> = {
  Pending: '待处理', Queued: '排队中', Processing: '评测中', Completed: '已完成', PlatformFailed: '平台失败',
}

export const GameplayFactResultLabel: Record<string, string> = {
  Correct: '正确', Wrong: '错误', Duplicate: '重复', AttemptsExhausted: '次数耗尽', Rejected: '已拒绝',
  Unlocked: '已解锁', Applied: '已应用', ServiceUp: '服务正常', ServiceDown: '服务异常', Controlled: '已控制', Uncontrolled: '未控制',
}

export const RuntimeKindLabel: Record<string, string> = {
  Container: '容器', Compose: 'Compose', OvaVm: '虚拟机',
}

export const RuntimeProviderLabel: Record<string, string> = {
  Docker: 'Docker', Kubernetes: 'Kubernetes', Libvirt: 'Libvirt',
}

export const RuntimeStateLabel: Record<string, string> = {
  Queued: '排队中', Provisioning: '准备中', Running: '运行中', Stopping: '停止中', Stopped: '已停止', Failed: '失败',
}

export const CheatIncidentStatusLabel: Record<string, string> = {
  Pending: '待处理', Confirmed: '已确认', Dismissed: '已驳回', Superseded: '已取代', Corrected: '已纠正',
}

export const LeaderboardVisibilityLabel: Record<string, string> = {
  Normal: '正常', Frozen: '冻结', Blackout: '遮蔽',
}

export const DataExportStatusLabel: Record<string, string> = {
  Queued: '排队中', Processing: '处理中', Available: '可下载', Failed: '失败', Expired: '已过期',
}

export const SpecificationKindLabel: Record<string, string> = {
  Attachment: '附件', AwdRound: 'AWD 轮次', RuntimeDefinition: '运行时定义', Hint: '提示',
}

export function enumLabel(map: Record<string, string>, value: string | number | null | undefined): string {
  if (value === null || value === undefined) return '—'
  return map[String(value)] ?? `#${value}`
}

export function adminFormatDateTime(value: string | null | undefined): string {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return date.toLocaleString('zh-CN', { hour12: false })
}

/** Convert a datetime-local input value to an ISO string, or undefined when empty. */
export function localInputToIso(value: string): string | undefined {
  if (!value) return undefined
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? undefined : date.toISOString()
}

/** Convert an ISO string to a datetime-local input value (local timezone). */
export function isoToLocalInput(value: string | null | undefined): string {
  if (!value) return ''
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return ''
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`
}

/** Whether an SDK error payload looks like an optimistic-concurrency (409) conflict. */
export function isRevisionConflict(error: unknown): boolean {
  const e = parseApiError(error)
  if (e.status === 409) return true
  if (e.code && /revision/i.test(e.code)) return true
  return /revision|已被.*修改|conflict/i.test(e.message)
}

/**
 * Standard error handling for admin writes: 409 -> refresh + conflict hint,
 * anything else -> plain error toast.
 */
export function toastWriteError(error: unknown, refresh?: () => void | Promise<void>): void {
  if (isRevisionConflict(error)) {
    toast.error('数据已被他人修改,请刷新后重试')
    void refresh?.()
    return
  }
  toast.error(parseApiError(error).message)
}
