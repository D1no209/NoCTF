import type { InjectionKey, Ref } from 'vue'
import { localeTag, translate } from './i18n'
import type {
  NoCtfapiEndpointsCompetitionsCompetitionResponse,
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

/** 竞赛上下文:由 pages/competitions/[id].vue provide,子路由 inject。 */
export interface CompetitionContext {
  competition: Ref<NoCtfapiEndpointsCompetitionsCompetitionResponse | null>
  loading: Ref<boolean>
  error: Ref<string | null>
  refresh: () => Promise<void>
}

export const competitionContextKey: InjectionKey<CompetitionContext> = Symbol('competition-context')

export function gameModeLabel(mode?: NoCtfapiEndpointsCompetitionsGameModeProtocol): string {
  if (!mode) return translate("ui.unknown")
  const labels = { Ctf: 'CTF', Awd: 'AWD', Awdp: 'AWDP', Koh: 'KoH' } satisfies Record<NoCtfapiEndpointsCompetitionsGameModeProtocol, string>
  return labels[mode]
}

export function competitionStatusLabel(status?: NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol): string {
  if (!status) return translate("ui.unknown")
  const labels = { Draft: translate("ui.draft"), Visible: translate("ui.comingSoon"), Published: translate("ui.aboutToStart"), Running: translate("ui.running"), Paused: translate("ui.suspended"), Finished: translate("ui.finished") } satisfies Record<NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol, string>
  return labels[status]
}

export function teamRegistrationStatusLabel(status?: NoCtfapiEndpointsTeamsTeamRegistrationStatusProtocol): string {
  if (!status) return translate("ui.unknown")
  const labels = { Pending: translate("ui.pendingReview"), Approved: translate("ui.passed"), Rejected: translate("ui.rejected") } satisfies Record<NoCtfapiEndpointsTeamsTeamRegistrationStatusProtocol, string>
  return labels[status]
}

export function runtimeStateLabel(state?: NoCtfapiEndpointsRuntimeRuntimeStateProtocol): string {
  if (!state) return translate("ui.unknown")
  const labels = { Queued: translate("ui.queuing"), Provisioning: translate("ui.deploying"), Running: translate("ui.running2"), Stopping: translate("ui.stopping"), Stopped: translate("ui.stopped"), Failed: translate("ui.failed") } satisfies Record<NoCtfapiEndpointsRuntimeRuntimeStateProtocol, string>
  return labels[state]
}

export function gameplayFactKindLabel(kind?: NoCtfapiEndpointsGameplayFactsGameplayFactKindProtocol): string {
  if (!kind) return translate("ui.gameplayFacts")
  const labels = { FlagAttempt: 'Flag', BreakAttempt: 'Break', FixAttempt: 'Fix', HintUnlock: translate("ui.promptToUnlock"), ManualAdjustment: translate("ui.manualAdjustment"), AwdServiceTransition: translate("ui.awdServiceStatus"), KohControlObservation: translate("ui.kohControlObservation") } satisfies Record<NoCtfapiEndpointsGameplayFactsGameplayFactKindProtocol, string>
  return labels[kind]
}

export function gameplayFactStateLabel(state?: NoCtfapiEndpointsGameplayFactsGameplayFactStateProtocol): string {
  if (!state) return translate("ui.unknown")
  const labels = { Pending: translate("ui.awaitingEvaluation"), Queued: translate("ui.queuing"), Processing: translate("ui.underEvaluation"), Completed: translate("ui.completed"), PlatformFailed: translate("ui.platformFailure") } satisfies Record<NoCtfapiEndpointsGameplayFactsGameplayFactStateProtocol, string>
  return labels[state]
}

export function gameplayFactResultLabel(result?: NoCtfapiEndpointsGameplayFactsGameplayFactResultProtocol | null): string {
  if (result === null || result === undefined) return translate("ui.underEvaluation")
  const labels = { Correct: translate("ui.correct"), Wrong: translate("ui.wrong"), Duplicate: translate("ui.repeat"), AttemptsExhausted: translate("ui.exhausted"), Rejected: translate("ui.rejected"), Unlocked: translate("ui.unlocked"), Applied: translate("ui.applied"), ServiceUp: translate("ui.serviceIsNormal"), ServiceDown: translate("ui.serviceException"), Controlled: translate("ui.controlled"), Uncontrolled: translate("ui.uncontrolled") } satisfies Record<NoCtfapiEndpointsGameplayFactsGameplayFactResultProtocol, string>
  return labels[result]
}

export function gameplayFactFailureCodeLabel(code?: NoCtfapiEndpointsGameplayFactsGameplayFactFailureCodeProtocol | null): string {
  if (!code) return '—'
  const labels = {
    FlagNotSupported: translate("ui.thisModeDoesNotSupportFlagSubmissions"),
    FixNotSupported: translate("ui.thisModeDoesNotSupportFixSubmissions"),
    BreakAttemptsExhausted: translate("ui.breakSubmissionAttemptsAreExhausted"),
    FixAttemptsExhausted: translate("ui.fixSubmissionAttemptsAreExhausted"),
    BreakRequired: translate("ui.breakMustBeCompletedFirst"),
    ArchiveValidationUnavailable: translate("ui.fixArchiveValidationIsUnavailable"),
    FixArchiveMissing: translate("ui.fixArchiveIsMissing"),
    FixArchiveLengthMismatch: translate("ui.fixArchiveSizeMismatch"),
    FixArchiveContentTypeMismatch: translate("ui.fixArchiveContentTypeMismatch"),
    FixArchiveHashMismatch: translate("ui.fixArchiveHashMismatch"),
    StorageTimeout: translate("ui.storageAccessTimedOut"),
    StorageUnavailable: translate("ui.storageIsUnavailable"),
    CheckerPlatformError: translate("ui.checkerPlatformError"),
    SelfAttackRejected: translate("ui.cannotAttackYourOwnTeam"),
    DuplicateAttack: translate("ui.duplicateAttack"),
    DuplicateAchievement: translate("ui.duplicateAchievement"),
    UnknownTeamIdentifier: translate("ui.unknownTeamIdentifier"),
    InvalidObservation: translate("ui.invalidObservation"),
    ProducerTimeout: translate("ui.flagGenerationTimedOut"),
    ProducerUnavailable: translate("ui.flagGeneratorUnavailable"),
    AmbiguousFlagMatch: translate("ui.flagMatchIsAmbiguous"),
    FlagExpired: translate("ui.flagHasExpired"),
    RoundOutOfRange: translate("ui.roundIsOutOfRange"),
    HardeningActive: translate("ui.submissionsAreDisabledDuringTheBlackoutPeriod"),
    AwdpExploitSucceeded: translate("ui.expStillSucceeds"),
    AwdpPatchFailed: translate("ui.patchExecutionFailed"),
    AwdpPatchTimeout: translate("ui.patchExecutionTimedOut"),
    AwdpServiceAbnormal: translate("ui.serviceException"),
    AwdpPlatformFailed: translate("ui.awdpPlatformError"),
    AwdpViolation: translate("ui.legacyAwdpViolationRecord"),
    ForeignTeamFlagDetected: translate("ui.submittedAFlagAssignedToAnotherTeam"),
    InsufficientScore: translate("ui.insufficientScore"),
    HintUnavailable: translate("ui.hintIsUnavailable"),
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
  if (days > 0) parts.push(translate("ui.d", { count: days }))
  if (hours > 0) parts.push(translate("ui.h", { count: hours }))
  if (minutes > 0 && days === 0) parts.push(translate("ui.m", { count: minutes }))
  if (days === 0 && hours === 0) parts.push(translate("ui.s", { count: seconds }))
  return parts.join(' ')
}

/** 竞赛动态文案(NoCTF.Domain.Competitions.Events.CompetitionEventKind)。 */
export function competitionEventText(
  event: NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse,
): string {
  const actor = event.actorDisplayName ?? translate("ui.system")
  const team = event.teamDisplayName ?? translate("ui.aTeam")
  const challenge = event.challengeTitle ?? translate("ui.aChallenge")
  const resolvedSuccessfully = event.gameplayFactState === 'Completed'
    && event.gameplayFactResult === 'Correct'
  if (event.kind === 'AwdpBreakResolved') {
    return resolvedSuccessfully
      ? translate("ui.teamPassedAttackVerificationOn", { team, challenge })
      : translate("ui.teamFailedAttackVerificationOn", { team, challenge })
  }
  if (event.kind === 'AwdpFixResolved') {
    return resolvedSuccessfully
      ? translate("ui.teamPassedDefenseVerificationOn", { team, challenge })
      : translate("ui.teamFailedDefenseVerificationOn", { team, challenge })
  }
  const templates: Partial<Record<NoCtfapiEndpointsCompetitionsEventsCompetitionEventKindProtocol, string>> = {
    GameplayFactPatchDownloaded: translate("ui.downloadedThePatchArchiveForTeam", { actor, team }),
    CompetitionAudienceChanged: translate("competitionAccess.changed"),
    CompetitionCreated: translate("ui.contestCreated"), CompetitionUpdated: translate("ui.competitionInformationHasBeenUpdated"), CompetitionLifecycleChanged: translate("ui.competitionLifeCycleChanges"),
    LeaderboardVisibilityChanged: translate("ui.leaderboardVisibilityChanged"), ChallengeCreated: translate("ui.challengeWasAddedToTheCompetition", { challenge }), ChallengeUpdated: translate("ui.challengeWasUpdated", { challenge }),
    ChallengePublished: translate("ui.challengeWasPublished", { challenge }), ChallengeDescriptionUpdated: translate("ui.challengeHasAnUpdatedDescription", { challenge }), ChallengeUnpublished: translate("ui.challengeWasUnpublished", { challenge }), HintPublished: translate("ui.challengeHasANewHint2", { challenge }),
    HintUnlocked: translate("ui.teamUnlockedAHintFor", { team, challenge }), TeamRegistered: translate("ui.teamRegisteredForTheCompetition", { team }), TeamRegistrationChanged: translate("ui.teamSRegistrationStatusChanged", { team }),
    TeamMemberJoined: translate("ui.aNewMemberJoinedTeam", { team }), TeamBanned: translate("ui.teamWasBanned", { team }), TeamUnbanned: translate("ui.teamWasUnbanned", { team }), TeamBanCorrectionPublished: translate("ui.aBanCorrectionWasPublishedForTeam", { team }), GameplayFactReceived: translate("ui.teamSubmitted", { team, challenge }),
    GameplayFactAdjudicated: translate("ui.teamSSubmissionForWasJudged", { team, challenge }), FirstBloodAwarded: translate("ui.teamEarnedFirstBloodOn", { team, challenge }), SecondBloodAwarded: translate("ui.teamEarnedSecondBloodOn", { team, challenge }),
    ThirdBloodAwarded: translate("ui.teamEarnedThirdBloodOn", { team, challenge }), RuntimeCreated: translate("ui.teamRequestedARuntimeFor", { team, challenge }), RuntimeStateChanged: translate("ui.teamSRuntimeForChangedState", { team, challenge }),
    AwdpBreakAttempted: translate("ui.teamMadeAnAttackAttemptOn", { team, challenge }), AwdpFixAttempted: translate("ui.teamSubmittedADefenseAttemptFor", { team, challenge }),
    AnnouncementPublished: translate("ui.anOfficialAnnouncementWasMade"), QuestionOpened: translate("ui.openedAQuestion", { actor }), QuestionReplied: translate("ui.questionReceivedAReply"), QuestionStatusChanged: translate("ui.questionStatusChanged"),
  }
  return event.kind ? templates[event.kind] ?? translate("ui.aCompetitionEventOccurred") : translate("ui.aCompetitionEventOccurred")
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
    ? `/competitions/${context.competitionId}/events?kind=${kind}`
    : context.detailPath
}

const notificationTargetResolvers = {
  Message: context => context.competitionId && context.questionId
    ? `/competitions/${context.competitionId}/questions?question=${context.questionId}`
    : context.detailPath,
  CompetitionAnnouncement: context => context.detailPath,
  QuestionOpened: context => context.competitionId && context.questionId
    ? `/competitions/${context.competitionId}/questions?question=${context.questionId}`
    : context.detailPath,
  QuestionStatusChanged: context => context.competitionId && context.questionId
    ? `/competitions/${context.competitionId}/questions?question=${context.questionId}`
    : context.detailPath,
  CompetitionLifecycleChanged: context => competitionEventTarget(context, 'CompetitionLifecycleChanged'),
  TeamRegistrationChanged: context => context.competitionId
    ? `/competitions/${context.competitionId}/my/team`
    : context.detailPath,
  GameplayFactAdjudicated: context => context.competitionId && context.challengeId
    ? `/competitions/${context.competitionId}/challenges?challenge=${context.challengeId}`
    : context.detailPath,
  RuntimeStateChanged: context => context.competitionId && context.challengeId
    ? `/competitions/${context.competitionId}/challenges?challenge=${context.challengeId}`
    : context.detailPath,
  StartGateFailed: context => context.detailPath,
  ManagementFailure: context => context.detailPath,
  BloodAwarded: context => context.competitionId && context.challengeId
    ? `/competitions/${context.competitionId}/challenges?challenge=${context.challengeId}`
    : context.detailPath,
  ChallengePublished: context => context.competitionId && context.challengeId
    ? `/competitions/${context.competitionId}/challenges?challenge=${context.challengeId}`
    : context.detailPath,
  HintPublished: context => context.competitionId && context.challengeId
    ? `/competitions/${context.competitionId}/challenges?challenge=${context.challengeId}`
    : context.detailPath,
  TeamBanned: context => context.competitionId
    ? `/competitions/${context.competitionId}/my/team#ban-appeal`
    : context.detailPath,
  CheatIncidentDetected: context => context.competitionId && context.gameplayFactId
    ? `/admin/competitions/${context.competitionId}/cheats?incident=${context.gameplayFactId}`
    : context.detailPath,
  TeamBanCorrected: context => context.competitionId
    ? `/competitions/${context.competitionId}/my/team#ban-appeal`
    : context.detailPath,
  TeamBanAppealSubmitted: context => context.competitionId
    ? `/admin/competitions/${context.competitionId}/teams?appeal=${context.appealEventId ?? ''}#ban-appeals`
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
    return typeof payload.title === 'string' ? payload.title : translate("ui.eventAnnouncement")
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
      : translate("ui.unknownUser")
    const action = typeof payload.action === 'string' ? payload.action : null
    const templates: Record<string, string> = {
      Activated: "ui.activatedTheAccountFor",
      Banned: "ui.bannedTheAccountFor",
      Disabled: "ui.disabledTheAccountFor",
      EmailVerified: "ui.manuallyVerifiedTheEmailAddressFor",
      EmailUnverified: "ui.revokedEmailVerificationFor",
      Anonymized: "ui.anonymizedTheAccountFor",
      PhysicallyDeleted: "ui.permanentlyDeletedTheAccountFor",
    }
    return translate(templates[action ?? ''] ?? "ui.anAdministratorUpdatedTheAccountStatusFor", { user })
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
    Message: "ui.viewConsultation",
    CompetitionAnnouncement: "ui.viewNotificationDetails",
    QuestionOpened: "ui.viewConsultation",
    QuestionStatusChanged: "ui.viewConsultation",
    CompetitionLifecycleChanged: "ui.viewGameUpdates",
    TeamRegistrationChanged: "ui.viewMyTeam",
    GameplayFactAdjudicated: "ui.viewSubmissions",
    RuntimeStateChanged: "ui.viewQuestions",
    StartGateFailed: "ui.viewNotificationDetails",
    ManagementFailure: "ui.viewNotificationDetails",
    BloodAwarded: "ui.viewQuestions",
    ChallengePublished: "ui.viewQuestions",
    HintPublished: "ui.viewQuestions",
    TeamBanned: "ui.viewBansAndAppeals",
    CheatIncidentDetected: "ui.viewCheatingIncidents",
    TeamBanCorrected: "ui.viewBansAndAppeals",
    TeamBanAppealSubmitted: "ui.reviewBanAppeal",
    PlatformAuditExported: "ui.viewNotificationDetails",
    UserAccountLifecycleChanged: "ui.viewNotificationDetails",
    CompetitionForceDeleted: "ui.viewNotificationDetails",
  } satisfies Record<NoCtfapiEndpointsNotificationsNotificationKindProtocol, string>
  return translate(notification.kind ? labels[notification.kind] : "ui.viewNotificationDetails")
}

/** 通知文案(NoCTF.Domain.Notifications.NotificationKind),content 为松散 JSON。 */
export function notificationText(
  notification: NoCtfapiEndpointsNotificationsNotificationResponse,
): string {
  const payload = notificationContent(notification)
  const title = typeof payload.competitionTitle === 'string' ? `「${payload.competitionTitle}」` : ''
  const team = typeof payload.teamName === 'string' ? `「${payload.teamName}」` : ''
  const challenge = typeof payload.challengeTitle === 'string' ? `「${payload.challengeTitle}」` : ''
  const announcementTitle = typeof payload.title === 'string' ? payload.title : translate("ui.eventAnnouncement")
  const announcementBody = typeof payload.body === 'string' ? payload.body : ''
  const announcement = announcementBody
    ? `${announcementTitle}：${announcementBody}`
    : announcementTitle
  const templates: Partial<Record<NoCtfapiEndpointsNotificationsNotificationKindProtocol, string>> = {
    CompetitionLifecycleChanged: translate("ui.competitionSLifecycleHasChanged", { title }), TeamRegistrationChanged: translate("ui.yourTeamSRegistrationStatusHasChanged", { team }), GameplayFactAdjudicated: translate("ui.yourSubmissionForHasBeenJudged", { challenge }),
    RuntimeStateChanged: translate("ui.theRuntimeEnvironmentForHasChanged", { challenge }), StartGateFailed: translate("ui.competitionFailedItsStartChecks", { title }), ManagementFailure: translate("ui.competitionEncounteredAManagementFailure", { title }),
    BloodAwarded: translate("ui.congratulationsYouEarnedABloodRankOn", { challenge }), ChallengePublished: translate("ui.competitionPublishedANewChallenge", { title, challenge }), HintPublished: translate("ui.hasANewHint", { challenge }),
    TeamBanned: translate("ui.yourTeamHasBeenBanned", { team }), QuestionOpened: translate("ui.competitionHasANewQuestion", { title }), Message: translate("ui.thereAreNewRepliesToYourInquiry"),
    QuestionStatusChanged: translate("ui.yourInquiryStatusHasChanged"), CheatIncidentDetected: translate("ui.suspectedCheatingDetected"), TeamBanCorrected: translate("ui.teamBanHasBeenCorrected"), TeamBanAppealSubmitted: translate("ui.teamSubmittedABanAppeal", { team }), PlatformAuditExported: translate("ui.platformAuditArchiveExported"),
    UserAccountLifecycleChanged: translate("ui.userAccountStatusHasChanged"), CompetitionForceDeleted: translate("ui.competitionForceDeleted"), CompetitionAnnouncement: announcement,
  }
  return notification.kind ? templates[notification.kind] ?? translate("ui.youHaveANewNotification") : translate("ui.youHaveANewNotification")
}
