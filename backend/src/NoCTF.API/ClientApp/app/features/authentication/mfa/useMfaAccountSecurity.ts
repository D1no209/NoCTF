import { markRaw } from 'vue'
import { beginMfaEnrollmentEndpoint, beginMfaRebindEndpoint, disableMfaEndpoint, getMfaStatusEndpoint, regenerateMfaRecoveryCodesEndpoint, logoutEndpoint } from '../../../api'
import type { NoCtfapiEndpointsAuthenticationMfaMfaStatusResponse } from '../../../api'
import type { UiMessage } from '../../../utils/i18n'
import { bindViewState } from '../../shared/view-state'
import StepUpView from '../../../components/views/authentication/MfaStepUpView.vue'
import { useMfaStepUp } from './useMfaStepUp'
export function useMfaAccountSecurity() {
  const auth = useAuth()
  const status = shallowRef<NoCtfapiEndpointsAuthenticationMfaMfaStatusResponse | null>(null)
  const error = shallowRef<UiMessage | null>(null)
  const loading = ref(true)
  const stepUp = bindViewState(useMfaStepUp())
  const recoveryCodes = useState<string[]>('mfa:recovery-codes', () => [])
  async function load() { const result = await getMfaStatusEndpoint(); loading.value = false; if (result.error || !result.data) error.value = parseApiError(result.error).displayMessage; else status.value = result.data }
  async function reauthenticate() { await logoutEndpoint(); auth.invalidate(); await navigateTo('/auth/login') }
  async function enroll() {
    try {
      const result = await beginMfaEnrollmentEndpoint()
      if (result.error || !result.data) throw result.error
      await navigateTo({ path: '/auth/mfa', query: { account: '1' } })
    } catch (requestError) { error.value = parseApiError(requestError).displayMessage }
  }
  async function manage(operation: 'RebindTotp' | 'DisableTotp' | 'RegenerateRecoveryCodes') {
    try {
      await stepUp.execute(operation, auth.user.value?.userId ?? null, async () => {
        try {
          if (operation === 'RebindTotp') {
            const result = await beginMfaRebindEndpoint(); if (result.error || !result.data) throw result.error
            await navigateTo({ path: '/auth/mfa', query: { account: '1' } })
          } else if (operation === 'RegenerateRecoveryCodes') {
            const result = await regenerateMfaRecoveryCodesEndpoint(); if (result.error || !result.data) throw result.error
            recoveryCodes.value = result.data.codes ?? []
            auth.invalidate(); await navigateTo({ path: '/auth/mfa', query: { account: '1' } })
          } else {
            const result = await disableMfaEndpoint(); if (result.error) throw result.error
            auth.invalidate(); await navigateTo('/auth/login')
          }
        } catch (requestError) { error.value = parseApiError(requestError).displayMessage }
      })
    } catch (requestError) { error.value = parseApiError(requestError).displayMessage }
  }
  function rebind() { void manage('RebindTotp') }
  function regenerate() { void manage('RegenerateRecoveryCodes') }
  function disable() { void manage('DisableTotp') }
  onMounted(() => void load())
  return { status, error, loading, stepUp, StepUpView: markRaw(StepUpView), reauthenticate, enroll, rebind, regenerate, disable }
}
export type MfaAccountSecurityViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useMfaAccountSecurity>>
