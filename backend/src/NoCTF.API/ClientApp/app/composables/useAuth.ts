
import { api } from '../lib/api'



import type { NoCTFAPIEndpointsAuthenticationCurrentUserResponse } from '../api/models'
import {
  beginImpersonationAccessToken,
  clearImpersonationAccessToken,
  getAccessToken,
  refreshSession,
  restoreImpersonationAccessToken,
  setAccessToken,
} from '../lib/session'

export type CurrentUser = NoCTFAPIEndpointsAuthenticationCurrentUserResponse

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
    let error: unknown;
    const data = await api.api.v1.auth.me.get().catch(cause => { error = cause; return undefined });
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
    let error: unknown;
    const data = await api.api.v1.auth.login.post({ login, password }, { headers: humanVerificationHeaders }).catch(cause => { error = cause; return undefined });
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
    await api.api.v1.auth.logout.post().catch(() => undefined)
    invalidate()
    await navigateTo('/')
  }

  /** Revoke every session platform-wide (password change / security). */
  async function logoutAll(): Promise<void> {
    if (impersonation.value) {
      await endImpersonation()
      return
    }
    await api.api.v1.auth.logoutAll.post().catch(() => undefined)
    invalidate()
    await navigateTo('/auth/login')
  }

  async function completeSsoLogin(flowId: string): Promise<string> {
    let error: unknown;
    const data = await api.api.v1.auth.sso.flows.byFlowId(flowId).completeLogin.post().catch(cause => { error = cause; return undefined });
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
      throw new Error(translate("common.validation.administratorSessionRequired"))
    if (impersonation.value)
      throw new Error(translate("common.label.identitySwitchAlreadyActive"))
    if (!beginImpersonationAccessToken(input.accessToken, input.expiresAt))
      throw new Error(translate("common.validation.administratorSessionRequired"))
    impersonation.value = {
      administratorUserId: administrator.userId,
      administratorUserName: administrator.userName ?? administrator.userId,
      targetUserId: input.targetUserId,
      targetUserName: input.targetUserName,
      expiresAt: input.expiresAt,
      returnPath: input.returnPath,
    }
    await fetchMe()
    if (user.value?.userId !== input.targetUserId) {
      await endImpersonation()
      throw new Error(translate("common.auth.description.impersonatedIdentityCouldVerified"))
    }
    await navigateTo('/')
  }

  function endImpersonation(): Promise<void> {
    endingImpersonation ??= (async () => {
      const active = impersonation.value
      if (!active) return
      impersonationEnding.value = true
      const restoredToken = restoreImpersonationAccessToken()
      if (restoredToken) await fetchMe()
      else if (await refreshSession()) await fetchMe()
      const administratorRestored = user.value?.userId === active.administratorUserId
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
