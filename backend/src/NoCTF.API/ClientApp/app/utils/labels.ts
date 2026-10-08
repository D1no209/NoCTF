import type { UiMessage } from './i18n'
import type { InjectionKey, Ref } from 'vue'
import { localeTag, translate } from './i18n'
import type {
  NoCtfapiEndpointsCompetitionsCompetitionResponse,
  NoCtfapiEndpointsCompetitionsScoreboardTeamResponse,
  NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse,
  NoCtfapiEndpointsNotificationsNotificationResponse,
  NoCtfapiEndpointsCompetitionsGameModeProtocol,
  NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol,
  NoCtfapiEndpointsTeamsTeamRegistrationStatusProtocol,
  NoCtfapiEndpointsRuntimeRuntimeStateProtocol,
  NoCtfapiEndpointsGameplayFactsGameplayFactKindProtocol,
  NoCtfapiEndpointsGameplayFactsGameplayFactStateProtocol,
  NoCtfapiEndpointsGameplayFactsGameplayFactResultProtocol,
  NoCtfapiEndpointsGameplayFactsGameplayFactFailureCodeProtocol,
  NoCtfapiEndpointsCompetitionsEventsCompetitionEventKindProtocol,
  NoCtfapiEndpointsNotificationsNotificationKindProtocol,
} from '../api'
import {
  adminCompetitionCheatsPath,
  adminCompetitionTeamsPath,
  competitionChallengePath,
  competitionEventsPath,
  competitionMyTeamPath,
  competitionQuestionsPath,
} from './app-routes'

/** 竞赛上下文:由 pages/competitions/[id].vue provide,子路由 inject。 */
export interface CompetitionContext {
  competition: Ref<NoCtfapiEndpointsCompetitionsCompetitionResponse | null>
  loading: Ref<boolean>
  error: Ref<UiMessage | null>
  refresh: () => Promise<void>
  standing: Ref<NoCtfapiEndpointsCompetitionsScoreboardTeamResponse | null>
  refreshStanding: () => Promise<void>
}

export const competitionContextKey: InjectionKey<CompetitionContext> = Symbol('competition-context')

export function gameModeLabel(mode?: NoCtfapiEndpointsCompetitionsGameModeProtocol): string {
  if (!mode) return translate("common.label.unknown")
  const labels = { Ctf: 'CTF', Awd: 'AWD', Awdp: 'AWDP', Koh: 'KoH', LiveSolo: 'LiveSolo' } satisfies Record<NoCtfapiEndpointsCompetitionsGameModeProtocol, string>
  return labels[mode]
}

export function competitionStatusLabel(status?: NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol): string {
  if (!status) return translate("common.label.unknown")
  const labels = { Draft: translate("common.label.draft"), Visible: translate("common.label.comingSoon"), Published: translate("common.label.aboutStart"), Running: translate("common.label.running"), Paused: translate("common.label.suspended"), Finished: translate("common.label.finished") } satisfies Record<NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol, string>
  return labels[status]
}

export function teamRegistrationStatusLabel(status?: NoCtfapiEndpointsTeamsTeamRegistrationStatusProtocol): string {
  if (!status) return translate("common.label.unknown")
  const labels = { Pending: translate("common.label.pendingReview"), Approved: translate("common.label.passed"), Rejected: translate("common.label.rejected"), Unregistered: translate("common.label.registered") } satisfies Record<NoCtfapiEndpointsTeamsTeamRegistrationStatusProtocol, string>
  return labels[status]
}

export function runtimeStateLabel(state?: NoCtfapiEndpointsRuntimeRuntimeStateProtocol): string {
  if (!state) return translate("common.label.unknown")
  const labels = { Queued: translate("common.label.queuing"), Provisioning: translate("common.label.deploying"), Running: translate("common.label.running.adminFormat"), Stopping: translate("common.label.stopping"), Stopped: translate("common.label.stopped"), Failed: translate("common.error.failed") } satisfies Record<NoCtfapiEndpointsRuntimeRuntimeStateProtocol, string>
  return labels[state]
}

