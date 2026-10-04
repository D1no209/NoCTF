import { toast } from './message-toast'
import type { NoCTFAPIEndpointsAdministrationChallengeBankSpecificationKindProtocol, NoCTFAPIEndpointsAdministrationCheatIncidentsCheatIncidentStatusProtocol, NoCTFAPIEndpointsCompetitionsCompetitionStatusProtocol, NoCTFAPIEndpointsCompetitionsGameModeProtocol, NoCTFAPIEndpointsCompetitionsLeaderboardVisibilityProtocol, NoCTFAPIEndpointsGameplayFactsGameplayFactKindProtocol, NoCTFAPIEndpointsGameplayFactsGameplayFactResultProtocol, NoCTFAPIEndpointsGameplayFactsGameplayFactStateProtocol, NoCTFAPIEndpointsRuntimeRuntimeKindProtocol, NoCTFAPIEndpointsRuntimeRuntimeFailureCodeProtocol, NoCTFAPIEndpointsRuntimeRuntimeProviderProtocol, NoCTFAPIEndpointsRuntimeRuntimeStateProtocol, NoCTFAPIEndpointsTeamsTeamBanAppealStatusProtocol, NoCTFAPIEndpointsTeamsTeamBanSourceProtocol, NoCTFAPIEndpointsTeamsTeamRegistrationStatusProtocol } from '../api/models'
import { parseApiError } from './api-error'
import { localeTag, translate } from './i18n'

/** Protocol enum label maps. HTTP enums are PascalCase strings. */

export const GameModeLabel = {
  Ctf: 'CTF', Awd: 'AWD', Awdp: 'AWDP', Koh: 'KoH',
} satisfies Record<NoCTFAPIEndpointsCompetitionsGameModeProtocol, string>

export const CompetitionStatusLabel = {
  Draft: "common.label.draft", Visible: "common.label.visible", Published: "administration.label.published", Running: "common.label.running", Paused: "common.label.suspended", Finished: "common.label.finished",
} satisfies Record<NoCTFAPIEndpointsCompetitionsCompetitionStatusProtocol, string>

export const TeamRegistrationStatusLabel = {
  Pending: "common.label.pendingApproval", Approved: "common.label.passed", Rejected: "common.label.rejected", Unregistered: "common.label.registered",
} satisfies Record<NoCTFAPIEndpointsTeamsTeamRegistrationStatusProtocol, string>

export const TeamBanSourceLabel = {
  ManualModeration: "common.label.manualBan", CheatIncident: "common.label.cheatIncident",
} satisfies Record<NoCTFAPIEndpointsTeamsTeamBanSourceProtocol, string>

export const TeamBanAppealStatusLabel = {
  Submitted: "common.label.pendingDecision", Upheld: "common.label.maintained", Accepted: "common.label.accepted",
} satisfies Record<NoCTFAPIEndpointsTeamsTeamBanAppealStatusProtocol, string>

export const GameplayFactKindLabel = {
  FlagAttempt: 'Flag', BreakAttempt: 'Break', FixAttempt: 'Fix', HintUnlock: "common.label.promptUnlock",
  ManualAdjustment: "common.label.manualAdjustment", AwdServiceTransition: "common.label.awdServiceStatus", KohControlObservation: "common.label.kohControlObservation",
} satisfies Record<NoCTFAPIEndpointsGameplayFactsGameplayFactKindProtocol, string>

export const GameplayFactStateLabel = {
  Pending: "administration.label.pending", Queued: "common.label.queuing", Processing: "common.label.underEvaluation", Completed: "common.label.completed", PlatformFailed: "common.error.platformFailed.adminFormat",
} satisfies Record<NoCTFAPIEndpointsGameplayFactsGameplayFactStateProtocol, string>

