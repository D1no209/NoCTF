import { dateObject } from '../../../../../utils/date-value'

import { api } from '../../../../../lib/api'
import { message as describeMessage } from '../../../../../utils/i18n'
import type { UiMessage } from '../../../../../utils/i18n'
import { adminUserPath, adminTeamPath, adminChallengePath } from '~/features/admin/admin-navigation'
import { proxyRefs } from 'vue'

import { toast } from '../../../../../utils/message-toast'

import type { NoCTFAPIEndpointsAdministrationCheatIncidentsCheatIncidentDetailResponse, NoCTFAPIEndpointsAdministrationCheatIncidentsCheatIncidentListItemResponse, NoCTFAPIEndpointsAdministrationCheatIncidentsCheatIncidentStatusProtocol } from '../../../../../api/models'
import type { CheatIncidentResolutionRequest } from '../../../../../composables/useCheatIncidentResolution'
import { useCheatIncidentResolution } from '../../../../../composables/useCheatIncidentResolution'
import { watchCompetition } from '../../../../../composables/useCompetitionHub'
import { createLatestRequestGuard } from '../../../../../lib/latest-request'
import { defaultCheatIncidentQueryRange, resolveCheatIncidentQueryRange } from '../../../../../lib/cheat-incident-query-range'
import { createLatestPageRefresh } from '../../../../../lib/latest-page-refresh'
import { useCompetitionAdmin } from '../../../../../lib/admin-competition'

type CheatIncidentStatus = NoCTFAPIEndpointsAdministrationCheatIncidentsCheatIncidentStatusProtocol

type CheatIncidentStatusFilter = 'All' | CheatIncidentStatus

