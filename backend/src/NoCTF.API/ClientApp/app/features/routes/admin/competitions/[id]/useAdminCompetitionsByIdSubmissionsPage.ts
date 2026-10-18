import AwdCheckReviewComponent from '~/features/admin/awd/AwdCheckReview.vue'
import { markRaw } from 'vue'
import { message as describeMessage } from '../../../../../utils/i18n'
import type { UiMessage } from '../../../../../utils/i18n'
import { adminUserPath, adminTeamPath, adminChallengePath } from '~/features/admin/admin-navigation'
import { proxyRefs } from 'vue'

import { toast } from '../../../../../utils/message-toast'
import { Download } from '@lucide/vue'
import { downloadSdkFile } from '../../../../../utils/download'
import { adminAccessCompetitionGameplayFactValue, adminCreateGameplayFactRejudgement, adminGetGameplayFact, adminDownloadGameplayFactPatch, adminListCompetitionChallenges, adminListGameplayFacts, adminPreviewHistoricalAdjudicationDifferences, adminListTeams, adminQueueGameplayFactEvaluation } from '../../../../../api'
import type { NoCtfapiEndpointsGameplayFactsAdminGameplayFactStatusResponse, NoCtfapiEndpointsGameplayFactsGameplayFactListItemResponse, NoCtfapiEndpointsGameplayFactsGameplayFactResultProtocol, NoCtfapiEndpointsGameplayFactsGameplayFactStateProtocol, NoCtfapiEndpointsGameplayFactsGameplayFactKindProtocol, NoCtfapiEndpointsAdministrationGameplayFactsHistoricalAdjudicationDifferenceItemResponse, NoCtfapiEndpointsAdministrationGameplayFactsAdjudicationDifferenceKindProtocol } from '../../../../../api'
import { useCompetitionAdmin } from '../../../../../lib/admin-competition'
import { useAdminCompetitionFilteredList, useAdminCompetitionReferences } from '~/features/admin/useAdminCompetitionList'
import { adminGetGameplayFactAdjudicationEvents } from '../../../../../api'
import type { NoCtfapiEndpointsAdministrationGameplayFactsAdjudicationEventResponse } from '../../../../../api'
import { adjudicationCounts, adjudicationSeverity, adjudicationSeverityLabel, adjudicationClassificationLabel, adjudicationCompletenessLabel, adjudicationEventLabel, adjudicationVariant } from '../../../../admin/adjudication-preview'

interface FilterOption<T extends string> {
  value: T
  label: string
}

