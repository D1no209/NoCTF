
import { api, RequestPolicyOption, nativeResponse } from '../../../../../lib/api'
import { message as describeMessage } from '../../../../../utils/i18n'
import type { UiMessage } from '../../../../../utils/i18n'
import { adminUserPath, adminTeamPath, adminChallengePath } from '~/features/admin/admin-navigation'
import { proxyRefs } from 'vue'

import { toast } from '../../../../../utils/message-toast'
import { Download } from '@lucide/vue'
import { downloadSdkFile } from '../../../../../utils/download'

import type { NoCTFAPIEndpointsGameplayFactsAdminGameplayFactStatusResponse, NoCTFAPIEndpointsGameplayFactsGameplayFactListItemResponse, NoCTFAPIEndpointsGameplayFactsGameplayFactResultProtocol, NoCTFAPIEndpointsGameplayFactsGameplayFactStateProtocol, NoCTFAPIEndpointsGameplayFactsGameplayFactKindProtocol, NoCTFAPIEndpointsAdministrationGameplayFactsHistoricalAdjudicationDifferenceItemResponse, NoCTFAPIEndpointsAdministrationGameplayFactsAdjudicationDifferenceKindProtocol } from '../../../../../api/models'
import { useCompetitionAdmin } from '../../../../../lib/admin-competition'

import type { NoCTFAPIEndpointsAdministrationGameplayFactsAdjudicationEventResponse } from '../../../../../api/models'
import { adjudicationCounts, adjudicationSeverity, adjudicationSeverityLabel, adjudicationClassificationLabel, adjudicationCompletenessLabel, adjudicationEventLabel, adjudicationVariant } from '../../../../admin/adjudication-preview'

interface FilterOption<T extends string> {
  value: T
  label: string
}

