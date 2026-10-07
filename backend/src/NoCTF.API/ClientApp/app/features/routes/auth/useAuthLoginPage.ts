import { message as describeMessage } from '../../../utils/i18n'
import type { UiMessage } from '../../../utils/i18n'
import { toast } from '../../../utils/message-toast'
import { useHumanVerification } from '~/features/security/useHumanVerification'
import { useAuthThemeArtwork } from './useAuthThemeArtwork'
import { authenticationSsoBeginLogin, authenticationSsoListProviders } from '~/api'
import type { NoCtfapiEndpointsAuthenticationPublicSsoProviderResponse } from '~/api'

/** Owns the standalone login page workflow. */
export function useAuthLoginPage() {
  const route = useRoute()
  const { login } = useAuth()
  const {
    request: requestHumanVerification,
    inlineCap,
    retryInlineCap,
  } = useHumanVerification()
  const { authArtwork } = useAuthThemeArtwork()
  const { configuration } = usePlatform()
  const loginName = ref('')
  const password = ref('')
  const error = ref<UiMessage | null>(null)
  const pending = ref(false)
  const capVerified = ref(false)
  const lastCapChallengeId = ref(0)

  watch(inlineCap, current => {
    if (!current) return
    lastCapChallengeId.value = current.id
  }, { flush: 'sync' })

  const capVerification = computed(() => {
    if (inlineCap.value) {
      const failed = inlineCap.value.errorMessage !== null
      return {
        id: inlineCap.value.id,
        state: failed ? 'error' as const : 'running' as const,
        progress: inlineCap.value.progress,
        label: inlineCap.value.errorMessage
          ?? translate('common.authLogin.label.computingProofWorkProgress', { progress: inlineCap.value.progress }),
      }
    }
    if (!capVerified.value) return null
    return {
      id: lastCapChallengeId.value,
      state: 'success' as const,
      progress: 100,
      label: translate('common.label.verificationComplete'),
    }
  })
  const capCanRetry = computed(() => capVerification.value?.state === 'error')
  const submitDisabled = computed(() => pending.value && !capCanRetry.value)
  const ssoProviders = ref<NoCtfapiEndpointsAuthenticationPublicSsoProviderResponse[]>([])
  const ssoLoading = ref(true)
  const ssoPendingId = ref<string | null>(null)

  async function loadSsoProviders() {
    ssoLoading.value = true
    const { data } = await authenticationSsoListProviders()
    ssoProviders.value = data?.items ?? []
    ssoLoading.value = false
  }

  async function beginSso(providerId: string) {
    if (ssoPendingId.value) return
    error.value = null
    ssoPendingId.value = providerId
    const candidate = route.query.redirect
    const returnPath = typeof candidate === 'string' && candidate.startsWith('/') && !candidate.startsWith('//')
      ? candidate
      : '/'
    try {
      const { data, error: requestError } = await authenticationSsoBeginLogin({
        body: { providerId, returnPath },
      })
      if (requestError || !data?.authorizationUrl) throw requestError
      window.location.assign(data.authorizationUrl)
    }
    catch (requestError) {
      error.value = parseApiError(requestError, describeMessage('sso.loginStartFailed')).displayMessage
      ssoPendingId.value = null
    }
  }

  onMounted(() => void loadSsoProviders())

  async function submit() {
    if (pending.value) {
      if (capCanRetry.value) retryInlineCap()
      return
    }
    error.value = null
    if (!loginName.value || !password.value) {
      error.value = describeMessage('common.authLogin.description.enterUsernameEmailPassword')
      return
    }
    pending.value = true
    capVerified.value = false
    lastCapChallengeId.value = 0
    try {
      const verificationHeaders = await requestHumanVerification('login', 'login-inline')
      if (verificationHeaders === null) return
      if (lastCapChallengeId.value > 0) {
        capVerified.value = true
      }
      const authenticated = await login(loginName.value, password.value, verificationHeaders)
      password.value = ''
      if (!authenticated) return
      toast.success(describeMessage('common.label.loginSuccessful'))
      const candidate = route.query.redirect
      const redirect = typeof candidate === 'string' && candidate.startsWith('/') && !candidate.startsWith('//')
        ? candidate
        : '/'
      await navigateTo(redirect)
    }
    catch (requestError) {
      error.value = parseApiError(requestError, describeMessage('auth.login.invalidCredentials')).displayMessage
    }
    finally {
      pending.value = false
      capVerified.value = false
    }
  }

  return {
    authArtwork,
    configuration,
    loginName,
    password,
    error,
    pending,
    capVerification,
    capCanRetry,
    submitDisabled,
    submit,
    ssoProviders,
    ssoLoading,
    ssoPendingId,
    beginSso,
  }
}

export type AuthLoginPageViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useAuthLoginPage>>
