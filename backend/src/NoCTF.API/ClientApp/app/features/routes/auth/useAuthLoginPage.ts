import { toast } from 'vue-sonner'
import { useHumanVerification } from '~/features/security/useHumanVerification'
import { useAuthThemeArtwork } from './useAuthThemeArtwork'

/** Owns the standalone login page workflow. */
export function useAuthLoginPage() {
  const route = useRoute()
  const { login } = useAuth()
  const { request: requestHumanVerification } = useHumanVerification()
  const { authArtwork } = useAuthThemeArtwork()
  const { configuration } = usePlatform()
  const loginName = ref('')
  const password = ref('')
  const error = ref<string | null>(null)
  const pending = ref(false)

  async function submit() {
    error.value = null
    if (!loginName.value || !password.value) {
      error.value = translate('ui.pleaseEnterUsernameEmailAndPassword')
      return
    }
    pending.value = true
    try {
      const verificationHeaders = await requestHumanVerification('login')
      if (verificationHeaders === null) return
      await login(loginName.value, password.value, verificationHeaders)
      password.value = ''
      toast.success(translate('ui.loginSuccessful'))
      const candidate = route.query.redirect
      const redirect = typeof candidate === 'string' && candidate.startsWith('/') && !candidate.startsWith('//')
        ? candidate
        : '/'
      await navigateTo(redirect)
    }
    catch (requestError) {
      error.value = parseApiError(requestError, translate('ui.loginFailedPleaseCheckUsernameOrPassword')).message
    }
    finally {
      pending.value = false
    }
  }

  return {
    authArtwork,
    configuration,
    loginName,
    password,
    error,
    pending,
    submit,
  }
}

export type AuthLoginPageViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useAuthLoginPage>>
