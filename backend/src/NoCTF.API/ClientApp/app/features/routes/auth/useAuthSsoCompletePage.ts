import { toast } from 'vue-sonner'
import {
  authenticationSsoCompleteBinding,
  authenticationSsoGetFlow,
} from '~/api'
import type { NoCtfapiEndpointsAuthenticationSsoFlowStatusResponse } from '~/api'

export function useAuthSsoCompletePage() {
  const route = useRoute()
  const auth = useAuth()
  const flow = ref<NoCtfapiEndpointsAuthenticationSsoFlowStatusResponse | null>(null)
  const loading = ref(true)
  const pending = ref(false)
  const error = ref<string | null>(null)
  const flowId = computed(() => {
    const value = route.query.flow
    return typeof value === 'string' && /^[0-9a-f]{32}$/i.test(value)
      ? `${value.slice(0, 8)}-${value.slice(8, 12)}-${value.slice(12, 16)}-${value.slice(16, 20)}-${value.slice(20)}`
      : null
  })

  async function load(attempt = 0): Promise<void> {
    if (!flowId.value) {
      error.value = translate('sso.invalidFlow')
      loading.value = false
      return
    }
    const { data, error: requestError } = await authenticationSsoGetFlow({
      path: { flowId: flowId.value },
    })
    if (requestError || !data) {
      error.value = parseApiError(requestError, translate('sso.flowExpired')).message
      loading.value = false
      return
    }
    flow.value = data
    if ((data.state === 'Pending' || data.state === 'Processing') && attempt < 8) {
      await new Promise(resolve => setTimeout(resolve, 350))
      return load(attempt + 1)
    }
    loading.value = false
    if (data.state === 'Failed') {
      error.value = translate(ssoFailureMessage(data.failureCode ?? undefined))
      return
    }
    if (data.state === 'Authenticated' && data.intent === 'Login')
      await completeLogin()
  }

  async function completeLogin() {
    if (!flowId.value || pending.value) return
    pending.value = true
    error.value = null
    try {
      const returnPath = await auth.completeSsoLogin(flowId.value)
      toast.success(translate('sso.loginSuccessful'))
      await navigateTo(returnPath)
    }
    catch (requestError) {
      const parsed = parseApiError(requestError, translate('sso.loginFailed'))
      error.value = parsed.code === 'IdentityNotLinked'
        ? translate('sso.identityNotLinked')
        : parsed.message
    }
    finally {
      pending.value = false
    }
  }

  async function completeBinding() {
    if (!flowId.value || pending.value) return
    pending.value = true
    error.value = null
    const { error: requestError } = await authenticationSsoCompleteBinding({
      path: { flowId: flowId.value },
    })
    pending.value = false
    if (requestError) {
      error.value = parseApiError(requestError, translate('sso.bindingFailed')).message
      return
    }
    toast.success(translate('sso.bindingSuccessful'))
    await navigateTo('/')
  }

  onMounted(() => void load())

  return {
    flow,
    loading,
    pending,
    error,
    completeLogin,
    completeBinding,
  }
}

function ssoFailureMessage(code?: string): string {
  const messages: Record<string, string> = {
    SsoDisabled: 'sso.failure.ssoDisabled',
    ProviderUnavailable: 'sso.failure.providerUnavailable',
    ProviderChanged: 'sso.failure.providerChanged',
    FlowExpired: 'sso.failure.flowExpired',
    InvalidCorrelation: 'sso.failure.invalidCorrelation',
    IdentityNotLinked: 'sso.failure.identityNotLinked',
    IdentityAlreadyLinked: 'sso.failure.identityAlreadyLinked',
    AccountAlreadyLinked: 'sso.failure.accountAlreadyLinked',
    AccountUnavailable: 'sso.failure.accountUnavailable',
    ReauthenticationRequired: 'sso.failure.reauthenticationRequired',
    AuthenticationFailed: 'sso.failure.authenticationFailed',
  }
  return messages[code ?? ''] ?? 'sso.failure.authenticationFailed'
}

export type AuthSsoCompletePageViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useAuthSsoCompletePage>>
