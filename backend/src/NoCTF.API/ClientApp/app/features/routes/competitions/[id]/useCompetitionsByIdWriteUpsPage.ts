import { markRaw } from 'vue'
import { Download, FileSearch, MessageCircleQuestion, MinusCircle, RefreshCw, Scale } from '@lucide/vue'
import { toast } from 'vue-sonner'

import { adminCreateManualAdjustment, createTeamWriteUpConsultation, downloadTeamWriteUp, listTeamWriteUps } from '../../../../api'
import type { NoCtfapiEndpointsTeamsWriteUpsTeamWriteUpChallengeScoreResponse, NoCtfapiEndpointsTeamsWriteUpsTeamWriteUpReviewItemResponse, NoCtfapiEndpointsTeamsWriteUpsTeamWriteUpReviewResponse } from '../../../../api'
import { createTrailingRefresh } from '../../../../lib/latest-page-refresh'
import { downloadSdkFile, readProtectedDownload } from '../../../../utils/download'
import CompetitionParticipantWorkspaceComponent from '../../../competition/CompetitionParticipantWorkspace.vue'

type ReviewItem = NoCtfapiEndpointsTeamsWriteUpsTeamWriteUpReviewItemResponse
type ChallengeScore = NoCtfapiEndpointsTeamsWriteUpsTeamWriteUpChallengeScoreResponse

