

import { authenticationCompletePasswordReset, authenticationRequestPasswordReset } from '../../../api'

/** Owns state, effects and commands for AuthPasswordResetPage. */
export function useAuthPasswordResetPage() {
  const route = useRoute()

  const { configuration } = usePlatform()

  const token = computed(() => (typeof route.query.token === 'string' ? route.query.token : null))

  const email = ref('')

  const requested = ref(false)

  const newPassword = ref('')

  const confirmPassword = ref('')

  const completed = ref(false)

  const error = ref<string | null>(null)

  const pending = ref(false)

  async function requestReset() {
    error.value = null
    pending.value = true
    try {
      const { error: apiError } = await authenticationRequestPasswordReset({ body: { email: email.value } })
      if (apiError) throw parseApiError(apiError)
      requested.value = true
    }
    catch (e) {
      error.value = parseApiError(e).message
    }
    finally {
      pending.value = false
    }
  }

  async function completeReset() {
    error.value = null
    if (newPassword.value !== confirmPassword.value) {
      error.value = translate("ui.thePasswordsEnteredTwiceAreInconsistent")
      return
    }
    pending.value = true
    try {
      const { error: apiError } = await authenticationCompletePasswordReset({
        body: { token: token.value!, newPassword: newPassword.value },
      })
      if (apiError) throw parseApiError(apiError)
      completed.value = true
    }
    catch (e) {
      error.value = parseApiError(e).message
    }
    finally {
      pending.value = false
    }
  }

  return {
      configuration,
      token,
      email,
      requested,
      newPassword,
      confirmPassword,
      completed,
      error,
      pending,
      requestReset,
      completeReset
    }
}

export type AuthPasswordResetPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAuthPasswordResetPage>>>
