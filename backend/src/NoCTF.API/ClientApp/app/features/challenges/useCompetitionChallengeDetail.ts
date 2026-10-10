import { adminFormatDateTime } from '~/utils/admin-format'
import { message as describeMessage } from '../../utils/i18n'
import type { UiMessage } from '../../utils/i18n'
import { markRaw, provide, toRefs } from 'vue'

import { toast } from '../../utils/message-toast'
import { Dice5, FileDown, History, BookOpen } from '@lucide/vue'
import { prepareChallengeAttachmentDownloadEndpoint, prepareRandomChallengeAttachmentDownloadEndpoint, getChallengeEndpoint, listChallengeAttachmentsEndpoint, startProgressionChallenge } from '../../api'
import type { NoCtfapiEndpointsAdministrationChallengeBankAttachmentDeliveryPolicyProtocol, NoCtfapiEndpointsAdministrationChallengeBankChallengeAttachmentResponse, NoCtfapiEndpointsChallengesChallengeResponse } from '../../api'
import { startAttachmentBrowserDownload } from '../../utils/download'
import ChallengeHintsComponent from './ChallengeHints.vue'
import ChallengeSubmissionHistoryComponent from './ChallengeSubmissionHistory.vue'
import AwdPanelComponent from './panels/AwdPanel.vue'
import AwdpPanelComponent from './panels/AwdpPanel.vue'
import CtfPanelComponent from './panels/CtfPanel.vue'
import KohPanelComponent from './panels/KohPanel.vue'
import { challengeGameplayFactStatusKey, createChallengeGameplayFactStatusReader } from './challenge-gameplay-fact-status'

