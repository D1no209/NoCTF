

import { toast } from 'vue-sonner'

/** Owns state, effects and commands for AuthLoginPage. */
export function useAuthLoginPage() {
  const route = useRoute()

  const { login } = useAuth()

  const { configuration } = usePlatform()

  const loginName = ref('')

  const password = ref('')

  const error = ref<string | null>(null)

  const pending = ref(false)

  async function submit() {
    error.value = null
    if (!loginName.value || !password.value) {
      error.value = translate("ui.pleaseEnterUsernameEmailAndPassword")
      return
    }
    pending.value = true
    try {
      await login(loginName.value, password.value)
      toast.success(translate("ui.loginSuccessful"))
      const redirect = typeof route.query.redirect === 'string' ? route.query.redirect : '/'
      await navigateTo(redirect)
    }
    catch (e) {
      error.value = parseApiError(e, translate("ui.loginFailedPleaseCheckUsernameOrPassword")).message
    }
    finally {
      pending.value = false
    }
  }

  return {
      login,
      configuration,
      loginName,
      password,
      error,
      pending,
      submit
    }
}

export type AuthLoginPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAuthLoginPage>>>
