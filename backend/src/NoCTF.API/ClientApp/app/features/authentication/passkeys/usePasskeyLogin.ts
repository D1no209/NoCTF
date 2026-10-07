import { beginPasskeyLoginEndpoint, completePasskeyLoginEndpoint, getPasskeyCapabilitiesEndpoint, cancelPasskeyCeremonyEndpoint } from '../../../api'
import { passkeyBrowserSupported, requestPasskey, passkeyCancelled } from './webauthn'
import { message } from '../../../utils/i18n'
import type { UiMessage } from '../../../utils/i18n'
import { safeMfaReturnPath } from '../mfa/flow'
export function usePasskeyLogin() {
  const auth = useAuth()
  const route = useRoute()
  const available = ref(false)
  const pending = ref(false)
  const error = shallowRef<UiMessage | null>(null)
  let abort: AbortController | undefined
  async function load() { const result = await getPasskeyCapabilitiesEndpoint(); available.value = Boolean(result.data?.available && passkeyBrowserSupported()) }
  async function signIn() {
    if (!available.value || pending.value) return
    pending.value = true; error.value = null; abort = new AbortController()
    try {
      const options = await beginPasskeyLoginEndpoint({ body: { returnPath: safeMfaReturnPath(route.query.redirect) }, signal: abort.signal })
      if (options.error || !options.data?.publicKey) throw options.error
      const credentialJson = await requestPasskey(options.data.publicKey, abort.signal)
      const complete = await completePasskeyLoginEndpoint({ body: { credentialJson }, signal: abort.signal })
      if (complete.error || !complete.data) throw complete.error
      if (await auth.acceptAuthentication(complete.data)) await navigateTo(complete.data.state === 'Authenticated' ? complete.data.returnPath : '/')
    } catch (requestError) {
      error.value = passkeyCancelled(requestError) ? message('passkeys.cancelled') : parseApiError(requestError, message('passkeys.failed')).displayMessage
      await cancelPasskeyCeremonyEndpoint()
    } finally { pending.value = false; abort = undefined }
  }
  function cancel() { abort?.abort() }
  onMounted(() => void load())
  onBeforeUnmount(cancel)
  return { available, pending, error, signIn, cancel }
}
