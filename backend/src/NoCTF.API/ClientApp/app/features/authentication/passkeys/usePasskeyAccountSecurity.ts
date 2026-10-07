import { markRaw } from 'vue'
import { beginPasskeyRegistrationEndpoint, completePasskeyRegistrationEndpoint, getMyPasskeysEndpoint, renameMyPasskeyEndpoint, removeMyPasskeyEndpoint, cancelPasskeyCeremonyEndpoint, logoutEndpoint } from '../../../api'
import type { NoCtfApplicationAuthenticationPasskeysPasskeyAccountStatus, NoCtfApplicationAuthenticationPasskeysPasskeyAccountCredential, NoCtfDomainIdentityMfaMfaOperation } from '../../../api'
import { message } from '../../../utils/i18n'
import type { UiMessage } from '../../../utils/i18n'
import { toast } from '../../../utils/message-toast'
import { bindViewState } from '../../shared/view-state'
import { useMfaStepUp } from '../mfa/useMfaStepUp'
import StepUpView from '../../../components/views/authentication/MfaStepUpView.vue'
import { createPasskey, passkeyBrowserSupported, passkeyCancelled } from './webauthn'

export function usePasskeyAccountSecurity() {
  const auth = useAuth()
  const status = shallowRef<NoCtfApplicationAuthenticationPasskeysPasskeyAccountStatus | null>(null)
  const loading = ref(true)
  const pending = ref(false)
  const error = shallowRef<UiMessage | null>(null)
  const dialog = ref<'add' | 'rename' | 'remove' | null>(null)
  const name = ref('')
  const selected = shallowRef<NoCtfApplicationAuthenticationPasskeysPasskeyAccountCredential | null>(null)
  const supported = ref(false)
  let abort: AbortController | undefined
  const stepUp = bindViewState(useMfaStepUp())
  const canAdd = computed(() => supported.value && status.value?.available && (status.value.credentials?.length ?? 0) < (status.value.maximumCredentials ?? 10))
  async function load() {
    const result = await getMyPasskeysEndpoint(); loading.value = false
    if (result.error || !result.data) error.value = parseApiError(result.error).displayMessage
    else status.value = result.data
  }
  function openAdd() { dialog.value = 'add'; name.value = ''; selected.value = null; error.value = null }
  function openRename(value: NoCtfApplicationAuthenticationPasskeysPasskeyAccountCredential) { dialog.value = 'rename'; name.value = value.name ?? ''; selected.value = value; error.value = null }
  function openRemove(value: NoCtfApplicationAuthenticationPasskeysPasskeyAccountCredential) { dialog.value = 'remove'; selected.value = value; error.value = null }
  function setDialogOpen(value: boolean) { if (!value && !pending.value) dialog.value = null }
  async function signInAgain() { await logoutEndpoint(); auth.invalidate(); await navigateTo('/auth/login') }
  async function guarded(operation: NoCtfDomainIdentityMfaMfaOperation, target: string, action: () => Promise<void>) {
    if (status.value?.needsLocalProof) await stepUp.execute(operation, target, action)
    else await action()
  }
  async function submitName() {
    if (pending.value || !name.value.trim() || name.value.trim().length > 64) return
    const value = name.value.trim(); const intent = dialog.value; const id = selected.value?.id
    error.value = null; dialog.value = null
    try {
      if (intent === 'add' && auth.user.value?.userId) await guarded('AddPasskey', auth.user.value.userId, () => add(value))
      else if (intent === 'rename' && id) await guarded('RenamePasskey', id, () => rename(id, value))
    } catch (requestError) { error.value = parseApiError(requestError).displayMessage }
  }
  async function add(value: string) {
    pending.value = true; abort = new AbortController()
    try {
      const options = await beginPasskeyRegistrationEndpoint({ body: { name: value }, signal: abort.signal })
      if (options.error || !options.data?.publicKey) throw options.error
      const credentialJson = await createPasskey(options.data.publicKey, abort.signal)
      const result = await completePasskeyRegistrationEndpoint({ body: { credentialJson }, signal: abort.signal })
      if (result.error || !result.data) throw result.error
      toast.success(message('passkeys.saved')); auth.invalidate(); await navigateTo('/auth/login')
    } catch (requestError) {
      error.value = passkeyCancelled(requestError) ? message('passkeys.cancelled') : parseApiError(requestError, message('passkeys.failed')).displayMessage
      await cancelPasskeyCeremonyEndpoint()
    } finally { pending.value = false; abort = undefined }
  }
  async function rename(id: string, value: string) {
    pending.value = true
    try {
      const result = await renameMyPasskeyEndpoint({ path: { credentialId: id }, body: { name: value } })
      if (result.error || !result.data) throw result.error
      toast.success(message('passkeys.renamed')); await load()
    } catch (requestError) { error.value = parseApiError(requestError).displayMessage }
    finally { pending.value = false }
  }
  async function confirmRemove() {
    const id = selected.value?.id; if (!id || pending.value) return
    dialog.value = null; error.value = null
    try {
      await guarded('RemovePasskey', id, async () => {
        pending.value = true
        try {
          const result = await removeMyPasskeyEndpoint({ path: { credentialId: id } }); if (result.error) throw result.error
          toast.success(message('passkeys.removed')); auth.invalidate(); await navigateTo('/auth/login')
        } catch (requestError) { error.value = parseApiError(requestError).displayMessage }
        finally { pending.value = false }
      })
    } catch (requestError) { error.value = parseApiError(requestError).displayMessage }
  }
  onMounted(() => { supported.value = passkeyBrowserSupported(); void load() })
  onBeforeUnmount(() => abort?.abort())
  return { status, loading, pending, error, dialog, name, selected, canAdd, supported, stepUp, StepUpView: markRaw(StepUpView),
    openAdd, openRename, openRemove, setDialogOpen, submitName, confirmRemove, signInAgain }
}
export type PasskeyAccountSecurityViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof usePasskeyAccountSecurity>>
