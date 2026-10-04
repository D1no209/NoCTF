import type { NoCTFAPIEndpointsAdministrationPlatformPlatformAuditLogResponse, NoCTFAPIEndpointsCompetitionsEventsCompetitionEventKindProtocol } from '../api/models'
import { translate } from './i18n'

type AuditLog = NoCTFAPIEndpointsAdministrationPlatformPlatformAuditLogResponse

const COMPETITION_STATUS_LABELS: Record<string, string> = {
  Draft: "common.label.draft",
  Visible: "common.label.visible",
  Published: "administration.label.published",
  Running: "common.label.running",
  Paused: "common.label.suspended",
  Finished: "common.label.finished",
}

const VISIBILITY_LABELS: Record<string, string> = {
  Normal: "administration.label.normal",
  Frozen: "common.label.freeze",
  Blackout: "common.label.banList",
}

const ACCOUNT_ACTION_LABELS: Record<string, string> = {
  Activated: "common.label.activateAccount",
  Banned: "common.label.banAccount",
  Disabled: "common.label.disableAccount",
  EmailVerified: "common.label.activateUserEmail",
  EmailUnverified: "common.label.revokeUserEmailActivation",
  Anonymized: "common.label.anonymizeAccount",
  PhysicallyDeleted: "common.label.permanentlyDeleteAccount",
}

const LIFECYCLE_REASON_LABELS: Record<string, string> = {
  manual_make_visible: "common.label.makeCompetitionVisible",
  manual_publish: "administration.label.postContest",
  manual_start: "common.label.startCompetition",
  manual_pause: "common.label.pauseCompetition",
  manual_resume: "common.label.resumeCompetition",
  manual_finish: "common.label.finishCompetition",
}

const EVENT_ACTION_LABELS: Partial<Record<NoCTFAPIEndpointsCompetitionsEventsCompetitionEventKindProtocol, string>> = {
  CompetitionCreated: "competitions.label.createContest",
  CompetitionUpdated: "common.label.updateCompetition",
  CompetitionDeleted: "administration.label.deleteContest",
  ChallengeCreated: "common.label.addCompetitionChallenge",
  ChallengeUpdated: "common.label.updateCompetitionChallenge",
  ChallengePublished: "administration.label.publishCompetitionChallenge",
  ChallengeUnpublished: "administration.label.unpublishCompetitionChallenge",
  ChallengeDeleted: "common.label.deleteCompetitionChallenge",
  HintPublished: "common.label.publishChallengeHint",
  TeamRegistrationChanged: "common.label.reviewTeamRegistration",
  TeamDeleted: "common.label.disbandCompetitionTeam",
  TeamBanned: "common.label.banCompetitionTeam",
  TeamUnbanned: "common.label.unbanCompetitionTeam",
  ProtectedGameplayFactValueAccessed: "common.label.viewProtectedFlag",
  GameplayFactPatchDownloaded: "common.label.downloadSubmissionPatch",
  CheatIncidentConfirmed: "common.platformAudit.label.confirmCheatingBanTeam",
  CheatIncidentDismissed: "common.label.dismissCheatingIncident",
  CheatIncidentSuperseded: "common.label.supersedeCheatIncidentResolution",
  CheatIncidentCorrected: "common.label.correctCheatIncidentResolution",
  CompetitionArchiveExported: "common.label.exportCompetitionArchive",
  TeamBanAppealUpheld: "common.label.upholdTeamBan",
  TeamBanAppealAccepted: "common.label.acceptTeamAppeal",
  TeamBanCorrectionPublished: "common.label.publishTeamBanCorrection",
  RuntimeForceTerminationRequested: "common.label.forcedRuntimeTermination",
  RuntimeForceTerminationCompleted: "common.label.completeForcedRuntimeTermination",
  RuntimeForceTerminationFailed: "common.error.forcedRuntimeTerminationFailed",
  AnnouncementPublished: "common.label.publishCompetitionAnnouncement",
  ChallengeDescriptionUpdated: "common.label.updateChallengeDescription",
  TrackConfigurationUpdated: "common.label.updateTrackConfiguration",
  TrackRegistrationPolicyUpdated: "common.label.updateTrackRegistrationPolicy",
  TeamTrackChanged: "common.label.changeTeamTrack",
}

function withReason(action: string, reason: string | null | undefined): string {
  const normalized = reason?.trim()
  return normalized && !normalized.startsWith('manual_')
    ? translate("administration.label.reason.platformAudit", { action: translate(action), reason: normalized })
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
      return translate("administration.label.changeCompetitionStatus", {
        from: statusLabel(log.fromCompetitionStatus),
        to: statusLabel(log.toCompetitionStatus),
      })
    }
    return translate("administration.label.changeCompetitionLifecycle")
  }

  if (log.kind === 'CompetitionLeaderboardVisibility') {
    return translate("administration.label.changeLeaderboardVisibility", {
      from: visibilityLabel(log.fromLeaderboardVisibility),
      to: visibilityLabel(log.toLeaderboardVisibility),
    })
  }

  if (log.kind === 'UserAccountLifecycle') {
    const action = log.userAccountAction ? ACCOUNT_ACTION_LABELS[String(log.userAccountAction)] : null
    return withReason(action ?? "common.label.changeAccountStatus", log.reason)
  }

  if (log.kind === 'CompetitionAdministration') {
    return withReason("administration.label.forceDeleteCompetition", log.reason)
  }

  if (log.kind === 'PlatformAdministration') {
    switch (log.platformAdministrationAction) {
      case 'AuditArchiveExported':
        return translate("administration.label.exportPlatformAuditArchive")
      case 'UserAccessTokenIssued':
        return withReason("common.label.issueUserAccessToken", log.reason)
      case 'UserAccessTokenRevoked':
        return translate("administration.label.revokeUserAccessToken")
      case 'UserTokensInvalidated':
        return translate("administration.label.invalidateUserTokens")
      default:
        return translate("administration.label.performPlatformAdministrationAction")
    }
  }

  const action = log.competitionEventKind ? EVENT_ACTION_LABELS[log.competitionEventKind] : null
  return withReason(action ?? "common.label.performCompetitionAdministrationAction", log.reason)
}
