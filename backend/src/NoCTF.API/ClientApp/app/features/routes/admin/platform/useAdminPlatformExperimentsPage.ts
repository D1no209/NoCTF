import { message as describeMessage } from '../../../../utils/i18n'
import type { UiMessage } from '../../../../utils/i18n'
import { Beaker, RefreshCw } from '@lucide/vue'
import { toast } from '../../../../utils/message-toast'
import { adminPlatformGetConfiguration, adminPlatformPatchConfiguration } from '../../../../api'

/** Owns state, effects and commands for the platform experiment controls. */
export function useAdminPlatformExperimentsPage() {
  const { refresh: refreshPlatform } = usePlatform()
  const loading = ref(true)
  const saving = ref(false)
  const loadError = ref<UiMessage | null>(null)
  const ctfPatchVerificationEnabled = ref(false)
  const savedValue = ref(false)
  const dirty = computed(() => ctfPatchVerificationEnabled.value !== savedValue.value)

  async function load(): Promise<void> {
    loading.value = true
    loadError.value = null
    const { data, error } = await adminPlatformGetConfiguration()
    loading.value = false
    if (error || !data?.experimentalFeatures) {
      loadError.value = parseApiError(error, describeMessage('administration.platform.error.loadPlatformConfigurationFailed')).displayMessage
      return
    }
    const enabled = data.experimentalFeatures.ctfPatchVerificationEnabled === true
    ctfPatchVerificationEnabled.value = enabled
    savedValue.value = enabled
  }

  async function save(): Promise<void> {
    if (saving.value || !dirty.value) return
    saving.value = true
    const { data, error } = await adminPlatformPatchConfiguration({
      body: {
        experimentalFeatures: {
          ctfPatchVerificationEnabled: ctfPatchVerificationEnabled.value,
        },
      },
    })
    saving.value = false
    if (error || !data?.experimentalFeatures) {
      toast.error(parseApiError(error, describeMessage('administration.error.experimentalFeatureSaveFailed')).displayMessage)
      return
    }
    const enabled = data.experimentalFeatures.ctfPatchVerificationEnabled === true
    ctfPatchVerificationEnabled.value = enabled
    savedValue.value = enabled
    await refreshPlatform()
    toast.success(describeMessage('administration.label.experimentalFeaturesSaved'))
  }

  onMounted(() => { void load() })

  return {
    Beaker,
    RefreshCw,
    loading,
    saving,
    loadError,
    ctfPatchVerificationEnabled,
    dirty,
    load,
    save,
  }
}

export type AdminPlatformExperimentsPageViewState = import('vue').ShallowUnwrapRef<
  Awaited<ReturnType<typeof useAdminPlatformExperimentsPage>>
>
