

import { Globe, RefreshCw } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { adminPlatformGetConfiguration, adminPlatformGetPublicGatewayStatus, adminPlatformPatchConfiguration } from '../../../../api'
import type { NoCtfapiEndpointsAdministrationPlatformPublicGatewayConfigurationResponse as Configuration, NoCtfapiEndpointsAdministrationPlatformPublicGatewayStatusResponse as GatewayStatus } from '../../../../api'
import { gatewayHost, gatewayOrigin, publicGatewayFailure, publicGatewayState } from '../../../../utils/public-gateway'

/** Owns state, effects and commands for AdminPlatformPublicGatewayPage. */
export function useAdminPlatformPublicGatewayPage() {
  const configuration = ref<Configuration | null>(null)

  const status = ref<GatewayStatus | null>(null)

  const loading = ref(true)

  const saving = ref(false)

  const error = ref<string | null>(null)

  const statusError = ref<string | null>(null)

  const fieldErrors = ref<Record<string, string>>({})

  const saved = ref('')

  const form = reactive({ enabled: false, connectorId: '', publicOrigin: '', directOrigins: '', publicRuntimeHost: '', directRuntimeHostOverride: '', maxPublishedPorts: 8 })

  const dirty = computed(() => JSON.stringify(form) !== saved.value)

  const capability = computed(() => configuration.value?.capability)

  let generation = 0

  let statusTimer: ReturnType<typeof setInterval> | undefined

  let readingStatus = false

  async function readStatus(): Promise<boolean> {
    if (readingStatus) return false
    readingStatus = true
    try {
      const result = await adminPlatformGetPublicGatewayStatus()
      if (result.error || !result.data) throw result.error
      status.value = result.data
      statusError.value = null
      return result.data.applied === true
    }
    catch (failure) {
      statusError.value = parseApiError(failure).message
      return false
    }
    finally { readingStatus = false }
  }

  const application = usePolling(readStatus, { interval: 1000, maxInterval: 3000, timeout: 30_000 })

  async function load() {
    const current = ++generation
    loading.value = true
    error.value = null
    try {
      const result = await adminPlatformGetConfiguration()
      if (current !== generation) return
      if (result.error || !result.data) throw result.error
      configuration.value = result.data.publicGateway ?? null
      const policy = result.data.publicGateway?.policy
      Object.assign(form, { enabled: policy?.enabled ?? false, connectorId: policy?.connectorId || result.data.publicGateway?.capability?.connectorId || '',
        publicOrigin: policy?.publicOrigin || result.data.publicGateway?.capability?.approvedOrigins?.[0] || '',
        directOrigins: (policy?.directOrigins ?? []).join('\n'), publicRuntimeHost: policy?.publicRuntimeHost ?? '',
        directRuntimeHostOverride: policy?.directRuntimeHostOverride ?? '', maxPublishedPorts: policy?.maxPublishedPorts || result.data.publicGateway?.capability?.maximumPorts || 8 })
      saved.value = JSON.stringify(form)
      await readStatus()
    }
    catch (failure) { error.value = parseApiError(failure).message }
    finally { if (current === generation) loading.value = false }
  }

  async function save() {
    if (saving.value) return
    const directOrigins = form.directOrigins.split(/\r?\n/).map(value => value.trim()).filter(Boolean)
    const issues: Record<string, string> = {}
    const origin = gatewayOrigin(form.publicOrigin, true)
    if (!origin || !capability.value?.approvedOrigins?.some(value => gatewayOrigin(value, true) === origin))
      issues.publicOrigin = translate("ui.selectAnHttpsPublicOriginApprovedByThisDeployment")
    if (!directOrigins.length || directOrigins.length > 16 || directOrigins.some(value => !gatewayOrigin(value))
      || new Set(directOrigins.map(value => gatewayOrigin(value))).size !== directOrigins.length
      || directOrigins.some(value => gatewayOrigin(value) === origin))
      issues.directOrigins = translate("ui.enter1To16UniqueDirectOriginsOnePerLine")
    if (!gatewayHost(form.publicRuntimeHost)) issues.publicRuntimeHost = translate("ui.enterAHostnameOrIpv4AddressWithoutASchemePath")
    if (form.directRuntimeHostOverride && !gatewayHost(form.directRuntimeHostOverride))
      issues.directRuntimeHostOverride = translate("ui.enterAHostnameOrIpv4AddressWithoutASchemePath")
    if (!Number.isInteger(form.maxPublishedPorts) || form.maxPublishedPorts < 1 || form.maxPublishedPorts > (capability.value?.maximumPorts ?? 0))
      issues.maxPublishedPorts = translate("ui.thePublicPortCountMustFitTheDeploymentQuota")
    fieldErrors.value = issues
    if (Object.keys(issues).length) return
    saving.value = true
    error.value = null
    try {
      const result = await adminPlatformPatchConfiguration({
        body: {
          publicGateway: {
            ...form,
            directOrigins,
            directRuntimeHostOverride: form.directRuntimeHostOverride || null,
          },
        },
      })
      if (result.error || !result.data) throw result.error
      configuration.value = result.data.publicGateway ?? configuration.value
      saved.value = JSON.stringify(form)
      toast.success(translate("ui.gatewaySettingsSavedAndBeingApplied"))
      application.start()
    }
    catch (failure) { error.value = parseApiError(failure).message }
    finally { saving.value = false }
  }

  onMounted(() => {
    void load()
    statusTimer = setInterval(() => {
      if (!application.polling.value && (configuration.value?.policy?.enabled || status.value?.runtimes?.length)) void readStatus()
    }, 5000)
  })

  onBeforeUnmount(() => { generation++; if (statusTimer) clearInterval(statusTimer) })

  return {
      Globe,
      RefreshCw,
      publicGatewayFailure,
      publicGatewayState,
      configuration,
      status,
      loading,
      saving,
      error,
      statusError,
      fieldErrors,
      form,
      dirty,
      capability,
      application,
      load,
      save
    }
}

export type AdminPlatformPublicGatewayPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminPlatformPublicGatewayPage>>>
