import { dateTimestamp } from '../../../../../utils/date-value'

import { ResponseMetadata, RequestPolicyOption, nativeResponse } from '../../../../../lib/api'

import { api, multipartBody, } from '../../../../../lib/api'
import { message as describeMessage } from '../../../../../utils/i18n'
import type { UiMessage } from '../../../../../utils/i18n'
import { markRaw } from 'vue'
import { useNow } from '@vueuse/core'
import { Download, Eye, FileText } from '@lucide/vue'
import { toast } from '../../../../../utils/message-toast'


import type { NoCTFAPIEndpointsTeamsWriteUpsTeamWriteUpResponse } from '../../../../../api/models'
import { downloadSdkFile, readProtectedDownload } from '../../../../../utils/download'
import CompetitionParticipantWorkspaceComponent from '../../../../competition/CompetitionParticipantWorkspace.vue'

const maximumWriteUpBytes = 64 * 1024 * 1024

/** Owns state, validation and authenticated PDF object URLs for the team WriteUp page. */
export function useCompetitionsByIdMyWriteUpPage() {
  const route = useRoute()
  const competitionId = route.params.id as string
  const ctx = inject(competitionContextKey)!
  const now = useNow({ interval: 1000 })

  const submissionRequired = computed(() =>
    ctx.competition.value?.writeUpSubmissionRequired === true,
  )
  const submissionDeadlineAt = computed(() =>
    ctx.competition.value?.writeUpSubmissionDeadlineAt ?? null,
  )
  const submissionClosed = computed(() => {
    if (!submissionDeadlineAt.value) return false
    const deadline = dateTimestamp(submissionDeadlineAt.value)
    return Number.isFinite(deadline) && now.value.getTime() > deadline
  })

  const writeUp = ref<NoCTFAPIEndpointsTeamsWriteUpsTeamWriteUpResponse | null>(null)
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
    let error: unknown;
    const response = new ResponseMetadata();
    const data = await api.api.v1.competitions.byCompetitionId(competitionId).teams.me.writeup.get({ options: [new RequestPolicyOption({ response: response })] }).catch(cause => { error = cause; return undefined });
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
      let error: unknown;
      const data = await api.api.v1.competitions.byCompetitionId(competitionId).teams.me.writeup.put(await multipartBody({ file: selectedFile.value })).catch(cause => { error = cause; return undefined });
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
        () => nativeResponse(responseOptions => api.api.v1.competitions.byCompetitionId(competitionId).teams.me.writeup.content.get({ options: [...responseOptions] })),
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
        nativeResponse(responseOptions => api.api.v1.competitions.byCompetitionId(competitionId).teams.me.writeup.content.get({ options: [...responseOptions] })),
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
