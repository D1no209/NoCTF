import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'
import { accessTokenNeedsRefresh, shouldRefreshSession } from '../app/lib/auth-refresh'
import { statusErrorMessage } from '../app/utils/api-error'
import { getAccessToken, refreshSession, setAccessToken, setSessionInvalidationHandler } from '../app/lib/session'

function accessToken(exp: number): string {
  const payload = btoa(JSON.stringify({ exp }))
    .replaceAll('+', '-')
    .replaceAll('/', '_')
    .replace(/=+$/, '')
  return `header.${payload}.signature`
}

describe('authentication response refresh', () => {
  test('does not refresh non-unauthorized responses', () => {
    const authenticated = new Headers({ Authorization: 'Bearer expired' })
    expect(shouldRefreshSession(200, authenticated)).toBeFalse()
    expect(shouldRefreshSession(403, authenticated)).toBeFalse()
  })

  test('does not refresh a request that did not carry an access token', () => {
    expect(shouldRefreshSession(401, new Headers())).toBeFalse()
  })

  test('refreshes an unauthorized request that carried an access token', () => {
    const authenticated = new Headers({ Authorization: 'Bearer expired' })
    expect(shouldRefreshSession(401, authenticated)).toBeTrue()
  })

  test('refreshes realtime tokens before expiry and rejects malformed tokens', () => {
    expect(accessTokenNeedsRefresh(accessToken(1_100), 1_000)).toBeFalse()
    expect(accessTokenNeedsRefresh(accessToken(1_030), 1_000)).toBeTrue()
    expect(accessTokenNeedsRefresh(accessToken(999), 1_000)).toBeTrue()
    expect(accessTokenNeedsRefresh('not-a-jwt', 1_000)).toBeTrue()
  })

  test('uses generated refresh and realtime token factories without handwritten routes', async () => {
    const session = await sourceFile(new URL('../app/lib/session.ts', import.meta.url)).text()
    const competitionHub = await sourceFile(new URL('../app/composables/useCompetitionHub.ts', import.meta.url)).text()
    const platformHub = await sourceFile(new URL('../app/composables/usePlatformLogHub.ts', import.meta.url)).text()

    expect(session).toContain('refreshClient.api.v1.auth.refresh.post()')
    expect(session).not.toContain("fetch('/api/v1/auth/refresh'")
    expect(competitionHub).toContain('accessTokenFactory: getRealtimeAccessToken')
    expect(platformHub).toContain('accessTokenFactory: getRealtimeAccessToken')
  })

  test('invalidates shared authenticated state when refresh fails', async () => {
    const originalFetch = globalThis.fetch
    let invalidations = 0
    setAccessToken('expired')
    setSessionInvalidationHandler(() => { invalidations += 1 })
    globalThis.fetch = (() => Promise.reject(new TypeError('offline'))) as typeof fetch
    try {
      expect(await refreshSession()).toBeFalse()
      expect(getAccessToken()).toBeNull()
      expect(invalidations).toBe(1)
    }
    finally {
      globalThis.fetch = originalFetch
      setSessionInvalidationHandler(null)
      await new Promise(resolve => setTimeout(resolve, 0))
    }
  })
})

describe('authentication status errors', () => {
  test('classifies unauthorized responses by attached credentials instead of endpoint paths', () => {
    expect(statusErrorMessage(401, false)).toBe('登录失败,请检查用户名或密码')
    expect(statusErrorMessage(401, true)).toBe('登录状态已失效,请重新登录')
  })
})
