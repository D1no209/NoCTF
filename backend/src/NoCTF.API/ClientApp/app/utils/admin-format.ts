import { toast } from 'vue-sonner'
import type {
  NoCtfapiEndpointsAdministrationChallengeBankSpecificationKindProtocol,
  NoCtfapiEndpointsAdministrationCheatIncidentsCheatIncidentStatusProtocol,
  NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol,
  NoCtfapiEndpointsCompetitionsGameModeProtocol,
  NoCtfapiEndpointsCompetitionsLeaderboardVisibilityProtocol,
  NoCtfapiEndpointsGameplayFactsGameplayFactKindProtocol,
  NoCtfapiEndpointsGameplayFactsGameplayFactResultProtocol,
  NoCtfapiEndpointsGameplayFactsGameplayFactStateProtocol,
  NoCtfapiEndpointsRuntimeRuntimeKindProtocol,
  NoCtfapiEndpointsRuntimeRuntimeProviderProtocol,
  NoCtfapiEndpointsRuntimeRuntimeStateProtocol,
  NoCtfapiEndpointsTeamsTeamBanAppealStatusProtocol,
  NoCtfapiEndpointsTeamsTeamBanSourceProtocol,
  NoCtfapiEndpointsTeamsTeamRegistrationStatusProtocol,
} from '../api'
import { parseApiError } from './api-error'
import { localeTag, translate } from './i18n'

/** Protocol enum label maps. HTTP enums are PascalCase strings. */

export const GameModeLabel = {
  Ctf: 'CTF', Awd: 'AWD', Awdp: 'AWDP', Koh: 'KoH',
} satisfies Record<NoCtfapiEndpointsCompetitionsGameModeProtocol, string>

export const CompetitionStatusLabel = {
  Draft: '草稿', Visible: '可见', Published: '已发布', Running: '进行中', Paused: '已暂停', Finished: '已结束',
} satisfies Record<NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol, string>

export const TeamRegistrationStatusLabel = {
  Pending: '待审批', Approved: '已通过', Rejected: '已拒绝',
} satisfies Record<NoCtfapiEndpointsTeamsTeamRegistrationStatusProtocol, string>

export const TeamBanSourceLabel = {
  ManualModeration: '人工封禁', CheatIncident: '作弊事件',
} satisfies Record<NoCtfapiEndpointsTeamsTeamBanSourceProtocol, string>

export const TeamBanAppealStatusLabel = {
  Submitted: '待裁决', Upheld: '已维持', Accepted: '已接受',
} satisfies Record<NoCtfapiEndpointsTeamsTeamBanAppealStatusProtocol, string>

export const GameplayFactKindLabel = {
  FlagAttempt: 'Flag', BreakAttempt: 'Break', FixAttempt: 'Fix', HintUnlock: '提示解锁',
  ManualAdjustment: '人工调分', AwdServiceTransition: 'AWD 服务状态', KohControlObservation: 'KoH 控制观测',
} satisfies Record<NoCtfapiEndpointsGameplayFactsGameplayFactKindProtocol, string>

export const GameplayFactStateLabel = {
  Pending: '待处理', Queued: '排队中', Processing: '评测中', Completed: '已完成', PlatformFailed: '平台失败',
} satisfies Record<NoCtfapiEndpointsGameplayFactsGameplayFactStateProtocol, string>

export const GameplayFactResultLabel = {
  Correct: '正确', Wrong: '错误', Duplicate: '重复', AttemptsExhausted: '次数耗尽', Rejected: '已拒绝',
  Unlocked: '已解锁', Applied: '已应用', ServiceUp: '服务正常', ServiceDown: '服务异常', Controlled: '已控制', Uncontrolled: '未控制',
} satisfies Record<NoCtfapiEndpointsGameplayFactsGameplayFactResultProtocol, string>

export const RuntimeKindLabel = {
  Container: '容器', Compose: 'Compose', OvaVm: '虚拟机',
} satisfies Record<NoCtfapiEndpointsRuntimeRuntimeKindProtocol, string>

export const RuntimeProviderLabel = {
  Docker: 'Docker', Kubernetes: 'Kubernetes', Libvirt: 'Libvirt',
} satisfies Record<NoCtfapiEndpointsRuntimeRuntimeProviderProtocol, string>

export const RuntimeStateLabel = {
  Queued: '排队中', Provisioning: '准备中', Running: '运行中', Stopping: '停止中', Stopped: '已停止', Failed: '失败',
} satisfies Record<NoCtfapiEndpointsRuntimeRuntimeStateProtocol, string>

export const CheatIncidentStatusLabel = {
  Pending: '待处理', Confirmed: '已确认', Dismissed: '已驳回', Superseded: '已取代', Corrected: '已纠正',
} satisfies Record<NoCtfapiEndpointsAdministrationCheatIncidentsCheatIncidentStatusProtocol, string>

export const LeaderboardVisibilityLabel = {
  Normal: '正常', Frozen: '冻结', Blackout: '遮蔽',
} satisfies Record<NoCtfapiEndpointsCompetitionsLeaderboardVisibilityProtocol, string>

export const SpecificationKindLabel = {
  Attachment: '附件', AwdRound: 'AWD 轮次', RuntimeDefinition: '运行时定义', RuntimeInstance: 'Runtime 实例', Hint: '提示',
} satisfies Record<NoCtfapiEndpointsAdministrationChallengeBankSpecificationKindProtocol, string>

export function enumLabel<T extends string>(
  map: Readonly<Record<T, string>>,
  value: T | null | undefined,
): string {
  if (value === null || value === undefined) return '—'
  return translate(map[value])
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

/** Show an administrative write failure without inventing transport-level conflict semantics. */
export function toastWriteError(error: unknown): void {
  toast.error(parseApiError(error).message)
}
