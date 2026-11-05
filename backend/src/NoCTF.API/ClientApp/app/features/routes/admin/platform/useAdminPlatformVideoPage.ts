import { computed, onMounted, reactive, ref } from 'vue'
import { adminPlatformGetConfiguration, adminPlatformPatchConfiguration } from '../../../../api'
import type { NoCtfApplicationLiveSoloMediaLiveSoloVideoPolicy, NoCtfapiEndpointsAdministrationPlatformLiveSoloVideoConfigurationRulesResponse } from '../../../../api'
import { platformVideoDraft, platformVideoDirty, platformVideoPatch } from '../../../admin/platform-video'
import type { PlatformVideoDraft } from '../../../admin/platform-video'
import { message } from '../../../../utils/i18n'
import type { UiMessage } from '../../../../utils/i18n'
import { toast } from '../../../../utils/message-toast'

export function useAdminPlatformVideoPage() {
  const platform = usePlatform()
  const loading = ref(true), saving = ref(false)
  const error = ref<UiMessage | null>(null)
  const policy = ref<NoCtfApplicationLiveSoloMediaLiveSoloVideoPolicy | null>(null)
  const rules = ref<NoCtfapiEndpointsAdministrationPlatformLiveSoloVideoConfigurationRulesResponse | null>(null)
  const draft = reactive<PlatformVideoDraft>({ width: null, height: null, fps: null, kilobitsPerSecond: null })
  const dirty = computed(() => platformVideoDirty(draft, policy.value))
  const valid = computed(() => platformVideoPatch(draft, rules.value) !== null)
  function sync(configuration: { liveSoloVideo?: NoCtfApplicationLiveSoloMediaLiveSoloVideoPolicy | null; liveSoloVideoRules?: NoCtfapiEndpointsAdministrationPlatformLiveSoloVideoConfigurationRulesResponse }) {
    if (!configuration.liveSoloVideo || !configuration.liveSoloVideoRules) throw new Error('The video configuration response is incomplete.')
    policy.value = configuration.liveSoloVideo; rules.value = configuration.liveSoloVideoRules
    Object.assign(draft, platformVideoDraft(policy.value))
  }
  async function load() {
    if (saving.value) return
    loading.value = true; error.value = null
    try {
      const result = await adminPlatformGetConfiguration()
      if (result.error || !result.data) throw result.error ?? new Error('The platform configuration was not returned.')
      sync(result.data)
    } catch (cause) { error.value = parseApiError(cause, message('liveSolo.videoConfiguration.loadFailed')).displayMessage }
    finally { loading.value = false }
  }
  async function save() {
    const patch = platformVideoPatch(draft, rules.value)
    if (saving.value || loading.value || !dirty.value || !patch) return
    saving.value = true; error.value = null
    try {
      const result = await adminPlatformPatchConfiguration({ body: { liveSoloVideo: patch } })
      if (result.error || !result.data) throw result.error ?? new Error('The updated platform configuration was not returned.')
      sync(result.data)
      await platform.refresh()
      toast.success(message('liveSolo.videoConfiguration.saved'))
    } catch (cause) { error.value = parseApiError(cause, message('liveSolo.videoConfiguration.saveFailed')).displayMessage }
    finally { saving.value = false }
  }
  onMounted(load)
  return { loading, saving, error, rules, draft, dirty, valid, load, save }
}

export type AdminPlatformVideoPageViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useAdminPlatformVideoPage>>
