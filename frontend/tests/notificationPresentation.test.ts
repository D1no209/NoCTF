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

    expect(Object.values(NotificationKind).slice(0, 6).map((kind, index) =>
      notificationCopy(createNotification(kind, {
        code: 'future_code',
        secret: `ignored-${index}`,
      })),
    )).toEqual(expectedNames.map(name => ({
      titleKey: `notifications.events.${name}.title`,
      bodyKey: `notifications.events.${name}.body`,
    })))
  })

  test('renders public competition events from safe payload fields only', () => {
    expect(notificationCopy(createNotification(
      NotificationKind.BloodAwarded,
      { challengeTitle: 'Web 100', teamName: 'Snow', bloodRank: 2, flag: 'ignored' },
    ))).toEqual({
      titleKey: 'notifications.events.bloodAwarded.title',
      bodyKey: 'notifications.events.bloodAwarded.body',
      bodyParams: { challenge: 'Web 100', team: 'Snow', rank: 2 },
    })
    expect(notificationCopy(createNotification(
      NotificationKind.ChallengePublished,
      { challengeTitle: 'Pwn 100', direction: 'Pwn', definitionJson: 'ignored' },
    ))).toEqual({
      titleKey: 'notifications.events.challengePublished.title',
      bodyKey: 'notifications.events.challengePublished.body',
      bodyParams: { challenge: 'Pwn 100', direction: 'Pwn' },
    })
    expect(notificationCopy(createNotification(
      NotificationKind.HintPublished,
      { challengeTitle: 'Crypto 100', cost: 50, content: 'must not render' },
    ))).toEqual({
      titleKey: 'notifications.events.hintPublished.title',
      bodyKey: 'notifications.events.hintPublished.body',
      bodyParams: { challenge: 'Crypto 100', cost: 50 },
    })
    expect(notificationCopy(createNotification(
      NotificationKind.TeamBanned,
      { teamName: 'Abuse Team', reason: 'must not render' },
    ))).toEqual({
      titleKey: 'notifications.events.teamBanned.title',
      bodyKey: 'notifications.events.teamBanned.body',
      bodyParams: { team: 'Abuse Team' },
    })
    expect(notificationCopy(createNotification(
      NotificationKind.CompetitionQuestionOpened,
      { title: 'Runtime connectivity', body: 'must not render' },
    ))).toEqual({
      titleKey: 'notifications.events.competitionQuestionOpened.title',
      bodyKey: 'notifications.events.competitionQuestionOpened.body',
      bodyParams: { title: 'Runtime connectivity' },
    })
    expect(notificationCopy(createNotification(
      NotificationKind.CompetitionQuestionReplied,
      { title: 'Runtime connectivity', body: 'must not render' },
    ))).toEqual({
      titleKey: 'notifications.events.competitionQuestionReplied.title',
      bodyKey: 'notifications.events.competitionQuestionReplied.body',
      bodyParams: { title: 'Runtime connectivity' },
    })
    expect(notificationCopy(createNotification(
      NotificationKind.CompetitionQuestionStatusChanged,
      { title: 'Runtime connectivity', status: 'Closed' },
    ))).toEqual({
      titleKey: 'notifications.events.competitionQuestionStatusChanged.title',
      bodyKey: 'notifications.events.competitionQuestionStatusChanged.body',
      bodyParams: { title: 'Runtime connectivity' },
    })
    expect(notificationCopy(createNotification(
      NotificationKind.CheatIncidentDetected,
      { submittedFlag: 'must not render', ownerTeamName: 'must not render' },
    ))).toEqual({
      titleKey: 'notifications.events.cheatIncidentDetected.title',
      bodyKey: 'notifications.events.cheatIncidentDetected.body',
    })
    expect(notificationCopy(createNotification(
      NotificationKind.TeamBanCorrected,
      { teamName: 'Snow', reason: 'must not render' },
    ))).toEqual({
      titleKey: 'notifications.events.teamBanCorrected.title',
      bodyKey: 'notifications.events.teamBanCorrected.body',
      bodyParams: { team: 'Snow' },
    })
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
