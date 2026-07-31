import type { PublicNotification } from '../src/api/notificationPresentation'
import { describe, expect, test } from 'bun:test'
import { NotificationKind } from '../src/api/notificationPresentation'
import { formatNotificationTime, notificationCopy } from '../src/components/notifications/notificationPresentation'

describe('notification presentation', () => {
  test('uses dedicated copy only for the two confirmed failure payloads', () => {
    expect(notificationCopy(createNotification(
      NotificationKind.RuntimeStateChanged,
      { code: 'awd_flag_injection_failed', generation: 7 },
    ))).toEqual({
      titleKey: 'notifications.events.awdFlagInjectionFailed.title',
      bodyKey: 'notifications.events.awdFlagInjectionFailed.body',
    })

    expect(notificationCopy(createNotification(
      NotificationKind.ManagementFailure,
      { code: 'awd_checker_callback_missing', checkerSequence: 12 },
    ))).toEqual({
      titleKey: 'notifications.events.awdCheckerCallbackMissing.title',
      bodyKey: 'notifications.events.awdCheckerCallbackMissing.body',
    })
  })

  test('maps every bounded kind without passing arbitrary payload into translations', () => {
    const expectedNames = [
      'competitionLifecycleChanged',
      'teamRegistrationChanged',
      'submissionEvaluated',
      'runtimeStateChanged',
      'startGateFailed',
      'managementFailure',
    ]

    expect(Object.values(NotificationKind).map((kind, index) =>
      notificationCopy(createNotification(kind, {
        code: 'future_code',
        secret: `ignored-${index}`,
      })),
    )).toEqual(expectedNames.map(name => ({
      titleKey: `notifications.events.${name}.title`,
      bodyKey: `notifications.events.${name}.body`,
    })))
  })

  test('uses safe fallback copy for a future unsupported kind', () => {
    expect(notificationCopy(createNotification(99 as never, { secret: 'ignored' }))).toEqual({
      titleKey: 'notifications.events.unknown.title',
      bodyKey: 'notifications.events.unknown.body',
    })
  })

  test('does not evaluate or interpolate hostile payload values', () => {
    const payload = {}
    Object.defineProperty(payload, 'code', {
      get() {
        throw new Error('payload getter must not run')
      },
    })

    expect(() => notificationCopy(createNotification(
      NotificationKind.ManagementFailure,
      payload,
    ))).not.toThrow()
    expect(notificationCopy(createNotification(
      NotificationKind.ManagementFailure,
      payload,
    ))).toEqual({
      titleKey: 'notifications.events.managementFailure.title',
      bodyKey: 'notifications.events.managementFailure.body',
    })

    const proxy = new Proxy({}, {
      getPrototypeOf() {
        throw new Error('payload proxy must not run')
      },
    })
    expect(() => notificationCopy(createNotification(
      NotificationKind.ManagementFailure,
      proxy,
    ))).not.toThrow()
  })

  test('returns an empty timestamp for invalid server data', () => {
    expect(formatNotificationTime('not-a-date', 'en-US')).toBe('')
    expect(formatNotificationTime('2026-07-17T08:30:00Z', 'en-US')).not.toBe('')
  })
})

function createNotification(
  kind: PublicNotification['kind'],
  payload: unknown,
): PublicNotification {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    competitionId: '22222222-2222-2222-2222-222222222222',
    entityId: '33333333-3333-3333-3333-333333333333',
    kind,
    payload,
    createdAt: '2026-07-17T08:30:00Z',
  }
}
