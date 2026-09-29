import { proxyRefs } from 'vue'
import { markRaw } from 'vue'

import { toast } from 'vue-sonner'
import { adminCreateRuntimeForceTermination, adminCreateSharedRuntime, adminCreateTeamRuntime, adminExtendTeamRuntime, adminGetRuntime, adminListCompetitionChallenges, adminListRuntimes, adminListTeams, adminTerminateRuntime } from '../../../../../api'
import type { NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse, NoCtfapiEndpointsRuntimeRuntimeKindProtocol, NoCtfapiEndpointsRuntimeRuntimeStateProtocol } from '../../../../../api'
import { useOffsetPagination } from '../../../../../composables/useOffsetPagination'
import { useCompetitionAdmin } from '../../../../../lib/admin-competition'
import { createTrailingRefresh } from '../../../../../lib/latest-page-refresh'
import { adminRuntimeTeamLabel } from '../../../../../utils/admin-runtime'
import { createRuntimeOperationCoordinator, type RuntimeOperationKind, type RuntimeOperationToken } from '../../../../../lib/runtime-operation-coordinator'
import { RUNTIME_STOP_POLL_DELAYS_MS, RUNTIME_STOP_POLL_MAX_INTERVAL_MS, RUNTIME_STOP_POLL_TIMEOUT_MS } from '../../../../../lib/runtime-stop-polling'
import { isRuntimeExtensionTooEarly, isRuntimeExtensionWindowOpen } from '../../../../../lib/runtime-extension'
import RuntimeAccessUrlComponent from '../../../../challenges/RuntimeAccessUrl.vue'

