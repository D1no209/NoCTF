import { message as describeMessage } from '../../../../utils/i18n'
import type { UiMessage } from '../../../../utils/i18n'
import { Beaker, RefreshCw } from '@lucide/vue'
import { toast } from '../../../../utils/message-toast'
import { adminPlatformGetConfiguration, adminPlatformPatchConfiguration } from '../../../../api'
import type { NoCtfapiEndpointsAdministrationPlatformLiveSoloVideoConfigurationRulesResponse as VideoRules } from '../../../../api'

/** Owns state, effects and commands for the platform experiment controls. */
export function useAdminPlatformExperimentsPage() {
  const { refresh: refreshPlatform } = usePlatform()
  const loading = ref(true)
  const saving = ref(false)
  const loadError = ref<UiMessage | null>(null)
  const ctfPatchVerificationEnabled = ref(false)
  const savedValue = ref(false)
  const dirty = computed(() => ctfPatchVerificationEnabled.value !== savedValue.value)
  const videoWidth=ref<number|null>(null),videoHeight=ref<number|null>(null),videoFps=ref<number|null>(null),videoKbps=ref<number|null>(null)
  const videoRules=ref<VideoRules|null>(null),videoSaved=ref('')
  const videoDirty=computed(()=>JSON.stringify([videoWidth.value,videoHeight.value,videoFps.value,videoKbps.value])!==videoSaved.value)
  const videoValid=computed(()=>{
    const rules=videoRules.value,values=[videoWidth.value,videoHeight.value,videoFps.value,videoKbps.value]
    return !!rules&&values.every(value=>value!==null&&Number.isSafeInteger(value))
      &&videoWidth.value!%2===0&&videoHeight.value!%2===0&&videoWidth.value!>=(rules.minimumWidth??Infinity)&&videoWidth.value!<=(rules.maximumWidth??0)
      &&videoHeight.value!>=(rules.minimumHeight??Infinity)&&videoHeight.value!<=(rules.maximumHeight??0)
      &&videoFps.value!>=(rules.minimumFramesPerSecond??Infinity)&&videoFps.value!<=(rules.maximumFramesPerSecond??0)
      &&videoKbps.value!*1000>=(rules.minimumBitrateBitsPerSecond??Infinity)&&videoKbps.value!*1000<=(rules.maximumBitrateBitsPerSecond??0)
  })
  function setVideo(data:import('../../../../api').NoCtfapiEndpointsAdministrationPlatformAdminPlatformConfigurationResponse){
    const video=data.liveSoloVideo;if(!video)return
    videoWidth.value=video.maximumWidth??null;videoHeight.value=video.maximumHeight??null;videoFps.value=video.maximumFramesPerSecond??null
    videoKbps.value=video.maximumBitrateBitsPerSecond==null?null:video.maximumBitrateBitsPerSecond/1000;videoRules.value=data.liveSoloVideoRules??null
    videoSaved.value=JSON.stringify([videoWidth.value,videoHeight.value,videoFps.value,videoKbps.value])
  }

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
    setVideo(data)
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
  async function saveVideo(){
    if(saving.value||!videoValid.value||!videoDirty.value)return
    saving.value=true
    try {
      const result=await adminPlatformPatchConfiguration({body:{liveSoloVideo:{maximumWidth:videoWidth.value!,maximumHeight:videoHeight.value!,maximumFramesPerSecond:videoFps.value!,maximumBitrateBitsPerSecond:videoKbps.value!*1000}}})
      if(result.error||!result.data?.liveSoloVideo)throw parseApiError(result.error,describeMessage('liveSolo.videoConfiguration.saveFailed'))
      setVideo(result.data);toast.success(describeMessage('liveSolo.videoConfiguration.saved'))
    }catch(cause){toast.error(parseApiError(cause,describeMessage('liveSolo.videoConfiguration.saveFailed')).displayMessage)}
    finally{saving.value=false}
  }

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
    videoWidth,videoHeight,videoFps,videoKbps,videoRules,videoDirty,videoValid,saveVideo,
  }
}

export type AdminPlatformExperimentsPageViewState = import('vue').ShallowUnwrapRef<
  Awaited<ReturnType<typeof useAdminPlatformExperimentsPage>>
>