export const GameplayFactResultLabel = {
  Correct: "common.label.correct", Wrong: "common.label.wrong", Duplicate: "common.label.repeat", AttemptsExhausted: "common.label.exhausted", Rejected: "common.label.rejected",
  Unlocked: "common.label.unlocked", Applied: "common.label.applied", ServiceUp: "common.label.serviceNormal", ServiceDown: "common.label.serviceException", Controlled: "common.label.controlled", Uncontrolled: "common.label.uncontrolled",
} satisfies Record<NoCTFAPIEndpointsGameplayFactsGameplayFactResultProtocol, string>

export const RuntimeKindLabel = {
  Container: "runtime.label.container", OvaVm: "runtime.label.virtualMachine",
} satisfies Record<NoCTFAPIEndpointsRuntimeRuntimeKindProtocol, string>

export const RuntimeProviderLabel = {
  Docker: 'Docker', Kubernetes: 'Kubernetes', Libvirt: 'Libvirt',
} satisfies Record<NoCTFAPIEndpointsRuntimeRuntimeProviderProtocol, string>

export const RuntimeStateLabel = {
  Queued: "common.label.queuing", Provisioning: "runtime.label.preparation", Running: "common.label.running.adminFormat", Stopping: "common.label.stopping", Stopped: "common.label.stopped", Failed: "common.error.failed",
} satisfies Record<NoCTFAPIEndpointsRuntimeRuntimeStateProtocol, string>

export const RuntimeFailureCodeLabel = {
  InvalidConfiguration: "common.error.runtimeConfigurationInvalid",
  RunnerUnavailable: "common.error.runnerUnavailable",
  ProviderUnavailable: "common.error.runtimeProviderUnavailable",
  ProvisionTimeout: "common.label.runtimeProvisioningTimedOut",
  ProviderRejected: "common.label.runtimeProviderRejected",
  CleanupFailed: "common.error.resourceCleanupFailed",
  UrlExpansionFailed: "common.error.accessUrlExpansionFailed",
} satisfies Record<NoCTFAPIEndpointsRuntimeRuntimeFailureCodeProtocol, string>

export const CheatIncidentStatusLabel = {
  Pending: "administration.label.pending", Confirmed: "administration.label.confirmed", Dismissed: "common.label.dismissed", Superseded: "administration.label.superseded", Corrected: "administration.label.corrected",
} satisfies Record<NoCTFAPIEndpointsAdministrationCheatIncidentsCheatIncidentStatusProtocol, string>

export const LeaderboardVisibilityLabel = {
  Normal: "administration.label.normal", Frozen: "common.label.freeze", Blackout: "common.label.cover",
} satisfies Record<NoCTFAPIEndpointsCompetitionsLeaderboardVisibilityProtocol, string>

export const SpecificationKindLabel = {
  Attachment: "common.label.accessories", AwdRound: "common.label.awdRounds", RuntimeDefinition: "common.label.runtimeDefinition", RuntimeInstance: "common.label.runtime", Hint: "administration.label.hint",
} satisfies Record<NoCTFAPIEndpointsAdministrationChallengeBankSpecificationKindProtocol, string>

export function enumLabel<T extends string>(
  map: Readonly<Record<T, string>>,
  value: T | null | undefined,
): string {
  if (value === null || value === undefined) return '—'
  return translate(map[value])
}

export function adminFormatDateTime(value: string | Date | null | undefined): string {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return String(value)
  return date.toLocaleString(localeTag(), { hour12: false })
}

/** Convert a datetime-local input value to an ISO string, or undefined when empty. */
export function localInputToIso(value: string): string | null | undefined {
  if (!value) return undefined
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? undefined : date.toISOString()
}

/** Convert an ISO string to a datetime-local input value (local timezone). */
export function isoToLocalInput(value: string | Date | null | undefined): string {
  if (!value) return ''
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return ''
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`
}

/** Show an administrative write failure without inventing transport-level conflict semantics. */
export function toastWriteError(error: unknown): void {
  toast.error(parseApiError(error).displayMessage)
}
