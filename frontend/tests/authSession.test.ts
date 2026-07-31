import { Buffer } from 'node:buffer'
import { afterEach, beforeEach, describe, expect, test } from 'bun:test'
import {
  clearAuthSession,
  configureAuthSessionRefresh,
  getTokenExpiry,
  isTokenExpired,
  millisecondsUntilTokenRefresh,
  readAuthSession,
  refreshAuthSessionIfNeeded,
  saveAuthSession,
  shouldRefreshToken,
} from '../src/api/auth-session'
import { PLATFORM_USER_ROLE } from '../src/api/userRole'

class MemoryStorage implements Storage {
  private readonly values = new Map<string, string>()

  get length() {
    return this.values.size
  }

  clear() {
    this.values.clear()
  }

  getItem(key: string) {
    return this.values.get(key) ?? null
  }

  key(index: number) {
    return [...this.values.keys()][index] ?? null
  }

  removeItem(key: string) {
    this.values.delete(key)
  }

  setItem(key: string, value: string) {
    this.values.set(key, value)
  }
}

function jwt(expiresAt: number, id: string) {
  const payload = Buffer.from(JSON.stringify({ exp: Math.floor(expiresAt / 1000), jti: id }))
    .toString('base64url')
  return `header.${payload}.signature`
}

beforeEach(() => {
  Object.defineProperty(globalThis, 'localStorage', {
    configurable: true,
    value: new MemoryStorage(),
  })
})

afterEach(() => {
  clearAuthSession()
})

describe('auth session refresh', () => {
  test('identifies the refresh window without expiring a valid token', () => {
    const now = Date.now()
    const token = jwt(now + 90_000, 'near-expiry')

    expect(getTokenExpiry(token)).toBe(Math.floor((now + 90_000) / 1000) * 1000)
    expect(shouldRefreshToken(token, now)).toBe(true)
    expect(millisecondsUntilTokenRefresh(token, now)).toBe(0)
    expect(isTokenExpired(token, now)).toBe(false)
  })

  test('coalesces concurrent refreshes and persists the renewed session', async () => {
    const now = Date.now()
    const oldToken = jwt(now + 30_000, 'old')
    const newToken = jwt(now + 60 * 60_000, 'new')
    let requests = 0

    saveAuthSession({ accessToken: oldToken, userName: 'admin', role: PLATFORM_USER_ROLE.administrator })
    configureAuthSessionRefresh(async () => {
      requests += 1
      return {
        data: { accessToken: newToken, userName: 'admin', role: PLATFORM_USER_ROLE.administrator },
        error: undefined,
        response: Response.json({}),
      }
    })

    const [first, second] = await Promise.all([
      refreshAuthSessionIfNeeded(),
      refreshAuthSessionIfNeeded(),
    ])

    expect(requests).toBe(1)
    expect(first?.accessToken).toBe(newToken)
    expect(second?.accessToken).toBe(newToken)
    expect(readAuthSession()?.accessToken).toBe(newToken)
  })

  test('clears a session rejected by the refresh endpoint', async () => {
    const token = jwt(Date.now() + 30_000, 'revoked')
    saveAuthSession({ accessToken: token, userName: 'admin', role: PLATFORM_USER_ROLE.administrator })
    configureAuthSessionRefresh(async () => ({
      data: undefined,
      error: {},
      response: new Response(null, { status: 401 }),
    }))

    expect(await refreshAuthSessionIfNeeded()).toBeNull()
    expect(readAuthSession()).toBeNull()
  })
})
