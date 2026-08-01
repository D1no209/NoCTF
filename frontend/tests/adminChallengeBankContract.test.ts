import { afterAll, beforeEach, describe, expect, test } from 'bun:test'
import { client } from '../src/api/generated/client.gen'
import { challengeBankAdminApi } from '../src/api/noctf'

const apiBaseUrl = 'https://api.noctf.test'
const challengeId = '11111111-1111-1111-1111-111111111111'
const originalClientConfig = client.getConfig()
const requests: Request[] = []

const template = {
  id: challengeId,
  ownerId: '22222222-2222-2222-2222-222222222222',
  managerIds: [],
  mode: 0,
  visibility: 0,
  title: 'Web Entry',
  description: null,
  direction: 'WEB',
  definitionJson: '{}',
  revision: 0,
  deletedAt: null,
  activeCompetitionReferenceCount: 0,
}

const contractFetch: typeof fetch = async (input, init) => {
  const normalizedInput = typeof input === 'string' && input.startsWith('/')
    ? new URL(input, apiBaseUrl)
    : input
  const request = new Request(normalizedInput, init)
  requests.push(request)

  const pathname = new URL(request.url).pathname
  if (pathname === '/api/v1/admin/challenges' && request.method === 'POST')
    return Response.json(template, { status: 201 })
  if (pathname === `/api/v1/admin/challenges/${challengeId}/attachments` && request.method === 'POST') {
    return Response.json({
      id: '33333333-3333-3333-3333-333333333333',
      challengeId,
      fileName: 'challenge.zip',
      contentType: 'application/zip',
    }, { status: 201 })
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

describe('generated challenge bank write contract', () => {
  test('creates a template and uploads its attachment through v1 multipart operations', async () => {
    await expect(challengeBankAdminApi.create({
      mode: 'Ctf',
      visibility: 'Private',
      title: template.title,
      direction: template.direction,
      definitionJson: template.definitionJson,
    })).resolves.toEqual(template)

    const file = new File(['archive'], 'challenge.zip', { type: 'application/zip' })
    await expect(challengeBankAdminApi.uploadAttachment(challengeId, file))
      .resolves.toMatchObject({ challengeId, fileName: file.name })

    expect(requests.map(request => [request.method, new URL(request.url).pathname])).toEqual([
      ['POST', '/api/v1/admin/challenges'],
      ['POST', `/api/v1/admin/challenges/${challengeId}/attachments`],
    ])
    expect(await requests[0]!.clone().json()).toMatchObject({
      mode: 'Ctf',
      visibility: 'Private',
      definitionJson: '{}',
    })
    const form = await requests[1]!.clone().formData()
    expect((form.get('file') as File).name).toBe(file.name)
  })
})
