import { afterAll, afterEach, beforeEach, describe, expect, test } from 'bun:test'
import {
  clearAuthSession,
  refreshAuthSessionIfNeeded,
  saveAuthSession,
} from '../src/api/auth-session'
import { client } from '../src/api/generated/client.gen'
import { authApi, healthApi } from '../src/api/noctf'
import { PLATFORM_USER_ROLE } from '../src/api/userRole'

const apiBaseUrl = 'https://api.noctf.test'
const originalClientConfig = client.getConfig()
const originalFetch = globalThis.fetch
const requests: Request[] = []

const contractFetch: typeof fetch = async (input, init) => {
  const normalizedInput = typeof input === 'string' && input.startsWith('/')
    ? new URL(input, apiBaseUrl)
    : input
  const request = new Request(normalizedInput, init)
  requests.push(request)

  const responses: Record<string, { body: unknown, status: number }> = {
    '/api/v1/auth/login': {
      body: {
        userId: 'user-1',
        userName: 'operator',
        role: PLATFORM_USER_ROLE.administrator,
        accessToken: 'login-token',
        expiresAt: '2026-08-01T00:00:00Z',
      },
      status: 200,
    },
    '/api/v1/auth/refresh': {
      body: {
        userId: 'user-1',
        userName: 'operator',
        role: PLATFORM_USER_ROLE.administrator,
        accessToken: 'refresh-token',
        expiresAt: '2026-08-01T01:00:00Z',
      },
      status: 200,
    },
    '/api/v1/auth/register': {
      body: {
        userId: 'user-2',
        userName: 'new-user',
        email: 'new@example.test',
        role: PLATFORM_USER_ROLE.user,
        emailVerified: false,
      },
      status: 201,
    },
    '/health': {
      body: { status: 'ok' },
      status: 200,
    },
  }
  const response = responses[new URL(request.url).pathname]

  return Response.json(response?.body ?? {}, {
    status: response?.status ?? 404,
  })
}

beforeEach(() => {
  requests.length = 0
  globalThis.fetch = contractFetch
  client.setConfig({
    baseUrl: apiBaseUrl,
    fetch: contractFetch,
    headers: { Authorization: 'Bearer stale-token' },
  })
})

afterAll(() => {
  globalThis.fetch = originalFetch
  client.setConfig(originalClientConfig)
})

afterEach(() => {
  clearAuthSession()
  client.setConfig({ headers: { Authorization: null } })
})

describe('generated authentication and health contracts', () => {
  test('login uses the generated v1 endpoint and request body', async () => {
    const response = await authApi.login('operator@example.test', 'secret')

    expect(response.accessToken).toBe('login-token')
    expect(requests).toHaveLength(1)
    expect(new URL(requests[0]!.url).pathname).toBe('/api/v1/auth/login')
    expect(requests[0]!.credentials).toBe('include')
    expect(await requests[0]!.json()).toEqual({
      login: 'operator@example.test',
      password: 'secret',
    })
  })

  test('registration uses the generated v1 endpoint and request type', async () => {
    const response = await authApi.register('new-user', 'new@example.test', 'secret')

    expect(response.userName).toBe('new-user')
    expect(requests).toHaveLength(1)
    expect(new URL(requests[0]!.url).pathname).toBe('/api/v1/auth/register')
    expect(requests[0]!.credentials).toBe('include')
    expect(await requests[0]!.json()).toEqual({
      userName: 'new-user',
      email: 'new@example.test',
      password: 'secret',
    })
  })

  test('refresh uses the generated v1 cookie endpoint', async () => {
    const response = await authApi.refresh()

    expect(response.accessToken).toBe('refresh-token')
    expect(requests).toHaveLength(1)
    expect(new URL(requests[0]!.url).pathname).toBe('/api/v1/auth/refresh')
    expect(requests[0]!.credentials).toBe('include')
    expect(requests[0]!.headers.has('Authorization')).toBe(false)
  })

  test('automatic session refresh uses the generated refresh operation', async () => {
    saveAuthSession({
      accessToken: 'expiring-token',
      userName: 'operator',
      role: PLATFORM_USER_ROLE.administrator,
    })

    const response = await refreshAuthSessionIfNeeded(true)

    expect(response?.accessToken).toBe('refresh-token')
    expect(requests).toHaveLength(1)
    expect(new URL(requests[0]!.url).pathname).toBe('/api/v1/auth/refresh')
    expect(requests[0]!.credentials).toBe('include')
    expect(requests[0]!.headers.has('Authorization')).toBe(false)
  })

  test('admin health uses the generated health endpoint', async () => {
    await expect(healthApi.get()).resolves.toEqual({ status: 'ok' })

    expect(requests).toHaveLength(1)
    expect(new URL(requests[0]!.url).pathname).toBe('/health')
  })
})
