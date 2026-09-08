import { proxyRefs } from 'vue'

import { ExternalLink, RefreshCw } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { adminPlatformForceTerminateRuntime, adminPlatformListActiveRuntimes, adminPlatformTerminateRuntime } from '../../../../api'
import type { NoCtfapiEndpointsAdministrationPlatformPlatformRuntimeResponse, NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse } from '../../../../api'
import { createLatestPageRefresh } from '../../../../lib/latest-page-refresh'
import { adminRuntimeTeamLabel } from '../../../../utils/admin-runtime'
import { emptyPlatformRuntimeFilters, platformRuntimeQuery } from '../../../../utils/platform-runtime-filters'

type PlatformRuntime = NoCtfapiEndpointsAdministrationPlatformPlatformRuntimeResponse

/** Owns state, effects and commands for AdminPlatformRuntimesPage. */
export function useAdminPlatformRuntimesPage() {
  const filters = reactive(emptyPlatformRuntimeFilters())

  const appliedQuery = ref(platformRuntimeQuery(filters))

  const detailTarget = ref<PlatformRuntime | null>(null)

  const {
    items,
    loading,
    error,
    hasMore,
    initialized,
    loadMore: loadRuntimePage,
    reset,
  } = useCursorPagination<PlatformRuntime>(async (cursor) => {
    const { data, error: requestError } = await adminPlatformListActiveRuntimes({
      query: { ...appliedQuery.value, cursor, limit: 50 },
    })
    if (requestError || !data) throw parseApiError(requestError)
    return data
  })

  const {
    loadNextPage: loadMore,
    refreshLatest: refresh,
  } = createLatestPageRefresh({
    loadMore: loadRuntimePage,
    reset: () => reset({ preserveItems: true }),
  })

  const detail = computed(() => items.value.find(item => item.runtime?.id === detailTarget.value?.runtime?.id) ?? detailTarget.value)

  async function applyFilters(): Promise<void> {
    appliedQuery.value = platformRuntimeQuery(filters)
    reset()
    await refresh()
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
      const { error: requestError } = await adminPlatformTerminateRuntime({
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
      const { error: requestError } = await adminPlatformForceTerminateRuntime({
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
    void loadMore()
    refreshTimer = setInterval(() => void refresh(), 10_000)
  })

  onBeforeUnmount(() => {
    if (refreshTimer) clearInterval(refreshTimer)
  })

  const viewBindings = {
      ExternalLink,
      RefreshCw,
      filters,
      detailTarget,
      items,
      loading,
      error,
      hasMore,
      initialized,
      loadMore,
      refresh,
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
