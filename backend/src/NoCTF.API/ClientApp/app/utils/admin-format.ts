import { toast } from './message-toast'
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
  Draft: "common.label.draft", Visible: "common.label.visible", Published: "administration.label.published", Running: "common.label.running", Paused: "common.label.suspended", Finished: "common.label.finished",
} satisfies Record<NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol, string>

export const TeamRegistrationStatusLabel = {
  Pending: "common.label.pendingApproval", Approved: "common.label.passed", Rejected: "common.label.rejected", Unregistered: "common.label.registered",
} satisfies Record<NoCtfapiEndpointsTeamsTeamRegistrationStatusProtocol, string>

export const TeamBanSourceLabel = {
  ManualModeration: "common.label.manualBan", CheatIncident: "common.label.cheatIncident",
} satisfies Record<NoCtfapiEndpointsTeamsTeamBanSourceProtocol, string>

export const TeamBanAppealStatusLabel = {
  Submitted: "common.label.pendingDecision", Upheld: "common.label.maintained", Accepted: "common.label.accepted",
} satisfies Record<NoCtfapiEndpointsTeamsTeamBanAppealStatusProtocol, string>

export const GameplayFactKindLabel = {
  FlagAttempt: 'Flag', BreakAttempt: 'Break', FixAttempt: 'Fix', HintUnlock: "common.label.promptUnlock",
  ManualAdjustment: "common.label.manualAdjustment", AwdServiceTransition: "common.label.awdServiceStatus", KohControlObservation: "common.label.kohControlObservation", AttachmentDownload: "cheats.label.attachmentDownload",
} satisfies Record<NoCtfapiEndpointsGameplayFactsGameplayFactKindProtocol, string>

export const GameplayFactStateLabel = {
  Pending: "administration.label.pending", Queued: "common.label.queuing", Processing: "common.label.underEvaluation", Completed: "common.label.completed", PlatformFailed: "common.error.platformFailed.adminFormat",
} satisfies Record<NoCtfapiEndpointsGameplayFactsGameplayFactStateProtocol, string>

export const GameplayFactResultLabel = {
  Correct: "common.label.correct", Wrong: "common.label.wrong", Duplicate: "common.label.repeat", AttemptsExhausted: "common.label.exhausted", Rejected: "common.label.rejected",
  Unlocked: "common.label.unlocked", Applied: "common.label.applied", ServiceUp: "common.label.serviceNormal", ServiceDown: "common.label.serviceException", Controlled: "common.label.controlled", Uncontrolled: "common.label.uncontrolled",
} satisfies Record<NoCtfapiEndpointsGameplayFactsGameplayFactResultProtocol, string>

export const RuntimeKindLabel = {
  Container: "runtime.label.container", OvaVm: "runtime.label.virtualMachine",
} satisfies Record<NoCtfapiEndpointsRuntimeRuntimeKindProtocol, string>

export const RuntimeProviderLabel = {
  Docker: 'Docker', Kubernetes: 'Kubernetes', Libvirt: 'Libvirt',
} satisfies Record<NoCtfapiEndpointsRuntimeRuntimeProviderProtocol, string>

export const RuntimeStateLabel = {
  Queued: "common.label.queuing", Provisioning: "runtime.label.preparation", Running: "common.label.running.adminFormat", Stopping: "common.label.stopping", Stopped: "common.label.stopped", Failed: "common.error.failed",
} satisfies Record<NoCtfapiEndpointsRuntimeRuntimeStateProtocol, string>

export const RuntimeFailureCodeLabel = {
  InvalidConfiguration: "common.error.runtimeConfigurationInvalid",
  RunnerUnavailable: "common.error.runnerUnavailable",
  ProviderUnavailable: "common.error.runtimeProviderUnavailable",
  ProvisionTimeout: "common.label.runtimeProvisioningTimedOut",
  ProviderRejected: "common.label.runtimeProviderRejected",
  CleanupFailed: "common.error.resourceCleanupFailed",
  UrlExpansionFailed: "common.error.accessUrlExpansionFailed",
} satisfies Record<NoCtfapiEndpointsRuntimeRuntimeFailureCodeProtocol, string>

export const CheatIncidentStatusLabel = {
  Pending: "administration.label.pending", Confirmed: "administration.label.confirmed", Dismissed: "common.label.dismissed", Superseded: "administration.label.superseded", Corrected: "administration.label.corrected",
} satisfies Record<NoCtfapiEndpointsAdministrationCheatIncidentsCheatIncidentStatusProtocol, string>

export const LeaderboardVisibilityLabel = {
  Normal: "administration.label.normal", Frozen: "common.label.freeze", Blackout: "common.label.cover",
} satisfies Record<NoCtfapiEndpointsCompetitionsLeaderboardVisibilityProtocol, string>

export const SpecificationKindLabel = {
  Attachment: "common.label.accessories", AwdRound: "common.label.awdRounds", RuntimeDefinition: "common.label.runtimeDefinition", RuntimeInstance: "common.label.runtime", Hint: "administration.label.hint",
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
  toast.error(parseApiError(error).displayMessage)
}