export function gameplayFactKindLabel(kind?: NoCtfapiEndpointsGameplayFactsGameplayFactKindProtocol): string {
  if (!kind) return translate("common.label.gameplayFacts")
  const labels = { FlagAttempt: 'Flag', BreakAttempt: 'Break', FixAttempt: translate('common.label.patchVerification'), HintUnlock: translate("common.label.promptUnlock"), ManualAdjustment: translate("common.label.manualAdjustment"), AwdServiceTransition: translate("common.label.awdServiceStatus"), AttachmentDownload: translate("cheats.label.attachmentDownload"), WriteUpUnlock: translate("challengeWriteUp.viewed"), KohControlObservation: translate("common.label.kohControlObservation") } satisfies Record<NoCtfapiEndpointsGameplayFactsGameplayFactKindProtocol, string>
  return labels[kind]
}

export function gameplayFactStateLabel(state?: NoCtfapiEndpointsGameplayFactsGameplayFactStateProtocol): string {
  if (!state) return translate("common.label.unknown")
  const labels = { Pending: translate("common.label.awaitingEvaluation"), Queued: translate("common.label.queuing"), Processing: translate("common.label.underEvaluation"), Completed: translate("common.label.completed"), PlatformFailed: translate("common.error.platformFailed") } satisfies Record<NoCtfapiEndpointsGameplayFactsGameplayFactStateProtocol, string>
  return labels[state]
}

export function gameplayFactResultLabel(result?: NoCtfapiEndpointsGameplayFactsGameplayFactResultProtocol | null): string {
  if (result === null || result === undefined) return translate("common.label.underEvaluation")
  const labels = { Correct: translate("common.label.correct"), Wrong: translate("common.label.wrong"), Duplicate: translate("common.label.repeat"), AttemptsExhausted: translate("common.label.exhausted"), Rejected: translate("common.label.rejected"), Unlocked: translate("common.label.unlocked"), Applied: translate("common.label.applied"), ServiceUp: translate("common.label.serviceNormal"), ServiceDown: translate("common.label.serviceException"), Controlled: translate("common.label.controlled"), Uncontrolled: translate("common.label.uncontrolled") } satisfies Record<NoCtfapiEndpointsGameplayFactsGameplayFactResultProtocol, string>
  return labels[result]
}