/** Owns state, effects and commands for CompetitionChallengeDetail. */
export function useCompetitionChallengeDetail(props: Readonly<{
  competitionId: string
  competitionChallengeId: string
  flagDockTarget?: string
}>) {
  const ctx = inject(competitionContextKey)!

  const { isLoggedIn, ready: authReady, user } = useAuth()

  const challenge = ref<NoCtfapiEndpointsChallengesChallengeResponse | null>(null)

  const loading = ref(true)

  const error = ref<UiMessage | null>(null)

  const attachments = ref<NoCtfapiEndpointsAdministrationChallengeBankChallengeAttachmentResponse[]>([])

  const attachmentDeliveryPolicy = ref<NoCtfapiEndpointsAdministrationChallengeBankAttachmentDeliveryPolicyProtocol>('All')

  const attachmentsLoading = ref(false)

  const attachmentError = ref<UiMessage | null>(null)

  const downloading = ref(false)

  const historyRefreshKey = ref(0)
  const historyOpen = ref(false)

  let loadSequence = 0
  const reads = new AbortController()
  provide(challengeGameplayFactStatusKey, createChallengeGameplayFactStatusReader(reads.signal))
  onBeforeUnmount(() => { loadSequence++; reads.abort() })

  function refreshSubmissionHistory(): void {
    historyRefreshKey.value += 1
  }

  function updateRemainingAttempts(remaining: number | null): void {
    if (!challenge.value) return
    challenge.value = challenge.value.interactionKind === 'PatchVerification'
      ? { ...challenge.value, remainingPatchAttempts: remaining }
      : { ...challenge.value, remainingFlagAttempts: remaining }
  }

  async function loadChallenge(): Promise<void> {
    const sequence = ++loadSequence
    loading.value = true
    error.value = null
    challenge.value = null
    attachments.value = []
    attachmentDeliveryPolicy.value = 'All'

    if (isLoggedIn.value && ctx.competition.value?.mode === 'Ctf') {
      const entry = useState<{ competitionId: string, challengeId: string, revision: number } | null>(
        'progression-entry', () => null)
      const expectedRevision = entry.value?.competitionId === props.competitionId
        && entry.value.challengeId === props.competitionChallengeId
        ? entry.value.revision : undefined
      entry.value = null
      const started = await startProgressionChallenge({
        signal: reads.signal,
        path: {
          competitionId: props.competitionId,
          competitionChallengeId: props.competitionChallengeId,
        },
        body: { expectedRevision },
      })
      if (sequence !== loadSequence) return
      if (started.error) {
        loading.value = false
        if (started.response?.status === 409 || started.response?.status === 404) {
          await navigateTo({ path: `/competitions/${props.competitionId}/progression`,
            query: { blocked: props.competitionChallengeId } })
          return
        }
        error.value = parseApiError(started.error, describeMessage('progression.startFailed')).displayMessage
        return
      }
    }

    const { data, error: requestError } = await getChallengeEndpoint({
      signal: reads.signal,
      path: {
        competitionId: props.competitionId,
        competitionChallengeId: props.competitionChallengeId,
      },
    })
    if (sequence !== loadSequence) return
    loading.value = false
    if (requestError || !data) {
      error.value = parseApiError(requestError, describeMessage("challenges.error.loadQuestionFailed")).displayMessage
      return
    }
    challenge.value = data
  }

  async function loadAttachments(): Promise<void> {
    if (!isLoggedIn.value) {
      attachments.value = []
      attachmentDeliveryPolicy.value = 'All'
      attachmentsLoading.value = false
      attachmentError.value = null
      return
    }
    const challengeId = props.competitionChallengeId
    attachmentsLoading.value = true
    attachmentError.value = null
    const { data, error: requestError } = await listChallengeAttachmentsEndpoint({
      signal: reads.signal,
      path: {
        competitionId: props.competitionId,
        competitionChallengeId: challengeId,
      },
    })
    if (reads.signal.aborted || challengeId !== props.competitionChallengeId) return
    attachmentsLoading.value = false
    if (requestError || !data) {
      attachmentError.value = parseApiError(requestError, describeMessage("challenges.competitionChallenge.error.loadChallengeAttachmentsFailed")).displayMessage
      return
    }
    attachmentDeliveryPolicy.value = data.deliveryPolicy ?? 'All'
    attachments.value = (data.items ?? []).filter(attachment => !attachment.deletedAt)
  }

  watch(
    () => [props.competitionId, props.competitionChallengeId, ctx.competition.value?.mode,
      user.value?.userId, authReady.value] as const,
    () => {
      if (!ctx.competition.value?.mode || !authReady.value) return
      void loadChallenge()
      void loadAttachments()
    },
    { immediate: true },
  )

  watch(isLoggedIn, () => void loadAttachments())

  watch(() => ctx.competition.value?.status, (status, previousStatus) => {
    if (previousStatus && status !== previousStatus)
      void loadChallenge()
  })

  async function downloadAttachment(attachmentId: string, _fileName: string): Promise<void> {
    if (downloading.value) return
    downloading.value = true
    try {
      const result = await prepareChallengeAttachmentDownloadEndpoint({
        path: { competitionId: props.competitionId, competitionChallengeId: props.competitionChallengeId, attachmentId },
        signal: reads.signal,
      })
      if (result.error || !result.data?.downloadUrl) throw result.error
      startAttachmentBrowserDownload(result.data.downloadUrl)
    }
    catch (downloadError) {
      toast.error(parseApiError(downloadError, describeMessage("challenges.error.attachmentDownloadFailed")).displayMessage)
    }
    finally {
      downloading.value = false
    }
  }

  async function downloadRandom(): Promise<void> {
    if (downloading.value) return
    downloading.value = true
    try {
      const result = await prepareRandomChallengeAttachmentDownloadEndpoint({
        path: { competitionId: props.competitionId, competitionChallengeId: props.competitionChallengeId },
        signal: reads.signal,
      })
      if (result.error || !result.data?.downloadUrl) throw result.error
      startAttachmentBrowserDownload(result.data.downloadUrl)
    }
    catch (downloadError) {
      toast.error(parseApiError(downloadError, describeMessage("challenges.error.attachmentDownloadFailed")).displayMessage)
    }
    finally {
      downloading.value = false
    }
  }

  const route = useRoute(), router = useRouter()
  const singleWriteUpsEnabled = computed(() => ctx.competition.value?.singleWriteUpsEnabled === true)
  const writeUpBenefit = computed(() => ctx.standing.value?.challengeBenefits?.find(x => x.competitionChallengeId === props.competitionChallengeId
    && x.writeUpUnlockedAt != null) ?? null)
  function openWriteUps() { void router.push({ path: `/competitions/${props.competitionId}/challenge-writeups/${props.competitionChallengeId}`, query: route.query }) }

  const mode = computed(() => ctx.competition.value?.mode)

  const ChallengeHints = markRaw(ChallengeHintsComponent)

  const ChallengeSubmissionHistory = markRaw(ChallengeSubmissionHistoryComponent)

  const AwdPanel = markRaw(AwdPanelComponent)

  const AwdpPanel = markRaw(AwdpPanelComponent)

  const CtfPanel = markRaw(CtfPanelComponent)

  const KohPanel = markRaw(KohPanelComponent)

  return {
      ...toRefs(props),
      formatTimingDate: adminFormatDateTime,
      BookOpen, singleWriteUpsEnabled, openWriteUps, writeUpBenefit,
      Dice5,
      FileDown,
      History,
      ctx,
      isLoggedIn,
      user,
      challenge,
      loading,
      error,
      attachments,
      attachmentDeliveryPolicy,
      attachmentsLoading,
      attachmentError,
      downloading,
      historyRefreshKey,
      historyOpen,
      refreshSubmissionHistory,
      updateRemainingAttempts,
      loadAttachments,
      downloadAttachment,
      downloadRandom,
      mode,
      ChallengeHints,
      ChallengeSubmissionHistory,
      AwdPanel,
      AwdpPanel,
      CtfPanel,
      KohPanel
    }
}

export type CompetitionChallengeDetailViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useCompetitionChallengeDetail>>>
