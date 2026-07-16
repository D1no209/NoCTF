import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { authApi, setAuthToken } from '@/api/noctf'

interface UserInfo {
  userName: string
  role: string
}

function readStoredUser(): UserInfo | null {
  const storedUser = localStorage.getItem('authUser')
  if (!storedUser)
    return null

  try {
    const parsed = JSON.parse(storedUser) as Partial<UserInfo>
    if (typeof parsed.userName === 'string' && typeof parsed.role === 'string')
      return { userName: parsed.userName, role: parsed.role }
  }
  catch {
    // A malformed persisted session should never prevent the app from booting.
  }

  localStorage.removeItem('authUser')
  return null
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

export const useAuthStore = defineStore('auth', () => {
  const accessToken = ref<string | null>(localStorage.getItem('accessToken'))
  const user = ref<UserInfo | null>(readStoredUser())

  if (isExpired(accessToken.value)) {
    accessToken.value = null
    user.value = null
    localStorage.removeItem('accessToken')
    localStorage.removeItem('authUser')
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
    localStorage.removeItem('accessToken')
    localStorage.removeItem('authUser')
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