/** Owns staff WriteUp review, authenticated PDF preview, score decisions and consultation creation. */
export function useCompetitionsByIdWriteUpsPage() {
  const route = useRoute()
  const router = useRouter()
  const competitionId = route.params.id as string

  const review = ref<NoCtfapiEndpointsTeamsWriteUpsTeamWriteUpReviewResponse | null>(null)
  const loading = ref(true)
  const loadError = ref<string | null>(null)
  const selectedTeamId = ref(typeof route.query.team === 'string' ? route.query.team : '')
  const selected = computed<ReviewItem | null>(() =>
    review.value?.items?.find(item => item.writeUp?.teamId === selectedTeamId.value) ?? null,
  )
  const canJudge = computed(() => review.value?.canJudge === true)

  const previewUrl = ref<string | null>(null)
  const previewLoading = ref(false)
  const previewError = ref<string | null>(null)
  const previewFileId = ref<string | null>(null)
  const downloadPending = ref(false)
  let previewRequest = 0

  function releasePreview() {
    if (previewUrl.value) URL.revokeObjectURL(previewUrl.value)
    previewUrl.value = null
    previewFileId.value = null
  }

  async function load() {
    loading.value = review.value === null
    loadError.value = null
    const { data, error } = await listTeamWriteUps({ path: { competitionId } })
    loading.value = false
    if (error || !data) {
      loadError.value = parseApiError(error, translate('writeUp.reviewLoadFailed')).message
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

  async function selectTeam(teamId?: string) {
    if (!teamId) return
    selectedTeamId.value = teamId
    await router.replace({
      query: { ...route.query, team: teamId },
    })
    const item = review.value?.items?.find(candidate => candidate.writeUp?.teamId === teamId)
    if (!item?.writeUp?.fileId || previewFileId.value === item.writeUp.fileId) return
    const request = ++previewRequest
    releasePreview()
    previewLoading.value = true
    previewError.value = null
    try {
      const result = await readProtectedDownload(
        () => downloadTeamWriteUp({
          path: { competitionId, teamId },
          parseAs: 'blob',
        }),
        item.writeUp.fileName ?? `writeup-${teamId}.pdf`,
      )
      if (request !== previewRequest) return
      previewUrl.value = URL.createObjectURL(result.blob)
      previewFileId.value = item.writeUp.fileId
    }
    catch (error) {
      if (request === previewRequest)
        previewError.value = parseApiError(error, translate('writeUp.previewFailed')).message
    }
    finally {
      if (request === previewRequest) previewLoading.value = false
    }
  }

  async function download() {
    const writeUp = selected.value?.writeUp
    if (!writeUp?.teamId || downloadPending.value) return
    downloadPending.value = true
    try {
      await downloadSdkFile(
        downloadTeamWriteUp({
          path: { competitionId, teamId: writeUp.teamId },
          parseAs: 'blob',
        }),
        writeUp.fileName ?? `writeup-${writeUp.teamId}.pdf`,
      )
    }
    catch (error) {
      toast.error(parseApiError(error, translate('writeUp.downloadFailed')).message)
    }
    finally {
      downloadPending.value = false
    }
  }

  const selectedChallengeId = ref('')
  const adjustmentDelta = ref(0)
  const adjustmentPending = ref(false)
  const adjustmentError = ref<string | null>(null)

  watch(selected, item => {
    const available = item?.challengeScores?.some(score =>
      score.competitionChallengeId === selectedChallengeId.value)
    if (!available)
      selectedChallengeId.value = item?.challengeScores?.[0]?.competitionChallengeId ?? ''
    adjustmentDelta.value = 0
    adjustmentError.value = null
  }, { immediate: true })

  async function adjustScore(delta: number) {
    const writeUp = selected.value?.writeUp
    if (!canJudge.value || !writeUp?.teamId || !selectedChallengeId.value || delta === 0)
      return
    if (!Number.isInteger(delta) || delta < -2147483648 || delta > 2147483647) {
      adjustmentError.value = translate('writeUp.invalidAdjustment')
      return
    }
    adjustmentPending.value = true
    adjustmentError.value = null
    try {
      const { error } = await adminCreateManualAdjustment({
        path: { competitionId },
        body: {
          teamId: writeUp.teamId,
          competitionChallengeId: selectedChallengeId.value,
          delta,
        },
      })
      if (error) throw error
      adjustmentDelta.value = 0
      toast.success(translate('writeUp.adjustmentAccepted'))
    }
    catch (error) {
      adjustmentError.value = parseApiError(error, translate('writeUp.adjustmentFailed')).message
      toast.error(adjustmentError.value)
    }
    finally {
      adjustmentPending.value = false
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
    selectedChallengeId.value = score.competitionChallengeId
    deduction.value = score
  }

  function setDeductionOpen(open: boolean) {
    if (!open && !adjustmentPending.value) deduction.value = null
  }

  async function confirmDeduction() {
    const points = deduction.value?.netPoints ?? 0
    if (points <= 0 || points > 2147483647) return
    deduction.value = null
    await adjustScore(-points)
  }

  const consultationOpen = ref(false)
  const consultationChallengeId = ref('none')
  const consultationTitle = ref('')
  const consultationBody = ref('')
  const consultationPending = ref(false)
  const consultationError = ref<string | null>(null)

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
      const { data, error } = await createTeamWriteUpConsultation({
        path: { competitionId, teamId: writeUp.teamId },
        body: {
          competitionChallengeId: consultationChallengeId.value === 'none'
            ? null
            : consultationChallengeId.value,
          title,
          body,
        },
      })
      if (error || !data?.threadRootId) throw error
      consultationOpen.value = false
      toast.success(translate('writeUp.consultationCreated'))
      await router.push({
        path: `/competitions/${competitionId}/questions`,
        query: { question: data.threadRootId },
      })
    }
    catch (error) {
      consultationError.value = parseApiError(error, translate('writeUp.consultationFailed')).message
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

  const refreshLatest = createTrailingRefresh(load)
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

  const CompetitionParticipantWorkspace = markRaw(CompetitionParticipantWorkspaceComponent)
  const viewBindings = {
    Download,
    FileSearch,
    MessageCircleQuestion,
    MinusCircle,
    RefreshCw,
    Scale,
    competitionId,
    review,
    loading,
    loadError,
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
    CompetitionParticipantWorkspace,
  }
  return viewBindings
}

export type CompetitionsByIdWriteUpsPageViewState = import('vue').ShallowUnwrapRef<
  ReturnType<typeof useCompetitionsByIdWriteUpsPage>
>
