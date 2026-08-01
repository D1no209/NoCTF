import type { PublicNotification } from '@/api/notificationPresentation'
import { NotificationKind } from '@/api/notificationPresentation'

export interface NotificationCopy {
  titleKey: string
  bodyKey: string
  bodyParams?: Record<string, string | number>
}

export function notificationCopy(notification: PublicNotification): NotificationCopy {
  const payloadCode = readPayloadCode(notification.payload)

  if (
    notification.kind === NotificationKind.RuntimeStateChanged
    && payloadCode === 'awd_flag_injection_failed'
  ) {
    return copy('awdFlagInjectionFailed')
  }
  if (
    notification.kind === NotificationKind.ManagementFailure
    && payloadCode === 'awd_checker_callback_missing'
  ) {
    return copy('awdCheckerCallbackMissing')
  }

  switch (notification.kind) {
    case NotificationKind.CompetitionLifecycleChanged:
      return copy('competitionLifecycleChanged')
    case NotificationKind.TeamRegistrationChanged:
      return copy('teamRegistrationChanged')
    case NotificationKind.SubmissionEvaluated:
      return copy('submissionEvaluated')
    case NotificationKind.RuntimeStateChanged:
      return copy('runtimeStateChanged')
    case NotificationKind.StartGateFailed:
      return copy('startGateFailed')
    case NotificationKind.ManagementFailure:
      return copy('managementFailure')
    case NotificationKind.BloodAwarded:
      return copy('bloodAwarded', {
        challenge: readPayloadString(notification.payload, 'challengeTitle'),
        team: readPayloadString(notification.payload, 'teamName'),
        rank: readPayloadNumber(notification.payload, 'bloodRank') ?? '',
      })
    case NotificationKind.ChallengePublished:
      return copy('challengePublished', {
        challenge: readPayloadString(notification.payload, 'challengeTitle'),
        direction: readPayloadString(notification.payload, 'direction'),
      })
    case NotificationKind.HintPublished:
      return copy('hintPublished', {
        challenge: readPayloadString(notification.payload, 'challengeTitle'),
        cost: readPayloadNumber(notification.payload, 'cost') ?? 0,
      })
    case NotificationKind.TeamBanned:
      return copy('teamBanned', {
        team: readPayloadString(notification.payload, 'teamName'),
      })
    default:
      return copy('unknown')
  }
}

export function formatNotificationTime(value: string, locale: string) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime()))
    return ''

  return new Intl.DateTimeFormat(locale, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(date)
}

function copy(
  name: string,
  bodyParams?: Record<string, string | number | null | undefined>,
): NotificationCopy {
  return {
    titleKey: `notifications.events.${name}.title`,
    bodyKey: `notifications.events.${name}.body`,
    bodyParams: bodyParams
      ? Object.fromEntries(
          Object.entries(bodyParams).map(([key, value]) => [key, value ?? '']),
        )
      : undefined,
  }
}

function readPayloadString(payload: unknown, key: string) {
  const value = readPayloadValue(payload, key)
  return typeof value === 'string' ? value : ''
}

function readPayloadNumber(payload: unknown, key: string) {
  const value = readPayloadValue(payload, key)
  return typeof value === 'number' && Number.isFinite(value) ? value : undefined
}

function readPayloadValue(payload: unknown, key: string): unknown {
  if (!payload || typeof payload !== 'object' || Array.isArray(payload))
    return undefined
  try {
    const prototype = Object.getPrototypeOf(payload)
    if (prototype !== Object.prototype && prototype !== null)
      return undefined
    const descriptor = Object.getOwnPropertyDescriptor(payload, key)
    return descriptor && 'value' in descriptor ? descriptor.value : undefined
  }
  catch {
    return undefined
  }
}

function readPayloadCode(payload: unknown) {
  const value = readPayloadValue(payload, 'code')
  return typeof value === 'string' ? value : undefined
}
