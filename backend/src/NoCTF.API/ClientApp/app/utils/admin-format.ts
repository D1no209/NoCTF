import { toast } from 'vue-sonner'
import { localeTag, translate } from './i18n'

/** Protocol enum label maps. HTTP enums are PascalCase strings. */

export const GameModeLabel: Record<string, string> = {
  Ctf: 'CTF', Awd: 'AWD', Awdp: 'AWDP', Koh: 'KoH',
}

export const CompetitionStatusLabel: Record<string, string> = {
  Draft: translate("草稿"), Visible: translate("可见"), Published: translate("已发布"), Running: translate("进行中"), Paused: translate("已暂停"), Finished: translate("已结束"),
}

export const TeamRegistrationStatusLabel: Record<string, string> = {
  Pending: translate("待审批"), Approved: translate("已通过"), Rejected: translate("已拒绝"),
}

export const TeamBanSourceLabel: Record<string, string> = {
  ManualModeration: translate("人工封禁"), CheatIncident: translate("作弊事件"),
}

export const TeamBanAppealStatusLabel: Record<string, string> = {
  Submitted: translate("待裁决"), Upheld: translate("已维持"), Accepted: translate("已接受"),
}

export const GameplayFactKindLabel: Record<string, string> = {
  FlagAttempt: 'Flag', BreakAttempt: 'Break', FixAttempt: 'Fix', HintUnlock: translate("提示解锁"),
  ManualAdjustment: translate("人工调分"), AwdServiceTransition: translate("AWD 服务状态"), KohControlObservation: translate("KoH 控制观测"),
}

export const GameplayFactStateLabel: Record<string, string> = {
  Pending: translate("待处理"), Queued: translate("排队中"), Processing: translate("评测中"), Completed: translate("已完成"), PlatformFailed: translate("平台失败"),
}

export const GameplayFactResultLabel: Record<string, string> = {
  Correct: translate("正确"), Wrong: translate("错误"), Duplicate: translate("重复"), AttemptsExhausted: translate("次数耗尽"), Rejected: translate("已拒绝"),
  Unlocked: translate("已解锁"), Applied: translate("已应用"), ServiceUp: translate("服务正常"), ServiceDown: translate("服务异常"), Controlled: translate("已控制"), Uncontrolled: translate("未控制"),
}

export const RuntimeKindLabel: Record<string, string> = {
  Container: translate("容器"), Compose: 'Compose', OvaVm: translate("虚拟机"),
}

export const RuntimeProviderLabel: Record<string, string> = {
  Docker: 'Docker', Kubernetes: 'Kubernetes', Libvirt: 'Libvirt',
}

export const RuntimeStateLabel: Record<string, string> = {
  Queued: translate("排队中"), Provisioning: translate("准备中"), Running: translate("运行中"), Stopping: translate("停止中"), Stopped: translate("已停止"), Failed: translate("失败"),
}

export const CheatIncidentStatusLabel: Record<string, string> = {
  Pending: translate("待处理"), Confirmed: translate("已确认"), Dismissed: translate("已驳回"), Superseded: translate("已取代"), Corrected: translate("已纠正"),
}

export const LeaderboardVisibilityLabel: Record<string, string> = {
  Normal: translate("正常"), Frozen: translate("冻结"), Blackout: translate("遮蔽"),
}

export const DataExportStatusLabel: Record<string, string> = {
  Queued: translate("排队中"), Processing: translate("处理中"), Available: translate("可下载"), Failed: translate("失败"), Expired: translate("已过期"),
}

export const SpecificationKindLabel: Record<string, string> = {
  Attachment: translate("附件"), AwdRound: translate("AWD 轮次"), RuntimeDefinition: translate("运行时定义"), Hint: translate("提示"),
}

export function enumLabel(map: Record<string, string>, value: string | number | null | undefined): string {
  if (value === null || value === undefined) return '—'
  return map[String(value)] ?? `#${value}`
}

export function adminFormatDateTime(value: string | null | undefined): string {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return date.toLocaleString(localeTag(), { hour12: false })
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
    toast.error(translate("数据已被他人修改,请刷新后重试"))
    void refresh?.()
    return
  }
  toast.error(parseApiError(error).message)
}
