
import { api } from '../../../lib/api'
import { message as describeMessage } from '../../../utils/i18n'
import type { UiMessage } from '../../../utils/i18n'




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

  const error = ref<UiMessage | null>(null)

  const pending = ref(false)

  async function requestReset() {
    error.value = null
    pending.value = true
    try {
      let apiError: unknown;
      await api.api.v1.auth.passwordReset.request.post({ email: email.value }).catch(cause => { apiError = cause; return undefined });
      if (apiError) throw parseApiError(apiError)
      requested.value = true
    }
    catch (e) {
      error.value = parseApiError(e).displayMessage
    }
    finally {
      pending.value = false
    }
  }

  async function completeReset() {
    error.value = null
    if (newPassword.value !== confirmPassword.value) {
      error.value = describeMessage("common.authRegister.description.passwordsEnteredTwiceInconsistent")
      return
    }
    pending.value = true
    try {
      let apiError: unknown;
      await api.api.v1.auth.passwordReset.complete.post({ token: token.value!, newPassword: newPassword.value }).catch(cause => { apiError = cause; return undefined });
      if (apiError) throw parseApiError(apiError)
      completed.value = true
    }
    catch (e) {
      error.value = parseApiError(e).displayMessage
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
