import { message as describeMessage } from '../../../utils/i18n'
import type { UiMessage } from '../../../utils/i18n'
import { authenticationRequestEmailVerification, registerEndpoint } from '../../../api'
import { useHumanVerification } from '~/features/security/useHumanVerification'
import { useAuthThemeArtwork } from './useAuthThemeArtwork'

/** Owns the standalone registration page workflow. */
export function useAuthRegisterPage() {
  const route = useRoute()
  const { configuration } = usePlatform()
  const { request: requestHumanVerification } = useHumanVerification()
  const { authArtwork } = useAuthThemeArtwork()
  const userName = ref('')
  const email = ref('')
  const password = ref('')
  const confirmPassword = ref('')
  const error = ref<UiMessage | null>(null)
  const pending = ref(false)
  const registered = ref<{
    requiresEmailVerification?: boolean
    verificationEmailQueued?: boolean
  } | null>(null)
  const resendPending = ref(false)
  const resendDone = ref(false)
  const resendError = ref<UiMessage | null>(null)
  const loginTarget = computed(() => {
    const candidate = route.query.redirect
    const redirect = typeof candidate === 'string'
      && candidate.startsWith('/')
      && !candidate.startsWith('//')
      ? candidate
      : null
    return redirect
      ? { path: '/auth/login', query: { redirect } }
      : { path: '/auth/login' }
  })

  async function submit() {
    error.value = null
    if (password.value !== confirmPassword.value) {
      error.value = describeMessage('common.authRegister.description.passwordsEnteredTwiceInconsistent')
      return
    }
    pending.value = true
    try {
      const verificationHeaders = await requestHumanVerification('registration')
      if (verificationHeaders === null) return
      const { data, error: requestError } = await registerEndpoint({
        headers: verificationHeaders,
        body: { userName: userName.value, email: email.value, password: password.value },
      })
      if (requestError) throw parseApiError(requestError)
      password.value = ''
      confirmPassword.value = ''
      registered.value = data ?? {}
    }
    catch (requestError) {
      error.value = parseApiError(requestError).displayMessage
    }
    finally {
      pending.value = false
    }
  }

  async function resendVerification() {
    resendError.value = null
    resendPending.value = true
    try {
      const { error: requestError } = await authenticationRequestEmailVerification({
        body: { email: email.value },
      })
      if (requestError) throw parseApiError(requestError)
      resendDone.value = true
    }
    catch (requestError) {
      resendError.value = parseApiError(requestError).displayMessage
    }
    finally {
      resendPending.value = false
    }
  }

  return {
    authArtwork,
    configuration,
    userName,
    email,
    password,
    confirmPassword,
    error,
    pending,
    registered,
    resendPending,
    resendDone,
    resendError,
    loginTarget,
    submit,
    resendVerification,
  }
}

export type AuthRegisterPageViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useAuthRegisterPage>>
