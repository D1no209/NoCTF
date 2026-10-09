import { afterEach, describe, expect, test } from 'bun:test'
import type { NoCtfapiEndpointsNotificationsNotificationResponse as Notification } from '../app/api'
import { setLocale } from '../app/utils/i18n'
import { notificationBody, notificationTargetPath, notificationTitle } from '../app/utils/labels'
const alert: Notification = { id: 'alert', kind: 'LiveSoloMediaInterrupted', relatedType: 'Competition', relatedId: 'competition',
  content: { type: 'live-solo-media-interrupted', competitionId: 'competition', matchId: 'match', userName: 'Ada', reason: 'ScreenInterrupted', state: 'Disconnected' } }
describe('LiveSolo durable media notices', () => {
  afterEach(() => setLocale('zh-CN'))
  test('links to the exact match, with a safe details fallback when the match is absent', () => {
    expect(notificationTargetPath(alert)).toBe('/competitions/competition/live-solo/matches/match')
    expect(notificationTargetPath({ ...alert, content: {} })).toBe('/notifications?notification=alert')
  })
  test('explains interruption and requires a judge decision in both languages', () => {
    setLocale('en'); expect(notificationTitle(alert)).toContain('LiveSolo')
    expect(notificationBody(alert)).toContain('Ada'); expect(notificationBody(alert)).toContain('does not automatically pause')
    setLocale('zh-CN'); expect(notificationBody(alert)).toContain('Ada'); expect(notificationBody(alert)).toContain('不会自动暂停、判负或修改结果')
  })
  test('room and authorization failures retain separate causes without inventing cheating or a result', () => {
    setLocale('en')
    expect(notificationBody({ ...alert, content: { reason: 'RoomUnavailable' } })).toContain('became unavailable')
    expect(notificationBody({ ...alert, content: { reason: 'AuthorizationChanged' } })).toContain('authorization no longer qualified')
    expect(notificationBody({ ...alert, content: { reason: 'ProgramStalled' } })).toContain('stopped producing new video')
    expect(notificationBody({ ...alert, content: { reason: 'RecordingFailed' } })).toContain('recording could not continue')
  })
})