export function gameplayFactFailureCodeLabel(code?: NoCtfapiEndpointsGameplayFactsGameplayFactFailureCodeProtocol | null): string {
  if (!code) return '—'
  const labels = {
    FlagNotSupported: translate("common.description.modeSupportFlagSubmissions"),
    FixNotSupported: translate("common.description.modeSupportFixSubmissions"),
    BreakAttemptsExhausted: translate("common.label.breakSubmissionAttemptsExhausted"),
    FixAttemptsExhausted: translate("common.label.fixSubmissionAttemptsExhausted"),
    BreakRequired: translate("common.validation.breakCompletedFormat"),
    ArchiveValidationUnavailable: translate("common.error.fixArchiveValidationUnavailable"),
    FixArchiveMissing: translate("common.label.fixArchiveMissing"),
    FixArchiveLengthMismatch: translate("common.label.fixArchiveSizeMismatch"),
    FixArchiveContentTypeMismatch: translate("common.label.fixArchiveContentType"),
    FixArchiveHashMismatch: translate("common.label.fixArchiveHashMismatch"),
    StorageTimeout: translate("common.label.storageAccessTimedOut"),
    StorageUnavailable: translate("common.error.storageUnavailable"),
    CheckerPlatformError: translate("common.error.checkerPlatformFailed"),
    SelfAttackRejected: translate("common.validation.attackOwnFormat"),
    DuplicateAttack: translate("common.label.duplicateAttack"),
    DuplicateAchievement: translate("common.label.duplicateAchievement"),
    UnknownTeamIdentifier: translate("common.label.unknownTeamIdentifier"),
    InvalidObservation: translate("common.error.observationInvalid"),
    ProducerTimeout: translate("common.label.flagGenerationTimedOut"),
    ProducerUnavailable: translate("common.error.flagGeneratorUnavailable"),
    AmbiguousFlagMatch: translate("common.label.flagMatchAmbiguous"),
    FlagExpired: translate("common.label.flagExpired"),
    RoundOutOfRange: translate("common.label.roundOutRange"),
    HardeningActive: translate("common.description.submissionsDisabledBlackoutPeriod"),
    AwdpExploitSucceeded: translate("common.label.expStillSucceeds"),
    AwdpPatchFailed: translate("common.error.patchExecutionFailed"),
    AwdpPatchTimeout: translate("common.label.patchExecutionTimedOut"),
    AwdpServiceAbnormal: translate("common.label.serviceException"),
    AwdpPlatformFailed: translate("common.error.awdpPlatformFailed"),
    ForeignTeamFlagDetected: translate("common.description.submittedFlagAssignedAnother"),
    StaticFlagWithoutContainer: translate("cheats.reason.containerMissing"),
    StaticFlagWithoutAttachment: translate("cheats.reason.attachmentMissing"),
    StaticFlagWithoutContainerAndAttachment: translate("cheats.reason.bothMissing"),
    InsufficientScore: translate("common.label.insufficientScore"),
    HintUnavailable: translate("common.error.hintUnavailable"),
    PatchStillExploitable: translate("common.label.expStillSucceeds"),
    PatchExecutionFailed: translate("common.error.patchExecutionFailed"),
    PatchServiceAbnormal: translate("common.label.serviceException"),
    PatchVerificationPlatformFailed: translate("common.error.patchVerificationPlatformFailed"),
  } satisfies Record<NoCtfapiEndpointsGameplayFactsGameplayFactFailureCodeProtocol, string>
  return labels[code]
}

/** 评测是否仍在进行中(需要继续轮询)。 */
export function isGameplayFactPending(state?: NoCtfapiEndpointsGameplayFactsGameplayFactStateProtocol): boolean {
  return state === 'Pending' || state === 'Queued' || state === 'Processing'
}

export function formatDateTime(value?: string | null): string {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '—'
  return date.toLocaleString(localeTag(), { hour12: false })
}

