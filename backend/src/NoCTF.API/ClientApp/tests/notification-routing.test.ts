import { describe, expect, test } from 'bun:test'
import type { NoCtfapiEndpointsNotificationsNotificationResponse } from '../app/api'
import { notificationThreadRootId, notificationTargetPath } from '../app/utils/labels'

function notification(
  kind: NoCtfapiEndpointsNotificationsNotificationResponse['kind'],
  competitionId?: string,
  content: Record<string, unknown> = {},
): NoCtfapiEndpointsNotificationsNotificationResponse {
  return {
    id: 'notification-1',
    kind,
    content: competitionId ? { competitionId, ...content } : content,
  }
}

describe('notificationTargetPath', () => {
  test('routes cheat incident cards to competition administration', () => {
    expect(notificationTargetPath(notification('CheatIncidentDetected', 'competition-1', {
      gameplayFactId: 'incident-1',
    })))
      .toBe('/admin/competitions/competition-1/cheats?incident=incident-1')
  })

  test('routes challenge and blood notifications to their challenge', () => {
    expect(notificationTargetPath(notification('ChallengePublished', 'competition-1', {
      competitionChallengeId: 'challenge-1',
    })))
      .toBe('/competitions/competition-1/challenges?challenge=challenge-1')
    expect(notificationTargetPath(notification('BloodAwarded', 'competition-1', {
      competitionChallengeId: 'challenge-1',
    })))
      .toBe('/competitions/competition-1/challenges?challenge=challenge-1')
  })

  test('routes question activity to the matching consultation and reads its root thread', () => {
    const item = notification('Message', 'competition-1', { questionId: 'question-1' })
    expect(notificationTargetPath(item))
      .toBe('/competitions/competition-1/questions?question=question-1')
    expect(notificationThreadRootId(item)).toBe('question-1')
  })

  test('routes an immutable question root using its related competition metadata', () => {
    const root: NoCtfapiEndpointsNotificationsNotificationResponse = {
      id: 'question-1',
      kind: 'QuestionOpened',
      relatedId: 'competition-1',
      content: { title: 'private question' },
    }
    expect(notificationTargetPath(root))
      .toBe('/competitions/competition-1/questions?question=question-1')
  })

  test('routes bans and corrections to the ban and appeal section', () => {
    expect(notificationTargetPath(notification('TeamBanned', 'competition-1', {
      teamId: 'team-1',
    })))
      .toBe('/competitions/competition-1/my/team#ban-appeal')
  })

  test('routes ordinary lifecycle messages to the competition event stream', () => {
    expect(notificationTargetPath(notification('CompetitionLifecycleChanged', 'competition-1')))
      .toBe('/competitions/competition-1/events?kind=CompetitionLifecycleChanged')
  })

  test('keeps notification-only kinds out of competition event filters', () => {
    for (const kind of ['StartGateFailed', 'ManagementFailure', 'DataExportReady', 'DataExportFailed'] as const) {
      const path = notificationTargetPath(notification(kind, 'competition-1'))
      expect(path).toBe('/notifications?notification=notification-1')
      expect(path).not.toContain('/events?kind=')
    }
  })

  test('falls back to notification detail when a precise target is missing or unknown', () => {
    expect(notificationTargetPath(notification('ChallengePublished', 'competition-1')))
      .toBe('/notifications?notification=notification-1')
    expect(notificationTargetPath({
      ...notification('ManagementFailure', 'competition-1'),
      kind: 'FutureNotificationKind' as never,
    }))
      .toBe('/notifications?notification=notification-1')
  })

  test('opens notification detail when there is no competition target', () => {
    expect(notificationTargetPath(notification('UserAccountLifecycleChanged')))
      .toBe('/notifications?notification=notification-1')
  })

  test('keeps official competition announcements in the global message center', () => {
    expect(notificationTargetPath(notification('CompetitionAnnouncement', 'competition-1')))
      .toBe('/notifications?notification=notification-1')
  })

  test('notification centers open readable detail and use the generated thread SDK', async () => {
    const component = await Bun.file(
      new URL('../app/components/notifications/NotificationCenter.vue', import.meta.url),
    ).text()
    const competitionPage = await Bun.file(
      new URL('../app/pages/competitions/[id]/notifications.vue', import.meta.url),
    ).text()

    expect(component).toContain('readNotificationThreadEndpoint({')
    expect(component).toContain("scope: 'Inbox'")
    expect(component).toContain('notificationTargetPath(actionTarget.value)')
    expect(component).toContain('sourceLabel(selected)')
    expect(component).toContain('notificationBody(selected)')
    expect(component).toContain('path: { notificationId: selectedId }')
    expect(component).toContain('data.items?.find(item => item.id === selectedId)')
    expect(component).toContain('routeError.value = parseApiError')
    expect(competitionPage).toContain("path: '/notifications'")
    expect(competitionPage).toContain('{ replace: true }')
  })
})
