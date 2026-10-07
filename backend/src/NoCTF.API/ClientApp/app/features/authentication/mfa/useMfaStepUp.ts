import { beginMfaStepUpEndpoint, verifyMfaStepUpEndpoint, cancelMfaFlowEndpoint } from '../../../api'
import type { NoCtfDomainIdentityMfaMfaOperation, NoCtfApplicationAuthenticationMfaMfaVerificationMethod } from '../../../api'
import type { UiMessage } from '../../../utils/i18n'
import { normalizeMfaCode, validMfaCode } from './flow'
export function useMfaStepUp() {
  const open = ref(false)
  const pending = ref(false)
  const code = ref('')
  const method = ref<NoCtfApplicationAuthenticationMfaMfaVerificationMethod>('Totp')
  const error = shallowRef<UiMessage | null>(null)
  let action: (() => Promise<void>) | null = null
  const canSubmit = computed(() => !pending.value && validMfaCode(code.value, method.value))
  async function execute(operation: NoCtfDomainIdentityMfaMfaOperation, targetId: string | null, command: () => Promise<void>) {
    if (pending.value || open.value) return
    pending.value = true; error.value = null; code.value = ''; method.value = 'Totp'
    const result = await beginMfaStepUpEndpoint({ body: { operation, targetId } })
    pending.value = false
    if (result.error || !result.data) throw parseApiError(result.error)
    action = command; open.value = true
  }
  async function submit() {
    if (!canSubmit.value || !action) return
    pending.value = true; error.value = null
    try {
      const result = await verifyMfaStepUpEndpoint({ body: { method: method.value, code: normalizeMfaCode(code.value, method.value) } })
      if (result.error || !result.data) throw result.error
      const command = action; action = null; code.value = ''; open.value = false
      await command()
    } catch (requestError) { error.value = parseApiError(requestError).displayMessage; code.value = '' }
    finally { pending.value = false }
  }
  function toggleMethod() { method.value = method.value === 'Totp' ? 'RecoveryCode' : 'Totp'; code.value = '' }
  async function setOpen(value: boolean) { if (!value && !pending.value) { open.value = false; action = null; code.value = ''; await cancelMfaFlowEndpoint() } }
  onBeforeUnmount(() => { action = null; code.value = '' })
  return { open, pending, code, method, error, canSubmit, execute, submit, toggleMethod, setOpen }
}
export type MfaStepUpViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useMfaStepUp>>
