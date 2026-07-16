import type { AuthSession } from '@/api/auth-session'
import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import {
  clearAuthSession,
  isTokenExpired,
  millisecondsUntilTokenRefresh,
  readAuthSession,
  refreshAuthSessionIfNeeded,
  saveAuthSession,
  shouldRefreshToken,
  subscribeAuthSession,
} from '@/api/auth-session'
import { authApi, setAuthToken } from '@/api/noctf'

interface UserInfo {
  userName: string
  role: string
}

const MIN_REFRESH_RETRY_MS = 30_000

export const useAuthStore = defineStore('auth', () => {
  const storedSession = readAuthSession()
  const accessToken = ref<string | null>(storedSession?.accessToken ?? null)
  const user = ref<UserInfo | null>(storedSession
    ? { userName: storedSession.userName, role: storedSession.role }
    : null)

  const isAuthenticated = computed(() => !!accessToken.value && !isTokenExpired(accessToken.value))
  const userRole = computed(() => user.value?.role ?? '')
  let refreshTimer: number | null = null

  function scheduleSessionRefresh(token: string | null) {
    if (refreshTimer !== null) {
      window.clearTimeout(refreshTimer)
      refreshTimer = null
    }

    const delay = millisecondsUntilTokenRefresh(token)
    if (delay === null)
      return

    refreshTimer = window.setTimeout(refreshBeforeExpiry, Math.max(delay, MIN_REFRESH_RETRY_MS))
  }

  async function refreshBeforeExpiry() {
    refreshTimer = null
    try {
      await refreshAuthSessionIfNeeded()
    }
    catch {
      if (accessToken.value && !isTokenExpired(accessToken.value))
        refreshTimer = window.setTimeout(refreshBeforeExpiry, MIN_REFRESH_RETRY_MS)
    }
  }

  function applySession(session: AuthSession | null) {
    accessToken.value = session?.accessToken ?? null
    user.value = session
      ? { userName: session.userName, role: session.role }
      : null
    setAuthToken(session?.accessToken ?? null)
    scheduleSessionRefresh(session?.accessToken ?? null)
  }

  subscribeAuthSession(applySession)
  applySession(storedSession)

  async function login(email: string, password: string) {
    const data = await authApi.login(email, password)
    saveAuthSession({
      accessToken: data.accessToken ?? '',
      userName: data.userName ?? '',
      role: data.role ?? '',
    })
  }

  async function register(userName: string, email: string, password: string) {
    await authApi.register(userName, email, password)
  }

  function logout() {
    clearAuthSession()
  }

  async function ensureFreshSession() {
    if (!accessToken.value) {
      logout()
      return false
    }

    if (shouldRefreshToken(accessToken.value)) {
      try {
        await refreshAuthSessionIfNeeded()
      }
      catch {
        // Keep a still-valid token when refresh fails due to a transient error.
      }
    }

    if (accessToken.value && !isTokenExpired(accessToken.value))
      return true

    logout()
    return false
  }

  return { user, accessToken, isAuthenticated, userRole, login, register, logout, ensureFreshSession }
})