/** Owns state, effects and commands for AdminCompetitionsByIdCheatsPage. */
export function useAdminCompetitionsByIdCheatsPage() {
  const { competitionId } = useCompetitionAdmin()

  const route = useRoute()

  const filterStatus = ref<CheatIncidentStatusFilter>('All')

  const filterFrom = ref('')

  const filterTo = ref('')

  const filterError = ref<UiMessage | null>(null)

  const appliedStatus = ref<CheatIncidentStatusFilter>('All')

  const appliedRange = ref(defaultCheatIncidentQueryRange())

  const pendingCount = ref<number | null>(null)

  const { items, loading, error: listError, hasMore, loadMore, reset, initialized } = useCursorPagination<
    NoCTFAPIEndpointsAdministrationCheatIncidentsCheatIncidentListItemResponse
  >(async (cursor) => {
    const data = await api.api.v1.admin.competitions.byCompetitionId(competitionId).cheatIncidents.get({ queryParameters: {
        status: appliedStatus.value === 'All' ? undefined : appliedStatus.value,
        from: dateObject(appliedRange.value.from ?? undefined),
        to: dateObject(appliedRange.value.to ?? undefined),
        cursor: cursor ?? undefined,
        limit: 30,
      } });
    pendingCount.value = data?.pendingCount ?? null
    return data ?? {}
  })

  const { loadNextPage, refreshLatest } = createLatestPageRefresh({ loadMore, reset })

  function applyFilters() {
    const resolved = resolveCheatIncidentQueryRange(filterFrom.value, filterTo.value)
    if (!resolved.range) {
      filterError.value = resolved.error
      return
    }

    filterError.value = null
    appliedStatus.value = filterStatus.value
    appliedRange.value = resolved.range
    void refreshLatest()
  }

  const detail = ref<NoCTFAPIEndpointsAdministrationCheatIncidentsCheatIncidentDetailResponse | null>(null)

  const detailOpen = ref(false)

  const detailLoading = ref(false)

  const showFlag = ref(false)

  const detailRequests = createLatestRequestGuard()

  watch(detailOpen, (open) => {
    if (open) return
    detailRequests.invalidate()
    detailLoading.value = false
  })

  async function openDetail(gameplayFactId?: string | null) {
    if (!gameplayFactId) return
    const request = detailRequests.begin()
    detailOpen.value = true
    detailLoading.value = true
    detail.value = null
    showFlag.value = false
    let error: unknown;
    const data = await api.api.v1.admin.competitions.byCompetitionId(competitionId).cheatIncidents.byGameplayFactId(gameplayFactId).get().catch(cause => { error = cause; return undefined });
    if (!detailRequests.isCurrent(request)) return
    if (error) toast.error(parseApiError(error).displayMessage)
    else detail.value = data ?? null
    detailLoading.value = false
  }

  const ActionMeta = {
    confirm: { title: "common.label.confirmCheatingBan", description: "common.competitionsBy.description.confirmIncidentCheatingBan" },
    dismiss: { title: "common.label.dismissCheatingIncident", description: "common.competitionsBy.description.rejectingCheatingIncidentResult" },
    correct: { title: "common.label.correctCheatingIncidents", description: "common.competitionsBy.description.markPreviouslyConfirmedCheating" },
  } as const

  const SuccessMessage = {
    confirm: "common.competitionsBy.description.cheatingConfirmedSourceTeam",
    dismiss: "common.label.cheatingIncidentDismissed",
    correct: "common.competitionsBy.description.cheatingIncidentCorrectedRelated",
  } as const

  async function refreshResolvedIncident(request: CheatIncidentResolutionRequest) {
    if (detailOpen.value)
      await openDetail(request.gameplayFactId)

    await refreshLatest()
  }

  const {
    action: resolutionAction,
    begin: beginResolution,
    canSubmit: canSubmitResolution,
    error: resolutionError,
    isOpen: resolutionOpen,
    isSubmitting: resolutionPending,
    reason: resolutionReason,
    remainingCharacters,
    setOpen: setResolutionOpen,
    submit: submitResolution,
    target: resolutionTarget,
  } = useCheatIncidentResolution({
    competitionId,
    readError: error => parseApiError(error).displayMessage,
    onSuccess: (request) => {
      toast.success(describeMessage(SuccessMessage[request.action]))
      void refreshResolvedIncident(request)
    },
  })

  const resolutionTargetLabel = computed(() => resolutionTarget.value?.sourceTeamName
    ?? resolutionTarget.value?.sourceTeamId
    ?? translate("administration.label.unknownTeam"))

  function openAction(mode: 'confirm' | 'dismiss' | 'correct') {
    const current = detail.value
    if (!current?.gameplayFactId)
      return

    beginResolution(mode, {
      gameplayFactId: current.gameplayFactId,
      sourceTeamId: current.sourceTeamId,
      sourceTeamName: current.sourceTeamName,
    })
  }

  async function handleResolutionSubmit() {
    const succeeded = await submitResolution()
    if (!succeeded && resolutionError.value)
      toast.error(resolutionError.value)
  }

  function handleResolutionOpen(value: boolean) {
    setResolutionOpen(value)
  }

  let unwatchCompetition: (() => void) | null = null

  onMounted(() => {
    void refreshLatest()
    const incidentId = typeof route.query.incident === 'string' ? route.query.incident : null
    if (incidentId) void openDetail(incidentId)
    unwatchCompetition = watchCompetition(competitionId, {
      competitionEventChanged: () => void refreshLatest(),
      onReconnected: () => void refreshLatest(),
    })
  })

  onBeforeUnmount(() => {
    unwatchCompetition?.()
    unwatchCompetition = null
  })

  const viewBindings = {
      competitionId,
      adminUserPath, adminTeamPath, adminChallengePath,
      filterStatus,
      filterFrom,
      filterTo,
      filterError,
      pendingCount,
      items,
      loading,
      listError,
      hasMore,
      loadMore,
      initialized,
      loadNextPage,
      applyFilters,
      detail,
      detailOpen,
      detailLoading,
      showFlag,
      openDetail,
      ActionMeta,
      resolutionAction,
      canSubmitResolution,
      resolutionError,
      resolutionOpen,
      resolutionPending,
      resolutionReason,
      remainingCharacters,
      resolutionTargetLabel,
      openAction,
      handleResolutionSubmit,
      handleResolutionOpen
    }
  const viewState = proxyRefs(viewBindings)

  function onClickShowFlag() {
    viewState.showFlag = !viewState.showFlag
  }

  return { ...viewBindings, onClickShowFlag }
}

export type AdminCompetitionsByIdCheatsPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminCompetitionsByIdCheatsPage>>>
