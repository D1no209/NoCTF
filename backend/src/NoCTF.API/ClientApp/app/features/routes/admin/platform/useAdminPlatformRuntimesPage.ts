
import { api, RequestPolicyOption } from '../../../../lib/api'
import { message as describeMessage } from '../../../../utils/i18n'
import type { UiMessage } from '../../../../utils/i18n'
import { markRaw, proxyRefs } from 'vue'
import { useAdminDetailRoute } from '~/features/admin/useAdminDetailRoute'
import { adminCompetitionPath, adminRuntimeTeamPath, adminRuntimeChallengePath, adminRuntimePath } from '~/features/admin/admin-navigation'
import RuntimeFlagsPanelComponent from '~/features/admin/RuntimeFlagsPanel.vue'

import { ExternalLink, RefreshCw } from '@lucide/vue'
import { toast } from '../../../../utils/message-toast'

import type { NoCTFAPIEndpointsAdministrationPlatformPlatformRuntimeResponse, NoCTFAPIEndpointsAdministrationRuntimeAdminRuntimeResponse } from '../../../../api/models'
import { useOffsetPagination } from '../../../../composables/useOffsetPagination'
import { adminRuntimeTeamLabel } from '../../../../utils/admin-runtime'
import { emptyPlatformRuntimeFilters, platformRuntimeQuery } from '../../../../utils/platform-runtime-filters'
import { formatCapacityAmount, runnerFailureLabel } from '../../../shared/runner-capacity'
import RuntimeAccessUrlComponent from '../../../challenges/RuntimeAccessUrl.vue'

type PlatformRuntime = NoCTFAPIEndpointsAdministrationPlatformPlatformRuntimeResponse

/** Owns state, effects and commands for AdminPlatformRuntimesPage. */
export function useAdminPlatformRuntimesPage() {
  const filters = reactive(emptyPlatformRuntimeFilters())

  const appliedQuery = ref(platformRuntimeQuery(filters))

  const pagination = useOffsetPagination<PlatformRuntime>(async ({ offset, limit, desc }) => {
    let requestError: unknown;
    const data = await api.api.v1.admin.platform.runtimes.get({ queryParameters: { ...appliedQuery.value, offset, limit, desc } }).catch(cause => { requestError = cause; return undefined });
    if (requestError || !data) throw parseApiError(requestError)
    return { items: data.items ?? [], total: data.total ?? 0 }
  }, { initialPageSize: 50, initialDesc: true })

  const { items, loading, error, initialized } = pagination

  async function refresh(): Promise<void> {
    await pagination.loadPage(pagination.page.value)
  }

  const selection = useAdminDetailRoute<PlatformRuntime>('runtimeId', '/admin/platform/runtimes', async (runtimeInstanceId, signal) => {
    let error: unknown;
    const data = await api.api.v1.admin.runtimes.byRuntimeInstanceId(runtimeInstanceId).get({ options: [new RequestPolicyOption({ signal: signal })] }).catch(cause => { error = cause; return undefined });
    if (error || !data) throw error ?? new Error(translate('adminNavigation.notFound'))
    const known = items.value.find(item => item.runtime?.id === runtimeInstanceId)
    if (known) return { ...known, runtime: data }
    const competition = await (data.competitionId ? api.api.v1.admin.competitions.byCompetitionId(data.competitionId).get({ options: [new RequestPolicyOption({ signal: signal })] }) : null);
    let challengeTitle = data.challengeId ?? data.competitionChallengeId ?? ''
    if (data.competitionId && data.competitionChallengeId) {
      const challenge = await api.api.v1.admin.competitions.byCompetitionId(data.competitionId).challenges.byCompetitionChallengeId(data.competitionChallengeId).get({ queryParameters: { includeDeleted: true }, options: [new RequestPolicyOption({ signal: signal })] });
      challengeTitle = challenge?.challenge?.title ?? challengeTitle
    }
    else if (data.challengeId) {
      const challenge = await api.api.v1.admin.challenges.byChallengeId(data.challengeId).get({ queryParameters: { includeDeleted: true }, options: [new RequestPolicyOption({ signal: signal })] });
      challengeTitle = challenge?.title ?? challengeTitle
    }
    return { runtime: data, scope: data.purpose === 'TemplateTest' ? 'ChallengeTest' : 'Competition',
      competitionTitle: competition?.competition?.title ?? data.competitionId, challengeTitle }
  })
  const { data: detailTarget, open: detailOpen, loading: detailLoading, error: detailError } = selection
  const detail = computed(() => {
    if (!detailTarget.value) return null
    return items.value.find(item => item.runtime?.id === detailTarget.value?.runtime?.id) ?? detailTarget.value
  })
  const RuntimeFlagsPanel = markRaw(RuntimeFlagsPanelComponent)


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

  const terminationError = ref<UiMessage | null>(null)

  const forceTerminateTarget = ref<PlatformRuntime | null>(null)

  const forceTerminateReason = ref('')

  const forceTerminateConfirmed = ref(false)

  const forceTerminatePending = ref(false)

  const forceTerminationError = ref<UiMessage | null>(null)

  let refreshTimer: ReturnType<typeof setInterval> | undefined

  function runtimeOf(item: PlatformRuntime | null): NoCTFAPIEndpointsAdministrationRuntimeAdminRuntimeResponse | null {
    return item?.runtime ?? null
  }

  function teamLabel(item: PlatformRuntime): string {
    const runtime = runtimeOf(item)
    return runtime ? adminRuntimeTeamLabel(runtime, translate) : '-'
  }

  function stateBadgeVariant(state?: string | null): 'default' | 'secondary' | 'outline' | 'destructive' {
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

      await api.api.v1.admin.runtimes.byRuntimeInstanceId(runtime.id).delete();
      toast.success(describeMessage("runtime.platformRuntimes.description.instanceTerminationAccepted"))
      terminateTarget.value = null
      await refresh()
    }
    catch (requestError) {
      terminationError.value = parseApiError(requestError).displayMessage
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
      await api.api.v1.admin.runtimes.byRuntimeInstanceId(runtime.id).forceTerminations.post({ reason });
      toast.success(describeMessage("runtime.platformRuntimes.description.forcedFinalizationHandedOver"))
      forceTerminateTarget.value = null
      await refresh()
    }
    catch (requestError) {
      forceTerminationError.value = parseApiError(requestError).displayMessage
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

  const RuntimeAccessUrl = markRaw(RuntimeAccessUrlComponent)

  const viewBindings = {
      RuntimeFlagsPanel, adminCompetitionPath, adminRuntimeTeamPath, adminRuntimeChallengePath, adminRuntimePath, detailOpen, detailLoading, detailError,
      formatCapacityAmount, runnerFailureLabel, ExternalLink, RuntimeAccessUrl,
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
    if (value?.runtime?.id) void selection.select(value.runtime.id)
  }

  function onUpdateOpenDetailTarget(open: boolean) {
     selection.close(open)
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
