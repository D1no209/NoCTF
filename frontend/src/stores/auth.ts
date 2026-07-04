import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { authApi, setAuthToken } from '@/api/noctf'

interface UserInfo {
  userName: string
  role: string
}

function getTokenExpiry(token: string | null): number | null {
  if (!token) return null

  try {
    const payload = token.split('.')[1]
    if (!payload) return null

    const normalizedPayload = payload.replace(/-/g, '+').replace(/_/g, '/')
    const paddedPayload = normalizedPayload.padEnd(Math.ceil(normalizedPayload.length / 4) * 4, '=')
    const decoded = JSON.parse(atob(paddedPayload))
    return typeof decoded.exp === 'number' ? decoded.exp * 1000 : null
  } catch {
    return null
  }
}

function isExpired(token: string | null) {
  const expiresAt = getTokenExpiry(token)
  return expiresAt !== null && expiresAt <= Date.now()
}

function parseStoredUser(value: string | null): UserInfo | null {
  if (!value) return null

  try {
    const parsed = JSON.parse(value)
    if (
      parsed &&
      typeof parsed === 'object' &&
      typeof parsed.userName === 'string' &&
      typeof parsed.role === 'string'
    ) {
      return { userName: parsed.userName, role: parsed.role }
    }
  } catch {
    // Invalid localStorage should clear the session instead of crashing the app shell.
  }

  return null
}

function clearStoredSession() {
  localStorage.removeItem('accessToken')
  localStorage.removeItem('authUser')
}

export const useAuthStore = defineStore('auth', () => {
  const accessToken = ref<string | null>(localStorage.getItem('accessToken'))
  const storedUser = localStorage.getItem('authUser')
  const parsedUser = parseStoredUser(storedUser)
  const user = ref<UserInfo | null>(parsedUser)

  if ((storedUser && !parsedUser) || isExpired(accessToken.value)) {
    accessToken.value = null
    user.value = null
    clearStoredSession()
  }

  const isAuthenticated = computed(() => !!accessToken.value && !isExpired(accessToken.value))
  const userRole = computed(() => user.value?.role ?? '')

  // Configure client auth header on init if token exists
  if (accessToken.value) {
    setAuthToken(accessToken.value)
  }

  async function login(email: string, password: string) {
    const data = await authApi.login(email, password)

    const token = data.accessToken ?? ''
    accessToken.value = token
    user.value = { userName: data.userName ?? '', role: data.role ?? '' }

    localStorage.setItem('accessToken', token)
    localStorage.setItem('authUser', JSON.stringify(user.value))
    setAuthToken(token)
  }

  async function register(userName: string, email: string, password: string) {
    await authApi.register(userName, email, password)
  }

  function logout() {
    accessToken.value = null
    user.value = null
    clearStoredSession()
    setAuthToken(null)
  }

  function ensureFreshSession() {
    if (!accessToken.value || isExpired(accessToken.value)) {
      logout()
      return false
    }

    return true
  }

  return { user, accessToken, isAuthenticated, userRole, login, register, logout, ensureFreshSession }
})
