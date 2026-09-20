import {
  getMeEndpoint,
  loginEndpoint,
  logoutAllEndpoint,
  logoutEndpoint,
  authenticationSsoCompleteLogin,
} from '../api'
import type { NoCtfapiEndpointsAuthenticationCurrentUserResponse } from '../api'
import {
  beginImpersonationAccessToken,
  clearImpersonationAccessToken,
  getAccessToken,
  refreshAdministratorSession,
  refreshSession,
  setAccessToken,
} from '../lib/session'

export type CurrentUser = NoCtfapiEndpointsAuthenticationCurrentUserResponse

export type ImpersonationSession = {
  administratorUserId: string
  administratorUserName: string
  targetUserId: string
  targetUserName: string
  expiresAt: string
  returnPath: string
}

let endingImpersonation: Promise<void> | null = null

export function useAuth() {
  const user = useState<CurrentUser | null>('auth:user', () => null)
  /** Whether the initial session restore attempt has finished. */
  const ready = useState<boolean>('auth:ready', () => false)
  const impersonation = useState<ImpersonationSession | null>('auth:impersonation', () => null)
  const impersonationEnding = useState<boolean>('auth:impersonation-ending', () => false)

  const isLoggedIn = computed(() => user.value !== null)
  const isAdministrator = computed(() => user.value?.role === 'Administrator')
  const canOrganize = computed(
    () => user.value?.role === 'Organizer' || user.value?.role === 'Administrator',
  )

  function invalidate(): void {
    clearImpersonationAccessToken()
    impersonation.value = null
    setAccessToken(null)
    user.value = null
  }

  async function fetchMe(): Promise<void> {
    const { data, error } = await getMeEndpoint()
    if (error || !data) {
      user.value = null
      setAccessToken(null)
      return
    }
    user.value = data
  }

  /** Restore the session from the refresh cookie; called once at app startup. */
  async function restore(): Promise<void> {
    try {
      if (getAccessToken() || (await refreshSession())) {
        await fetchMe()
      }
    }
    finally {
      ready.value = true
    }
  }

  async function login(
    login: string,
    password: string,
    humanVerificationHeaders: Record<string, string>,
  ): Promise<void> {
    const { data, error } = await loginEndpoint({
      headers: humanVerificationHeaders,
      body: { login, password },
    })
    if (error || !data?.accessToken) {
      throw parseApiError(error)
    }
    setAccessToken(data.accessToken)
    await fetchMe()
  }

  async function logout(): Promise<void> {
    if (impersonation.value) {
      await endImpersonation()
      return
    }
    await logoutEndpoint().catch(() => undefined)
    invalidate()
    await navigateTo('/')
  }

  /** Revoke every session platform-wide (password change / security). */
  async function logoutAll(): Promise<void> {
    if (impersonation.value) {
      await endImpersonation()
      return
    }
    await logoutAllEndpoint().catch(() => undefined)
    invalidate()
    await navigateTo('/auth/login')
  }

  async function completeSsoLogin(flowId: string): Promise<string> {
    const { data, error } = await authenticationSsoCompleteLogin({
      path: { flowId },
    })
    if (error || !data?.accessToken)
      throw parseApiError(error)
    setAccessToken(data.accessToken)
    await fetchMe()
    return data.returnPath ?? '/'
  }

  async function startImpersonation(input: {
    accessToken: string
    expiresAt: string
    targetUserId: string
    targetUserName: string
    returnPath: string
  }): Promise<void> {
    const administrator = user.value
    if (!administrator?.userId || administrator.role !== 'Administrator')
      throw new Error(translate("ui.administratorSessionRequired"))
    impersonation.value = {
      administratorUserId: administrator.userId,
      administratorUserName: administrator.userName ?? administrator.userId,
      targetUserId: input.targetUserId,
      targetUserName: input.targetUserName,
      expiresAt: input.expiresAt,
      returnPath: input.returnPath,
    }
    beginImpersonationAccessToken(input.accessToken, input.expiresAt)
    await fetchMe()
    if (user.value?.userId !== input.targetUserId) {
      await endImpersonation()
      throw new Error(translate("ui.impersonatedIdentityCouldNotBeVerified"))
    }
    await navigateTo('/')
  }

  function endImpersonation(): Promise<void> {
    endingImpersonation ??= (async () => {
      const active = impersonation.value
      if (!active) return
      impersonationEnding.value = true
      clearImpersonationAccessToken()
      const restored = await refreshAdministratorSession()
      if (restored) await fetchMe()
      const administratorRestored = restored
        && user.value?.userId === active.administratorUserId
        && user.value.role === 'Administrator'
      impersonation.value = null
      if (!administratorRestored) {
        invalidate()
        await navigateTo('/auth/login')
        return
      }
      await navigateTo(active.returnPath)
    })().finally(() => {
      impersonationEnding.value = false
      endingImpersonation = null
    })
    return endingImpersonation
  }

  return {
    user,
    ready,
    isLoggedIn,
    isAdministrator,
    canOrganize,
    impersonation,
    impersonationEnding,
    invalidate,
    restore,
    login,
    completeSsoLogin,
    logout,
    logoutAll,
    fetchMe,
    startImpersonation,
    endImpersonation,
  }
}
