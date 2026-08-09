import { describe, expect, test } from 'bun:test'
import type { NoCtfapiEndpointsNotificationsNotificationResponse } from '../app/api'
import { notificationTargetPath } from '../app/utils/labels'

function notification(
  kind: NoCtfapiEndpointsNotificationsNotificationResponse['kind'],
  competitionId?: string,
): NoCtfapiEndpointsNotificationsNotificationResponse {
  return {
    kind,
    content: competitionId ? { competitionId } : {},
  }
}

describe('notificationTargetPath', () => {
  test('routes cheat incident cards to competition administration', () => {
    expect(notificationTargetPath(notification('CheatIncidentDetected', 'competition-1')))
      .toBe('/admin/competitions/competition-1/cheats')
  })

  test('keeps ordinary competition cards in the participant workspace', () => {
    expect(notificationTargetPath(notification('CompetitionLifecycleChanged', 'competition-1')))
      .toBe('/competitions/competition-1')
  })

  test('keeps notifications without a competition on the notification page', () => {
    expect(notificationTargetPath(notification('UserAccountLifecycleChanged')))
      .toBe('/notifications')
  })

  test('notification cards use the centralized target resolver', async () => {
    const page = await Bun.file(new URL('../app/pages/notifications.vue', import.meta.url)).text()
    expect(page).toContain(':to="notificationTargetPath(notification)"')
  })
})
