
import { api } from '../../../lib/api'
import type { MessageKey } from '~/locales/en'
import { message as describeMessage } from '../../../utils/i18n'
import type { UiMessage } from '../../../utils/i18n'
import { toast } from '../../../utils/message-toast'

import type { NoCTFAPIEndpointsAuthenticationSsoFlowStatusResponse } from '~/api/models'

export function useAuthSsoCompletePage() {
  const route = useRoute()
  const auth = useAuth()
  const flow = ref<NoCTFAPIEndpointsAuthenticationSsoFlowStatusResponse | null>(null)
  const loading = ref(true)
  const pending = ref(false)
  const error = ref<UiMessage | null>(null)
  const identityNotLinked = ref(false)
  const flowId = computed(() => {
    const value = route.query.flow
    return typeof value === 'string' && /^[0-9a-f]{32}$/i.test(value)
      ? `${value.slice(0, 8)}-${value.slice(8, 12)}-${value.slice(12, 16)}-${value.slice(16, 20)}-${value.slice(20)}`
      : null
  })
  const bindingReturnPath = computed(() => {
    const providerId = flow.value?.providerId
    const query = new URLSearchParams({ account: 'security' })
    if (providerId) query.set('ssoProvider', providerId)
    return `/?${query.toString()}`
  })
  const bindingLoginTarget = computed(() => ({
    path: '/auth/login',
    query: { redirect: bindingReturnPath.value },
  }))
  const bindingRegisterTarget = computed(() => ({
    path: '/auth/register',
    query: { redirect: bindingReturnPath.value },
  }))

  async function load(attempt = 0): Promise<void> {
    if (!flowId.value) {
      error.value = describeMessage('sso.invalidFlow')
      loading.value = false
      return
    }
    let requestError: unknown;
    const data = await api.api.v1.auth.sso.flows.byFlowId(flowId.value).get().catch(cause => { requestError = cause; return undefined });
    if (requestError || !data) {
      error.value = parseApiError(requestError, describeMessage('sso.flowExpired')).displayMessage
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
      error.value = describeMessage(ssoFailureMessage(data.failureCode ?? undefined))
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
      toast.success(describeMessage('sso.loginSuccessful'))
      await navigateTo(returnPath)
    }
    catch (requestError) {
      const parsed = parseApiError(requestError, describeMessage('sso.loginFailed'))
      if (parsed.code === 'IdentityNotLinked') {
        identityNotLinked.value = true
        error.value = null
      }
      else {
        error.value = parsed.displayMessage
      }
    }
    finally {
      pending.value = false
    }
  }

  async function completeBinding() {
    if (!flowId.value || pending.value) return
    pending.value = true
    error.value = null
    let requestError: unknown;
    await api.api.v1.auth.me.ssoBinding.flows.byFlowId(flowId.value).complete.post().catch(cause => { requestError = cause; return undefined });
    pending.value = false
    if (requestError) {
      error.value = parseApiError(requestError, describeMessage('sso.bindingFailed')).displayMessage
      return
    }
    toast.success(describeMessage('sso.bindingSuccessful'))
    await navigateTo('/')
  }

  onMounted(() => void load())

  return {
    flow,
    loading,
    pending,
    error,
    identityNotLinked,
    bindingLoginTarget,
    bindingRegisterTarget,
    completeLogin,
    completeBinding,
  }
}

function ssoFailureMessage(code?: string | null): MessageKey {
  const messages: Record<string, MessageKey> = {
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
