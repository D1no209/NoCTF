import { authenticationRequestEmailVerification, registerEndpoint } from '../../../api'
import registerCharacter from '~/assets/images/auth/register-character.png'
import loginCharacter from '~/assets/images/auth/login-character.png'

/** Owns the standalone registration page workflow. */
export function useAuthRegisterPage() {
  const { configuration } = usePlatform()
  const userName = ref('')
  const email = ref('')
  const password = ref('')
  const confirmPassword = ref('')
  const error = ref<string | null>(null)
  const pending = ref(false)
  const registered = ref<{
    requiresEmailVerification?: boolean
    verificationEmailQueued?: boolean
  } | null>(null)
  const resendPending = ref(false)
  const resendDone = ref(false)
  const resendError = ref<string | null>(null)

  async function submit() {
    error.value = null
    if (password.value !== confirmPassword.value) {
      error.value = translate('ui.thePasswordsEnteredTwiceAreInconsistent')
      return
    }
    pending.value = true
    try {
      const { data, error: requestError } = await registerEndpoint({
        body: { userName: userName.value, email: email.value, password: password.value },
      })
      if (requestError) throw parseApiError(requestError)
      password.value = ''
      confirmPassword.value = ''
      registered.value = data ?? {}
    }
    catch (requestError) {
      error.value = parseApiError(requestError).message
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
      resendError.value = parseApiError(requestError).message
    }
    finally {
      resendPending.value = false
    }
  }

  return {
    registerCharacter,
    loginCharacter,
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
    submit,
    resendVerification,
  }
}

export type AuthRegisterPageViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useAuthRegisterPage>>
