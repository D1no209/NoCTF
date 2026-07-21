import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

export type RegisterOutcome = 'verification' | 'created' | 'failed'

export function useRegisterPage() {
  const auth = useAuthStore()
  const router = useRouter()
  const loading = ref(false)

  async function register(userName: string, email: string, password: string): Promise<RegisterOutcome> {
    loading.value = true
    try {
      const result = await auth.register(userName, email, password)
      if (result.requiresEmailVerification) {
        await router.push({
          name: 'verify-email',
          query: {
            email,
            delivery: result.verificationEmailSent ? 'sent' : 'failed',
          },
        })
        return 'verification'
      }
      await router.push('/login')
      return 'created'
    }
    catch {
      return 'failed'
    }
    finally {
      loading.value = false
    }
  }

  function goLogin() {
    void router.push('/login')
  }

  return {
    loading,
    register,
    goLogin,
  }
}
