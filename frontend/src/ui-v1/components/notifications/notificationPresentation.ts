import type { UserNotification } from '@/features/notifications/useNotificationCenter'

export interface NotificationCopy {
  titleKey: string
  bodyKey: string
  params: Record<string, string>
}

export function notificationCopy(notification: UserNotification): NotificationCopy {
  const params = notification.data ?? {}

  switch (notification.type) {
    case 'competition.started':
      return copy('competitionStarted', params)
    case 'challenge.published':
      return copy('challengePublished', params)
    case 'hint.published':
      return copy('hintPublished', params)
    case 'blood.first':
      return copy('firstBlood', params)
    case 'blood.second':
      return copy('secondBlood', params)
    case 'blood.third':
      return copy('thirdBlood', params)
    case 'team.penalized':
      return copy('teamPenalized', params)
    case 'announcement':
      return copy('announcement', params)
    default:
      return copy('unknown', {})
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

function copy(name: string, params: Record<string, string>): NotificationCopy {
  return {
    titleKey: `notifications.events.${name}.title`,
    bodyKey: `notifications.events.${name}.body`,
    params,
  }
}
