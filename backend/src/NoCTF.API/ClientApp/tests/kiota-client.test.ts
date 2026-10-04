import { afterEach, expect, test } from 'bun:test'
import { api, createApiClient, initializeApiClient, multipartBody, nativeResponse, ProjectionResponseOption, RequestPolicyOption, ResponseMetadata } from '../app/lib/api'
import { createNoCTFAPIEndpointsCompetitionsScoreboardSnapshotResponseFromDiscriminatorValue } from '../app/api/models'
import { getAccessToken, setAccessToken, refreshSession } from '../app/lib/session'
import { parseApiError } from '../app/utils/api-error'

const originalFetch = globalThis.fetch
const competitionId = '10000000-0000-4000-8000-000000000001'
const jwt = (signature: string, subject = competitionId) => `${btoa('{}')}.${btoa(JSON.stringify({ sub: subject }))}.${signature}`
const oldAccess = jwt('old')
const newAccess = jwt('new')
const challengeId = '20000000-0000-4000-8000-000000000002'
const hooks = { token: getAccessToken, refresh: refreshSession, impersonating: () => false, endImpersonation() {} }
afterEach(() => { globalThis.fetch = originalFetch; setAccessToken(null); initializeApiClient(hooks) })

test('generated login deserializes dates and keeps optional headers outside JSON', async () => {
  let sent: Request | undefined
  let credentials: RequestCredentials | undefined
  globalThis.fetch = (async (input, init) => {
    credentials = init?.credentials
    sent = input as Request
    return Response.json({ accessToken: 'access', expiresAt: '2026-10-05T00:00:00Z', role: 'Administrator' })
  }) as typeof fetch
  const result = await createApiClient(false).api.v1.auth.login.post({ login: 'user', password: 'password' },
    { headers: { 'X-NoCTF-Human-Verification': 'proof' } })
  expect(result?.expiresAt).toBeInstanceOf(Date)
  expect(result?.role).toBe('Administrator')
  expect(sent?.headers.get('X-NoCTF-Human-Verification')).toBe('proof')
  expect(await sent?.json()).toEqual({ login: 'user', password: 'password' })
  expect(credentials).toBe('same-origin')
})

test('concurrent 401 responses refresh once and replay each request only once', async () => {
  setAccessToken(oldAccess)
  initializeApiClient(hooks)
  let refreshes = 0
  let attempts = 0
  const keys: string[] = []
  globalThis.fetch = (async input => {
    const request = input as Request
    if (new URL(request.url).pathname.endsWith('/auth/refresh')) {
      refreshes++
      await new Promise(resolve => setTimeout(resolve, 5))
      expect(request.headers.has('Authorization')).toBe(false)
      return Response.json({ accessToken: newAccess })
    }
    attempts++
    keys.push(request.headers.get('Idempotency-Key')!)
    return request.headers.get('Authorization') === `Bearer ${oldAccess}`
      ? new Response(null, { status: 401 }) : Response.json({ description: 'saved' })
  }) as typeof fetch
  await Promise.all([
    api.api.v1.auth.me.profile.patch({ profile: { description: 'one' } }),
    api.api.v1.auth.me.profile.patch({ profile: { description: 'two' } }),
  ])
  expect(refreshes).toBe(1)
  expect(attempts).toBe(4)
  expect(keys[0]).toBe(keys[2])
  expect(keys[1]).toBe(keys[3])
})

test('429 is not retried and keeps its status without a problem body', async () => {
  let count = 0
  globalThis.fetch = (async () => { count++; return new Response(null, { status: 429 }) }) as typeof fetch
  const error = await createApiClient(false).api.v1.auth.login.post({ login: 'user', password: 'password' }).catch(e => e)
  expect(count).toBe(1)
  expect(parseApiError(error).status).toBe(429)
})

test('multipart uploads preserve original names, multiple files and exact bytes', async () => {
  let form: FormData | undefined
  globalThis.fetch = (async input => { form = await (input as Request).formData(); return Response.json({ items: [] }) }) as typeof fetch
  await createApiClient(false).api.v1.admin.challenges.byChallengeId(challengeId).attachments.post(await multipartBody({
    deliveryPolicy: 'All', files: [new File(['first'], 'first.txt'), new File(['第二个'], 'second.txt')],
  }))
  expect(form?.get('deliveryPolicy')).toBe('All')
  const files = form?.getAll('files') as File[]
  expect(files.map(file => file.name)).toEqual(['first.txt', 'second.txt'])
  expect(await Promise.all(files.map(file => file.text()))).toEqual(['first', '第二个'])
})

test('an uncertain upload reuses its key across random multipart boundaries', async () => {
  const keys: string[] = []
  globalThis.fetch = (async input => {
    keys.push((input as Request).headers.get('Idempotency-Key')!)
    if (keys.length === 1) throw new TypeError('network disconnected')
    return Response.json({ userId: competitionId })
  }) as typeof fetch
  const client = createApiClient(false)
  const upload = () => multipartBody({ file: new File(['same bytes'], 'avatar.png', { type: 'image/png' }) })
  await client.api.v1.auth.me.avatar.put(await upload()).catch(() => undefined)
  await client.api.v1.auth.me.avatar.put(await upload())
  expect(keys[0]).toBe(keys[1])
  await client.api.v1.auth.me.avatar.put(await upload())
  expect(keys[2]).not.toBe(keys[1])
})

