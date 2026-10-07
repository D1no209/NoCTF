import { message as describeMessage } from '../../../utils/i18n'
import type { UiMessage } from '../../../utils/i18n'
import { toast } from '../../../utils/message-toast'
import { useHumanVerification } from '~/features/security/useHumanVerification'
import type { HumanVerificationHeaders } from '~/lib/human-verification-coordinator'
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
    inlineCapReady,
    retryInlineCap,
    consumeInlineCap,
  } = useHumanVerification()
  const { authArtwork } = useAuthThemeArtwork()
  const { configuration, ensureLoaded } = usePlatform()
  const loginName = ref('')
  const password = ref('')
  const error = ref<UiMessage | null>(null)
  const pending = ref(false)
  const lastCapChallengeId = ref(0)
  let preparedCap: Promise<HumanVerificationHeaders | null> | null = null
  let disposed = false

  onScopeDispose(() => {
    disposed = true
    preparedCap = null
  })

  async function prepareLoginCap() {
    await ensureLoaded()
    if (disposed || configuration.value?.humanVerification?.provider !== 'Cap') return null
    preparedCap ??= requestHumanVerification('login', 'login-inline')
    return preparedCap
  }

  watch(inlineCapReady, ready => {
    if (ready || pending.value || disposed) return
    preparedCap = null
    void prepareLoginCap()
  })

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
    if (!inlineCapReady.value) return null
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

  onMounted(() => {
    void loadSsoProviders()
    void prepareLoginCap()
  })

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
    try {
      await ensureLoaded()
      if (disposed) return
      let verificationHeaders: HumanVerificationHeaders | null
      if (configuration.value?.humanVerification?.provider === 'Cap') {
        if (capCanRetry.value) retryInlineCap()
        const prepared = await prepareLoginCap()
        preparedCap = null
        if (disposed || prepared === null) return
        verificationHeaders = consumeInlineCap(prepared)
        // Renew a proof that expired while submission was waiting for verification.
        if (verificationHeaders === null) {
          const renewed = await prepareLoginCap()
          preparedCap = null
          if (disposed || renewed === null) return
          verificationHeaders = consumeInlineCap(renewed)
        }
      }
      else {
        verificationHeaders = await requestHumanVerification('login', 'login-inline')
      }
      if (disposed || verificationHeaders === null) return
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
      if (!disposed) void prepareLoginCap()
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
    retryInlineCap,
    submit,
    ssoProviders,
    ssoLoading,
    ssoPendingId,
    beginSso,
  }
}

export type AuthLoginPageViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useAuthLoginPage>>
