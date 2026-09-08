import type {
  NoCtfapiEndpointsAdministrationPlatformPlatformAuditLogResponse,
  NoCtfapiEndpointsCompetitionsEventsCompetitionEventKindProtocol,
} from '../api'
import { translate } from './i18n'

type AuditLog = NoCtfapiEndpointsAdministrationPlatformPlatformAuditLogResponse

const COMPETITION_STATUS_LABELS: Record<string, string> = {
  Draft: "ui.draft",
  Visible: "ui.visible",
  Published: "ui.published",
  Running: "ui.running",
  Paused: "ui.suspended",
  Finished: "ui.finished",
}

const VISIBILITY_LABELS: Record<string, string> = {
  Normal: "ui.normal",
  Frozen: "ui.freeze",
  Blackout: "ui.banTheList",
}

const ACCOUNT_ACTION_LABELS: Record<string, string> = {
  Activated: "ui.activateAccount",
  Banned: "ui.banAccount",
  Disabled: "ui.disableAccount",
  EmailVerified: "ui.activateUserEmail",
  EmailUnverified: "ui.revokeUserEmailActivation",
  Anonymized: "ui.anonymizeAccount",
  PhysicallyDeleted: "ui.permanentlyDeleteAccount",
}

const LIFECYCLE_REASON_LABELS: Record<string, string> = {
  manual_make_visible: "ui.makeCompetitionVisible",
  manual_publish: "ui.postAContest",
  manual_start: "ui.startCompetition",
  manual_pause: "ui.pauseCompetition",
  manual_resume: "ui.resumeCompetition",
  manual_finish: "ui.finishCompetition",
}

const EVENT_ACTION_LABELS: Partial<Record<NoCtfapiEndpointsCompetitionsEventsCompetitionEventKindProtocol, string>> = {
  CompetitionCreated: "ui.createContest",
  CompetitionUpdated: "ui.updateCompetition",
  CompetitionDeleted: "ui.deleteContest",
  ChallengeCreated: "ui.addCompetitionChallenge",
  ChallengeUpdated: "ui.updateCompetitionChallenge",
  ChallengePublished: "ui.publishCompetitionChallenge",
  ChallengeUnpublished: "ui.unpublishCompetitionChallenge",
  ChallengeDeleted: "ui.deleteCompetitionChallenge",
  HintPublished: "ui.publishChallengeHint",
  TeamRegistrationChanged: "ui.reviewTeamRegistration",
  TeamDeleted: "ui.disbandCompetitionTeam",
  TeamBanned: "ui.banCompetitionTeam",
  TeamUnbanned: "ui.unbanCompetitionTeam",
  ProtectedGameplayFactValueAccessed: "ui.viewProtectedFlag",
  GameplayFactPatchDownloaded: "ui.downloadSubmissionPatch",
  CheatIncidentConfirmed: "ui.confirmCheatingAndBanTeam",
  CheatIncidentDismissed: "ui.dismissCheatingIncident",
  CheatIncidentSuperseded: "ui.supersedeCheatIncidentResolution",
  CheatIncidentCorrected: "ui.correctCheatIncidentResolution",
  CompetitionArchiveExported: "ui.exportCompetitionArchive",
  TeamBanAppealUpheld: "ui.upholdTeamBan",
  TeamBanAppealAccepted: "ui.acceptTeamAppeal",
  TeamBanCorrectionPublished: "ui.publishTeamBanCorrection",
  RuntimeForceTerminationRequested: "ui.requestForcedRuntimeTermination",
  RuntimeForceTerminationCompleted: "ui.completeForcedRuntimeTermination",
  RuntimeForceTerminationFailed: "ui.forcedRuntimeTerminationFailed",
  AnnouncementPublished: "ui.publishCompetitionAnnouncement",
  ChallengeDescriptionUpdated: "ui.updateChallengeDescription",
  TrackConfigurationUpdated: "ui.updateTrackConfiguration",
  TeamTrackChanged: "ui.changeTeamTrack",
}

function withReason(action: string, reason: string | null | undefined): string {
  const normalized = reason?.trim()
  return normalized && !normalized.startsWith('manual_')
    ? translate("ui.reason2", { action: translate(action), reason: normalized })
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
      return translate("ui.changeCompetitionStatus", {
        from: statusLabel(log.fromCompetitionStatus),
        to: statusLabel(log.toCompetitionStatus),
      })
    }
    return translate("ui.changeCompetitionLifecycle")
  }

  if (log.kind === 'CompetitionLeaderboardVisibility') {
    return translate("ui.changeLeaderboardVisibility", {
      from: visibilityLabel(log.fromLeaderboardVisibility),
      to: visibilityLabel(log.toLeaderboardVisibility),
    })
  }

  if (log.kind === 'UserAccountLifecycle') {
    const action = log.userAccountAction ? ACCOUNT_ACTION_LABELS[String(log.userAccountAction)] : null
    return withReason(action ?? "ui.changeAccountStatus", log.reason)
  }

  if (log.kind === 'CompetitionAdministration') {
    return withReason("ui.forceDeleteCompetition", log.reason)
  }

  if (log.kind === 'PlatformAdministration') {
    return log.platformAdministrationAction === 'AuditArchiveExported'
      ? translate("ui.exportPlatformAuditArchive")
      : translate("ui.performPlatformAdministrationAction")
  }

  const action = log.competitionEventKind ? EVENT_ACTION_LABELS[log.competitionEventKind] : null
  return withReason(action ?? "ui.performCompetitionAdministrationAction", log.reason)
}