export function formatBytes(bytes?: number | null): string {
  if (bytes === null || bytes === undefined) return ''
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`
}

/** 将剩余毫秒格式化为「Xd Xh Xm Xs」倒计时文本。 */
export function formatDuration(ms: number): string {
  if (ms <= 0) return '0s'
  const total = Math.floor(ms / 1000)
  const days = Math.floor(total / 86400)
  const hours = Math.floor((total % 86400) / 3600)
  const minutes = Math.floor((total % 3600) / 60)
  const seconds = total % 60
  const parts: string[] = []
  if (days > 0) parts.push(translate("common.label.d", { count: days }))
  if (hours > 0) parts.push(translate("common.label.h", { count: hours }))
  if (minutes > 0 && days === 0) parts.push(translate("common.label.m", { count: minutes }))
  if (days === 0 && hours === 0) parts.push(translate("common.label.s", { count: seconds }))
  return parts.join(' ')
}

/** 竞赛动态文案(NoCTF.Domain.Competitions.Events.CompetitionEventKind)。 */
export function competitionEventText(
  event: NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse,
): string {
  const actor = event.actorDisplayName ?? translate("common.label.system")
  const team = event.teamDisplayName ?? translate("common.label.team.competitionBroadcast")
  const challenge = event.challengeTitle ?? translate("common.label.challenge")
  const resolvedSuccessfully = event.gameplayFactState === 'Completed'
    && event.gameplayFactResult === 'Correct'
  if (event.kind === 'AwdpBreakResolved') {
    return resolvedSuccessfully
      ? translate("common.label.teamPassedAttackVerification", { team, challenge })
      : translate("common.error.teamAttackVerificationFailed", { team, challenge })
  }
  if (event.kind === 'AwdpFixResolved') {
    return resolvedSuccessfully
      ? translate("common.label.teamPassedDefenseVerification", { team, challenge })
      : translate("common.error.teamDefenseVerificationFailed", { team, challenge })
  }
  if (event.kind === 'GameplayFactAdjudicated'
    && event.gameplayFactKind === 'FixAttempt') {
    return resolvedSuccessfully
      ? translate('common.label.teamPassedPatchVerification', { team, challenge })
      : translate('common.error.teamPatchVerificationFailed', { team, challenge })
  }
  const templates: Partial<Record<NoCtfapiEndpointsCompetitionsEventsCompetitionEventKindProtocol, string>> = {
    GameplayFactPatchDownloaded: translate("common.description.downloadedPatchArchiveTeam", { actor, team }),
    CompetitionAudienceChanged: translate("competitionAccess.changed"),
    TeamWriteUpSubmitted: translate("writeUp.submittedEvent"),
    ChallengeWriteUpSubmitted: translate('challengeWriteUp.event.submitted', { team, challenge }),
    ChallengeWriteUpPublished: translate('challengeWriteUp.event.published', { challenge }),
    ChallengeWriteUpWithdrawn: translate('challengeWriteUp.event.withdrawn', { challenge }),
    ChallengeWriteUpRejected: translate('challengeWriteUp.event.rejected', { team, challenge }),
    ChallengeWriteUpUnlocked: translate('challengeWriteUp.event.unlocked', { team, challenge }),
    RuntimeTrafficCaptureStored: translate("runtime.captureStoredEvent"),
    RuntimeTrafficCaptureDeleted: translate("runtime.captureDeletedEvent"),
    CompetitionCreated: translate("common.label.contestCreated"), CompetitionUpdated: translate("common.label.competitionInformationUpdated"), CompetitionLifecycleChanged: translate("common.label.competitionLifeCycleChanges"),
    LeaderboardVisibilityChanged: translate("common.label.leaderboardVisibilityChanged"), ChallengeCreated: translate("common.description.challengeWasAddedCompetition", { challenge }), ChallengeUpdated: translate("common.label.challengeWasUpdated", { challenge }),
    ChallengePublished: translate("common.label.challengeWasPublished", { challenge }), ChallengeDescriptionUpdated: translate("common.label.challengeUpdatedDescription", { challenge }), ChallengeUnpublished: translate("common.label.challengeWasUnpublished", { challenge }), HintPublished: translate("common.label.challengeNewHint", { challenge }),
    HintUnlocked: translate("common.label.teamUnlockedHint", { team, challenge }), TeamRegistered: translate("common.label.teamRegisteredCompetition", { team }), TeamRegistrationChanged: translate("common.label.teamSRegistrationStatus", { team }),
    TeamMemberJoined: translate("common.label.newMemberJoinedTeam", { team }), TeamBanned: translate("common.label.teamWasBanned", { team }), TeamUnbanned: translate("common.label.teamWasUnbanned", { team }), TeamBanCorrectionPublished: translate("common.description.banCorrectionWasPublished", { team }), GameplayFactReceived: translate("common.label.teamSubmitted", { team, challenge }),
    GameplayFactAdjudicated: translate("common.description.teamSSubmissionWas", { team, challenge }), FirstBloodAwarded: translate("common.label.teamEarnedFirstBlood", { team, challenge }), SecondBloodAwarded: translate("common.label.teamEarnedSecondBlood", { team, challenge }),
    ThirdBloodAwarded: translate("common.label.teamEarnedThirdBlood", { team, challenge }), RuntimeCreated: translate("common.label.teamRequestedRuntime", { team, challenge }), RuntimeStateChanged: translate("common.description.teamSRuntimeChanged", { team, challenge }),
    AwdpBreakAttempted: translate("common.description.teamMadeAttackAttempt", { team, challenge }), AwdpFixAttempted: translate("common.description.teamSubmittedDefenseAttempt", { team, challenge }),
    AnnouncementPublished: translate("common.label.officialAnnouncementWasMade"), QuestionOpened: translate("common.label.openedQuestion", { actor }), QuestionReplied: translate("common.label.questionReceivedReply"), QuestionStatusChanged: translate("common.label.questionStatusChanged"),
  }
  return event.kind ? templates[event.kind] ?? translate("common.label.competitionEventOccurred") : translate("common.label.competitionEventOccurred")
}

function notificationContent(
  notification: NoCtfapiEndpointsNotificationsNotificationResponse,
): Record<string, unknown> {
  return notification.content && typeof notification.content === 'object'
    ? notification.content as Record<string, unknown>
    : {}
}

export function notificationCompetitionId(
  notification: NoCtfapiEndpointsNotificationsNotificationResponse,
): string | null {
  const content = notificationContent(notification)
  return typeof content.competitionId === 'string' ? content.competitionId : null
}

function notificationContentId(
  notification: NoCtfapiEndpointsNotificationsNotificationResponse,
  key: string,
): string | null {
  const value = notificationContent(notification)[key]
  return typeof value === 'string' && value.length > 0 ? value : null
}

export function notificationThreadRootId(
  notification: NoCtfapiEndpointsNotificationsNotificationResponse,
): string | null {
  return notification.threadRootId
    ?? notificationContentId(notification, 'threadRootId')
    ?? notification.id
    ?? null
}

interface NotificationTargetContext {
  detailPath: string
  competitionId: string | null
  challengeId: string | null
  questionId: string | null
  gameplayFactId: string | null
  appealEventId: string | null
}

type NotificationTargetResolver = (context: NotificationTargetContext) => string

function notificationDetailPath(
  notification: NoCtfapiEndpointsNotificationsNotificationResponse,
): string {
  return notification.id ? `/notifications?notification=${notification.id}` : '/notifications'
}

function competitionEventTarget(
  context: NotificationTargetContext,
  kind: NoCtfapiEndpointsCompetitionsEventsCompetitionEventKindProtocol,
): string {
  return context.competitionId
    ? `${competitionEventsPath(context.competitionId)}?kind=${kind}`
    : context.detailPath
}

const notificationTargetResolvers = {
  Message: context => context.competitionId && context.questionId
    ? `${competitionQuestionsPath(context.competitionId)}?question=${context.questionId}`
    : context.detailPath,
  CompetitionAnnouncement: context => context.detailPath,
  QuestionOpened: context => context.competitionId && context.questionId
    ? `${competitionQuestionsPath(context.competitionId)}?question=${context.questionId}`
    : context.detailPath,
  QuestionStatusChanged: context => context.competitionId && context.questionId
    ? `${competitionQuestionsPath(context.competitionId)}?question=${context.questionId}`
    : context.detailPath,
  CompetitionLifecycleChanged: context => competitionEventTarget(context, 'CompetitionLifecycleChanged'),
  TeamRegistrationChanged: context => context.competitionId
    ? competitionMyTeamPath(context.competitionId)
    : context.detailPath,
  GameplayFactAdjudicated: context => context.competitionId && context.challengeId
    ? competitionChallengePath(context.competitionId, context.challengeId)
    : context.detailPath,
  RuntimeStateChanged: context => context.competitionId && context.challengeId
    ? competitionChallengePath(context.competitionId, context.challengeId)
    : context.detailPath,
  StartGateFailed: context => context.detailPath,
  ManagementFailure: context => context.detailPath,
  BloodAwarded: context => context.competitionId && context.challengeId
    ? competitionChallengePath(context.competitionId, context.challengeId)
    : context.detailPath,
  ChallengePublished: context => context.competitionId && context.challengeId
    ? competitionChallengePath(context.competitionId, context.challengeId)
    : context.detailPath,
  HintPublished: context => context.competitionId && context.challengeId
    ? competitionChallengePath(context.competitionId, context.challengeId)
    : context.detailPath,
  TeamBanned: context => context.competitionId
    ? `${competitionMyTeamPath(context.competitionId)}#ban-appeal`
    : context.detailPath,
  CheatIncidentDetected: context => context.competitionId && context.gameplayFactId
    ? `${adminCompetitionCheatsPath(context.competitionId)}?incident=${context.gameplayFactId}`
    : context.detailPath,
  TeamBanCorrected: context => context.competitionId
    ? `${competitionMyTeamPath(context.competitionId)}#ban-appeal`
    : context.detailPath,
  TeamBanAppealSubmitted: context => context.competitionId
    ? `${adminCompetitionTeamsPath(context.competitionId)}?appeal=${context.appealEventId ?? ''}#ban-appeals`
    : context.detailPath,
  PlatformAuditExported: context => context.detailPath,
  UserAccountLifecycleChanged: context => context.detailPath,
  CompetitionForceDeleted: context => context.detailPath,
} satisfies Record<NoCtfapiEndpointsNotificationsNotificationKindProtocol, NotificationTargetResolver>

