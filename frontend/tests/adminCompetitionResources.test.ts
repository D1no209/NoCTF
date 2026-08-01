import { afterAll, beforeEach, describe, expect, test } from 'bun:test'
import { client } from '../src/api/generated/client.gen'
import { competitionRuntimeAdminApi, competitionTeamAdminApi } from '../src/api/noctf'

const apiBaseUrl = 'https://api.noctf.test'
const competitionId = '11111111-1111-1111-1111-111111111111'
const teamId = '22222222-2222-2222-2222-222222222222'
const challengeId = '33333333-3333-3333-3333-333333333333'
const originalClientConfig = client.getConfig()
const requests: Request[] = []

const team = {
  id: teamId,
  competitionId,
  name: 'Byte Brigade',
  captainId: '44444444-4444-4444-4444-444444444444',
  memberIds: ['44444444-4444-4444-4444-444444444444'],
  registrationStatus: 0,
  isLocked: false,
  registeredAt: '2026-08-01T00:00:00Z',
}

const runtime = {
  id: '55555555-5555-5555-5555-555555555555',
  competitionId,
  competitionChallengeId: challengeId,
  teamId,
  generation: 1,
  runtimeKind: 0,
  provider: 0,
  runnerPool: 'docker',
  state: 2,
  processingVersion: 3,
  urls: ['http://127.0.0.1:61001'],
  createdAt: '2026-08-01T00:00:00Z',
}

const contractFetch: typeof fetch = async (input, init) => {
  const normalizedInput = typeof input === 'string' && input.startsWith('/')
    ? new URL(input, apiBaseUrl)
    : input
  const request = new Request(normalizedInput, init)
  requests.push(request)
  const pathname = new URL(request.url).pathname

  if (pathname === `/api/v1/admin/competitions/${competitionId}/teams`)
    return Response.json({ items: [team] })
  if (pathname === `/api/v1/admin/competitions/${competitionId}/runtimes`)
    return Response.json({ items: [runtime], nextCursor: null })
  if (request.method === 'POST') {
    if (pathname.endsWith('/approve') || pathname.endsWith('/reject') || pathname.endsWith('/ban') || pathname.endsWith('/unban'))
      return new Response(null, { status: 204 })
    if (pathname.endsWith('/runtime/reset') || pathname.endsWith('/runtime/stop'))
      return Response.json({ runtimeInstanceId: runtime.id, statusUrl: `/status/${runtime.id}` }, { status: 202 })
  }

  return Response.json({}, { status: 404 })
}

beforeEach(() => {
  requests.length = 0
  client.setConfig({ baseUrl: apiBaseUrl, fetch: contractFetch })
})

afterAll(() => {
  client.setConfig(originalClientConfig)
})

describe('generated competition-scoped admin resources', () => {
  test('lists and moderates teams through generated v1 operations', async () => {
    await expect(competitionTeamAdminApi.list(competitionId)).resolves.toEqual([team])
    await competitionTeamAdminApi.approve(competitionId, teamId)
    await competitionTeamAdminApi.reject(competitionId, teamId)
    await competitionTeamAdminApi.ban(competitionId, teamId, 'suspected cheat')
    await competitionTeamAdminApi.unban(competitionId, teamId)

    expect(requests.map(request => new URL(request.url).pathname)).toEqual([
      `/api/v1/admin/competitions/${competitionId}/teams`,
      `/api/v1/admin/competitions/${competitionId}/teams/${teamId}/approve`,
      `/api/v1/admin/competitions/${competitionId}/teams/${teamId}/reject`,
      `/api/v1/admin/competitions/${competitionId}/teams/${teamId}/ban`,
      `/api/v1/admin/competitions/${competitionId}/teams/${teamId}/unban`,
    ])
    expect(await requests[3]!.clone().json()).toEqual({ reason: 'suspected cheat' })
  })

  test('lists, resets, and stops a team runtime through generated v1 operations', async () => {
    await expect(competitionRuntimeAdminApi.list(competitionId)).resolves.toEqual([runtime])
    await competitionRuntimeAdminApi.reset(competitionId, runtime)
    await competitionRuntimeAdminApi.stop(competitionId, runtime)

    expect(requests.map(request => new URL(request.url).pathname)).toEqual([
      `/api/v1/admin/competitions/${competitionId}/runtimes`,
      `/api/v1/admin/competitions/${competitionId}/teams/${teamId}/challenges/${challengeId}/runtime/reset`,
      `/api/v1/admin/competitions/${competitionId}/teams/${teamId}/challenges/${challengeId}/runtime/stop`,
    ])
  })
})
