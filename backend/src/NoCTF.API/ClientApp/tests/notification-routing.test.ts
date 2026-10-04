import { sourceFile } from './support/feature-source'
import { afterEach, describe, expect, test } from 'bun:test'
import type { NoCTFAPIEndpointsNotificationsNotificationResponse } from '../app/api/models'
import { setLocale } from '../app/utils/i18n'
import { notificationBody, notificationThreadRootId, notificationTargetPath } from '../app/utils/labels'

function notification(
  kind: NoCTFAPIEndpointsNotificationsNotificationResponse['kind'],
  competitionId?: string,
  content: Record<string, unknown> = {},
): NoCTFAPIEndpointsNotificationsNotificationResponse {
  return {
    id: 'notification-1',
    kind,
    content: competitionId ? { competitionId, ...content } : content,
  }
}

describe('notificationTargetPath', () => {
  afterEach(() => setLocale('zh-CN'))

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
      .toBe('/competitions/competition-1/challenges/challenge-1')
    expect(notificationTargetPath(notification('BloodAwarded', 'competition-1', {
      competitionChallengeId: 'challenge-1',
    })))
      .toBe('/competitions/competition-1/challenges/challenge-1')
  })

  test('routes adjudicated submissions to the matching challenge history', () => {
    expect(notificationTargetPath(notification('GameplayFactAdjudicated', 'competition-1', {
      competitionChallengeId: 'challenge-1',
      gameplayFactId: 'fact-1',
    })))
      .toBe('/competitions/competition-1/challenges/challenge-1')
  })

  test('routes question activity to the matching consultation and reads its root thread', () => {
    const item = notification('Message', 'competition-1', { threadRootId: 'question-1' })
    expect(notificationTargetPath(item))
      .toBe('/competitions/competition-1/questions?question=question-1')
    expect(notificationThreadRootId(item)).toBe('question-1')
  })

  test('uses the generated thread root as the canonical consultation identity', () => {
    const item = {
      ...notification('Message', 'competition-1'),
      threadRootId: 'thread-root',
    }
    expect(notificationTargetPath(item))
      .toBe('/competitions/competition-1/questions?question=thread-root')
    expect(notificationThreadRootId(item)).toBe('thread-root')
  })

  test('routes an immutable question root using its related competition metadata', () => {
    const root: NoCTFAPIEndpointsNotificationsNotificationResponse = {
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
    expect(notificationTargetPath(notification('TeamBanAppealSubmitted', 'competition-1', {
      appealEventId: 'appeal-1',
    })))
      .toBe('/admin/competitions/competition-1/teams?appeal=appeal-1#ban-appeals')
  })

  test('routes ordinary lifecycle messages to the competition event stream', () => {
    expect(notificationTargetPath(notification('CompetitionLifecycleChanged', 'competition-1')))
      .toBe('/competitions/competition-1/events?kind=CompetitionLifecycleChanged')
  })

  test('keeps notification-only kinds out of competition event filters', () => {
    for (const kind of ['StartGateFailed', 'ManagementFailure', 'PlatformAuditExported'] as const) {
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

  test('localizes account lifecycle details without exposing internal reason codes', () => {
    const item = notification('UserAccountLifecycleChanged', undefined, {
      targetUserName: 'IssueJuice',
      action: 'EmailVerified',
      reason: 'manual_verify_email',
    })

    expect(notificationBody(item)).toBe('已手动验证用户「IssueJuice」的邮箱。')
    expect(notificationBody(item)).not.toContain('manual_verify_email')

    setLocale('en')
    expect(notificationBody(item)).toBe('Manually verified the email address for “IssueJuice”.')
  })

  test('keeps official competition announcements in the global message center', () => {
    expect(notificationTargetPath(notification('CompetitionAnnouncement', 'competition-1')))
      .toBe('/notifications?notification=notification-1')
  })

  test('the canonical notification center opens readable detail with the generated thread SDK', async () => {
    const component = await sourceFile(
      new URL('../app/features/notifications/NotificationCenter.vue', import.meta.url),
    ).text()
    expect(component).toMatch(/api\.api\.v1\.notifications\.byNotificationId\([^)]*\)\.thread\.get\(/)
    expect(component).toContain("scope: 'Inbox'")
    expect(component).toContain('notificationTargetPath(actionTarget.value)')
    expect(component).toContain('sourceLabel(selected)')
    expect(component).toContain('notificationBody(selected)')
    expect(component).toContain('.notifications.byNotificationId(selectedId)')
    expect(component).toContain('data.items?.find(item => item.id === selectedId)')
    expect(component).toContain('routeError.value = parseApiError')
  })
})
