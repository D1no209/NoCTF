import { toast } from 'vue-sonner'

/** Numeric enum label maps — mirror the C# enum member order in NoCTF.Domain. */

export const GameModeLabel: Record<number, string> = {
  0: 'CTF',
  1: 'AWD',
  2: 'AWDP',
  3: 'KoH',
}

export const CompetitionStatusLabel: Record<number, string> = {
  0: '草稿',
  1: '可见',
  2: '已发布',
  3: '进行中',
  4: '已暂停',
  5: '已结束',
}

export const TeamRegistrationStatusLabel: Record<number, string> = {
  0: '待审批',
  1: '已通过',
  2: '已拒绝',
}

export const TeamBanSourceLabel: Record<number, string> = {
  0: '人工封禁',
  1: '作弊事件',
}

export const TeamBanAppealStatusLabel: Record<number, string> = {
  0: '待裁决',
  1: '已维持',
  2: '已接受',
}

export const SubmissionKindLabel: Record<number, string> = {
  0: 'Flag',
  1: 'Break',
  2: 'Fix',
}

export const SubmissionEvaluationStateLabel: Record<number, string> = {
  0: '待处理',
  1: '排队中',
  2: '评测中',
  3: '已完成',
  4: '平台失败',
}

export const ScoringResultLabel: Record<number, string> = {
  0: '正确',
  1: '错误',
  2: '重复',
  3: '次数耗尽',
  4: '平台失败',
  5: '已拒绝',
}

export const RuntimeKindLabel: Record<number, string> = {
  0: '容器',
  1: 'Compose',
  2: '虚拟机',
}

export const RuntimeProviderLabel: Record<number, string> = {
  0: 'Docker',
  1: 'Kubernetes',
  2: 'Libvirt',
}

export const RuntimeStateLabel: Record<number, string> = {
  0: '排队中',
  1: '准备中',
  2: '运行中',
  3: '停止中',
  4: '已停止',
  5: '失败',
}

export const CheatIncidentStatusLabel: Record<number, string> = {
  0: '待处理',
  1: '已确认',
  2: '已驳回',
  3: '已取代',
  4: '已纠正',
}

export const LeaderboardVisibilityLabel: Record<number, string> = {
  0: '正常',
  1: '冻结',
  2: '遮蔽',
}

export const DataExportStatusLabel: Record<number, string> = {
  0: '排队中',
  1: '处理中',
  2: '可下载',
  3: '失败',
  4: '已过期',
}

export const SpecificationKindLabel: Record<number, string> = {
  0: '附件',
  1: 'AWD 轮次',
  2: '运行时定义',
  3: '提示',
}

export function enumLabel(map: Record<number, string>, value: number | null | undefined): string {
  if (value === null || value === undefined) return '—'
  return map[value] ?? `#${value}`
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
