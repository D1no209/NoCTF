import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { client } from '@/api/generated/client.gen'
import {
  noCtfapiEndpointsAuthLoginEndpoint,
  noCtfapiEndpointsAuthRegisterEndpoint,
} from '@/api/generated/sdk.gen'

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
    client.setConfig({
      headers: { Authorization: `Bearer ${accessToken.value}` },
    })
  }

  async function login(email: string, password: string) {
    const { data, error } = await noCtfapiEndpointsAuthLoginEndpoint({
      body: { email, password },
    })

    if (error || !data) {
      throw new Error('Login failed')
    }

    const token = data.accessToken ?? ''
    accessToken.value = token
    user.value = { userName: data.userName ?? '', role: data.role ?? '' }

    localStorage.setItem('accessToken', token)
    localStorage.setItem('authUser', JSON.stringify(user.value))
    client.setConfig({
      headers: { Authorization: `Bearer ${token}` },
    })
  }

  async function register(userName: string, email: string, password: string) {
    const { data, error } = await noCtfapiEndpointsAuthRegisterEndpoint({
      body: { userName, email, password },
    })

    if (error || !data) {
      throw new Error('Registration failed')
    }
  }

  function logout() {
    accessToken.value = null
    user.value = null
    localStorage.removeItem('accessToken')
    localStorage.removeItem('authUser')
    client.setConfig({ headers: { Authorization: '' } })
  }

  return { user, accessToken, isAuthenticated, userRole, login, register, logout }
})
