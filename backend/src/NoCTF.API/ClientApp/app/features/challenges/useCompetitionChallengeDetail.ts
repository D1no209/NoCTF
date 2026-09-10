import { markRaw, toRefs } from 'vue'

import { toast } from 'vue-sonner'
import { Dice5, FileDown, History } from '@lucide/vue'
import { downloadChallengeAttachmentEndpoint, downloadRandomChallengeAttachmentEndpoint, getChallengeEndpoint, listChallengeAttachmentsEndpoint } from '../../api'
import type { NoCtfapiEndpointsAdministrationChallengeBankAttachmentDeliveryPolicyProtocol, NoCtfapiEndpointsAdministrationChallengeBankChallengeAttachmentResponse, NoCtfapiEndpointsChallengesChallengeResponse } from '../../api'
import { downloadSdkFile } from '../../utils/download'
import ChallengeHintsComponent from './ChallengeHints.vue'
import ChallengeSubmissionHistoryComponent from './ChallengeSubmissionHistory.vue'
import AwdPanelComponent from './panels/AwdPanel.vue'
import AwdpPanelComponent from './panels/AwdpPanel.vue'
import CtfPanelComponent from './panels/CtfPanel.vue'
import KohPanelComponent from './panels/KohPanel.vue'

/** Owns state, effects and commands for CompetitionChallengeDetail. */
export function useCompetitionChallengeDetail(props: Readonly<{
  competitionId: string
  competitionChallengeId: string
  flagDockTarget?: string
}>) {
  const ctx = inject(competitionContextKey)!

  const { isLoggedIn, user } = useAuth()

  const challenge = ref<NoCtfapiEndpointsChallengesChallengeResponse | null>(null)

  const loading = ref(true)

  const error = ref<string | null>(null)

  const attachments = ref<NoCtfapiEndpointsAdministrationChallengeBankChallengeAttachmentResponse[]>([])

  const attachmentDeliveryPolicy = ref<NoCtfapiEndpointsAdministrationChallengeBankAttachmentDeliveryPolicyProtocol>('All')

  const attachmentsLoading = ref(false)

  const attachmentError = ref<string | null>(null)

  const downloading = ref(false)

  const historyRefreshKey = ref(0)
  const historyOpen = ref(false)

  let loadSequence = 0
  const reads = new AbortController()
  onBeforeUnmount(() => { loadSequence++; reads.abort() })

  function refreshSubmissionHistory(): void {
    historyRefreshKey.value += 1
  }

  function updateRemainingAttempts(remaining: number | null): void {
    if (challenge.value) challenge.value = { ...challenge.value, remainingFlagAttempts: remaining }
  }

  async function loadChallenge(): Promise<void> {
    const sequence = ++loadSequence
    loading.value = true
    error.value = null
    challenge.value = null
    attachments.value = []
    attachmentDeliveryPolicy.value = 'All'

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
      error.value = parseApiError(requestError, translate("ui.failedToLoadQuestion")).message
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
      attachmentError.value = parseApiError(requestError, translate("ui.failedToLoadChallengeAttachments")).message
      return
    }
    attachmentDeliveryPolicy.value = data.deliveryPolicy ?? 'All'
    attachments.value = (data.items ?? []).filter(attachment => !attachment.deletedAt)
  }

  watch(
    () => [props.competitionId, props.competitionChallengeId] as const,
    () => {
      void loadChallenge()
      void loadAttachments()
    },
    { immediate: true },
  )

  watch(isLoggedIn, () => void loadAttachments())

  watch(() => user.value?.userId, () => void loadChallenge())

  async function downloadAttachment(attachmentId: string, fileName: string): Promise<void> {
    downloading.value = true
    try {
      await downloadSdkFile(
        downloadChallengeAttachmentEndpoint({
          path: {
            competitionId: props.competitionId,
            competitionChallengeId: props.competitionChallengeId,
            attachmentId,
          },
          parseAs: 'blob',
        }),
        fileName,
      )
    }
    catch (downloadError) {
      toast.error(parseApiError(downloadError, translate("ui.attachmentDownloadFailed")).message)
    }
    finally {
      downloading.value = false
    }
  }

  async function downloadRandom(): Promise<void> {
    downloading.value = true
    try {
      await downloadSdkFile(
        downloadRandomChallengeAttachmentEndpoint({
          path: {
            competitionId: props.competitionId,
            competitionChallengeId: props.competitionChallengeId,
          },
          parseAs: 'blob',
        }),
        'attachment',
      )
    }
    catch (downloadError) {
      toast.error(parseApiError(downloadError, translate("ui.attachmentDownloadFailed")).message)
    }
    finally {
      downloading.value = false
    }
  }

  const mode = computed(() => ctx.competition.value?.mode)

  const ChallengeHints = markRaw(ChallengeHintsComponent)

  const ChallengeSubmissionHistory = markRaw(ChallengeSubmissionHistoryComponent)

  const AwdPanel = markRaw(AwdPanelComponent)

  const AwdpPanel = markRaw(AwdpPanelComponent)

  const CtfPanel = markRaw(CtfPanelComponent)

  const KohPanel = markRaw(KohPanelComponent)

  return {
      ...toRefs(props),
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
