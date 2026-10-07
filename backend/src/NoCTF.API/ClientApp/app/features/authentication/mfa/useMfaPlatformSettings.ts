import { markRaw } from 'vue'
import { adminPlatformMfaChangePolicy, adminPlatformMfaChangeOidcTrust, adminPlatformMfaGetConfiguration } from '../../../api'
import type { NoCtfDomainIdentityMfaMfaPolicy, NoCtfapiEndpointsAdministrationPlatformOidcMfaTrustResponse } from '../../../api'
import type { UiMessage } from '../../../utils/i18n'
import { message } from '../../../utils/i18n'
import { toast } from '../../../utils/message-toast'
import { bindViewState } from '../../shared/view-state'
import StepUpView from '../../../components/views/authentication/MfaStepUpView.vue'
import { useMfaStepUp } from './useMfaStepUp'
export function useMfaPlatformSettings() {
  const auth = useAuth()
  const loading = ref(true)
  const error = shallowRef<UiMessage | null>(null)
  const policy = ref<NoCtfDomainIdentityMfaMfaPolicy>('Optional')
  const policies: NoCtfDomainIdentityMfaMfaPolicy[] = ['Optional', 'RequirePrivileged', 'RequireAllHumanUsers']
  const providers = shallowRef<NoCtfapiEndpointsAdministrationPlatformOidcMfaTrustResponse[]>([])
  const providerId = ref('')
  const trust = reactive({ enabled: false, maxAgeSeconds: 300, acr: '', amr: '' })
  const stepUp = bindViewState(useMfaStepUp())
  const available = computed(() => auth.user.value?.kind === 'Human')
  function syncProvider() {
    const provider = providers.value.find(value => value.providerId === providerId.value)
    trust.enabled = provider?.enabled ?? false; trust.maxAgeSeconds = provider?.maxAgeSeconds ?? 300
    trust.acr = provider?.acrValues?.join('\n') ?? ''; trust.amr = provider?.amrCombinations?.map(group => group.join(' ')).join('\n') ?? ''
  }
  async function load() {
    const result = await adminPlatformMfaGetConfiguration(); loading.value = false
    if (result.error || !result.data) { error.value = parseApiError(result.error).displayMessage; return }
    policy.value = result.data.policy ?? 'Optional'; providers.value = result.data.providers ?? []
    if (!providerId.value) providerId.value = providers.value[0]?.providerId ?? ''
    syncProvider()
  }
  async function savePolicy() {
    const value = policy.value
    try {
      await stepUp.execute('ChangePolicy', null, async () => {
        const result = await adminPlatformMfaChangePolicy({ body: { policy: value } })
        if (result.error || !result.data) { error.value = parseApiError(result.error).displayMessage; return }
        toast.success(message('mfa.savedSettings')); await load()
      })
    } catch (requestError) { error.value = parseApiError(requestError).displayMessage }
  }
  async function saveTrust() {
    const id = providerId.value; if (!id) return
    const body = { enabled: trust.enabled, maxAgeSeconds: trust.maxAgeSeconds,
      acrValues: trust.acr.split('\n').map(value => value.trim()).filter(Boolean),
      amrCombinations: trust.amr.split('\n').map(value => value.trim()).filter(Boolean).map(value => value.split(/\s+/)) }
    try {
      await stepUp.execute('ChangeOidcTrust', id, async () => {
        const result = await adminPlatformMfaChangeOidcTrust({ path: { providerId: id }, body })
        if (result.error || !result.data) { error.value = parseApiError(result.error).displayMessage; return }
        toast.success(message('mfa.savedSettings')); await load()
      })
    } catch (requestError) { error.value = parseApiError(requestError).displayMessage }
  }
  watch(providerId, syncProvider)
  onMounted(() => { if (available.value) void load(); else loading.value = false })
  return { available, loading, error, policy, policies, providers, providerId, trust, stepUp, StepUpView: markRaw(StepUpView), savePolicy, saveTrust }
}
export type MfaPlatformSettingsViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useMfaPlatformSettings>>
