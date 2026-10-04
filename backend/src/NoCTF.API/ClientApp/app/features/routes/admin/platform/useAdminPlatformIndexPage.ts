
import { api, multipartBody } from '../../../../lib/api'
import { message as describeMessage } from '../../../../utils/i18n'
import type { UiMessage } from '../../../../utils/i18n'

import type { ComponentPublicInstance } from 'vue'
import { Upload } from '@lucide/vue'
import { toast } from '../../../../utils/message-toast'

import type { NoCTFAPIEndpointsAdministrationPlatformPlatformBrandingResponse, NoCTFAPIEndpointsAdministrationPlatformPlatformInformationResponse } from '../../../../api/models'

/** Owns state, effects and commands for AdminPlatformIndexPage. */
export function useAdminPlatformIndexPage() {
  const { configuration: globalConfiguration } = usePlatform()

  const information = ref<NoCTFAPIEndpointsAdministrationPlatformPlatformInformationResponse | null>(null)

  const configuration = ref<NoCTFAPIEndpointsAdministrationPlatformPlatformBrandingResponse | null>(null)

  const loading = ref(true)

  const loadError = ref<UiMessage | null>(null)

  const name = ref('')

  const description = ref('')

  const saving = ref(false)

  const logoInput = ref<HTMLInputElement | null>(null)

  const logoUploading = ref(false)

  const logoSrc = computed(() => configuration.value?.logoUrl ?? null)

  async function load(): Promise<void> {
    loading.value = true
    loadError.value = null
    const settledRequests = await Promise.allSettled([
      api.api.v1.admin.platform.information.get(),
      api.api.v1.admin.platform.configuration.get(),
    ]);
    const infoResult = settledRequests[0].status === 'fulfilled' ? settledRequests[0].value : undefined;
    const infoResultError = settledRequests[0].status === 'rejected' ? settledRequests[0].reason : undefined;
    const configResult = settledRequests[1].status === 'fulfilled' ? settledRequests[1].value : undefined;
    const configResultError = settledRequests[1].status === 'rejected' ? settledRequests[1].reason : undefined;

    loading.value = false
    if (infoResultError || configResultError) {
      loadError.value = parseApiError(infoResultError ?? configResultError).displayMessage
      return
    }
    information.value = infoResult ?? null
    configuration.value = configResult?.branding ?? null
    name.value = configResult?.branding?.name ?? ''
    description.value = configResult?.branding?.description ?? ''
  }

  async function save(): Promise<void> {
    if (!configuration.value || !name.value.trim()) return
    saving.value = true
    let error: unknown;
    const data = await api.api.v1.admin.platform.configuration.patch({
        branding: {
          name: name.value.trim(),
          description: description.value.trim() || null,
        },
      }).catch(cause => { error = cause; return undefined });
    saving.value = false
    if (error) {
      toast.error(parseApiError(error).displayMessage)
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
    toast.success(describeMessage("administration.label.platformConfigurationSaved"))
  }

  async function uploadLogo(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement
    const file = input.files?.[0]
    input.value = ''
    if (!file || !configuration.value) return
    logoUploading.value = true
    let error: unknown;
    const data = await api.api.v1.admin.platform.configuration.logo.put(await multipartBody({ file })).catch(cause => { error = cause; return undefined });
    logoUploading.value = false
    if (error) {
      toast.error(parseApiError(error).displayMessage)
      return
    }
    if (data) {
      configuration.value = data
      if (globalConfiguration.value) {
        globalConfiguration.value = { ...globalConfiguration.value, logoUrl: data.logoUrl }
      }
    }
    toast.success(describeMessage("administration.label.logoUpdated"))
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
