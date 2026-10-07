import { exchangeMfaRecoveryEndpoint } from '../../../api'
import { message } from '../../../utils/i18n'
import type { UiMessage } from '../../../utils/i18n'
export function useMfaRecoveryPage() {
  const route = useRoute()
  const auth = useAuth()
  const error = shallowRef<UiMessage | null>(null)
  const loading = ref(true)
  async function exchange() {
    const token = typeof route.query.token === 'string' ? route.query.token : ''
    const query = { ...route.query }; delete query.token
    await navigateTo({ path: route.path, query }, { replace: true })
    auth.invalidate()
    if (!token) { error.value = message('mfa.error.InvalidRecoveryGrant'); loading.value = false; return }
    const result = await exchangeMfaRecoveryEndpoint({ body: { token } })
    if (result.error || !result.data) { error.value = parseApiError(result.error, message('mfa.error.InvalidRecoveryGrant')).displayMessage; loading.value = false; return }
    await navigateTo('/auth/mfa', { replace: true })
  }
  onMounted(() => void exchange())
  return { error, loading }
}
export type MfaRecoveryPageViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useMfaRecoveryPage>>
