
import { api, nativeResponse } from '../../../../lib/api'
import { message as describeMessage } from '../../../../utils/i18n'
import type { UiMessage } from '../../../../utils/i18n'
import { ArrowLeft, Download, FileSearch, MessageCircleQuestion, MinusCircle, RefreshCw, Scale } from '@lucide/vue'
import { toast } from '../../../../utils/message-toast'


import type { NoCTFAPIEndpointsTeamsWriteUpsTeamWriteUpChallengeScoreResponse, NoCTFAPIEndpointsTeamsWriteUpsTeamWriteUpReviewItemResponse, NoCTFAPIEndpointsTeamsWriteUpsTeamWriteUpReviewResponse } from '../../../../api/models'
import { createTrailingRefresh } from '../../../../lib/latest-page-refresh'
import { downloadSdkFile } from '../../../../utils/download'

type ReviewItem = NoCTFAPIEndpointsTeamsWriteUpsTeamWriteUpReviewItemResponse
type ChallengeScore = NoCTFAPIEndpointsTeamsWriteUpsTeamWriteUpChallengeScoreResponse

/** Owns staff WriteUp review, authenticated PDF preview, score decisions and consultation creation. */
export function useCompetitionsByIdWriteUpsPage(options: { management?: boolean } = {}) {
  const route = useRoute()
  const router = useRouter()
  const competitionId = route.params.id as string
  const management = options.management === true

  const review = ref<NoCTFAPIEndpointsTeamsWriteUpsTeamWriteUpReviewResponse | null>(null)
  const loading = ref(true)
  const loadError = ref<UiMessage | null>(null)
  const selectedTeamId = ref(typeof route.query.team === 'string' ? route.query.team : '')
  const selected = computed<ReviewItem | null>(() =>
    review.value?.items?.find(item => item.writeUp?.teamId === selectedTeamId.value) ?? null,
  )
  const teamOptions = computed(() => review.value?.items?.flatMap(item => {
    const teamId = item.writeUp?.teamId
    if (!teamId) return []
    return [{ value: teamId, label: item.writeUp?.teamName ?? teamId, item }]
  }) ?? [])
  const canJudge = computed(() => review.value?.canJudge === true)

  const previewUrl = ref<string | null>(null)
  const previewLoading = ref(false)
  const previewError = ref<UiMessage | null>(null)
  const previewFileId = ref<string | null>(null)
  const downloadPending = ref(false)
  let previewRequest = 0

  function releasePreview() {
    previewUrl.value = null
    previewFileId.value = null
  }

  async function load() {
    loading.value = review.value === null
    loadError.value = null
    let error: unknown;
    const data = await api.api.v1.competitions.byCompetitionId(competitionId).writeups.get().catch(cause => { error = cause; return undefined });
    loading.value = false
    if (error || !data) {
      loadError.value = parseApiError(error, describeMessage('writeUp.reviewLoadFailed')).displayMessage
      return
    }
    review.value = data
    const stillSelected = data.items?.some(item =>
      item.writeUp?.teamId === selectedTeamId.value)
    if (!stillSelected)
      selectedTeamId.value = data.items?.[0]?.writeUp?.teamId ?? ''
    if (selectedTeamId.value)
      await selectTeam(selectedTeamId.value)
  }

  async function selectTeam(teamId?: string | null) {
    if (!teamId) return
    selectedTeamId.value = teamId
    void router.replace({
      query: { ...route.query, team: teamId },
    })
    const item = review.value?.items?.find(candidate => candidate.writeUp?.teamId === teamId)
    if (!item?.writeUp?.fileId || previewFileId.value === item.writeUp.fileId) return
    const request = ++previewRequest
    releasePreview()
    previewLoading.value = true
    previewError.value = null
    try {
      let error: unknown;
      const data = await api.api.v1.competitions.byCompetitionId(competitionId).teams.byTeamId(teamId).writeup.preview.post().catch(cause => { error = cause; return undefined });
      if (error || !data?.previewUrl) throw error
      if (request !== previewRequest) return
      previewUrl.value = data.previewUrl
      previewFileId.value = item.writeUp.fileId
    }
    catch (error) {
      if (request === previewRequest)
        previewError.value = parseApiError(error, describeMessage('writeUp.previewFailed')).displayMessage
    }
    finally {
      if (request === previewRequest) previewLoading.value = false
    }
  }

  async function download() {
    const writeUp = selected.value?.writeUp
    const teamId = writeUp?.teamId
    if (!writeUp || !teamId || downloadPending.value) return
    downloadPending.value = true
    try {
      await downloadSdkFile(
        nativeResponse(responseOptions => api.api.v1.competitions.byCompetitionId(competitionId).teams.byTeamId(teamId).writeup.content.get({ options: [...responseOptions] })),
        writeUp.fileName ?? `writeup-${writeUp.teamId}.pdf`,
      )
    }
    catch (error) {
      toast.error(parseApiError(error, describeMessage('writeUp.downloadFailed')).displayMessage)
    }
    finally {
      downloadPending.value = false
    }
  }

  const refreshLatest = createTrailingRefresh(load)
  const selectedChallengeId = ref('')
  const adjustmentDelta = ref(0)
  const adjustmentRequestPending = ref(false)
  const adjustmentError = ref<UiMessage | null>(null)
  const pendingAdjustment = ref<{
    teamId: string
    competitionChallengeId: string
    expectedNetPoints: number
  } | null>(null)

  async function observeAdjustment(): Promise<boolean> {
    const expectation = pendingAdjustment.value
    if (!expectation) return true
    await refreshLatest()
    const item = review.value?.items?.find(candidate =>
      candidate.writeUp?.teamId === expectation.teamId)
    const score = item?.challengeScores?.find(candidate =>
      candidate.competitionChallengeId === expectation.competitionChallengeId)
    if (score?.netPoints !== expectation.expectedNetPoints) return false
    pendingAdjustment.value = null
    toast.success(describeMessage('writeUp.adjustmentApplied'))
    return true
  }

  const {
    polling: adjustmentRefreshing,
    timedOut: adjustmentRefreshTimedOut,
    start: startAdjustmentRefresh,
  } = usePolling(observeAdjustment, {
    interval: 500,
    maxInterval: 2_000,
    timeout: 30_000,
  })
  const adjustmentPending = adjustmentRequestPending
  const adjustmentBusy = computed(() =>
    adjustmentPending.value || adjustmentRefreshing.value,
  )

  watch(adjustmentRefreshTimedOut, timedOut => {
    if (!timedOut) return
    pendingAdjustment.value = null
    adjustmentError.value = describeMessage('writeUp.adjustmentRefreshTimedOut')
    toast.error(adjustmentError.value)
  })

  watch(selected, item => {
    const available = item?.challengeScores?.some(score =>
      score.competitionChallengeId === selectedChallengeId.value)
    if (!available)
      selectedChallengeId.value = item?.challengeScores?.[0]?.competitionChallengeId ?? ''
    adjustmentDelta.value = 0
    adjustmentError.value = null
  }, { immediate: true })

  async function adjustScore(delta: number): Promise<boolean> {
    const writeUp = selected.value?.writeUp
    if (!canJudge.value || !writeUp?.teamId || !selectedChallengeId.value
      || delta === 0 || adjustmentBusy.value)
      return false
    if (!Number.isInteger(delta) || delta < -2147483648 || delta > 2147483647) {
      adjustmentError.value = describeMessage('writeUp.invalidAdjustment')
      return false
    }
    const score = selected.value?.challengeScores?.find(candidate =>
      candidate.competitionChallengeId === selectedChallengeId.value)
    if (score?.netPoints === null || score?.netPoints === undefined) {
      adjustmentError.value = describeMessage('writeUp.invalidAdjustment')
      return false
    }
    adjustmentRequestPending.value = true
    adjustmentError.value = null
    try {
      let error: unknown;
      const data = await api.api.v1.admin.competitions.byCompetitionId(competitionId).gameplayFacts.manualAdjustments.post({
          teamId: writeUp.teamId,
          competitionChallengeId: selectedChallengeId.value,
          delta,
        }).catch(cause => { error = cause; return undefined });
      if (error || !data?.gameplayFactId) throw error
      adjustmentDelta.value = 0
      pendingAdjustment.value = {
        teamId: writeUp.teamId,
        competitionChallengeId: selectedChallengeId.value,
        expectedNetPoints: score.netPoints + delta,
      }
      startAdjustmentRefresh()
      toast.success(describeMessage('writeUp.adjustmentAccepted'))
      return true
    }
    catch (error) {
      adjustmentError.value = parseApiError(error, describeMessage('writeUp.adjustmentFailed')).displayMessage
      toast.error(adjustmentError.value)
      return false
    }
    finally {
      adjustmentRequestPending.value = false
    }
  }

  function submitAdjustment() {
    void adjustScore(adjustmentDelta.value)
  }

  function clearAdjustmentError() {
    adjustmentError.value = null
  }

  function selectChallenge(score: ChallengeScore) {
    selectedChallengeId.value = score.competitionChallengeId ?? ''
  }

  const deduction = ref<ChallengeScore | null>(null)

  function openDeduction(score: ChallengeScore) {
    if (!canJudge.value || !score.competitionChallengeId || (score.netPoints ?? 0) <= 0)
      return
    adjustmentError.value = null
    selectedChallengeId.value = score.competitionChallengeId
    deduction.value = score
  }

  function setDeductionOpen(open: boolean) {
    if (!open && !adjustmentPending.value) deduction.value = null
  }

  async function confirmDeduction() {
    const points = deduction.value?.netPoints ?? 0
    if (points <= 0 || points > 2147483647) return
    if (await adjustScore(-points)) deduction.value = null
  }

  const consultationOpen = ref(false)
  const consultationChallengeId = ref('none')
  const consultationTitle = ref('')
  const consultationBody = ref('')
  const consultationPending = ref(false)
  const consultationError = ref<UiMessage | null>(null)

  function openConsultation() {
    const item = selected.value
    if (!canJudge.value || !item?.writeUp?.teamName) return
    consultationChallengeId.value = selectedChallengeId.value || 'none'
    consultationTitle.value = translate('writeUp.consultationDefaultTitle', {
      team: item.writeUp.teamName,
    })
    consultationBody.value = ''
    consultationError.value = null
    consultationOpen.value = true
  }

  async function submitConsultation() {
    const writeUp = selected.value?.writeUp
    const title = consultationTitle.value.trim()
    const body = consultationBody.value.trim()
    if (!writeUp?.teamId || !canJudge.value || consultationPending.value) return
    consultationError.value = title.length < 4 || body.length < 4
      ? translate('writeUp.consultationRequirement')
      : null
    if (consultationError.value) return
    consultationPending.value = true
    try {
      let error: unknown;
      const data = await api.api.v1.competitions.byCompetitionId(competitionId).teams.byTeamId(writeUp.teamId).writeup.consultations.post({
          competitionChallengeId: consultationChallengeId.value === 'none'
            ? null
            : consultationChallengeId.value,
          title,
          body,
        }).catch(cause => { error = cause; return undefined });
      if (error || !data?.threadRootId) throw error
      consultationOpen.value = false
      toast.success(describeMessage('writeUp.consultationCreated'))
      await router.push({
        path: `/competitions/${competitionId}/questions`,
        query: { question: data.threadRootId },
      })
    }
    catch (error) {
      consultationError.value = parseApiError(error, describeMessage('writeUp.consultationFailed')).displayMessage
      toast.error(consultationError.value)
    }
    finally {
      consultationPending.value = false
    }
  }

  function setConsultationOpen(open: boolean) {
    if (!consultationPending.value) consultationOpen.value = open
    if (!open) consultationError.value = null
  }

  function clearConsultationError() {
    consultationError.value = null
  }

  let unwatch: (() => void) | undefined
  onMounted(() => {
    void load()
    unwatch = watchCompetition(competitionId, {
      competitionEventChanged: event => {
        if (event.kind === 'TeamWriteUpSubmitted') void refreshLatest()
      },
      scoreboardUpdated: () => void refreshLatest(),
      onReconnected: () => void refreshLatest(),
    })
  })
  onUnmounted(() => {
    previewRequest++
    unwatch?.()
    releasePreview()
  })

  const viewBindings = {
    ArrowLeft,
    Download,
    FileSearch,
    MessageCircleQuestion,
    MinusCircle,
    RefreshCw,
    Scale,
    competitionId,
    management,
    review,
    loading,
    loadError,
    teamOptions,
    selectedTeamId,
    selected,
    canJudge,
    previewUrl,
    previewLoading,
    previewError,
    downloadPending,
    load,
    selectTeam,
    download,
    selectedChallengeId,
    adjustmentDelta,
    adjustmentPending,
    adjustmentRefreshing,
    adjustmentBusy,
    adjustmentError,
    submitAdjustment,
    clearAdjustmentError,
    selectChallenge,
    deduction,
    openDeduction,
    setDeductionOpen,
    confirmDeduction,
    consultationOpen,
    consultationChallengeId,
    consultationTitle,
    consultationBody,
    consultationPending,
    consultationError,
    openConsultation,
    submitConsultation,
    setConsultationOpen,
    clearConsultationError,
  }
  return viewBindings
}

export type CompetitionsByIdWriteUpsPageViewState = import('vue').ShallowUnwrapRef<
  ReturnType<typeof useCompetitionsByIdWriteUpsPage>
>
