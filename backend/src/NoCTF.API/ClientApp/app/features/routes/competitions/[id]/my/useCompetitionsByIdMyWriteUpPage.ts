import { message as describeMessage } from '../../../../../utils/i18n'
import type { UiMessage } from '../../../../../utils/i18n'
import { markRaw } from 'vue'
import { useNow } from '@vueuse/core'
import { Download, Eye, FileText } from '@lucide/vue'
import { toast } from '../../../../../utils/message-toast'

import { downloadMyTeamWriteUp, getMyTeamWriteUp, replaceMyTeamWriteUp } from '../../../../../api'
import type { NoCtfapiEndpointsTeamsWriteUpsTeamWriteUpResponse } from '../../../../../api'
import { downloadSdkFile, readProtectedDownload } from '../../../../../utils/download'
import MySingleWriteUpsComponent from '~/features/writeups/MySingleWriteUps.vue'
import CompetitionParticipantWorkspaceComponent from '../../../../competition/CompetitionParticipantWorkspace.vue'

const maximumWriteUpBytes = 64 * 1024 * 1024

/** Owns state, validation and authenticated PDF object URLs for the team WriteUp page. */
export function useCompetitionsByIdMyWriteUpPage() {
  const route = useRoute()
  const competitionId = route.params.id as string
  const ctx = inject(competitionContextKey)!
  const activeTab = ref(ctx.competition.value?.singleWriteUpsEnabled ? 'single' : 'whole')
  const singleEnabled = computed(() => ctx.competition.value?.singleWriteUpsEnabled === true)
  watch(singleEnabled, enabled => { if (!enabled) activeTab.value = 'whole' })
  const MySingleWriteUps = markRaw(MySingleWriteUpsComponent)
  const now = useNow({ interval: 1000 })

  const submissionRequired = computed(() =>
    ctx.competition.value?.writeUpSubmissionRequired === true,
  )
  const submissionDeadlineAt = computed(() =>
    ctx.competition.value?.writeUpSubmissionDeadlineAt ?? null,
  )
  const submissionClosed = computed(() => {
    if (!submissionDeadlineAt.value) return false
    const deadline = Date.parse(submissionDeadlineAt.value)
    return Number.isFinite(deadline) && now.value.getTime() > deadline
  })

  const writeUp = ref<NoCtfapiEndpointsTeamsWriteUpsTeamWriteUpResponse | null>(null)
  const selectedFile = ref<File | null>(null)
  const uploadInputKey = ref(0)
  const loading = ref(true)
  const loadError = ref<UiMessage | null>(null)
  const uploadError = ref<UiMessage | null>(null)
  const uploadPending = ref(false)
  const previewUrl = ref<string | null>(null)
  const previewLoading = ref(false)
  const previewError = ref<UiMessage | null>(null)
  const downloadPending = ref(false)
  let selectionRequest = 0
  let previewRequest = 0

  function releasePreview() {
    if (previewUrl.value) URL.revokeObjectURL(previewUrl.value)
    previewUrl.value = null
  }

  async function load() {
    loading.value = true
    loadError.value = null
    const { data, error, response } = await getMyTeamWriteUp({
      path: { competitionId },
    })
    loading.value = false
    if (response?.status === 404) {
      writeUp.value = null
      return
    }
    if (error || !data) {
      loadError.value = parseApiError(error, describeMessage('writeUp.loadFailed')).displayMessage
      return
    }
    writeUp.value = data
  }

  async function selectFile(event: Event) {
    const request = ++selectionRequest
    const file = (event.target as HTMLInputElement).files?.[0] ?? null
    selectedFile.value = file
    uploadError.value = null
    if (!file) return
    if (!file.name.toLowerCase().endsWith('.pdf') || file.type !== 'application/pdf') {
      uploadError.value = describeMessage('writeUp.pdfOnly')
      selectedFile.value = null
      return
    }
    if (file.size > maximumWriteUpBytes) {
      uploadError.value = describeMessage('writeUp.tooLarge', { size: formatBytes(maximumWriteUpBytes) })
      selectedFile.value = null
      return
    }
    const header = new Uint8Array(await file.slice(0, 5).arrayBuffer())
    if (request !== selectionRequest) return
    if (new TextDecoder('ascii').decode(header) !== '%PDF-') {
      uploadError.value = describeMessage('writeUp.invalidPdf')
      selectedFile.value = null
    }
  }

  async function submit() {
    if (submissionClosed.value || !selectedFile.value || uploadPending.value) return
    uploadPending.value = true
    uploadError.value = null
    try {
      const { data, error } = await replaceMyTeamWriteUp({
        path: { competitionId },
        body: { file: selectedFile.value },
      })
      if (error || !data) throw error
      writeUp.value = data
      selectedFile.value = null
      selectionRequest++
      uploadInputKey.value++
      previewRequest++
      releasePreview()
      toast.success(describeMessage('writeUp.uploaded'))
    }
    catch (error) {
      uploadError.value = parseApiError(error, describeMessage('writeUp.uploadFailed')).displayMessage
      toast.error(uploadError.value)
    }
    finally {
      uploadPending.value = false
    }
  }

  async function preview() {
    if (!writeUp.value || previewLoading.value) return
    const request = ++previewRequest
    previewLoading.value = true
    previewError.value = null
    try {
      const result = await readProtectedDownload(
        () => downloadMyTeamWriteUp({
          path: { competitionId },
          parseAs: 'blob',
        }),
        writeUp.value.fileName ?? 'writeup.pdf',
      )
      if (request !== previewRequest) return
      releasePreview()
      previewUrl.value = URL.createObjectURL(result.blob)
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
    if (!writeUp.value || downloadPending.value) return
    downloadPending.value = true
    try {
      await downloadSdkFile(
        downloadMyTeamWriteUp({
          path: { competitionId },
          parseAs: 'blob',
        }),
        writeUp.value.fileName ?? 'writeup.pdf',
      )
    }
    catch (error) {
      toast.error(parseApiError(error, describeMessage('writeUp.downloadFailed')).displayMessage)
    }
    finally {
      downloadPending.value = false
    }
  }

  onMounted(() => void load())
  onUnmounted(() => {
    previewRequest++
    releasePreview()
  })

  const CompetitionParticipantWorkspace = markRaw(CompetitionParticipantWorkspaceComponent)
  const viewBindings = {
    activeTab, singleEnabled, MySingleWriteUps,
    Download,
    Eye,
    FileText,
    maximumWriteUpBytes,
    competitionId,
    submissionRequired,
    submissionDeadlineAt,
    submissionClosed,
    writeUp,
    selectedFile,
    uploadInputKey,
    loading,
    loadError,
    uploadError,
    uploadPending,
    previewUrl,
    previewLoading,
    previewError,
    downloadPending,
    load,
    selectFile,
    submit,
    preview,
    download,
    CompetitionParticipantWorkspace,
  }
  return viewBindings
}

export type CompetitionsByIdMyWriteUpPageViewState = import('vue').ShallowUnwrapRef<
  ReturnType<typeof useCompetitionsByIdMyWriteUpPage>
>
