import type {
  NoCtfapiEndpointsNotificationsNotificationListResponse,
  NoCtfapiEndpointsNotificationsNotificationResponse,
  NoCtfDomainNotificationsNotificationKind,
} from './generated/types.gen'

export const NotificationKind = {
  CompetitionLifecycleChanged: 0,
  TeamRegistrationChanged: 1,
  SubmissionEvaluated: 2,
  RuntimeStateChanged: 3,
  StartGateFailed: 4,
  ManagementFailure: 5,
  BloodAwarded: 6,
  ChallengePublished: 7,
  HintPublished: 8,
  TeamBanned: 9,
  CompetitionQuestionOpened: 10,
  CompetitionQuestionReplied: 11,
  CompetitionQuestionStatusChanged: 12,
  CheatIncidentDetected: 13,
  TeamBanCorrected: 14,
  DataExportReady: 15,
  DataExportFailed: 16,
} as const satisfies Record<string, NoCtfDomainNotificationsNotificationKind>

export interface PublicNotification {
  id: string
  competitionId: string | null
  entityId: string | null
  kind: NoCtfDomainNotificationsNotificationKind
  payload: unknown
  createdAt: string
}

export interface PublicNotificationPage {
  items: PublicNotification[]
  nextCursor: string | null
}

export function toPublicNotification(
  notification: NoCtfapiEndpointsNotificationsNotificationResponse,
): PublicNotification {
  if (
    !notification
    || typeof notification !== 'object'
    || Array.isArray(notification)
    || !isNonEmptyString(notification.id)
  ) {
    throw new TypeError('Notification response is missing id.')
  }
  if (!isNullableNonEmptyString(notification.competitionId))
    throw new TypeError('Notification response is missing competitionId.')
  if (!isNullableNonEmptyString(notification.entityId))
    throw new TypeError('Notification response is missing entityId.')
  if (!Object.hasOwn(notification, 'payload'))
    throw new TypeError('Notification response is missing payload.')
  if (!isNonEmptyString(notification.createdAt))
    throw new TypeError('Notification response is missing createdAt.')

  return {
    id: notification.id,
    competitionId: notification.competitionId,
    entityId: notification.entityId,
    kind: toNotificationKind(notification.kind),
    payload: notification.payload,
    createdAt: notification.createdAt,
  }
}

export function toPublicNotificationPage(
  response: NoCtfapiEndpointsNotificationsNotificationListResponse,
): PublicNotificationPage {
  if (
    !response
    || typeof response !== 'object'
    || Array.isArray(response)
    || !Array.isArray(response.items)
  ) {
    throw new TypeError('Notification list response is missing items.')
  }
  if (
    response.nextCursor !== null
    && !isNonEmptyString(response.nextCursor)
  ) {
    throw new TypeError('Notification list response is missing nextCursor.')
  }

  return {
    items: response.items.map(toPublicNotification),
    nextCursor: response.nextCursor,
  }
}

export function nextNotificationCursor(
  cursor: string | null,
  seenCursors: ReadonlySet<string>,
): string | null {
  if (cursor === null || seenCursors.has(cursor))
    return null
  return cursor
}

export function nextNotificationPageParam(
  lastPage: PublicNotificationPage,
  pages: PublicNotificationPage[],
): string | undefined {
  const seenCursors = new Set(
    pages
      .slice(0, -1)
      .map(page => page.nextCursor)
      .filter((cursor): cursor is string => cursor !== null),
  )
  return nextNotificationCursor(lastPage.nextCursor, seenCursors) ?? undefined
}

function toNotificationKind(
  kind: NoCtfDomainNotificationsNotificationKind | undefined,
): NoCtfDomainNotificationsNotificationKind {
  switch (kind) {
    case NotificationKind.CompetitionLifecycleChanged:
    case NotificationKind.TeamRegistrationChanged:
    case NotificationKind.SubmissionEvaluated:
    case NotificationKind.RuntimeStateChanged:
    case NotificationKind.StartGateFailed:
    case NotificationKind.ManagementFailure:
    case NotificationKind.BloodAwarded:
    case NotificationKind.ChallengePublished:
    case NotificationKind.HintPublished:
    case NotificationKind.TeamBanned:
    case NotificationKind.CompetitionQuestionOpened:
    case NotificationKind.CompetitionQuestionReplied:
    case NotificationKind.CompetitionQuestionStatusChanged:
    case NotificationKind.CheatIncidentDetected:
    case NotificationKind.TeamBanCorrected:
    case NotificationKind.DataExportReady:
    case NotificationKind.DataExportFailed:
      return kind
    default:
      throw new TypeError('Notification response has an unsupported kind.')
  }
}

function isNullableNonEmptyString(value: unknown): value is string | null {
  return value === null || isNonEmptyString(value)
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}
