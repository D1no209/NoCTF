import { adminUserPath, adminTeamPath, adminChallengePath } from '~/features/admin/admin-navigation'
import { proxyRefs } from 'vue'

import { toast } from 'vue-sonner'
import { Download } from '@lucide/vue'
import { downloadSdkFile } from '../../../../../utils/download'
import { adminAccessCompetitionGameplayFactValue, adminCreateGameplayFactRejudgement, adminGetGameplayFact, adminDownloadGameplayFactPatch, adminListCompetitionChallenges, adminListGameplayFacts, adminPreviewHistoricalAdjudicationDifferences, adminListTeams, adminQueueGameplayFactEvaluation } from '../../../../../api'
import type { NoCtfapiEndpointsGameplayFactsAdminGameplayFactStatusResponse, NoCtfapiEndpointsGameplayFactsGameplayFactListItemResponse, NoCtfapiEndpointsGameplayFactsGameplayFactResultProtocol, NoCtfapiEndpointsGameplayFactsGameplayFactStateProtocol, NoCtfapiEndpointsGameplayFactsGameplayFactKindProtocol, NoCtfapiEndpointsAdministrationGameplayFactsHistoricalAdjudicationDifferenceItemResponse, NoCtfapiEndpointsAdministrationGameplayFactsAdjudicationDifferenceKindProtocol } from '../../../../../api'
import { useCompetitionAdmin } from '../../../../../lib/admin-competition'
import { adminGetGameplayFactAdjudicationEvents } from '../../../../../api'
import type { NoCtfapiEndpointsAdministrationGameplayFactsAdjudicationEventResponse } from '../../../../../api'
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

  const patchErrors = ref<Record<string, string>>({})

  async function downloadPatch(id?: string) {
    if (!id || !canDownloadPatch.value || patchDownloading.value.has(id)) return
    patchDownloading.value.add(id)
    delete patchErrors.value[id]
    try {
      await downloadSdkFile(adminDownloadGameplayFactPatch({
        path: { competitionId, gameplayFactId: id }, parseAs: 'blob',
      }), `patch-${id}.tar.gz`)
    }
    catch (error) {
      patchErrors.value[id] = parseApiError(error).message
      toast.error(patchErrors.value[id])
    }
    finally {
      patchDownloading.value.delete(id)
    }
  }

  const gameplayFactKindOptions = [
    { value: 'FlagAttempt', label: 'Flag' },
    { value: 'BreakAttempt', label: 'Break' },
    { value: 'FixAttempt', label: translate('ui.patchVerification') },
  ] satisfies FilterOption<NoCtfapiEndpointsGameplayFactsGameplayFactKindProtocol>[]

  const gameplayFactStateOptions = [
    { value: 'Pending', label: "ui.pending" },
    { value: 'Queued', label: "ui.queuing" },
    { value: 'Processing', label: "ui.underEvaluation" },
    { value: 'Completed', label: "ui.completed" },
    { value: 'PlatformFailed', label: "ui.platformFailed" },
  ] satisfies FilterOption<NoCtfapiEndpointsGameplayFactsGameplayFactStateProtocol>[]

  const gameplayFactResultOptions = [
    { value: 'Correct', label: "ui.correct" },
    { value: 'Wrong', label: "ui.wrong" },
    { value: 'Duplicate', label: "ui.repeat" },
    { value: 'AttemptsExhausted', label: "ui.exhausted" },
    { value: 'Rejected', label: "ui.rejected" },
  ] satisfies FilterOption<NoCtfapiEndpointsGameplayFactsGameplayFactResultProtocol>[]

  const challengeOptions = ref<{ id: string; title: string }[]>([])

  const teamOptions = ref<{ id: string; name: string }[]>([])

  const teamName = (id?: string | null) => teamOptions.value.find(t => t.id === id)?.name ?? id ?? '-'

  const challengeTitle = (id?: string | null) => challengeOptions.value.find(c => c.id === id)?.title ?? id ?? '-'

  async function loadRefs() {
    const [challenges, teams] = await Promise.all([
      adminListCompetitionChallenges({ path: { competitionId }, query: { includeDeleted: false } }),
      adminListTeams({ path: { competitionId }, query: { keyword: null, offset: 0, limit: 200, desc: false } }),
    ])
    challengeOptions.value = (challenges.data?.items ?? [])
      .map(c => ({ id: c.id!, title: c.title ?? '' }))
    teamOptions.value = (teams.data?.items ?? []).map(t => ({ id: t.id!, name: t.name ?? '' }))
  }

  const filterChallenge = ref('')

  const filterTeam = ref('')

  const filterKind = ref<NoCtfapiEndpointsGameplayFactsGameplayFactKindProtocol | ''>('')

  const filterState = ref<NoCtfapiEndpointsGameplayFactsGameplayFactStateProtocol | ''>('')

  const filterResult = ref<NoCtfapiEndpointsGameplayFactsGameplayFactResultProtocol | ''>('')

  const filterFlag = ref('')

  const previewItems = ref<NoCtfapiEndpointsAdministrationGameplayFactsHistoricalAdjudicationDifferenceItemResponse[]>([])

  const previewCursor = ref<string | null>(null)

  const previewLoading = ref(false)

  const previewError = ref<string | null>(null)

  const previewInitialized = ref(false)
  const previewIncludeInformational = ref(false)
  const previewScanned = ref<number | null>(0)
  const previewCounts = computed(() => adjudicationCounts(previewItems.value))
  const evidenceOpen = ref(false)
  const evidenceTarget = ref<NoCtfapiEndpointsAdministrationGameplayFactsHistoricalAdjudicationDifferenceItemResponse | null>(null)
  let evidenceAbort: AbortController | undefined
  let previewAbort: AbortController | undefined
  const { items: evidenceRows, nextCursor: evidenceCursor, loading: evidenceLoading, error: evidenceError, loadMore: loadEvidence, reset: resetEvidence } = useCursorPagination<NoCtfapiEndpointsAdministrationGameplayFactsAdjudicationEventResponse>(async cursor => {
    const factId = evidenceTarget.value?.gameplayFactId
    if (!factId) return { items: [], nextCursor: null }
    evidenceAbort = new AbortController()
    const { data, error } = await adminGetGameplayFactAdjudicationEvents({
      path: { competitionId, gameplayFactId: factId },
      query: { cursor, limit: 50 },
      signal: evidenceAbort.signal,
    })
    if (error || !data) throw parseApiError(error, translate('adjudication.evidenceFailure'))
    return { items: data.events ?? [], nextCursor: data.nextCursor }
  })

  function openEvidence(item: NoCtfapiEndpointsAdministrationGameplayFactsHistoricalAdjudicationDifferenceItemResponse) {
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

  const differenceLabels: Record<NoCtfapiEndpointsAdministrationGameplayFactsAdjudicationDifferenceKindProtocol, string> = {
    DuplicateWithoutCurrentPredecessor: "ui.theCurrentDuplicateResultHasNoPrecedingFactThatRemains",
    HistoricalResultChanged: "ui.historicalAdjudicationConflictsWithTheCurrentResultOrChangedOver",
    MissingAdjudicationRecord: "ui.theCurrentResultHasNoImmutableAdjudicationEvent",
    TeamEligibilityHistoryRequiresReview: "ui.currentTeamEligibilityCannotProveBloodAwardEligibilityAtThe",
    MissingBloodAward: "ui.aDeterministicallyExpectedBloodAwardIsMissing",
    UnexpectedBloodAward: "ui.aRecordedBloodAwardIsNotSupportedByTheCurrent",
    WrongBloodRank: "ui.theRecordedBloodRankDiffersFromAuthoritativeOrdering",
    DuplicateBloodAward: "ui.theSameGameplayFactHasDuplicateBloodAwards",
  }

  const bloodRankLabel = (rank?: string | null) => rank === 'First'
    ? translate("ui.firstBlood")
    : rank === 'Second'
      ? translate("ui.secondBlood")
      : rank === 'Third'
        ? translate("ui.thirdBlood")
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
      const { data, error } = await adminPreviewHistoricalAdjudicationDifferences({
        signal: previewAbort.signal,
        path: { competitionId },
        query: {
          competitionChallengeId: filterChallenge.value || null,
          cursor,
          limit: 30,
          includeInformational: previewIncludeInformational.value,
        },
      })
      if (error || !data) throw parseApiError(error)
      if (generation !== previewGeneration) return
      previewItems.value.push(...(data.items ?? []))
      previewScanned.value = previewScanned.value !== null && data.scannedFacts != null ? previewScanned.value + data.scannedFacts : null
      previewCursor.value = data.nextCursor ?? null
    }
    catch (requestError) {
      if (generation !== previewGeneration) return
      previewError.value = parseApiError(requestError, translate("ui.failedToLoadHistoricalAdjudicationDifferences")).message
    }
    finally {
      if (generation === previewGeneration) {
        previewLoading.value = false
        previewInitialized.value = true
      }
    }
  }

  const { items, loading, error: listError, hasMore, loadMore, reset, initialized } = useCursorPagination<
    NoCtfapiEndpointsGameplayFactsGameplayFactListItemResponse
  >(async (cursor) => {
    const { data, error } = await adminListGameplayFacts({
      path: { competitionId },
      query: {
        competitionChallengeId: filterChallenge.value || null,
        teamId: filterTeam.value || null,
        gameplayFactKind: filterKind.value || null,
        state: filterState.value || null,
        gameplayFactResult: filterResult.value || null,
        value: filterFlag.value || null,
        offset: cursor ? Number(cursor) || 0 : 0,
        limit: 30,
        desc: true,
      },
    })
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

  const detail = ref<NoCtfapiEndpointsGameplayFactsAdminGameplayFactStatusResponse | null>(null)

  const detailOpen = ref(false)

  const detailLoading = ref(false)

  const detailError = ref<string | null>(null)

  let detailRequest = 0

  async function openDetail(id?: string) {
    if (!id) return
    const request = ++detailRequest
    detailOpen.value = true
    detailLoading.value = true
    detailError.value = null
    detail.value = null
    try {
      const { data, error } = await adminGetGameplayFact({ path: { competitionId, gameplayFactId: id } })
      if (error || !data) throw error
      if (request === detailRequest) detail.value = data
    }
    catch (error) {
      if (request === detailRequest) detailError.value = parseApiError(error).message
    }
    finally {
      if (request === detailRequest) detailLoading.value = false
    }
  }

  watch(detailOpen, open => { if (!open) { detailRequest++; detailLoading.value = false } })

  const actionPending = ref<string | null>(null)

  async function rejudgeOne(gameplayFactId?: string) {
    if (!gameplayFactId) return
    actionPending.value = gameplayFactId
    try {
      const { error } = await adminCreateGameplayFactRejudgement({
        path: { competitionId },
        body: { targetKind: 'GameplayFact', targetId: gameplayFactId },
      })
      if (error) throw error
      toast.success(translate("ui.alreadyJoinedTheReSentencingQueue"))
      applyFilters()
    }
    catch (e) {
      toast.error(parseApiError(e).message)
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
      const { error } = await adminCreateGameplayFactRejudgement({
        path: { competitionId },
        body: { targetKind: 'CompetitionChallenge', targetId: batchTarget.value },
      })
      if (error) throw error
      toast.success(translate("ui.allQuestionsHaveBeenSubmittedAndAddedToTheRe"))
      applyFilters()
    }
    catch (e) {
      toast.error(parseApiError(e).message)
    }
    finally {
      actionPending.value = null
    }
  }

  async function queueEvaluation() {
    if (!batchTarget.value) return
    actionPending.value = 'queue'
    try {
      const { error } = await adminQueueGameplayFactEvaluation({
        path: { competitionId },
        body: { competitionChallengeId: batchTarget.value },
      })
      if (error) throw error
      toast.success(translate("ui.reviewQueueTriggered"))
    }
    catch (e) {
      toast.error(parseApiError(e).message)
    }
    finally {
      actionPending.value = null
    }
  }

  const flagDialog = ref<{ gameplayFactId: string } | null>(null)

  const flagResult = ref<string | null>(null)

  const flagError = ref<string | null>(null)

  const flagPending = ref(false)

  let flagRequestSequence = 0

  function openFlagAccess(gameplayFactId?: string) {
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
      const { data, error } = await adminAccessCompetitionGameplayFactValue({
        path: { competitionId, gameplayFactId: ctx.gameplayFactId },
      })
      if (error) throw error
      if (requestSequence !== flagRequestSequence || flagDialog.value?.gameplayFactId !== ctx.gameplayFactId) return
      flagResult.value = data?.value ?? translate("ui.noContent")
    }
    catch (e) {
      if (requestSequence !== flagRequestSequence || flagDialog.value?.gameplayFactId !== ctx.gameplayFactId) return
      flagError.value = parseApiError(e).message
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
