import { afterAll, beforeEach, describe, expect, test } from 'bun:test'
import { client } from '../src/api/generated/client.gen'
import { teamApi } from '../src/api/noctf'
import { toPublicTeam } from '../src/api/teamPresentation'

const apiBaseUrl = 'https://api.noctf.test'
const competitionId = '11111111-1111-1111-1111-111111111111'
const teamId = '22222222-2222-2222-2222-222222222222'
const originalClientConfig = client.getConfig()
const requests: Request[] = []

const team = {
  id: teamId,
  competitionId,
  name: 'Byte Brigade',
  avatarUrl: null,
  captainId: '33333333-3333-3333-3333-333333333333',
  memberIds: [
    '33333333-3333-3333-3333-333333333333',
    '44444444-4444-4444-4444-444444444444',
  ],
  registrationStatus: 1,
  isLocked: false,
  registeredAt: '2026-08-01T00:00:00Z',
} as const

let myTeamExists = true

const contractFetch: typeof fetch = async (input, init) => {
  const normalizedInput = typeof input === 'string' && input.startsWith('/')
    ? new URL(input, apiBaseUrl)
    : input
  const request = new Request(normalizedInput, init)
  requests.push(request)

  const pathname = new URL(request.url).pathname
  const teamsPath = `/api/v1/competitions/${competitionId}/teams`

  if (pathname === `${teamsPath}/me/membership` && request.method === 'DELETE')
    return new Response(null, { status: 204 })
  if (pathname === `${teamsPath}/me`) {
    return myTeamExists
      ? Response.json(team)
      : Response.json({}, { status: 404 })
  }
  if (pathname === `${teamsPath}/join` && request.method === 'POST')
    return new Response(null, { status: 204 })
  if (pathname === `${teamsPath}/${teamId}` && request.method === 'PUT')
    return Response.json({ ...team, name: 'Renamed Brigade', avatarUrl: 'https://cdn.noctf.test/team.png' })
  if (pathname === teamsPath && request.method === 'POST')
    return Response.json(team, { status: 201 })
  if (pathname === teamsPath)
    return Response.json({ items: [team] })

  return Response.json({}, { status: 404 })
}

beforeEach(() => {
  requests.length = 0
  myTeamExists = true
  client.setConfig({
    baseUrl: apiBaseUrl,
    fetch: contractFetch,
  })
})

afterAll(() => {
  client.setConfig(originalClientConfig)
})

describe('generated public team contract', () => {
  test('maps every generated registration status and rejects incomplete teams', () => {
    const statuses = ['pending', 'approved', 'rejected'] as const

    expect(statuses.map((_, registrationStatus) =>
      toPublicTeam({ ...team, registrationStatus }).registrationStatus,
    )).toEqual(statuses)
    expect(toPublicTeam(team).memberCount).toBe(2)
    expect(() => toPublicTeam({ ...team, id: undefined }))
      .toThrow('Team response is missing id.')
    expect(() => toPublicTeam({ ...team, memberIds: undefined }))
      .toThrow('Team response is missing memberIds.')
  })

  test('lists competition teams through the generated items envelope', async () => {
    await expect(teamApi.list(competitionId)).resolves.toEqual([
      {
        ...team,
        memberIds: [...team.memberIds],
        memberCount: 2,
        registrationStatus: 'approved',
      },
    ])

    expect(requests).toHaveLength(1)
    expect(new URL(requests[0]!.url).pathname)
      .toBe(`/api/v1/competitions/${competitionId}/teams`)
  })

  test('gets my competition team and maps generated 404 to null', async () => {
    await expect(teamApi.getMy(competitionId)).resolves.toMatchObject({
      id: team.id,
      registrationStatus: 'approved',
    })

    myTeamExists = false
    await expect(teamApi.getMy(competitionId)).resolves.toBeNull()

    expect(requests).toHaveLength(2)
    expect(new URL(requests[0]!.url).pathname)
      .toBe(`/api/v1/competitions/${competitionId}/teams/me`)
  })

  test('creates a team with the generated nested request contract', async () => {
    await expect(teamApi.create(competitionId, {
      name: team.name,
      avatarUrl: null,
    })).resolves.toMatchObject({ id: team.id })

    expect(requests).toHaveLength(1)
    expect(requests[0]!.method).toBe('POST')
    expect(new URL(requests[0]!.url).pathname)
      .toBe(`/api/v1/competitions/${competitionId}/teams`)
    expect(await requests[0]!.json()).toEqual({
      name: team.name,
      avatarUrl: null,
    })
  })

  test('updates a team with the generated competition-scoped contract', async () => {
    await expect(teamApi.update(competitionId, teamId, {
      name: 'Renamed Brigade',
      avatarUrl: 'https://cdn.noctf.test/team.png',
    })).resolves.toMatchObject({
      id: teamId,
      name: 'Renamed Brigade',
      avatarUrl: 'https://cdn.noctf.test/team.png',
    })

    expect(requests).toHaveLength(1)
    expect(requests[0]!.method).toBe('PUT')
    expect(new URL(requests[0]!.url).pathname)
      .toBe(`/api/v1/competitions/${competitionId}/teams/${teamId}`)
    expect(await requests[0]!.json()).toEqual({
      name: 'Renamed Brigade',
      avatarUrl: 'https://cdn.noctf.test/team.png',
    })
  })

  test('joins by invitation and leaves with generated void operations', async () => {
    await expect(teamApi.join(competitionId, 'A'.repeat(32))).resolves.toBeUndefined()
    await expect(teamApi.leave(competitionId)).resolves.toBeUndefined()

    expect(requests).toHaveLength(2)
    expect(new URL(requests[0]!.url).pathname)
      .toBe(`/api/v1/competitions/${competitionId}/teams/join`)
    expect(await requests[0]!.json()).toEqual({ invitationToken: 'A'.repeat(32) })
    expect(requests[1]!.method).toBe('DELETE')
    expect(new URL(requests[1]!.url).pathname)
      .toBe(`/api/v1/competitions/${competitionId}/teams/me/membership`)
  })
})
