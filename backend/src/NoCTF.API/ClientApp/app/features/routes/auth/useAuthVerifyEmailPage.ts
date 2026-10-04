import type { UiMessage } from '../../../utils/i18n'
import { message as describeMessage } from '../../../utils/i18n'


import { authenticationRequestEmailVerification, resendEmailVerificationEndpoint, verifyEmailEndpoint } from '../../../api'

/** Owns state, effects and commands for AuthVerifyEmailPage. */
export function useAuthVerifyEmailPage() {
  const route = useRoute()

  const { isLoggedIn } = useAuth()

  const { configuration } = usePlatform()

  const state = ref<'idle' | 'verifying' | 'success' | 'failed'>('idle')

  const message = ref<UiMessage | null>(null)

  const resendPending = ref(false)

  const resendDone = ref(false)

  const email = ref('')

  const token = computed(() => (typeof route.query.token === 'string' ? route.query.token : null))

  onMounted(async () => {
    if (!token.value) return
    state.value = 'verifying'
    const { error } = await verifyEmailEndpoint({ body: { token: token.value } })
    if (error) {
      state.value = 'failed'
      message.value = parseApiError(error, describeMessage("common.authVerify.error.verificationLinkExpiredInvalid")).displayMessage
    }
    else {
      state.value = 'success'
    }
  })

  async function resend() {
    message.value = null
    resendPending.value = true
    try {
      const { error } = isLoggedIn.value
        ? await resendEmailVerificationEndpoint()
        : await authenticationRequestEmailVerification({ body: { email: email.value } })
      if (error) {
        message.value = parseApiError(error).displayMessage
        return
      }
      resendDone.value = true
    }
    catch (error) {
      message.value = parseApiError(error).displayMessage
    }
    finally {
      resendPending.value = false
    }
  }

  return {
      isLoggedIn,
      configuration,
      state,
      message,
      resendPending,
      resendDone,
      email,
      token,
      resend
    }
}

export type AuthVerifyEmailPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAuthVerifyEmailPage>>>
