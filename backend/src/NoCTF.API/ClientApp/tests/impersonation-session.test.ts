import { describe, expect, test } from 'bun:test'
import {
  beginImpersonationAccessToken,
  clearImpersonationAccessToken,
  getAccessToken,
  getRealtimeAccessToken,
  isImpersonatingSession,
  refreshSession,
  requestImpersonationEnd,
  restoreImpersonationAccessToken,
  setAccessToken,
  setImpersonationEndHandler,
} from '../app/lib/session'
import { sourceFile } from './support/feature-source'

describe('administrator impersonation session', () => {
  test('an in-flight administrator refresh cannot overwrite a newer impersonation token', async () => {
    const originalFetch = globalThis.fetch
    let completeRefresh: ((response: Response) => void) | undefined
    globalThis.fetch = (() => new Promise<Response>((resolve) => {
      completeRefresh = resolve
    })) as typeof fetch
    clearImpersonationAccessToken()
    setAccessToken('administrator-token')
    const refreshing = refreshSession()
    beginImpersonationAccessToken(
      'impersonated-token',
      new Date(Date.now() + 60_000).toISOString(),
    )
    while (!completeRefresh) await Bun.sleep(1)
    completeRefresh(new Response(JSON.stringify({
      accessToken: 'late-administrator-token',
      expiresAt: new Date(Date.now() + 60_000).toISOString(),
    }), {
      status: 200,
      headers: { 'content-type': 'application/json' },
    }))

    try {
      expect(await refreshing).toBeFalse()
      expect(getAccessToken()).toBe('impersonated-token')
      expect(isImpersonatingSession()).toBeTrue()
    }
    finally {
      clearImpersonationAccessToken()
      globalThis.fetch = originalFetch
    }
  })

  test('a stale administrator refresh failure cannot clear a newer impersonation token', async () => {
    const originalFetch = globalThis.fetch
    let failRefresh: ((reason: unknown) => void) | undefined
    globalThis.fetch = (() => new Promise<Response>((_resolve, reject) => {
      failRefresh = reject
    })) as typeof fetch
    clearImpersonationAccessToken()
    setAccessToken('administrator-token')
    const refreshing = refreshSession()
    beginImpersonationAccessToken(
      'impersonated-token',
      new Date(Date.now() + 60_000).toISOString(),
    )
    while (!failRefresh) await Bun.sleep(1)
    failRefresh(new TypeError('offline'))

    try {
      expect(await refreshing).toBeFalse()
      expect(getAccessToken()).toBe('impersonated-token')
      expect(isImpersonatingSession()).toBeTrue()
    }
    finally {
      clearImpersonationAccessToken()
      globalThis.fetch = originalFetch
    }
  })

  test('never refreshes an impersonated token through the administrator cookie', async () => {
    const originalFetch = globalThis.fetch
    let fetches = 0
    let ended = 0
    globalThis.fetch = (async () => {
      fetches += 1
      throw new Error('refresh must not run')
    }) as typeof fetch
    setImpersonationEndHandler(() => { ended += 1 })
    clearImpersonationAccessToken()
    setAccessToken('administrator-token')
    beginImpersonationAccessToken(
      'impersonated-token',
      new Date(Date.now() + 60_000).toISOString(),
    )
    try {
      expect(isImpersonatingSession()).toBeTrue()
      expect(await refreshSession()).toBeFalse()
      expect(await getRealtimeAccessToken()).toBe('impersonated-token')
      expect(fetches).toBe(0)
      requestImpersonationEnd('unauthorized')
      expect(ended).toBe(1)
    }
    finally {
      clearImpersonationAccessToken()
      setImpersonationEndHandler(null)
      globalThis.fetch = originalFetch
    }
    expect(getAccessToken()).toBeNull()
  })

  test('expiry restores the saved administrator token through the identity-switch end handler', async () => {
    clearImpersonationAccessToken()
    setAccessToken('administrator-token')
    const ended = new Promise<string>((resolve) => {
      setImpersonationEndHandler((reason) => {
        restoreImpersonationAccessToken()
        resolve(reason)
      })
    })
    beginImpersonationAccessToken(
      'expired-impersonated-token',
      new Date(Date.now() - 1).toISOString(),
    )

    try {
      expect(await ended).toBe('expired')
      expect(isImpersonatingSession()).toBeFalse()
      expect(getAccessToken()).toBe('administrator-token')
    }
    finally {
      clearImpersonationAccessToken()
      setImpersonationEndHandler(null)
    }
  })

  test('401 handling returns the original response before restoring the administrator', async () => {
    const plugin = await sourceFile(new URL('../app/plugins/api.client.ts', import.meta.url)).text()
    const auth = await sourceFile(new URL('../app/composables/useAuth.ts', import.meta.url)).text()
    const transport = await sourceFile(new URL('../app/lib/api.ts', import.meta.url)).text()
    expect(plugin).toContain("requestImpersonationEnd('unauthorized')")
    expect(transport).toContain('if (currentToken === originalToken && session.impersonating()) session.endImpersonation()')
    expect(transport).toContain('else if (!session.impersonating() && sameIdentity(currentToken))')
    expect(auth).toContain('restoreImpersonationAccessToken()')
    expect(auth).toContain('else if (await refreshSession())')
    expect(auth).toContain('user.value?.userId === active.administratorUserId')
    expect(auth).toContain('await navigateTo(active.returnPath)')
  })

  test('user management exposes stateless one-time issuance and frontend identity switching', async () => {
    const controller = await sourceFile(new URL('../app/features/routes/admin/platform/useAdminPlatformUsersPage.ts', import.meta.url)).text()
    const view = await sourceFile(new URL('../app/components/views/page/admin/platform/AdminPlatformUsersPageView.vue', import.meta.url)).text()
    const layout = await sourceFile(new URL('../app/components/views/layout/DefaultLayoutView.vue', import.meta.url)).text()

    expect(controller).toMatch(/api\.api\.v1\.admin\.platform\.users\.byUserId\([^)]*\)\.tokens\.post\(/)
    expect(controller).not.toContain('adminPlatformListUserTokens')
    expect(controller).not.toContain('adminPlatformRevokeUserToken')
    expect(controller).toContain('issuedToken.value = null')
    expect(controller).toContain('!tokenIssueRequests.isCurrent(request) || !tokenOpen.value')
    expect(controller).toContain('await startImpersonation({')
    expect(view).toContain("openToken(detail, 'impersonate')")
    expect(view).not.toContain('revokeIssuedToken(token)')
    expect(view).not.toContain('tokenReason')
    expect(layout).toContain('impersonation.targetUserName')
    expect(layout).toContain('endImpersonation')
    expect(layout).toContain('sticky top-20')
    expect(layout).toContain('data-impersonation-banner="true"')
    expect(layout).toContain('slot-name="impersonation-banner"')
    expect(layout).toContain('aria-live="polite"')
    expect(layout).toContain('<CardContent class="flex flex-wrap items-center justify-between gap-3">')
  })

  test('identity switching restores the exact previous token and rejects nesting', () => {
    clearImpersonationAccessToken()
    setAccessToken('administrator-token')
    beginImpersonationAccessToken('target-token', new Date(Date.now() + 60_000).toISOString())

    expect(beginImpersonationAccessToken(
      'nested-token',
      new Date(Date.now() + 60_000).toISOString(),
    )).toBeFalse()
    expect(restoreImpersonationAccessToken()).toBe('administrator-token')
    expect(getAccessToken()).toBe('administrator-token')
    expect(isImpersonatingSession()).toBeFalse()
  })

  test('access tokens remain memory-only', async () => {
    const session = await sourceFile(new URL('../app/lib/session.ts', import.meta.url)).text()
    expect(session).not.toContain('.localStorage')
    expect(session).not.toContain('.sessionStorage')
  })
})
