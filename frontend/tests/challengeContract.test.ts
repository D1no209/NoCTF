import { afterAll, beforeEach, describe, expect, test } from 'bun:test'
import {
  shouldOfferRandomAttachment,
  toPublicChallenge,
} from '../src/api/challengePresentation'
import { client } from '../src/api/generated/client.gen'
import { challengeApi } from '../src/api/noctf'

const apiBaseUrl = 'https://api.noctf.test'
const competitionId = '11111111-1111-1111-1111-111111111111'
const competitionChallengeId = '22222222-2222-2222-2222-222222222222'
const attachmentId = '33333333-3333-3333-3333-333333333333'
const originalClientConfig = client.getConfig()
const requests: Request[] = []

const challenge = {
  id: competitionChallengeId,
  competitionId,
  challengeId: '44444444-4444-4444-4444-444444444444',
  title: 'WEB Notes',
  description: 'Inspect the authorization boundary.',
  direction: 'WEB',
  baseScore: 500,
  order: 1,
  isPublished: true,
  revision: 3,
  deletedAt: null,
  createdAt: '2026-08-01T00:00:00Z',
  updatedAt: '2026-08-01T01:00:00Z',
  controlFlag: 'flag{control}',
  urls: ['https://runtime.noctf.test'],
  leaderboardVisibility: 0,
  dataScope: 0,
} as const

const attachment = {
  id: attachmentId,
  challengeId: challenge.challengeId,
  fileName: 'evidence.zip',
  contentType: 'application/zip',
  byteLength: 3,
  sha256: 'AABBCC',
  deletedAt: null,
  createdAt: '2026-08-01T00:30:00Z',
} as const

const contractFetch: typeof fetch = async (input, init) => {
  const normalizedInput = typeof input === 'string' && input.startsWith('/')
    ? new URL(input, apiBaseUrl)
    : input
  const request = new Request(normalizedInput, init)
  requests.push(request)
  const pathname = new URL(request.url).pathname
  const challengePath = `/api/v1/competitions/${competitionId}/challenges/${competitionChallengeId}`

  if (pathname === `/api/v1/competitions/${competitionId}/challenges`)
    return Response.json({ items: [challenge] })
  if (pathname === `${challengePath}/attachments`)
    return Response.json({ items: [attachment] })
  if (pathname === `${challengePath}/attachments/${attachmentId}`) {
    return new Response(new Uint8Array([1, 2, 3]), {
      headers: {
        'Content-Disposition': 'attachment; filename="evidence.zip"',
        'Content-Type': 'application/zip',
      },
    })
  }
  if (pathname === `${challengePath}/attachment`) {
    return new Response(new Uint8Array([4, 5, 6]), {
      headers: {
        'Content-Disposition': 'attachment; filename*=UTF-8\'\'assigned%20evidence.zip',
        'Content-Type': 'application/zip',
      },
    })
  }
  if (pathname === challengePath)
    return Response.json(challenge)

  return Response.json({}, { status: 404 })
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

describe('generated public challenge contract', () => {
  test('list uses the generated v1 path and unwraps response items', async () => {
    await expect(challengeApi.list(competitionId)).resolves.toEqual([challenge])

    expect(requests).toHaveLength(1)
    expect(new URL(requests[0]!.url).pathname)
      .toBe(`/api/v1/competitions/${competitionId}/challenges`)
  })

  test('get binds competitionId and competitionChallengeId', async () => {
    await expect(challengeApi.get(competitionId, competitionChallengeId)).resolves.toEqual(challenge)

    expect(requests).toHaveLength(1)
    expect(new URL(requests[0]!.url).pathname)
      .toBe(`/api/v1/competitions/${competitionId}/challenges/${competitionChallengeId}`)
  })

  test('preserves hidden scores while rejecting missing identity fields', () => {
    expect(toPublicChallenge({ ...challenge, baseScore: null }).baseScore).toBeNull()
    expect(() => toPublicChallenge({ ...challenge, id: undefined }))
      .toThrow('Challenge response is missing id.')
  })

  test('lists attachment metadata through the generated items envelope', async () => {
    await expect(
      challengeApi.listAttachments(competitionId, competitionChallengeId),
    ).resolves.toEqual([attachment])

    expect(requests).toHaveLength(1)
    expect(new URL(requests[0]!.url).pathname)
      .toBe(`/api/v1/competitions/${competitionId}/challenges/${competitionChallengeId}/attachments`)
  })

  test('downloads a selected attachment through the generated operation', async () => {
    const result = await challengeApi.downloadAttachment(
      competitionId,
      competitionChallengeId,
      attachmentId,
    )

    expect(result.fileName).toBe('evidence.zip')
    expect(result.content).toBeInstanceOf(Blob)
    expect(await result.content.arrayBuffer()).toEqual(new Uint8Array([1, 2, 3]).buffer)
    expect(new URL(requests[0]!.url).pathname)
      .toBe(`/api/v1/competitions/${competitionId}/challenges/${competitionChallengeId}/attachments/${attachmentId}`)
  })

  test('downloads a random team attachment only through the singular generated operation', async () => {
    expect(requests).toHaveLength(0)

    const result = await challengeApi.downloadRandomAttachment(
      competitionId,
      competitionChallengeId,
    )

    expect(result.fileName).toBe('assigned evidence.zip')
    expect(result.content).toBeInstanceOf(Blob)
    expect(new URL(requests[0]!.url).pathname)
      .toBe(`/api/v1/competitions/${competitionId}/challenges/${competitionChallengeId}/attachment`)
  })

  test('treats only an eligible detail-backed 404 as RandomOnePerTeam', () => {
    expect(shouldOfferRandomAttachment(404, true, true)).toBe(true)
    expect(shouldOfferRandomAttachment(404, false, true)).toBe(false)
    expect(shouldOfferRandomAttachment(404, true, false)).toBe(false)
    expect(shouldOfferRandomAttachment(401, true, true)).toBe(false)
    expect(shouldOfferRandomAttachment(403, true, true)).toBe(false)
  })
})
