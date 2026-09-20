import { proxyRefs } from 'vue'

import { ExternalLink, RefreshCw } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { adminCreateRuntimeForceTermination, adminPlatformListActiveRuntimes, adminTerminateRuntime } from '../../../../api'
import type { NoCtfapiEndpointsAdministrationPlatformPlatformRuntimeResponse, NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse } from '../../../../api'
import { useOffsetPagination } from '../../../../composables/useOffsetPagination'
import { adminRuntimeTeamLabel } from '../../../../utils/admin-runtime'
import { emptyPlatformRuntimeFilters, platformRuntimeQuery } from '../../../../utils/platform-runtime-filters'
import { formatCapacityAmount, runnerFailureLabel } from '../../../shared/runner-capacity'

type PlatformRuntime = NoCtfapiEndpointsAdministrationPlatformPlatformRuntimeResponse

/** Owns state, effects and commands for AdminPlatformRuntimesPage. */
export function useAdminPlatformRuntimesPage() {
  const filters = reactive(emptyPlatformRuntimeFilters())

  const appliedQuery = ref(platformRuntimeQuery(filters))

  const detailTarget = ref<PlatformRuntime | null>(null)

  const pagination = useOffsetPagination<PlatformRuntime>(async ({ offset, limit, desc }) => {
    const { data, error: requestError } = await adminPlatformListActiveRuntimes({
      query: { ...appliedQuery.value, offset, limit, desc },
    })
    if (requestError || !data) throw parseApiError(requestError)
    return { items: data.items ?? [], total: data.total ?? 0 }
  }, { initialPageSize: 50, initialDesc: true })

  const { items, loading, error, initialized } = pagination

  async function refresh(): Promise<void> {
    await pagination.loadPage(pagination.page.value)
  }

  const detail = computed(() => items.value.find(item => item.runtime?.id === detailTarget.value?.runtime?.id) ?? detailTarget.value)

  async function applyFilters(): Promise<void> {
    appliedQuery.value = platformRuntimeQuery(filters)
    pagination.reset()
    await pagination.loadPage(1)
  }

  function clearFilters(): void {
    Object.assign(filters, emptyPlatformRuntimeFilters())
    void applyFilters()
  }

  const terminateTarget = ref<PlatformRuntime | null>(null)

  const terminatePending = ref(false)

  const terminationError = ref<string | null>(null)

  const forceTerminateTarget = ref<PlatformRuntime | null>(null)

  const forceTerminateReason = ref('')

  const forceTerminateConfirmed = ref(false)

  const forceTerminatePending = ref(false)

  const forceTerminationError = ref<string | null>(null)

  let refreshTimer: ReturnType<typeof setInterval> | undefined

  function runtimeOf(item: PlatformRuntime | null): NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse | null {
    return item?.runtime ?? null
  }

  function teamLabel(item: PlatformRuntime): string {
    const runtime = runtimeOf(item)
    return runtime ? adminRuntimeTeamLabel(runtime, translate) : '-'
  }

  function stateBadgeVariant(state?: string): 'default' | 'secondary' | 'outline' | 'destructive' {
    if (state === 'Running') return 'default'
    if (state === 'Stopping') return 'destructive'
    if (state === 'Queued') return 'outline'
    return 'secondary'
  }

  function canTerminate(item: PlatformRuntime): boolean {
    const state = runtimeOf(item)?.state
    return state === 'Queued' || state === 'Provisioning' || state === 'Running'
  }

  function openTermination(item: PlatformRuntime): void {
    terminationError.value = null
    terminateTarget.value = item
  }

  function openForceTermination(item: PlatformRuntime): void {
    forceTerminationError.value = null
    forceTerminateTarget.value = item
    forceTerminateReason.value = ''
    forceTerminateConfirmed.value = false
  }

  async function submitTermination(): Promise<void> {
    const runtime = runtimeOf(terminateTarget.value)
    if (!runtime?.id || terminatePending.value) return
    terminatePending.value = true
    terminationError.value = null
    try {
      const { error: requestError } = await adminTerminateRuntime({
        path: { runtimeInstanceId: runtime.id },
      })
      if (requestError) throw requestError
      toast.success(translate("ui.instanceTerminationOperationHasBeenAccepted"))
      terminateTarget.value = null
      await refresh()
    }
    catch (requestError) {
      terminationError.value = parseApiError(requestError).message
      toast.error(terminationError.value)
    }
    finally {
      terminatePending.value = false
    }
  }

  async function submitForceTermination(): Promise<void> {
    const runtime = runtimeOf(forceTerminateTarget.value)
    const reason = forceTerminateReason.value.trim()
    if (!runtime?.id
      || reason.length < 8
      || !forceTerminateConfirmed.value
      || forceTerminatePending.value)
      return

    forceTerminatePending.value = true
    forceTerminationError.value = null
    try {
      const { error: requestError } = await adminCreateRuntimeForceTermination({
        path: { runtimeInstanceId: runtime.id },
        body: { reason },
      })
      if (requestError) throw requestError
      toast.success(translate("ui.forcedFinalizationHasBeenHandedOverToRunnerForCleanup"))
      forceTerminateTarget.value = null
      await refresh()
    }
    catch (requestError) {
      forceTerminationError.value = parseApiError(requestError).message
      toast.error(forceTerminationError.value)
    }
    finally {
      forceTerminatePending.value = false
    }
  }

  onMounted(() => {
    void pagination.loadPage(1)
    refreshTimer = setInterval(() => void refresh(), 10_000)
  })

  onBeforeUnmount(() => {
    if (refreshTimer) clearInterval(refreshTimer)
  })

  const viewBindings = {
      formatCapacityAmount, runnerFailureLabel, ExternalLink,
      RefreshCw,
      filters,
      detailTarget,
      items,
      loading,
      error,
      initialized,
      refresh,
      page: pagination.page,
      pageCount: pagination.pageCount,
      total: pagination.total,
      pageLimit: pagination.limit,
      loadPage: pagination.loadPage,
      setPageSize: pagination.setPageSize,
      detail,
      applyFilters,
      clearFilters,
      terminateTarget,
      terminatePending,
      terminationError,
      forceTerminateTarget,
      forceTerminateReason,
      forceTerminateConfirmed,
      forceTerminatePending,
      forceTerminationError,
      teamLabel,
      stateBadgeVariant,
      canTerminate,
      openTermination,
      openForceTermination,
      submitTermination,
      submitForceTermination
    }
  const viewState = proxyRefs(viewBindings)

  function onClickDetailTarget(value: typeof viewState.detailTarget) {
    viewState.detailTarget = value
  }

  function onUpdateOpenDetailTarget(open: boolean) {
     if (!open) viewState.detailTarget = null
  }

  function onUpdateOpenTerminateTarget(open: boolean) {
     if (!open && !viewState.terminatePending) viewState.terminateTarget = null
  }

  function onUpdateOpenForceTerminateTarget(open: boolean) {
     if (!open && !viewState.forceTerminatePending) viewState.forceTerminateTarget = null
  }

  return { ...viewBindings, onClickDetailTarget, onUpdateOpenDetailTarget, onUpdateOpenTerminateTarget, onUpdateOpenForceTerminateTarget }
}

export type AdminPlatformRuntimesPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminPlatformRuntimesPage>>>