/** Owns state, effects and commands for AdminCompetitionsByIdSubmissionsPage. */
export function useAdminCompetitionsByIdSubmissionsPage() {
  const { competitionId, competition, role, canWrite, canJudge } = useCompetitionAdmin()
  const router = useRouter()
  const isSolo = computed(() => competition.value?.mode === 'LiveSolo')
  const canRejudge = computed(() => canWrite.value && !isSolo.value)
  async function soloResults() { await router.push(`/admin/competitions/${competitionId}/live-solo`) }

  const { isAdministrator } = useAuth()

  const canDownloadPatch = computed(() => isAdministrator.value
    || role.value === 'Owner' || role.value === 'Manager' || role.value === 'Judge')

  const patchDownloading = ref(new Set<string>())

  const patchErrors = ref<Record<string, UiMessage>>({})

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
  ] satisfies FilterOption<NoCtfapiEndpointsGameplayFactsGameplayFactKindProtocol>[]

  const gameplayFactStateOptions = [
    { value: 'Pending', label: "administration.label.pending" },
    { value: 'Queued', label: "common.label.queuing" },
    { value: 'Processing', label: "common.label.underEvaluation" },
    { value: 'Completed', label: "common.label.completed" },
    { value: 'PlatformFailed', label: "common.error.platformFailed.adminFormat" },
  ] satisfies FilterOption<NoCtfapiEndpointsGameplayFactsGameplayFactStateProtocol>[]

  const gameplayFactResultOptions = [
    { value: 'Correct', label: "common.label.correct" },
    { value: 'Wrong', label: "common.label.wrong" },
    { value: 'Duplicate', label: "common.label.repeat" },
    { value: 'AttemptsExhausted', label: "common.label.exhausted" },
    { value: 'Rejected', label: "common.label.rejected" },
  ] satisfies FilterOption<NoCtfapiEndpointsGameplayFactsGameplayFactResultProtocol>[]

  const { challengeOptions, teamOptions, challengeTitle, teamName, loadRefs } = useAdminCompetitionReferences(competitionId, {
    listChallenges: adminListCompetitionChallenges,
    listTeams: adminListTeams,
  })

  const filterChallenge = ref('')

  const filterTeam = ref('')

  const filterKind = ref<NoCtfapiEndpointsGameplayFactsGameplayFactKindProtocol | ''>('')

  const filterState = ref<NoCtfapiEndpointsGameplayFactsGameplayFactStateProtocol | ''>('')

  const filterResult = ref<NoCtfapiEndpointsGameplayFactsGameplayFactResultProtocol | ''>('')

  const filterFlag = ref('')

  const previewItems = ref<NoCtfapiEndpointsAdministrationGameplayFactsHistoricalAdjudicationDifferenceItemResponse[]>([])

  const previewCursor = ref<string | null>(null)

  const previewLoading = ref(false)

  const previewError = ref<UiMessage | null>(null)

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
    if (error || !data) throw parseApiError(error, describeMessage('adjudication.evidenceFailure'))
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
    pagination.reset()
    resetEvidence()
    previewGeneration++
    evidenceAbort?.abort()
    previewAbort?.abort()
  })

  let previewGeneration = 0

  const differenceLabels: Record<NoCtfapiEndpointsAdministrationGameplayFactsAdjudicationDifferenceKindProtocol, string> = {
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
    if (isSolo.value) { await soloResults(); return }
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
      previewError.value = parseApiError(requestError, describeMessage("administration.competitionsBy.error.loadHistoricalAdjudicationFailed")).displayMessage
    }
    finally {
      if (generation === previewGeneration) {
        previewLoading.value = false
        previewInitialized.value = true
      }
    }
  }

  type SubmissionListFilters = {
    challengeId: string
    teamId: string
    kind: NoCtfapiEndpointsGameplayFactsGameplayFactKindProtocol | ''
    state: NoCtfapiEndpointsGameplayFactsGameplayFactStateProtocol | ''
    result: NoCtfapiEndpointsGameplayFactsGameplayFactResultProtocol | ''
    value: string
  }

  const list = useAdminCompetitionFilteredList<NoCtfapiEndpointsGameplayFactsGameplayFactListItemResponse, SubmissionListFilters>(
    async (filters, request) => {
      const { data, error } = await adminListGameplayFacts({
        path: { competitionId },
        query: {
          competitionChallengeId: filters.challengeId || null,
          teamId: filters.teamId || null,
          gameplayFactKind: filters.kind || null,
          state: filters.state || null,
          gameplayFactResult: filters.result || null,
          value: filters.value || null,
          ...request,
        },
      })
      if (error || !data) throw parseApiError(error)
      return { items: data.items ?? [], total: data.total ?? 0 }
    },
    {
      readFilters: () => ({
        challengeId: filterChallenge.value,
        teamId: filterTeam.value,
        kind: filterKind.value,
        state: filterState.value,
        result: filterResult.value,
        value: filterFlag.value,
      }),
      onApplied: () => { if (previewInitialized.value) void loadPreview(true) },
    },
  )

  const { pagination, applyFilters } = list

  const detail = ref<NoCtfapiEndpointsGameplayFactsAdminGameplayFactStatusResponse | null>(null)

  const detailOpen = ref(false)

  const detailLoading = ref(false)

  const detailError = ref<UiMessage | null>(null)

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
      if (request === detailRequest) detailError.value = parseApiError(error).displayMessage
    }
    finally {
      if (request === detailRequest) detailLoading.value = false
    }
  }

  watch(detailOpen, open => { if (!open) { detailRequest++; detailLoading.value = false } })

  const actionPending = ref<string | null>(null)

  async function rejudgeOne(gameplayFactId?: string) {
    if (isSolo.value) { await soloResults(); return }
    if (!gameplayFactId) return
    actionPending.value = gameplayFactId
    try {
      const { error } = await adminCreateGameplayFactRejudgement({
        path: { competitionId },
        body: { targetKind: 'GameplayFact', targetId: gameplayFactId },
      })
      if (error) throw error
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
    if (isSolo.value) { await soloResults(); return }
    if (!batchTarget.value) return
    actionPending.value = 'batch'
    try {
      const { error } = await adminCreateGameplayFactRejudgement({
        path: { competitionId },
        body: { targetKind: 'CompetitionChallenge', targetId: batchTarget.value },
      })
      if (error) throw error
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
    if (isSolo.value) { await soloResults(); return }
    if (!batchTarget.value) return
    actionPending.value = 'queue'
    try {
      const { error } = await adminQueueGameplayFactEvaluation({
        path: { competitionId },
        body: { competitionChallengeId: batchTarget.value },
      })
      if (error) throw error
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
    void pagination.loadPage(1)
  })

  const viewBindings = {
      competitionId, isSolo, canRejudge, soloResults,
      isAwd: computed(() => competition.value?.mode === 'Awd'), AwdCheckReview: markRaw(AwdCheckReviewComponent),
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
      ...list.listBindings,
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
