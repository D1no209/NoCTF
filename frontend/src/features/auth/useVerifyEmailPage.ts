import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { authApi } from '@/api/noctf'

export type VerificationState = 'waiting' | 'verifying' | 'verified' | 'invalid'
export type ResendOutcome = 'sent' | 'failed' | 'skipped'

export function useVerifyEmailPage() {
  const route = useRoute()
  const router = useRouter()
  const state = ref<VerificationState>('waiting')
  const email = ref(typeof route.query.email === 'string' ? route.query.email : '')
  const resending = ref(false)
  const retrySeconds = ref(0)
  const deliveryFailed = computed(() => route.query.delivery === 'failed')
  let retryTimer: number | null = null

  function startRetryCountdown() {
    retrySeconds.value = 60
    if (retryTimer !== null)
      window.clearInterval(retryTimer)
    retryTimer = window.setInterval(() => {
      retrySeconds.value = Math.max(0, retrySeconds.value - 1)
      if (retrySeconds.value === 0 && retryTimer !== null) {
        window.clearInterval(retryTimer)
        retryTimer = null
      }
    }, 1000)
  }

  async function verify(token: string) {
    state.value = 'verifying'
    await router.replace({ name: 'verify-email', query: email.value ? { email: email.value } : {} })
    try {
      await authApi.verifyEmail(token)
      state.value = 'verified'
    }
    catch {
      state.value = 'invalid'
    }
  }

  async function resend(): Promise<ResendOutcome> {
    if (!email.value.trim() || resending.value || retrySeconds.value > 0)
      return 'skipped'

    resending.value = true
    try {
      await authApi.resendEmailVerification(email.value.trim())
      startRetryCountdown()
      return 'sent'
    }
    catch {
      return 'failed'
    }
    finally {
      resending.value = false
    }
  }

  function goLogin() {
    void router.push('/login')
  }

  onMounted(() => {
    const token = typeof route.query.token === 'string' ? route.query.token : ''
    if (token)
      void verify(token)
  })

  onBeforeUnmount(() => {
    if (retryTimer !== null)
      window.clearInterval(retryTimer)
  })

  return {
    state,
    email,
    resending,
    retrySeconds,
    deliveryFailed,
    verify,
    resend,
    goLogin,
  }
}
