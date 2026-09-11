
import type { ComponentPublicInstance } from 'vue'
import { Upload } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { adminPlatformGetConfiguration, adminPlatformGetInformation, adminPlatformPatchConfiguration, adminPlatformUploadLogo } from '../../../../api'
import type { NoCtfapiEndpointsAdministrationPlatformPlatformBrandingResponse, NoCtfapiEndpointsAdministrationPlatformPlatformInformationResponse } from '../../../../api'

/** Owns state, effects and commands for AdminPlatformIndexPage. */
export function useAdminPlatformIndexPage() {
  const { configuration: globalConfiguration } = usePlatform()

  const information = ref<NoCtfapiEndpointsAdministrationPlatformPlatformInformationResponse | null>(null)

  const configuration = ref<NoCtfapiEndpointsAdministrationPlatformPlatformBrandingResponse | null>(null)

  const loading = ref(true)

  const loadError = ref<string | null>(null)

  const name = ref('')

  const description = ref('')

  const saving = ref(false)

  const logoInput = ref<HTMLInputElement | null>(null)

  const logoUploading = ref(false)

  const logoSrc = computed(() => configuration.value?.logoUrl ?? null)

  async function load(): Promise<void> {
    loading.value = true
    loadError.value = null
    const [infoResult, configResult] = await Promise.all([
      adminPlatformGetInformation(),
      adminPlatformGetConfiguration(),
    ])
    loading.value = false
    if (infoResult.error || configResult.error) {
      loadError.value = parseApiError(infoResult.error ?? configResult.error).message
      return
    }
    information.value = infoResult.data ?? null
    configuration.value = configResult.data?.branding ?? null
    name.value = configResult.data?.branding?.name ?? ''
    description.value = configResult.data?.branding?.description ?? ''
  }

  async function save(): Promise<void> {
    if (!configuration.value || !name.value.trim()) return
    saving.value = true
    const { data, error } = await adminPlatformPatchConfiguration({
      body: {
        branding: {
          name: name.value.trim(),
          description: description.value.trim() || null,
        },
      },
    })
    saving.value = false
    if (error) {
      toast.error(parseApiError(error).message)
      return
    }
    configuration.value = data?.branding ?? configuration.value
    if (data?.branding && globalConfiguration.value) {
      globalConfiguration.value = {
        ...globalConfiguration.value,
        name: data.branding.name,
        description: data.branding.description,
        logoUrl: data.branding.logoUrl,
      }
    }
    toast.success(translate("ui.platformConfigurationSaved"))
  }

  async function uploadLogo(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement
    const file = input.files?.[0]
    input.value = ''
    if (!file || !configuration.value) return
    logoUploading.value = true
    const { data, error } = await adminPlatformUploadLogo({
      body: { file },
    })
    logoUploading.value = false
    if (error) {
      toast.error(parseApiError(error).message)
      return
    }
    if (data) {
      configuration.value = data
      if (globalConfiguration.value) {
        globalConfiguration.value = { ...globalConfiguration.value, logoUrl: data.logoUrl }
      }
    }
    toast.success(translate("ui.logoHasBeenUpdated"))
  }

  onMounted(() => {
    void load()
  })

  function setLogoInputRef(element: Element | ComponentPublicInstance | null) { logoInput.value = (element instanceof Element ? element : element?.$el ?? null) as typeof logoInput.value }

  return {
      Upload,
      information,
      loading,
      loadError,
      name,
      description,
      saving,
      logoInput,
      logoUploading,
      logoSrc,
      save,
      uploadLogo,
      setLogoInputRef
    }
}

export type AdminPlatformIndexPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminPlatformIndexPage>>>
