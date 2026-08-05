import { afterAll, beforeEach, describe, expect, test } from 'bun:test'
import { client } from '../src/api/generated/client.gen'
import { questionApi } from '../src/api/questionApi'

const apiBaseUrl = 'https://api.noctf.test'
const competitionId = '11111111-1111-1111-1111-111111111111'
const questionId = '22222222-2222-2222-2222-222222222222'
const replyEntryId = '33333333-3333-3333-3333-333333333333'
const originalClientConfig = client.getConfig()
const requests: Request[] = []

const question = {
  id: questionId,
  competitionId,
  competitionChallengeId: null,
  teamId: '44444444-4444-4444-4444-444444444444',
  askedByUserId: '55555555-5555-5555-5555-555555555555',
  askerDisplayName: 'participant',
  teamDisplayName: 'Snow',
  submissionId: null,
  subject: 'Platform',
  title: 'Runtime connectivity',
  body: 'The assigned runtime cannot be reached.',
  status: 'Pending',
  access: 'Asker',
  revision: 1,
  publishedAt: null,
  createdAt: '2026-08-05T00:00:00Z',
  updatedAt: '2026-08-05T00:00:00Z',
  canReply: true,
  canResolve: false,
  canClose: false,
  canPublish: false,
  canViewPrivate: true,
  entries: [],
} as const

const contractFetch: typeof fetch = async (input, init) => {
  const normalizedInput = typeof input === 'string' && input.startsWith('/')
    ? new URL(input, apiBaseUrl)
    : input
  const request = new Request(normalizedInput, init)
  requests.push(request)
  const url = new URL(request.url)
  const basePath = `/api/v1/competitions/${competitionId}/questions`

  if (url.pathname === basePath && request.method === 'GET')
    return Response.json({ items: [question] })
  if (url.pathname === basePath && request.method === 'POST')
    return Response.json(question, { status: 201 })
  if (url.pathname === `${basePath}/${questionId}` && request.method === 'GET')
    return Response.json(question)
  if (url.pathname === `${basePath}/${questionId}/messages`)
    return Response.json({ ...question, status: 'Replied', revision: 2 })
  if (url.pathname === `${basePath}/${questionId}/status`)
    return Response.json({ ...question, status: 'Resolved', revision: 2 })
  if (url.pathname === `${basePath}/${questionId}/publication`)
    return Response.json({ ...question, publishedAt: '2026-08-05T01:00:00Z', revision: 2 })

  return Response.json({}, { status: 404 })
}

beforeEach(() => {
  requests.length = 0
  client.setConfig({ baseUrl: apiBaseUrl, fetch: contractFetch })
})

afterAll(() => client.setConfig(originalClientConfig))

describe('generated private competition question contract', () => {
  test('does not render a link to the removed AWDP screen route', async () => {
    const workspaceSource = await Bun.file(
      new URL('../src/components/competition-detail/CompetitionDetailWorkspace.vue', import.meta.url),
    ).text()

    expect(workspaceSource).not.toMatch(/name:\s*['"]awdp-screen['"]/)
  })

  test('lists a strongly typed platform queue through the generated operation', async () => {
    await expect(questionApi.list(competitionId, {
      subject: 'Platform',
      publishedOnly: false,
    })).resolves.toEqual([question])

    const request = requests[0]!
    const url = new URL(request.url)
    expect(request.method).toBe('GET')
    expect(url.pathname).toBe(`/api/v1/competitions/${competitionId}/questions`)
    expect(url.searchParams.get('subject')).toBe('Platform')
    expect(url.searchParams.get('publishedOnly')).toBe('false')
  })

  test('creates a private platform question without an attachment contract', async () => {
    await expect(questionApi.create(competitionId, {
      subject: 'Platform',
      competitionChallengeId: null,
      submissionId: null,
      title: question.title,
      body: question.body,
    })).resolves.toEqual(question)

    const request = requests[0]!
    expect(request.method).toBe('POST')
    expect(await request.json()).toEqual({
      subject: 'Platform',
      competitionChallengeId: null,
      submissionId: null,
      title: question.title,
      body: question.body,
    })
  })

  test('uses generated detail, reply, status, and publication operations', async () => {
    await questionApi.get(competitionId, questionId)
    await questionApi.addMessage(competitionId, questionId, {
      body: 'Please retry after the runner restart.',
      expectedRevision: 1,
    })
    await questionApi.changeStatus(competitionId, questionId, {
      status: 'Resolved',
      expectedRevision: 1,
    })
    await questionApi.publish(competitionId, questionId, {
      replyEntryId,
      expectedRevision: 1,
    })

    expect(requests.map(request => `${request.method} ${new URL(request.url).pathname}`)).toEqual([
      `GET /api/v1/competitions/${competitionId}/questions/${questionId}`,
      `POST /api/v1/competitions/${competitionId}/questions/${questionId}/messages`,
      `PUT /api/v1/competitions/${competitionId}/questions/${questionId}/status`,
      `PUT /api/v1/competitions/${competitionId}/questions/${questionId}/publication`,
    ])
  })
})