export function notificationTargetPath(
  notification: NoCtfapiEndpointsNotificationsNotificationResponse,
): string {
  const isQuestionActivity = notification.kind === 'QuestionOpened'
    || notification.kind === 'Message'
    || notification.kind === 'QuestionStatusChanged'
  const competitionId = notificationCompetitionId(notification)
    ?? (isQuestionActivity ? notification.relatedId ?? null : null)
  const detailPath = notificationDetailPath(notification)
  const kind = notification.kind
  if (!kind) return detailPath

  const context: NotificationTargetContext = {
    detailPath,
    competitionId,
    challengeId: notificationContentId(notification, 'competitionChallengeId'),
    questionId: notification.threadRootId
      ?? notificationContentId(notification, 'threadRootId')
      ?? (kind === 'QuestionOpened' ? notification.id ?? null : null),
    gameplayFactId: notificationContentId(notification, 'gameplayFactId'),
    appealEventId: notificationContentId(notification, 'appealEventId'),
  }
  return notificationTargetResolvers[kind]?.(context) ?? detailPath
}

export function notificationTitle(
  notification: NoCtfapiEndpointsNotificationsNotificationResponse,
): string {
  const payload = notificationContent(notification)
  if (notification.kind === 'CompetitionAnnouncement')
    return typeof payload.title === 'string' ? payload.title : translate("common.label.eventAnnouncement")
  if (notification.kind === 'QuestionOpened' || notification.kind === 'Message' || notification.kind === 'QuestionStatusChanged')
    return typeof payload.title === 'string' ? payload.title : notificationText(notification)
  return notificationText(notification)
}

