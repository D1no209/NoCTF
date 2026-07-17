import type { UserNotification } from '../src/api/noctf'
import { describe, expect, test } from 'bun:test'
import { formatNotificationTime, notificationCopy } from '../src/components/notifications/notificationPresentation'

describe('notification presentation', () => {
  test('maps confirmed event types without interpreting payload data', () => {
    const notification = createNotification('blood.first', {
      team_name: 'Team Pixel',
      problem_title: 'Warmup',
    })

    expect(notificationCopy(notification)).toEqual({
      titleKey: 'notifications.events.firstBlood.title',
      bodyKey: 'notifications.events.firstBlood.body',
      params: notification.data,
    })
  })

  test('uses safe fallback copy for unknown event types', () => {
    expect(notificationCopy(createNotification('future.event', { secret: 'ignored by copy' }))).toEqual({
      titleKey: 'notifications.events.unknown.title',
      bodyKey: 'notifications.events.unknown.body',
      params: {},
    })
  })

  test('returns an empty timestamp for invalid server data', () => {
    expect(formatNotificationTime('not-a-date', 'en-US')).toBe('')
    expect(formatNotificationTime('2026-07-17T08:30:00Z', 'en-US')).not.toBe('')
  })
})

function createNotification(type: string, data: Record<string, string>): UserNotification {
  return {
    id: 'notification-id',
    competitionId: 'competition-id',
    type,
    data,
    isRead: false,
    createdAt: '2026-07-17T08:30:00Z',
  }
}