test('202 projection responses are not deserialized as completed snapshots', async () => {
  const response = new ResponseMetadata()
  globalThis.fetch = (async () => Response.json({ statusUrl: '/status' }, { status: 202 })) as typeof fetch
  const result = await createApiClient(false).api.v1.competitions.byCompetitionId(competitionId).leaderboard.get({ options: [
    new RequestPolicyOption({ response }),
    new ProjectionResponseOption(createNoCTFAPIEndpointsCompetitionsScoreboardSnapshotResponseFromDiscriminatorValue),
  ] })
  expect(result).toBeUndefined()
  expect(response.status).toBe(202)
})

test('native download retains content disposition and readable stream', async () => {
  globalThis.fetch = (async () => new Response('archive', { headers: { 'content-disposition': "attachment; filename*=UTF-8''archive.zip" } })) as typeof fetch
  const client = createApiClient(false)
  const response = await nativeResponse(options => client.api.v1.competitions.byCompetitionId(competitionId)
    .challenges.byCompetitionChallengeId(challengeId).attachment.get({ options }))
  expect(response.headers.get('content-disposition')).toContain('archive.zip')
  expect(response.body).toBeInstanceOf(ReadableStream)
  expect(await response.text()).toBe('archive')
})

test('explicit null is sent while an omitted patch field stays absent', async () => {
  let body: unknown
  globalThis.fetch = (async input => { body = await (input as Request).json(); return Response.json({}) }) as typeof fetch
  await createApiClient(false).api.v1.auth.me.profile.patch({ profile: { description: null } })
  expect(body).toEqual({ profile: { description: null } })
})

test('unmapped failures retain problem details and mapped validation retains localization fields', async () => {
  const client = createApiClient(false)
  globalThis.fetch = (async () => Response.json({ detail: '列表暂时不可用' }, { status: 500 })) as typeof fetch
  const failure = await client.api.v1.competitions.byCompetitionId(competitionId).teams.get().catch(error => error)
  expect(parseApiError(failure).status).toBe(500)
  expect(parseApiError(failure).message).toBe('列表暂时不可用')
  globalThis.fetch = (async () => Response.json({
    errors: { Password: ['invalid'] },
    errorMessages: { Password: [{ key: 'api.pagination.invalidCursor', arguments: {} }] },
  }, { status: 400, headers: { 'content-type': 'application/problem+json' } })) as typeof fetch
  const invalid = await client.api.v1.auth.login.post({ login: 'user', password: 'bad' }).catch(error => error)
  expect(parseApiError(invalid).fieldErrors).toEqual({ Password: ['invalid'] })
  expect(parseApiError(invalid).fieldMessages?.Password?.[0]).toMatchObject({ key: 'api.pagination.invalidCursor' })
})

test('impersonation 401 ends the identity switch without refreshing or replaying', async () => {
  let refreshes = 0
  let ended = 0
  let attempts = 0
  initializeApiClient({ token: () => 'impersonated', impersonating: () => true,
    refresh: async () => { refreshes++; return true }, endImpersonation: () => { ended++ } })
  globalThis.fetch = (async () => { attempts++; return new Response(null, { status: 401 }) }) as typeof fetch
  const error = await api.api.v1.auth.me.get().catch(error => error)
  expect(parseApiError(error).status).toBe(401)
  expect(parseApiError(error).displayMessage).toMatchObject({ key: 'auth.session.expired' })
  expect({ attempts, ended, refreshes }).toEqual({ attempts: 1, ended: 1, refreshes: 0 })
})

test('request policy forwards cancellation to fetch', async () => {
  const controller = new AbortController()
  controller.abort()
  globalThis.fetch = (async input => {
    expect((input as Request).signal.aborted).toBe(true)
    throw new DOMException('aborted', 'AbortError')
  }) as typeof fetch
  const error = await createApiClient(false).api.v1.auth.me.get({ options: [new RequestPolicyOption({ signal: controller.signal })] }).catch(error => error)
  expect(parseApiError(error).code).toBe('RequestTimeout')
})

test('a delayed 401 cannot replay a command under a different identity', async () => {
  let token = oldAccess
  let attempts = 0
  let refreshes = 0
  let finish!: (response: Response) => void
  let started!: () => void
  const pendingRequest = new Promise<void>(resolve => { started = resolve })
  initializeApiClient({ token: () => token, impersonating: () => false,
    refresh: async () => { refreshes++; return true }, endImpersonation() {} })
  globalThis.fetch = (() => { attempts++; started(); return new Promise<Response>(resolve => { finish = resolve }) }) as typeof fetch
  const pending = api.api.v1.auth.me.profile.patch({ profile: { description: 'old identity command' } }).catch(error => error)
  await pendingRequest
  token = jwt('other', challengeId)
  finish(new Response(null, { status: 401 }))
  expect(parseApiError(await pending).status).toBe(401)
  expect({ attempts, refreshes }).toEqual({ attempts: 1, refreshes: 0 })
})
