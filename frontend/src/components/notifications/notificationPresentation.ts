import type { PublicNotification } from '@/api/notificationPresentation'
import { NotificationKind } from '@/api/notificationPresentation'

export interface NotificationCopy {
  titleKey: string
  bodyKey: string
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

function copy(name: string): NotificationCopy {
  return {
    titleKey: `notifications.events.${name}.title`,
    bodyKey: `notifications.events.${name}.body`,
  }
}

function readPayloadCode(payload: unknown) {
  if (!payload || typeof payload !== 'object' || Array.isArray(payload))
    return undefined
  try {
    const prototype = Object.getPrototypeOf(payload)
    if (prototype !== Object.prototype && prototype !== null)
      return undefined
    const descriptor = Object.getOwnPropertyDescriptor(payload, 'code')
    return descriptor && 'value' in descriptor && typeof descriptor.value === 'string'
      ? descriptor.value
      : undefined
  }
  catch {
    return undefined
  }
}
