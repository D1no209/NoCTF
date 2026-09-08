

import { authenticationRequestEmailVerification, registerEndpoint } from '../../../api'

/** Owns state, effects and commands for AuthRegisterPage. */
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
      error.value = translate("ui.thePasswordsEnteredTwiceAreInconsistent")
      return
    }
    pending.value = true
    try {
      const { data, error: apiError } = await registerEndpoint({
        body: { userName: userName.value, email: email.value, password: password.value },
      })
      if (apiError) throw parseApiError(apiError)
      registered.value = data ?? {}
    }
    catch (e) {
      error.value = parseApiError(e).message
    }
    finally {
      pending.value = false
    }
  }

  async function resendVerification() {
    resendError.value = null
    resendPending.value = true
    try {
      const { error: apiError } = await authenticationRequestEmailVerification({
        body: { email: email.value },
      })
      if (apiError) throw parseApiError(apiError)
      resendDone.value = true
    }
    catch (e) {
      resendError.value = parseApiError(e).message
    }
    finally {
      resendPending.value = false
    }
  }

  return {
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
      resendVerification
    }
}

export type AuthRegisterPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAuthRegisterPage>>>
