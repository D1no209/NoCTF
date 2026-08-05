import { afterAll, beforeEach, describe, expect, test } from 'bun:test'
import { client } from '../src/api/generated/client.gen'
import { notificationApi } from '../src/api/noctf'
import {
  nextNotificationCursor,
  nextNotificationPageParam,
  NotificationKind,
  toPublicNotification,
  toPublicNotificationPage,
} from '../src/api/notificationPresentation'

const apiBaseUrl = 'https://api.noctf.test'
const opaqueCursor = 'eyJvZmZzZXQiOiIrLz0ifQ.signature+/='
const originalClientConfig = client.getConfig()
const requests: Request[] = []

const runtimeFailure = {
  id: '11111111-1111-1111-1111-111111111111',
  competitionId: '22222222-2222-2222-2222-222222222222',
  entityId: '33333333-3333-3333-3333-333333333333',
  kind: 3,
  payload: {
    code: 'awd_flag_injection_failed',
    generation: 7,
  },
  createdAt: '2026-08-01T00:00:00Z',
} as const

const contractFetch: typeof fetch = async (input, init) => {
  const normalizedInput = typeof input === 'string' && input.startsWith('/')
    ? new URL(input, apiBaseUrl)
    : input
  const request = new Request(normalizedInput, init)
  requests.push(request)

  const url = new URL(request.url)
  if (url.pathname !== '/api/v1/notifications')
    return Response.json({}, { status: 404 })

  if (url.searchParams.get('cursor') === opaqueCursor) {
    return Response.json({
      items: [],
      nextCursor: null,
    })
  }

  return Response.json({
    items: [runtimeFailure],
    nextCursor: opaqueCursor,
  })
}

beforeEach(() => {
  requests.length = 0
  client.setConfig({
    baseUrl: apiBaseUrl,
    fetch: contractFetch,
  })
})

afterAll(() => {
  client.setConfig(originalClientConfig)
})

describe('generated permanent notification contract', () => {
  test('lists the latest page through the generated v1 operation', async () => {
    await expect(notificationApi.listPage({ limit: 20 })).resolves.toEqual({
      items: [runtimeFailure],
      nextCursor: opaqueCursor,
    })

    expect(requests).toHaveLength(1)
    const url = new URL(requests[0]!.url)
    expect(url.pathname).toBe('/api/v1/notifications')
    expect(url.searchParams.get('limit')).toBe('20')
    expect(url.searchParams.has('cursor')).toBe(false)
  })

  test('passes an opaque cursor verbatim and exposes no read mutations', async () => {
    await expect(
      notificationApi.listPage({ cursor: opaqueCursor, limit: 20 }),
    ).resolves.toEqual({
      items: [],
      nextCursor: null,
    })

    expect(new URL(requests[0]!.url).searchParams.get('cursor')).toBe(opaqueCursor)
    expect('markRead' in notificationApi).toBe(false)
    expect('markAllRead' in notificationApi).toBe(false)
  })

  test('maps all bounded kinds and rejects unsupported or incomplete events', () => {
    expect(NotificationKind).toEqual({
      CompetitionLifecycleChanged: 0,
      TeamRegistrationChanged: 1,
      SubmissionEvaluated: 2,
      RuntimeStateChanged: 3,
      StartGateFailed: 4,
      ManagementFailure: 5,
      BloodAwarded: 6,
      ChallengePublished: 7,
      HintPublished: 8,
      TeamBanned: 9,
      CompetitionQuestionOpened: 10,
      CompetitionQuestionReplied: 11,
      CompetitionQuestionStatusChanged: 12,
      CheatIncidentDetected: 13,
      TeamBanCorrected: 14,
      DataExportReady: 15,
      DataExportFailed: 16,
    })

    for (const kind of Object.values(NotificationKind)) {
      expect(toPublicNotification({ ...runtimeFailure, kind }).kind).toBe(kind)
    }

    expect(() => toPublicNotification({ ...runtimeFailure, kind: 17 as never }))
      .toThrow('Notification response has an unsupported kind.')
    expect(() => toPublicNotification({ ...runtimeFailure, id: undefined }))
      .toThrow('Notification response is missing id.')
    expect(() => toPublicNotification({ ...runtimeFailure, id: 42 as never }))
      .toThrow('Notification response is missing id.')
    expect(() => toPublicNotification({ ...runtimeFailure, id: ' ' }))
      .toThrow('Notification response is missing id.')
    expect(() => toPublicNotification(null as never))
      .toThrow('Notification response is missing id.')
    expect(() => toPublicNotification({ ...runtimeFailure, competitionId: '' }))
      .toThrow('Notification response is missing competitionId.')
    expect(() => toPublicNotification({ ...runtimeFailure, entityId: {} as never }))
      .toThrow('Notification response is missing entityId.')
    expect(() => toPublicNotification({ ...runtimeFailure, entityId: ' ' }))
      .toThrow('Notification response is missing entityId.')
    expect(() => toPublicNotification({ ...runtimeFailure, createdAt: 42 as never }))
      .toThrow('Notification response is missing createdAt.')
    expect(() => toPublicNotificationPage({ nextCursor: null }))
      .toThrow('Notification list response is missing items.')
    expect(() => toPublicNotificationPage(null as never))
      .toThrow('Notification list response is missing items.')
    expect(() => toPublicNotificationPage({ items: {} as never, nextCursor: null }))
      .toThrow('Notification list response is missing items.')
    expect(() => toPublicNotificationPage({ items: [runtimeFailure] }))
      .toThrow('Notification list response is missing nextCursor.')
    expect(() => toPublicNotificationPage({
      items: [runtimeFailure],
      nextCursor: 42 as never,
    })).toThrow('Notification list response is missing nextCursor.')
    expect(() => toPublicNotificationPage({
      items: [runtimeFailure],
      nextCursor: '',
    })).toThrow('Notification list response is missing nextCursor.')
    expect(() => toPublicNotificationPage({
      items: [runtimeFailure],
      nextCursor: ' ',
    })).toThrow('Notification list response is missing nextCursor.')
  })

  test('stops pagination when the server repeats or cycles to a seen cursor', () => {
    expect(nextNotificationCursor(opaqueCursor, new Set())).toBe(opaqueCursor)
    expect(nextNotificationCursor(null, new Set())).toBeNull()
    expect(nextNotificationCursor(opaqueCursor, new Set([opaqueCursor]))).toBeNull()

    const firstPage = toPublicNotificationPage({
      items: [runtimeFailure],
      nextCursor: opaqueCursor,
    })
    const repeatedPage = toPublicNotificationPage({
      items: [],
      nextCursor: opaqueCursor,
    })
    expect(nextNotificationPageParam(firstPage, [firstPage])).toBe(opaqueCursor)
    expect(nextNotificationPageParam(repeatedPage, [firstPage, repeatedPage]))
      .toBeUndefined()
  })
})
