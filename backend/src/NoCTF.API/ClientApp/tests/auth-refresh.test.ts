import { describe, expect, test } from 'bun:test'
import { accessTokenNeedsRefresh, shouldRefreshSession } from '../app/lib/auth-refresh'

function accessToken(exp: number): string {
  const payload = btoa(JSON.stringify({ exp }))
    .replaceAll('+', '-')
    .replaceAll('/', '_')
    .replace(/=+$/, '')
  return `header.${payload}.signature`
}

describe('authentication response refresh', () => {
  test('does not refresh non-unauthorized responses', () => {
    expect(shouldRefreshSession(200, '/api/v1/auth/me')).toBeFalse()
    expect(shouldRefreshSession(403, '/api/v1/auth/me')).toBeFalse()
  })

  test.each([
    '/api/v1/auth/login',
    '/api/v1/auth/logout',
    '/api/v1/auth/refresh',
    '/api/v1/auth/register',
    '/api/v1/auth/password-reset/request',
    '/api/v1/auth/password-reset/complete',
    '/api/v1/auth/email-verification/verify',
  ])('does not refresh anonymous endpoint %s', (path) => {
    expect(shouldRefreshSession(401, path)).toBeFalse()
  })

  test.each([
    '/api/v1/auth/me',
    '/api/v1/auth/me/profile',
    '/api/v1/auth/me/avatar',
    '/api/v1/auth/password',
    '/api/v1/auth/logout-all',
    '/api/v1/auth/email-verification/resend',
  ])('refreshes protected endpoint %s', (path) => {
    expect(shouldRefreshSession(401, path)).toBeTrue()
  })

  test('matches exact paths while accepting absolute URLs and query strings', () => {
    expect(shouldRefreshSession(401, 'https://noctf.test/api/v1/auth/login?redirect=1')).toBeFalse()
    expect(shouldRefreshSession(401, '/api/v1/auth/login-extra')).toBeTrue()
  })

  test('refreshes realtime tokens before expiry and rejects malformed tokens', () => {
    expect(accessTokenNeedsRefresh(accessToken(1_100), 1_000)).toBeFalse()
    expect(accessTokenNeedsRefresh(accessToken(1_030), 1_000)).toBeTrue()
    expect(accessTokenNeedsRefresh(accessToken(999), 1_000)).toBeTrue()
    expect(accessTokenNeedsRefresh('not-a-jwt', 1_000)).toBeTrue()
  })

  test('uses generated refresh and realtime token factories without handwritten routes', async () => {
    const session = await Bun.file(new URL('../app/lib/session.ts', import.meta.url)).text()
    const competitionHub = await Bun.file(new URL('../app/composables/useCompetitionHub.ts', import.meta.url)).text()
    const platformHub = await Bun.file(new URL('../app/composables/usePlatformLogHub.ts', import.meta.url)).text()

    expect(session).toContain('refreshTokenEndpoint()')
    expect(session).not.toContain("fetch('/api/v1/auth/refresh'")
    expect(competitionHub).toContain('accessTokenFactory: getRealtimeAccessToken')
    expect(platformHub).toContain('accessTokenFactory: getRealtimeAccessToken')
  })
})