export function notificationBody(
  notification: NoCtfapiEndpointsNotificationsNotificationResponse,
): string | null {
  const payload = notificationContent(notification)
  if (notification.kind === 'UserAccountLifecycleChanged') {
    const user = typeof payload.targetUserName === 'string' && payload.targetUserName.trim().length > 0
      ? payload.targetUserName
      : translate("common.label.unknownUser")
    const action = typeof payload.action === 'string' ? payload.action : null
    const templates: Record<string, string> = {
      Activated: "common.label.activatedAccount",
      Banned: "common.label.bannedAccount",
      Disabled: "common.label.disabledAccount",
      EmailVerified: "common.description.manuallyVerifiedEmailAddress",
      EmailUnverified: "common.label.revokedEmailVerification",
      Anonymized: "common.label.anonymizedAccount",
      PhysicallyDeleted: "common.label.permanentlyDeletedAccount",
    }
    return translate(templates[action ?? ''] ?? "common.description.administratorUpdatedAccountStatus", { user })
  }
  for (const key of ['body', 'reason', 'detail', 'message']) {
    const value = payload[key]
    if (typeof value === 'string' && value.trim().length > 0)
      return value
  }
  return null
}

export function notificationActionLabel(
  notification: NoCtfapiEndpointsNotificationsNotificationResponse,
): string {
  const labels = {
    Message: "common.label.viewConsultation",
    CompetitionAnnouncement: "common.label.viewNotificationDetails",
    QuestionOpened: "common.label.viewConsultation",
    QuestionStatusChanged: "common.label.viewConsultation",
    CompetitionLifecycleChanged: "common.label.viewGameUpdates",
    TeamRegistrationChanged: "common.label.viewMyTeam",
    GameplayFactAdjudicated: "common.label.viewSubmissions",
    RuntimeStateChanged: "administration.label.viewQuestions",
    StartGateFailed: "common.label.viewNotificationDetails",
    ManagementFailure: "common.label.viewNotificationDetails",
    BloodAwarded: "administration.label.viewQuestions",
    ChallengePublished: "administration.label.viewQuestions",
    HintPublished: "administration.label.viewQuestions",
    TeamBanned: "common.label.viewBansAppeals",
    CheatIncidentDetected: "common.label.viewCheatingIncidents",
    TeamBanCorrected: "common.label.viewBansAppeals",
    TeamBanAppealSubmitted: "common.label.reviewBanAppeal",
    PlatformAuditExported: "common.label.viewNotificationDetails",
    UserAccountLifecycleChanged: "common.label.viewNotificationDetails",
    CompetitionForceDeleted: "common.label.viewNotificationDetails",
  } satisfies Record<NoCtfapiEndpointsNotificationsNotificationKindProtocol, string>
  return translate(notification.kind ? labels[notification.kind] : "common.label.viewNotificationDetails")
}

