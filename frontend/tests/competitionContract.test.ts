import { afterAll, beforeEach, describe, expect, test } from 'bun:test'
import { toPublicCompetition } from '../src/api/competitionPresentation'
import { client } from '../src/api/generated/client.gen'
import { competitionApi } from '../src/api/noctf'

const apiBaseUrl = 'https://api.noctf.test'
const originalClientConfig = client.getConfig()
const requests: Request[] = []

const competition = {
  id: '11111111-1111-1111-1111-111111111111',
  title: 'NoCTF Finals',
  description: 'Four supported modes, one arena.',
  mode: 2,
  startTime: '2026-08-01T00:00:00Z',
  endTime: '2026-08-02T00:00:00Z',
  status: 3,
  teamRegistrationAutoApprove: true,
  maxTeamMembers: 5,
  ownerId: '22222222-2222-2222-2222-222222222222',
} as const

const contractFetch: typeof fetch = async (input, init) => {
  const normalizedInput = typeof input === 'string' && input.startsWith('/')
    ? new URL(input, apiBaseUrl)
    : input
  const request = new Request(normalizedInput, init)
  requests.push(request)

  const pathname = new URL(request.url).pathname
  if (pathname === '/api/v1/competitions')
    return Response.json({ items: [competition] })
  if (pathname === `/api/v1/competitions/${competition.id}`)
    return Response.json(competition)

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

describe('generated public competition contract', () => {
  test('list uses the generated v1 path and unwraps the response items', async () => {
    await expect(competitionApi.list()).resolves.toEqual([
      {
        ...competition,
        mode: 'awdp',
        status: 'running',
      },
    ])

    expect(requests).toHaveLength(1)
    expect(new URL(requests[0]!.url).pathname).toBe('/api/v1/competitions')
  })

  test('get uses the generated competitionId path parameter', async () => {
    await expect(competitionApi.get(competition.id)).resolves.toMatchObject({
      id: competition.id,
      mode: 'awdp',
      status: 'running',
    })

    expect(requests).toHaveLength(1)
    expect(new URL(requests[0]!.url).pathname)
      .toBe(`/api/v1/competitions/${competition.id}`)
  })

  test('maps every generated mode and status value to its presentation key', () => {
    const modes = ['ctf', 'awd', 'awdp', 'koh'] as const
    const statuses = ['draft', 'visible', 'published', 'running', 'paused', 'finished'] as const

    expect(modes.map((_, mode) => toPublicCompetition({ ...competition, mode }).mode))
      .toEqual(modes)
    expect(statuses.map((_, status) => toPublicCompetition({ ...competition, status }).status))
      .toEqual(statuses)
  })

  test('rejects incomplete generated responses instead of inventing fields', () => {
    expect(() => toPublicCompetition({ ...competition, id: undefined }))
      .toThrow('Competition response is missing id.')
    expect(() => toPublicCompetition({ ...competition, mode: undefined }))
      .toThrow('Competition response has an unsupported mode.')
  })
})