/** Owns state, effects and commands for AdminCompetitionsByIdRuntimesPage. */
export function useAdminCompetitionsByIdRuntimesPage() {
  const { competitionId, canWrite } = useCompetitionAdmin()

  const { isAdministrator } = useAuth()

  const challengeOptions = ref<{ id: string; title: string }[]>([])

  const teamOptions = ref<{ id: string; name: string }[]>([])

  const runtimeTeamLabel = (
    runtime: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse | null | undefined,
  ) => runtime
    ? adminRuntimeTeamLabel(
        runtime,
        translate,
        id => teamOptions.value.find(team => team.id === id)?.name,
      )
    : '-'

  const isPlayerManagedRuntime = (
    runtime: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse,
  ) => runtime.purpose !== 'AwdpTarget'

  const challengeTitle = (id?: string | null) => challengeOptions.value.find(c => c.id === id)?.title ?? id ?? '-'

  async function loadRefs() {
    const [challenges, teams] = await Promise.all([
      adminListCompetitionChallenges({ path: { competitionId }, query: { includeDeleted: false } }),
      adminListTeams({ path: { competitionId }, query: { keyword: null, offset: 0, limit: 200, desc: false } }),
    ])
    challengeOptions.value = (challenges.data?.items ?? []).map(c => ({ id: c.id!, title: c.title ?? '' }))
    teamOptions.value = (teams.data?.items ?? []).map(t => ({ id: t.id!, name: t.name ?? '' }))
  }

  const filterChallenge = ref('')

  const filterTeam = ref('')

  const filterState = ref('')

  const filterKind = ref('')

  const appliedFilters = ref({ challengeId: '', teamId: '', state: '', kind: '' })

  const pagination = useOffsetPagination<NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse>(async ({ offset, limit, desc }) => {
    const filters = appliedFilters.value
    const { data, error } = await adminListRuntimes({
      path: { competitionId },
      query: {
        competitionChallengeId: filters.challengeId || null,
        teamId: filters.teamId || null,
        state: filters.state === '' ? null : filters.state as NoCtfapiEndpointsRuntimeRuntimeStateProtocol,
        runtimeKind: filters.kind === '' ? null : filters.kind as NoCtfapiEndpointsRuntimeRuntimeKindProtocol,
        offset,
        limit,
        desc,
      },
    })
    if (error || !data) throw parseApiError(error)
    return { items: data.items ?? [], total: data.total ?? 0 }
  }, { initialPageSize: 10, initialDesc: true })

  const { items, loading, error: listError, initialized } = pagination
  const refreshRuntimeList = createTrailingRefresh(() => pagination.loadPage())

  async function applyFilters() {
    appliedFilters.value = {
      challengeId: filterChallenge.value,
      teamId: filterTeam.value,
      state: filterState.value,
      kind: filterKind.value,
    }
    pagination.reset()
    await pagination.loadPage(1)
  }

  function refreshList() {
    return refreshRuntimeList()
  }

  const detail = ref<NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse | null>(null)

  const detailOpen = ref(false)

  const detailLoading = ref(false)

  async function openDetail(id?: string) {
    if (!id) return
    detailOpen.value = true
    detailLoading.value = true
    const { data, error } = await adminGetRuntime({ path: { runtimeInstanceId: id } })
    if (error) toast.error(parseApiError(error).message)
    else detail.value = data ?? null
    detailLoading.value = false
  }

  const runtimeOperations = createRuntimeOperationCoordinator({
    maxAttempts: 50,
    maxIntervalMs: RUNTIME_STOP_POLL_MAX_INTERVAL_MS,
    delaysMs: RUNTIME_STOP_POLL_DELAYS_MS,
    timeoutMs: RUNTIME_STOP_POLL_TIMEOUT_MS,
  })

  const opMessage = ref<string | null>(null)

  function runtimeOperationKey(rt: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse | null): string | null {
    return rt?.id ?? null
  }

  function isRuntimePending(rt: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse | null): boolean {
    const key = runtimeOperationKey(rt)
    return key !== null && runtimeOperations.pending.has(key)
  }

  function isRuntimeOperationPending(
    rt: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse | null,
    operation: RuntimeOperationKind,
  ): boolean {
    const key = runtimeOperationKey(rt)
    return key !== null && runtimeOperations.pending.get(key) === operation
  }

  function beginRuntimeOperation(
    rt: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse,
    operation: RuntimeOperationKind,
  ): RuntimeOperationToken | null {
    const key = runtimeOperationKey(rt)
    if (!key) return null
    opMessage.value = null
    return runtimeOperations.begin(key, operation)
  }

  function markRuntimeStopping(runtimeInstanceId: string): void {
    items.value = items.value.map(item => item.id === runtimeInstanceId
      ? { ...item, state: 'Stopping' }
      : item)
    if (detail.value?.id === runtimeInstanceId)
      detail.value = { ...detail.value, state: 'Stopping' }
  }

  function refreshRuntimeInBackground(
    token: RuntimeOperationToken,
    runtimeInstanceId: string,
    done: (state?: string | null) => boolean,
  ): void {
    void runtimeOperations.poll(token, async (signal) => {
      const { data, error } = await adminGetRuntime({
        path: { runtimeInstanceId },
        signal,
      })
      if (error || !data) return false

      if (detailOpen.value && detail.value?.id === data.id)
        detail.value = data
      await refreshList()
      return done(data.state)
    }).then((result) => {
      if (result === 'exhausted')
        opMessage.value = translate("ui.theRequestWasAcceptedStatusUpdatesAreTakingLongerThan")
    }).finally(() => {
      runtimeOperations.finish(token)
    })
  }

  async function runRuntimeOp(
    rt: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse,
    op: 'start' | 'reset',
  ) {
    if (!rt.id || !rt.competitionChallengeId) return
    const token = beginRuntimeOperation(rt, op)
    if (!token) return
    try {
      const ccPath = { competitionId, competitionChallengeId: rt.competitionChallengeId }
      const teamPath = { ...ccPath, teamId: rt.teamId ?? '' }
      const body = { replacesRuntimeId: op === 'reset' ? rt.id : null }
      const { data, error } = rt.teamId
        ? await adminCreateTeamRuntime({ path: teamPath, body })
        : await adminCreateSharedRuntime({ path: ccPath, body })
      if (!runtimeOperations.isActive(token)) return
      if (error) throw error
      const label = op === 'start' ? translate("ui.start") : translate("ui.reset")
      toast.success(translate("ui.requestAccepted", { action: label }))
      void refreshList()
      refreshRuntimeInBackground(token, data?.runtimeInstanceId ?? rt.id, state =>
        op === 'reset' ? state === 'Stopped' : state === 'Running' || state === 'Failed')
    }
    catch (e) {
      const shouldNotify = runtimeOperations.isActive(token)
      runtimeOperations.finish(token)
      if (shouldNotify) toast.error(parseApiError(e).message)
    }
  }

  const terminateDialog = ref<NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse | null>(null)

  const terminatePending = computed(() => isRuntimeOperationPending(terminateDialog.value, 'terminate'))

  function canTerminate(rt: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse) {
    return rt.state === 'Queued'
      || rt.state === 'Provisioning'
      || rt.state === 'Running'
      || rt.state === 'Failed'
  }

  async function submitTermination() {
    const rt = terminateDialog.value
    if (!rt?.id) return
    const token = beginRuntimeOperation(rt, 'terminate')
    if (!token) return
    try {
      const { data, error } = await adminTerminateRuntime({
        path: { runtimeInstanceId: rt.id },
      })
      if (!runtimeOperations.isActive(token)) return
      if (error) throw error
      markRuntimeStopping(rt.id)
      toast.success(translate("ui.instanceTerminationOperationHasBeenAccepted"))
      terminateDialog.value = null
      void refreshList()
      refreshRuntimeInBackground(token, data?.runtimeInstanceId ?? rt.id, state => state === 'Stopped' || state === 'Failed')
    }
    catch (e) {
      const shouldNotify = runtimeOperations.isActive(token)
      runtimeOperations.finish(token)
      if (shouldNotify) toast.error(parseApiError(e).message)
    }
  }

  const forceTerminateDialog = ref<NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse | null>(null)

  const forceTerminateReason = ref('')

  const forceTerminateConfirmed = ref(false)

  const forceTerminatePending = computed(() => isRuntimeOperationPending(forceTerminateDialog.value, 'force-terminate'))

  function openForceTermination(rt: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse) {
    forceTerminateReason.value = ''
    forceTerminateConfirmed.value = false
    forceTerminateDialog.value = rt
  }

  async function submitForceTermination() {
    const rt = forceTerminateDialog.value
    const reason = forceTerminateReason.value.trim()
    if (!rt?.id || reason.length < 8 || !forceTerminateConfirmed.value) return
    const token = beginRuntimeOperation(rt, 'force-terminate')
    if (!token) return
    try {
      const { data, error } = await adminCreateRuntimeForceTermination({
        path: { runtimeInstanceId: rt.id },
        body: {
          reason,
        },
      })
      if (!runtimeOperations.isActive(token)) return
      if (error) throw error
      markRuntimeStopping(rt.id)
      toast.success(translate("ui.forcedFinalizationHasBeenHandedOverToRunnerForCleanup"))
      forceTerminateDialog.value = null
      void refreshList()
      refreshRuntimeInBackground(token, data?.runtimeInstanceId ?? rt.id, state => state === 'Stopped')
    }
    catch (e) {
      const shouldNotify = runtimeOperations.isActive(token)
      runtimeOperations.finish(token)
      if (shouldNotify) toast.error(parseApiError(e).message)
    }
  }

  const extendDialog = ref<NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse | null>(null)

  const extendSeconds = ref(1800)

  const now = ref(Date.now())

  let clockTimer: ReturnType<typeof setInterval> | undefined

  function canExtendRuntime(rt: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse): boolean {
    return rt.state === 'Running' && !!rt.teamId && isPlayerManagedRuntime(rt)
      && isRuntimeExtensionWindowOpen(rt.expiresAt, now.value)
  }

  function renewalHint(rt: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse): string | null {
    if (isRuntimeExtensionTooEarly(rt.expiresAt, now.value))
      return translate('ui.renewalAvailableInFinalTenMinutes')
    return !isRuntimeExtensionWindowOpen(rt.expiresAt, now.value)
      ? translate('ui.expired') : null
  }

  const extendPending = computed(() => isRuntimeOperationPending(extendDialog.value, 'extend'))

  async function submitExtend() {
    const rt = extendDialog.value
    if (!rt?.id || !rt.teamId || !rt.competitionChallengeId) return
    if (!canExtendRuntime(rt)) {
      toast.error(renewalHint(rt) ?? translate('ui.renewalFailed'))
      return
    }
    if (!Number.isInteger(extendSeconds.value) || extendSeconds.value < 60) {
      toast.error(translate('ui.renewalFailed'))
      return
    }
    const token = beginRuntimeOperation(rt, 'extend')
    if (!token) return
    try {
      const { error } = await adminExtendTeamRuntime({
        path: {
          competitionId,
          teamId: rt.teamId,
          competitionChallengeId: rt.competitionChallengeId,
          runtimeInstanceId: rt.id,
        },
        body: {
          expiresAt: new Date(
            new Date(rt.expiresAt!).getTime() + extendSeconds.value * 1000,
          ).toISOString(),
        },
      })
      if (!runtimeOperations.isActive(token)) return
      if (error) throw error
      toast.success(translate("ui.renewalOperationHasBeenAccepted"))
      extendDialog.value = null
      void refreshList()
      refreshRuntimeInBackground(token, rt.id, () => true)
    }
    catch (e) {
      const shouldNotify = runtimeOperations.isActive(token)
      runtimeOperations.finish(token)
      if (shouldNotify) toast.error(parseApiError(e).message)
    }
  }

  onMounted(() => {
    void loadRefs()
    void pagination.loadPage(1)
    clockTimer = setInterval(() => { now.value = Date.now() }, 1000)
  })

  onBeforeUnmount(() => {
    if (clockTimer) clearInterval(clockTimer)
    runtimeOperations.cancelAll()
  })

  const RuntimeAccessUrl = markRaw(RuntimeAccessUrlComponent)

  const viewBindings = {
      canWrite,
      isAdministrator,
      challengeOptions,
      teamOptions,
      runtimeTeamLabel,
      isPlayerManagedRuntime,
      challengeTitle,
      filterChallenge,
      filterTeam,
      filterState,
      filterKind,
      items,
      loading,
      listError,
      initialized,
      page: pagination.page,
      pageCount: pagination.pageCount,
      total: pagination.total,
      pageLimit: pagination.limit,
      loadPage: pagination.loadPage,
      setPageSize: pagination.setPageSize,
      applyFilters,
      detail,
      detailOpen,
      detailLoading,
      openDetail,
      opMessage,
      isRuntimePending,
      isRuntimeOperationPending,
      runRuntimeOp,
      terminateDialog,
      terminatePending,
      canTerminate,
      submitTermination,
      forceTerminateDialog,
      forceTerminateReason,
      forceTerminateConfirmed,
      forceTerminatePending,
      openForceTermination,
      submitForceTermination,
      extendDialog,
      extendSeconds,
      extendPending,
      canExtendRuntime,
      renewalHint,
      submitExtend,
      RuntimeAccessUrl
    }
  const viewState = proxyRefs(viewBindings)

  function onClickFilterChallenge() {
    viewState.filterChallenge = ''; viewState.filterTeam = ''; viewState.filterState = ''; viewState.filterKind = ''; viewState.applyFilters()
  }

  function onClickTerminateDialog(value: typeof viewState.terminateDialog) {
    viewState.terminateDialog = value
  }

  function onClickExtendDialog(rt: typeof viewState.extendDialog) {
    if (rt && !canExtendRuntime(rt)) return
    viewState.extendDialog = rt; viewState.extendSeconds = 1800
  }

  function onUpdateOpenExtendDialog(v: boolean) {
     if (!v) viewState.extendDialog = null
  }

  function onClickExtendDialog2(value: typeof viewState.extendDialog) {
    viewState.extendDialog = value
  }

  function onUpdateOpenTerminateDialog(open: boolean) {
     if (!open && !viewState.terminatePending) viewState.terminateDialog = null
  }

  function onUpdateOpenForceTerminateDialog(open: boolean) {
     if (!open && !viewState.forceTerminatePending) viewState.forceTerminateDialog = null
  }

  return { ...viewBindings, onClickFilterChallenge, onClickTerminateDialog, onClickExtendDialog, onUpdateOpenExtendDialog, onClickExtendDialog2, onUpdateOpenTerminateDialog, onUpdateOpenForceTerminateDialog }
}

export type AdminCompetitionsByIdRuntimesPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminCompetitionsByIdRuntimesPage>>>
