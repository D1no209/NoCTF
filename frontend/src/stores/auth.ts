import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { authApi, setAuthToken } from '@/api/noctf'

interface UserInfo {
  userName: string
  role: string
}

export const useAuthStore = defineStore('auth', () => {
  const accessToken = ref<string | null>(localStorage.getItem('accessToken'))
  const storedUser = localStorage.getItem('authUser')
  const user = ref<UserInfo | null>(storedUser ? JSON.parse(storedUser) : null)

  const isAuthenticated = computed(() => !!accessToken.value)
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

  return { user, accessToken, isAuthenticated, userRole, login, register, logout }
})
