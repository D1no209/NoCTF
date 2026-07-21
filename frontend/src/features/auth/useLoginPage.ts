import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ApiError } from '@/api/noctf'
import { useAuthStore } from '@/stores/auth'

export type LoginOutcome = 'success' | 'unverified' | 'failed'

export function useLoginPage() {
  const auth = useAuthStore()
  const router = useRouter()
  const route = useRoute()
  const loading = ref(false)

  async function login(email: string, password: string): Promise<LoginOutcome> {
    loading.value = true
    try {
      await auth.login(email, password)
      const redirect = typeof route.query.redirect === 'string' && route.query.redirect.startsWith('/')
        ? route.query.redirect
        : '/'
      await router.push(redirect)
      return 'success'
    }
    catch (error) {
      if (error instanceof ApiError && error.status === 403) {
        const query = email.includes('@') ? { email } : undefined
        await router.push({ name: 'verify-email', query })
        return 'unverified'
      }
      return 'failed'
    }
    finally {
      loading.value = false
    }
  }

  function goRegister() {
    void router.push('/register')
  }

  return {
    loading,
    login,
    goRegister,
  }
}