/** Owns state, effects and commands for AdminCompetitionsByIdSubmissionsPage. */
export function useAdminCompetitionsByIdSubmissionsPage() {
  const { competitionId, role, canWrite, canJudge } = useCompetitionAdmin()

  const { isAdministrator } = useAuth()

  const canDownloadPatch = computed(() => isAdministrator.value
    || role.value === 'Owner' || role.value === 'Manager' || role.value === 'Judge')

  const patchDownloading = ref(new Set<string>())

  const patchErrors = ref<Record<string, UiMessage>>({})

  async function downloadPatch(id?: string | null) {
    if (!id || !canDownloadPatch.value || patchDownloading.value.has(id)) return
    patchDownloading.value.add(id)
    delete patchErrors.value[id]
    try {
      await downloadSdkFile(nativeResponse(responseOptions => api.api.v1.admin.competitions.byCompetitionId(competitionId).gameplayFacts.byGameplayFactId(id).patchPath.get({ options: [...responseOptions] })), `patch-${id}.tar.gz`)
    }
    catch (error) {
      patchErrors.value[id] = parseApiError(error).displayMessage
      toast.error(patchErrors.value[id]!)
    }
    finally {
      patchDownloading.value.delete(id)
    }
  }

  const gameplayFactKindOptions = [
    { value: 'FlagAttempt', label: 'Flag' },
    { value: 'BreakAttempt', label: 'Break' },
    { value: 'FixAttempt', label: translate('common.label.patchVerification') },
  ] satisfies FilterOption<NoCTFAPIEndpointsGameplayFactsGameplayFactKindProtocol>[]

  const gameplayFactStateOptions = [
    { value: 'Pending', label: "administration.label.pending" },
    { value: 'Queued', label: "common.label.queuing" },
    { value: 'Processing', label: "common.label.underEvaluation" },
    { value: 'Completed', label: "common.label.completed" },
    { value: 'PlatformFailed', label: "common.error.platformFailed.adminFormat" },
  ] satisfies FilterOption<NoCTFAPIEndpointsGameplayFactsGameplayFactStateProtocol>[]

  const gameplayFactResultOptions = [
    { value: 'Correct', label: "common.label.correct" },
    { value: 'Wrong', label: "common.label.wrong" },
    { value: 'Duplicate', label: "common.label.repeat" },
    { value: 'AttemptsExhausted', label: "common.label.exhausted" },
    { value: 'Rejected', label: "common.label.rejected" },
  ] satisfies FilterOption<NoCTFAPIEndpointsGameplayFactsGameplayFactResultProtocol>[]

  const challengeOptions = ref<{ id: string; title: string }[]>([])

  const teamOptions = ref<{ id: string; name: string }[]>([])

  const teamName = (id?: string | null) => teamOptions.value.find(t => t.id === id)?.name ?? id ?? '-'

  const challengeTitle = (id?: string | null) => challengeOptions.value.find(c => c.id === id)?.title ?? id ?? '-'

  async function loadRefs() {
    const settledRequests = await Promise.allSettled([
      api.api.v1.admin.competitions.byCompetitionId(competitionId).challenges.get({ queryParameters: { includeDeleted: false } }),
      api.api.v1.admin.competitions.byCompetitionId(competitionId).teams.get({ queryParameters: { keyword: undefined, offset: 0, limit: 200, desc: false } }),
    ]);
    const challenges = settledRequests[0].status === 'fulfilled' ? settledRequests[0].value : undefined;
    const teams = settledRequests[1].status === 'fulfilled' ? settledRequests[1].value : undefined;

    challengeOptions.value = (challenges?.items ?? [])
      .map(c => ({ id: c.id!, title: c.title ?? '' }))
    teamOptions.value = (teams?.items ?? []).map(t => ({ id: t.id!, name: t.name ?? '' }))
  }

  const filterChallenge = ref('')

  const filterTeam = ref('')

  const filterKind = ref<NoCTFAPIEndpointsGameplayFactsGameplayFactKindProtocol | ''>('')

  const filterState = ref<NoCTFAPIEndpointsGameplayFactsGameplayFactStateProtocol | ''>('')

  const filterResult = ref<NoCTFAPIEndpointsGameplayFactsGameplayFactResultProtocol | ''>('')

  const filterFlag = ref('')

  const previewItems = ref<NoCTFAPIEndpointsAdministrationGameplayFactsHistoricalAdjudicationDifferenceItemResponse[]>([])

  const previewCursor = ref<string | null>(null)

  const previewLoading = ref(false)

  const previewError = ref<UiMessage | null>(null)

  const previewInitialized = ref(false)
  const previewIncludeInformational = ref(false)
  const previewScanned = ref<number | null>(0)
  const previewCounts = computed(() => adjudicationCounts(previewItems.value))
  const evidenceOpen = ref(false)
  const evidenceTarget = ref<NoCTFAPIEndpointsAdministrationGameplayFactsHistoricalAdjudicationDifferenceItemResponse | null>(null)
  let evidenceAbort: AbortController | undefined
  let previewAbort: AbortController | undefined
  const { items: evidenceRows, nextCursor: evidenceCursor, loading: evidenceLoading, error: evidenceError, loadMore: loadEvidence, reset: resetEvidence } = useCursorPagination<NoCTFAPIEndpointsAdministrationGameplayFactsAdjudicationEventResponse>(async cursor => {
    const factId = evidenceTarget.value?.gameplayFactId
    if (!factId) return { items: [], nextCursor: null }
    evidenceAbort = new AbortController()
    let error: unknown;
    const data = await api.api.v1.admin.competitions.byCompetitionId(competitionId).gameplayFacts.byGameplayFactId(factId).adjudicationEvents.get({ queryParameters: { cursor: cursor ?? undefined, limit: 50 }, options: [new RequestPolicyOption({ signal: evidenceAbort.signal })] }).catch(cause => { error = cause; return undefined });
    if (error || !data) throw parseApiError(error, describeMessage('adjudication.evidenceFailure'))
    return { items: data.events ?? [], nextCursor: data.nextCursor }
  })

  function openEvidence(item: NoCTFAPIEndpointsAdministrationGameplayFactsHistoricalAdjudicationDifferenceItemResponse) {
    evidenceAbort?.abort()
    evidenceTarget.value = item
    resetEvidence()
    evidenceOpen.value = true
    void loadEvidence()
  }

  watch(evidenceOpen, open => {
    if (!open) {
      evidenceAbort?.abort()
      resetEvidence()
      evidenceTarget.value = null
    }
  })
  watch(previewIncludeInformational, () => { if (previewInitialized.value) void loadPreview(true) })
  onUnmounted(() => {
    resetEvidence()
    previewGeneration++
    evidenceAbort?.abort()
    previewAbort?.abort()
  })

  let previewGeneration = 0

  const differenceLabels: Record<NoCTFAPIEndpointsAdministrationGameplayFactsAdjudicationDifferenceKindProtocol, string> = {
    DuplicateWithoutCurrentPredecessor: "common.competitionsBy.description.duplicateResultPrecedingFact",
    HistoricalResultChanged: "common.competitionsBy.description.historicalAdjudicationConflictsResult",
    MissingAdjudicationRecord: "common.competitionsBy.description.resultImmutableAdjudicationEvent",
    TeamEligibilityHistoryRequiresReview: "common.competitionsBy.validation.teamEligibilityFormat",
    MissingBloodAward: "common.competitionsBy.description.deterministicallyExpectedBloodAward",
    UnexpectedBloodAward: "common.competitionsBy.validation.recordedBloodFormat",
    WrongBloodRank: "common.competitionsBy.description.recordedBloodRankDiffers",
    DuplicateBloodAward: "common.competitionsBy.description.sameGameplayFactDuplicate",
  }

  const bloodRankLabel = (rank?: string | null) => rank === 'First'
    ? translate("common.label.firstBlood")
    : rank === 'Second'
      ? translate("common.label.secondBlood")
      : rank === 'Third'
        ? translate("common.label.thirdBlood")
        : '-'

  async function loadPreview(reset = false) {
    if (previewLoading.value && !reset) return
    if (reset) {
      previewGeneration++
      previewAbort?.abort()
      previewItems.value = []
      previewCursor.value = null
      previewScanned.value = 0
    }
    const generation = previewGeneration
    const cursor = previewCursor.value
    previewLoading.value = true
    previewAbort = new AbortController()
    previewError.value = null
    try {
      let error: unknown;
      const data = await api.api.v1.admin.competitions.byCompetitionId(competitionId).gameplayFacts.adjudicationDifferences.get({ queryParameters: {
          competitionChallengeId: filterChallenge.value || undefined,
          cursor: cursor ?? undefined,
          limit: 30,
          includeInformational: previewIncludeInformational.value,
        }, options: [new RequestPolicyOption({ signal: previewAbort.signal })] }).catch(cause => { error = cause; return undefined });
      if (error || !data) throw parseApiError(error)
      if (generation !== previewGeneration) return
      previewItems.value.push(...(data.items ?? []))
      previewScanned.value = previewScanned.value !== null && data.scannedFacts != null ? previewScanned.value + data.scannedFacts : null
      previewCursor.value = data.nextCursor ?? null
    }
    catch (requestError) {
      if (generation !== previewGeneration) return
      previewError.value = parseApiError(requestError, describeMessage("administration.competitionsBy.error.loadHistoricalAdjudicationFailed")).displayMessage
    }
    finally {
      if (generation === previewGeneration) {
        previewLoading.value = false
        previewInitialized.value = true
      }
    }
  }

  const { items, loading, error: listError, hasMore, loadMore, reset, initialized } = useCursorPagination<
    NoCTFAPIEndpointsGameplayFactsGameplayFactListItemResponse
  >(async (cursor) => {
    let error: unknown;
    const data = await api.api.v1.admin.competitions.byCompetitionId(competitionId).gameplayFacts.get({ queryParameters: {
        competitionChallengeId: filterChallenge.value || undefined,
        teamId: filterTeam.value || undefined,
        gameplayFactKind: filterKind.value || undefined,
        state: filterState.value || undefined,
        gameplayFactResult: filterResult.value || undefined,
        value: filterFlag.value || undefined,
        offset: cursor ? Number(cursor) || 0 : 0,
        limit: 30,
        desc: true,
      } }).catch(cause => { error = cause; return undefined });
    if (error || !data) throw parseApiError(error)
    const pageItems = data.items ?? []
    const offset = cursor ? Number(cursor) || 0 : 0
    const nextOffset = offset + pageItems.length
    return { items: pageItems, nextCursor: nextOffset < (data.total ?? 0) ? String(nextOffset) : null }
  })

  function applyFilters() {
    reset({ preserveItems: true })
    void loadMore()
    if (previewInitialized.value) void loadPreview(true)
  }

  const detail = ref<NoCTFAPIEndpointsGameplayFactsAdminGameplayFactStatusResponse | null>(null)

  const detailOpen = ref(false)

  const detailLoading = ref(false)

  const detailError = ref<UiMessage | null>(null)

  let detailRequest = 0

  async function openDetail(id?: string | null) {
    if (!id) return
    const request = ++detailRequest
    detailOpen.value = true
    detailLoading.value = true
    detailError.value = null
    detail.value = null
    try {
      let error: unknown;
      const data = await api.api.v1.admin.competitions.byCompetitionId(competitionId).gameplayFacts.byGameplayFactId(id).get().catch(cause => { error = cause; return undefined });
      if (error || !data) throw error
      if (request === detailRequest) detail.value = data
    }
    catch (error) {
      if (request === detailRequest) detailError.value = parseApiError(error).displayMessage
    }
    finally {
      if (request === detailRequest) detailLoading.value = false
    }
  }

  watch(detailOpen, open => { if (!open) { detailRequest++; detailLoading.value = false } })

  const actionPending = ref<string | null>(null)

  async function rejudgeOne(gameplayFactId?: string | null) {
    if (!gameplayFactId) return
    actionPending.value = gameplayFactId
    try {

      await api.api.v1.admin.competitions.byCompetitionId(competitionId).gameplayFactRejudgements.post({ targetKind: 'GameplayFact', targetId: gameplayFactId });
      toast.success(describeMessage("administration.competitionsBy.description.alreadyJoinedReSentencing"))
      applyFilters()
    }
    catch (e) {
      toast.error(parseApiError(e).displayMessage)
    }
    finally {
      actionPending.value = null
    }
  }

  const batchTarget = ref<string>('')

  async function rejudgeBatch() {
    if (!batchTarget.value) return
    actionPending.value = 'batch'
    try {
      await api.api.v1.admin.competitions.byCompetitionId(competitionId).gameplayFactRejudgements.post({ targetKind: 'CompetitionChallenge', targetId: batchTarget.value });
      toast.success(describeMessage("administration.competitionsBy.description.questionsSubmittedAddedRe"))
      applyFilters()
    }
    catch (e) {
      toast.error(parseApiError(e).displayMessage)
    }
    finally {
      actionPending.value = null
    }
  }

  async function queueEvaluation() {
    if (!batchTarget.value) return
    actionPending.value = 'queue'
    try {
      await api.api.v1.admin.competitions.byCompetitionId(competitionId).gameplayFacts.queueEvaluation.post({ competitionChallengeId: batchTarget.value });
      toast.success(describeMessage("administration.label.reviewQueueTriggered"))
    }
    catch (e) {
      toast.error(parseApiError(e).displayMessage)
    }
    finally {
      actionPending.value = null
    }
  }

  const flagDialog = ref<{ gameplayFactId: string } | null>(null)

  const flagResult = ref<string | null>(null)

  const flagError = ref<UiMessage | null>(null)

  const flagPending = ref(false)

  let flagRequestSequence = 0

  function openFlagAccess(gameplayFactId?: string | null) {
    if (!gameplayFactId) return
    const requestSequence = ++flagRequestSequence
    flagDialog.value = { gameplayFactId }
    flagResult.value = null
    flagError.value = null
    flagPending.value = false
    void accessFlag(requestSequence)
  }

  function closeFlagAccess() {
    flagRequestSequence++
    flagDialog.value = null
    flagPending.value = false
  }

  async function accessFlag(requestSequence = ++flagRequestSequence) {
    const ctx = flagDialog.value
    if (!ctx) return
    flagPending.value = true
    flagError.value = null
    try {
      const data = await api.api.v1.admin.competitions.byCompetitionId(competitionId).gameplayFacts.byGameplayFactId(ctx.gameplayFactId).flagAccess.post();
      if (requestSequence !== flagRequestSequence || flagDialog.value?.gameplayFactId !== ctx.gameplayFactId) return
      flagResult.value = data?.value ?? translate("administration.label.content")
    }
    catch (e) {
      if (requestSequence !== flagRequestSequence || flagDialog.value?.gameplayFactId !== ctx.gameplayFactId) return
      flagError.value = parseApiError(e).displayMessage
      toast.error(flagError.value)
    }
    finally {
      if (requestSequence === flagRequestSequence)
        flagPending.value = false
    }
  }

  onMounted(() => {
    void loadRefs()
    void loadMore()
  })

  const viewBindings = {
      competitionId,
      adminUserPath, adminTeamPath, adminChallengePath,
      previewIncludeInformational, previewScanned, previewCounts,
      evidenceOpen, evidenceTarget, evidenceRows, evidenceCursor, evidenceLoading, evidenceError, openEvidence, loadEvidence,
      adjudicationSeverity, adjudicationSeverityLabel, adjudicationClassificationLabel, adjudicationCompletenessLabel, adjudicationEventLabel, adjudicationVariant,
      Download,
      canWrite,
      canJudge,
      isAdministrator,
      canDownloadPatch,
      patchDownloading,
      patchErrors,
      downloadPatch,
      gameplayFactKindOptions,
      gameplayFactStateOptions,
      gameplayFactResultOptions,
      challengeOptions,
      teamOptions,
      teamName,
      challengeTitle,
      filterChallenge,
      filterTeam,
      filterKind,
      filterState,
      filterResult,
      filterFlag,
      previewItems,
      previewCursor,
      previewLoading,
      previewError,
      previewInitialized,
      differenceLabels,
      bloodRankLabel,
      loadPreview,
      items,
      loading,
      listError,
      hasMore,
      loadMore,
      initialized,
      applyFilters,
      detail,
      detailOpen,
      detailLoading,
      detailError,
      openDetail,
      actionPending,
      rejudgeOne,
      batchTarget,
      rejudgeBatch,
      queueEvaluation,
      flagDialog,
      flagResult,
      flagError,
      flagPending,
      openFlagAccess,
      closeFlagAccess,
      accessFlag
    }
  const viewState = proxyRefs(viewBindings)

  function onClickFilterChallenge() {
    viewState.filterChallenge = ''; viewState.filterTeam = ''; viewState.filterKind = ''; viewState.filterState = ''; viewState.filterResult = ''; viewState.filterFlag = ''; viewState.applyFilters()
  }

  function onUpdateOpenChange(v: boolean) {
     if (!v) viewState.closeFlagAccess()
  }

  return { ...viewBindings, onClickFilterChallenge, onUpdateOpenChange }
}

export type AdminCompetitionsByIdSubmissionsPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminCompetitionsByIdSubmissionsPage>>>
