import {
  getMeEndpoint,
  loginEndpoint,
  logoutAllEndpoint,
  logoutEndpoint,
} from '~/api'
import type { NoCtfapiEndpointsAuthenticationCurrentUserResponse } from '~/api'
import { getAccessToken, refreshSession, setAccessToken } from '~/lib/session'

export type CurrentUser = NoCtfapiEndpointsAuthenticationCurrentUserResponse

/** Platform roles — mirrors NoCTF.Domain.Identity.UserRole. */
export const UserRole = {
  User: 'User', Organizer: 'Organizer', Administrator: 'Administrator',
} as const

export function useAuth() {
  const user = useState<CurrentUser | null>('auth:user', () => null)
  /** Whether the initial session restore attempt has finished. */
  const ready = useState<boolean>('auth:ready', () => false)

  const isLoggedIn = computed(() => user.value !== null)
  const isAdministrator = computed(() => user.value?.role === UserRole.Administrator)
  const canOrganize = computed(
    () => user.value?.role === UserRole.Organizer || user.value?.role === UserRole.Administrator,
  )

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

  async function login(login: string, password: string): Promise<void> {
    const { data, error } = await loginEndpoint({ body: { login, password } })
    if (error || !data?.accessToken) {
      throw parseApiError(error)
    }
    setAccessToken(data.accessToken)
    await fetchMe()
  }

  async function logout(): Promise<void> {
    await logoutEndpoint().catch(() => undefined)
    setAccessToken(null)
    user.value = null
    await navigateTo('/')
  }

  /** Revoke every session platform-wide (password change / security). */
  async function logoutAll(): Promise<void> {
    await logoutAllEndpoint().catch(() => undefined)
    setAccessToken(null)
    user.value = null
    await navigateTo('/auth/login')
  }

  return {
    user,
    ready,
    isLoggedIn,
    isAdministrator,
    canOrganize,
    restore,
    login,
    logout,
    logoutAll,
    fetchMe,
  }
}
