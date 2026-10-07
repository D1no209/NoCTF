import { beginMfaEnrollmentEndpoint, cancelMfaFlowEndpoint, getMfaFlowEndpoint, verifyMfaChallengeEndpoint, confirmMfaEnrollmentEndpoint, confirmAccountMfaEnrollmentEndpoint } from '../../../api'
import type { NoCtfapiEndpointsAuthenticationMfaMfaFlowResponse, NoCtfApplicationAuthenticationMfaMfaVerificationMethod } from '../../../api'
import { message } from '../../../utils/i18n'
import type { UiMessage } from '../../../utils/i18n'
import { isEnrollmentFlow, normalizeMfaCode, validMfaCode, safeMfaReturnPath } from './flow'

export function useMfaPage() {
  const route = useRoute()
  const auth = useAuth()
  const flow = shallowRef<NoCtfapiEndpointsAuthenticationMfaMfaFlowResponse | null>(null)
  const loading = ref(true)
  const pending = ref(false)
  const error = shallowRef<UiMessage | null>(null)
  const code = ref('')
  const method = ref<NoCtfApplicationAuthenticationMfaMfaVerificationMethod>('Totp')
  const recoveryCodes = useState<string[]>('mfa:recovery-codes', () => [])
  const copied = ref(false)
  const now = ref(Date.now())
  let timer: ReturnType<typeof setInterval> | undefined
  const enrollment = computed(() => isEnrollmentFlow(flow.value))
  const expired = computed(() => Boolean(flow.value?.expiresAt && new Date(flow.value.expiresAt).getTime() <= now.value))
  const canSubmit = computed(() => !pending.value && !expired.value && validMfaCode(code.value, method.value))
  const returnPath = computed(() => safeMfaReturnPath(route.query.redirect ?? flow.value?.returnPath))

  async function load() {
    if (recoveryCodes.value.length) { loading.value = false; return }
    loading.value = true
    error.value = null
    const result = await getMfaFlowEndpoint()
    if (result.error || !result.data) { error.value = parseApiError(result.error, message('mfa.error.FlowExpired')).displayMessage; loading.value = false; return }
    flow.value = result.data
    if (enrollment.value && !flow.value.primaryAuthenticationRequired && !flow.value.secret) {
      const begun = await beginMfaEnrollmentEndpoint()
      if (begun.error || !begun.data) error.value = parseApiError(begun.error).displayMessage
      else flow.value = begun.data
    }
    loading.value = false
  }
  async function submit() {
    if (!canSubmit.value || !flow.value || flow.value.primaryAuthenticationRequired) return
    pending.value = true
    error.value = null
    try {
      const value = normalizeMfaCode(code.value, method.value)
      if (route.query.account === '1') {
        const result = await confirmAccountMfaEnrollmentEndpoint({ body: { code: value } })
        if (result.error || !result.data) throw result.error
        recoveryCodes.value = result.data.recoveryCodes ?? []
        auth.invalidate()
      } else {
        const result = enrollment.value ? await confirmMfaEnrollmentEndpoint({ body: { code: value } })
          : await verifyMfaChallengeEndpoint({ body: { code: value, method: method.value } })
        if (result.error || !result.data) throw result.error
        await auth.acceptAuthentication(result.data)
        if (result.data.state === 'Authenticated') recoveryCodes.value = result.data.recoveryCodes ?? []
      }
      flow.value = flow.value ? { ...flow.value, secret: null, provisioningUri: null } : null
      code.value = ''
      if (!recoveryCodes.value.length) await finish()
    } catch (requestError) { error.value = parseApiError(requestError).displayMessage; code.value = '' }
    finally { pending.value = false }
  }
  function toggleMethod() { method.value = method.value === 'Totp' ? 'RecoveryCode' : 'Totp'; code.value = ''; error.value = null }
  async function copy(value: string) { try { await navigator.clipboard.writeText(value); copied.value = true } catch { copied.value = false } }
  function copySecret() { if (flow.value?.secret) void copy(flow.value.secret) }
  function copyCodes() { void copy(recoveryCodes.value.join('\n')) }
  async function cancel() { await cancelMfaFlowEndpoint(); auth.invalidate(); await navigateTo('/auth/login') }
  async function finish() { recoveryCodes.value = []; await navigateTo(route.query.account === '1' ? '/auth/login' : returnPath.value) }
  function primaryLogin() { return navigateTo({ path: '/auth/login', query: { redirect: '/auth/mfa' } }) }
  onMounted(() => { void load(); timer = setInterval(() => { now.value = Date.now() }, 1000) })
  onBeforeUnmount(() => { if (timer) clearInterval(timer); flow.value = null; code.value = ''; recoveryCodes.value = [] })
  return { flow, loading, pending, error, code, method, recoveryCodes, copied, enrollment, expired, canSubmit, submit, toggleMethod, copySecret, copyCodes, cancel, finish, primaryLogin }
}
export type MfaPageViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useMfaPage>>
