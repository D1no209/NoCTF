import { describe, expect, test } from 'bun:test'
import {
  beginImpersonationAccessToken,
  clearImpersonationAccessToken,
  getAccessToken,
  getRealtimeAccessToken,
  isImpersonatingSession,
  refreshSession,
  requestImpersonationEnd,
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
    completeRefresh?.(new Response(JSON.stringify({
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
    failRefresh?.(new TypeError('offline'))

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

  test('expiry clears the target token through the impersonation end handler', async () => {
    const ended = new Promise<string>((resolve) => {
      setImpersonationEndHandler((reason) => {
        clearImpersonationAccessToken()
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
      expect(getAccessToken()).toBeNull()
    }
    finally {
      clearImpersonationAccessToken()
      setImpersonationEndHandler(null)
    }
  })

  test('401 handling returns the original response before restoring the administrator', async () => {
    const plugin = await sourceFile(new URL('../app/plugins/api.client.ts', import.meta.url)).text()
    const auth = await sourceFile(new URL('../app/composables/useAuth.ts', import.meta.url)).text()
    const branch = plugin.slice(
      plugin.indexOf('if (isImpersonatingSession())'),
      plugin.indexOf('const refreshed = await refreshSession()'),
    )

    expect(branch).toContain("requestImpersonationEnd('unauthorized')")
    expect(branch).toContain('return response')
    expect(branch).not.toContain('fetch(')
    expect(auth).toContain('await refreshAdministratorSession()')
    expect(auth).toContain('user.value?.userId === active.administratorUserId')
    expect(auth).toContain('await navigateTo(active.returnPath)')
  })

  test('user management exposes one-time issue, impersonation, and individual revocation', async () => {
    const controller = await sourceFile(new URL('../app/features/routes/admin/platform/useAdminPlatformUsersPage.ts', import.meta.url)).text()
    const botController = await sourceFile(new URL('../app/features/routes/admin/platform/useAdminPlatformBotsPage.ts', import.meta.url)).text()
    const view = await sourceFile(new URL('../app/components/views/page/admin/platform/AdminPlatformUsersPageView.vue', import.meta.url)).text()
    const layout = await sourceFile(new URL('../app/components/views/layout/DefaultLayoutView.vue', import.meta.url)).text()

    expect(controller).toContain('adminPlatformIssueUserToken')
    expect(controller).toContain('adminPlatformListUserTokens')
    expect(controller).toContain('adminPlatformRevokeUserToken')
    expect(controller).toContain('issuedToken.value = null')
    expect(controller).toContain('!tokenIssueRequests.isCurrent(request) || !tokenOpen.value')
    expect(controller).toContain('!tokenListRequests.isCurrent(request) || detail.value?.id !== targetUserId')
    expect(controller).toContain('function clearIssuedTokens(): void')
    expect(botController).toContain('!tokenIssueRequests.isCurrent(request) || !issueOpen.value')
    expect(controller).toContain('await startImpersonation({')
    expect(view).toContain("openToken(detail, 'impersonate')")
    expect(view).toContain('revokeIssuedToken(token)')
    expect(layout).toContain('impersonation.targetUserName')
    expect(layout).toContain('endImpersonation')
    expect(layout).toContain('sticky top-20')
    expect(layout).toContain('data-impersonation-banner="true"')
    expect(layout).toContain('slot-name="impersonation-banner"')
    expect(layout).toContain('aria-live="polite"')
    expect(layout).toContain('<CardContent class="flex flex-wrap items-center justify-between gap-3">')
  })
})