/** 通知文案(NoCTF.Domain.Notifications.NotificationKind),content 为松散 JSON。 */
export function notificationText(
  notification: NoCtfapiEndpointsNotificationsNotificationResponse,
): string {
  const payload = notificationContent(notification)
  const title = typeof payload.competitionTitle === 'string' ? `「${payload.competitionTitle}」` : ''
  const team = typeof payload.teamName === 'string' ? `「${payload.teamName}」` : ''
  const challenge = typeof payload.challengeTitle === 'string' ? `「${payload.challengeTitle}」` : ''
  const announcementTitle = typeof payload.title === 'string' ? payload.title : translate("common.label.eventAnnouncement")
  const announcementBody = typeof payload.body === 'string' ? payload.body : ''
  const announcement = announcementBody
    ? `${announcementTitle}：${announcementBody}`
    : announcementTitle
  const templates: Partial<Record<NoCtfapiEndpointsNotificationsNotificationKindProtocol, string>> = {
    CompetitionLifecycleChanged: translate("common.label.competitionSLifecycleChanged", { title }), TeamRegistrationChanged: translate("common.description.teamSRegistrationStatus", { team }), GameplayFactAdjudicated: translate("common.description.submissionJudged", { challenge }),
    RuntimeStateChanged: translate("common.description.runtimeEnvironmentChanged", { challenge }), StartGateFailed: translate("common.error.competitionStartChecksFailed", { title }), ManagementFailure: translate("common.error.competitionEncounteredManagementFailed", { title }),
    BloodAwarded: translate("common.description.congratulationsEarnedBloodRank", { challenge }), ChallengePublished: translate("common.label.competitionPublishedNewChallenge", { title, challenge }), HintPublished: translate("common.label.newHint", { challenge }),
    TeamBanned: translate("common.label.teamBanned", { team }), QuestionOpened: translate("common.label.competitionNewQuestion", { title }), Message: translate("common.description.thereNewRepliesInquiry"),
    QuestionStatusChanged: translate("common.label.inquiryStatusChanged"), CheatIncidentDetected: translate("common.label.suspectedCheatingDetected"), TeamBanCorrected: translate("common.label.teamBanCorrected"), TeamBanAppealSubmitted: translate("common.label.teamSubmittedBanAppeal", { team }), PlatformAuditExported: translate("common.label.platformAuditArchiveExported"),
    UserAccountLifecycleChanged: translate("common.label.userAccountStatusChanged"), CompetitionForceDeleted: translate("common.label.competitionForceDeleted"), CompetitionAnnouncement: announcement,
  }
  return notification.kind ? templates[notification.kind] ?? translate("common.label.newNotification") : translate("common.label.newNotification")
}
