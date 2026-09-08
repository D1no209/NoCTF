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
  NoCtfapiEndpointsRuntimeRuntimeFailureCodeProtocol,
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
  Draft: "ui.draft", Visible: "ui.visible", Published: "ui.published", Running: "ui.running", Paused: "ui.suspended", Finished: "ui.finished",
} satisfies Record<NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol, string>

export const TeamRegistrationStatusLabel = {
  Pending: "ui.pendingApproval", Approved: "ui.passed", Rejected: "ui.rejected",
} satisfies Record<NoCtfapiEndpointsTeamsTeamRegistrationStatusProtocol, string>

export const TeamBanSourceLabel = {
  ManualModeration: "ui.manualBan", CheatIncident: "ui.cheatIncident",
} satisfies Record<NoCtfapiEndpointsTeamsTeamBanSourceProtocol, string>

export const TeamBanAppealStatusLabel = {
  Submitted: "ui.pendingDecision", Upheld: "ui.maintained", Accepted: "ui.accepted",
} satisfies Record<NoCtfapiEndpointsTeamsTeamBanAppealStatusProtocol, string>

export const GameplayFactKindLabel = {
  FlagAttempt: 'Flag', BreakAttempt: 'Break', FixAttempt: 'Fix', HintUnlock: "ui.promptToUnlock",
  ManualAdjustment: "ui.manualAdjustment", AwdServiceTransition: "ui.awdServiceStatus", KohControlObservation: "ui.kohControlObservation",
} satisfies Record<NoCtfapiEndpointsGameplayFactsGameplayFactKindProtocol, string>

export const GameplayFactStateLabel = {
  Pending: "ui.pending", Queued: "ui.queuing", Processing: "ui.underEvaluation", Completed: "ui.completed", PlatformFailed: "ui.platformFailed",
} satisfies Record<NoCtfapiEndpointsGameplayFactsGameplayFactStateProtocol, string>

export const GameplayFactResultLabel = {
  Correct: "ui.correct", Wrong: "ui.wrong", Duplicate: "ui.repeat", AttemptsExhausted: "ui.exhausted", Rejected: "ui.rejected",
  Unlocked: "ui.unlocked", Applied: "ui.applied", ServiceUp: "ui.serviceIsNormal", ServiceDown: "ui.serviceException", Controlled: "ui.controlled", Uncontrolled: "ui.uncontrolled",
} satisfies Record<NoCtfapiEndpointsGameplayFactsGameplayFactResultProtocol, string>

export const RuntimeKindLabel = {
  Container: "ui.container", Compose: 'Compose', OvaVm: "ui.virtualMachine",
} satisfies Record<NoCtfapiEndpointsRuntimeRuntimeKindProtocol, string>

export const RuntimeProviderLabel = {
  Docker: 'Docker', Kubernetes: 'Kubernetes', Libvirt: 'Libvirt',
} satisfies Record<NoCtfapiEndpointsRuntimeRuntimeProviderProtocol, string>

export const RuntimeStateLabel = {
  Queued: "ui.queuing", Provisioning: "ui.inPreparation", Running: "ui.running2", Stopping: "ui.stopping", Stopped: "ui.stopped", Failed: "ui.failed",
} satisfies Record<NoCtfapiEndpointsRuntimeRuntimeStateProtocol, string>

export const RuntimeFailureCodeLabel = {
  InvalidConfiguration: "ui.invalidRuntimeConfiguration",
  RunnerUnavailable: "ui.runnerUnavailable",
  ProviderUnavailable: "ui.runtimeProviderUnavailable",
  ProvisionTimeout: "ui.runtimeProvisioningTimedOut",
  ProviderRejected: "ui.runtimeProviderRejectedTheRequest",
  CleanupFailed: "ui.resourceCleanupFailed",
  UrlExpansionFailed: "ui.accessUrlExpansionFailed",
} satisfies Record<NoCtfapiEndpointsRuntimeRuntimeFailureCodeProtocol, string>

export const CheatIncidentStatusLabel = {
  Pending: "ui.pending", Confirmed: "ui.confirmed", Dismissed: "ui.dismissed", Superseded: "ui.superseded", Corrected: "ui.corrected",
} satisfies Record<NoCtfapiEndpointsAdministrationCheatIncidentsCheatIncidentStatusProtocol, string>

export const LeaderboardVisibilityLabel = {
  Normal: "ui.normal", Frozen: "ui.freeze", Blackout: "ui.cover2",
} satisfies Record<NoCtfapiEndpointsCompetitionsLeaderboardVisibilityProtocol, string>

export const SpecificationKindLabel = {
  Attachment: "ui.accessories", AwdRound: "ui.awdRounds", RuntimeDefinition: "ui.runtimeDefinition", RuntimeInstance: "ui.runtime2", Hint: "ui.hint",
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
