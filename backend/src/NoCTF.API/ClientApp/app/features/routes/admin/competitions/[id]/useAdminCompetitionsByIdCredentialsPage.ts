import { message as describeMessage, translate } from '../../../../../utils/i18n'
import type { UiMessage } from '../../../../../utils/i18n'
import { toast } from '../../../../../utils/message-toast'
import { parseApiError } from '../../../../../utils/api-error'
import {
  adminDownloadCompetitionCredentialBatch,
  adminDownloadCompetitionCredentialBatchPdf,
  adminDownloadCompetitionCredentialTemplate,
  adminGetCompetitionCredentialBatch,
  adminListCompetitionCredentialBatches,
  adminImportCompetitionCredentials,
  adminRotateCompetitionCredentialBatch,
  adminRotateCompetitionCredentialUser,
} from '../../../../../api'
import type { NoCtfapiEndpointsAdministrationCompetitionsCredentialsGetCompetitionCredentialBatchResponse } from '../../../../../api'
import type { NoCtfapiEndpointsAdministrationCompetitionsCredentialsImportCompetitionCredentialsResponse } from '../../../../../api'
import type { NoCtfApplicationCompetitionsCredentialsCompetitionCredentialBatchSummary } from '../../../../../api'
import { useCompetitionAdmin } from '../../../../../lib/admin-competition'
import { downloadSdkFile } from '../../../../../utils/download'
import { credentialImportFeedback } from '../../../../admin/credentials/import-feedback'

export function useAdminCompetitionsByIdCredentialsPage() {
  const { competitionId, canJudge } = useCompetitionAdmin()
  const selectedFile = ref<File | null>(null)
  const importPending = ref(false)
  const importError = ref<UiMessage | null>(null)
  const batch = ref<NoCtfapiEndpointsAdministrationCompetitionsCredentialsGetCompetitionCredentialBatchResponse | null>(null)
  const batchId = ref('')
  const batches = ref<NoCtfApplicationCompetitionsCredentialsCompetitionCredentialBatchSummary[]>([])
  const loadPending = ref(false)
  const rotatePending = ref(false)
  const downloadPending = ref(false)
  const importIssues = ref<ReturnType<typeof credentialImportFeedback>['issues']>([])
  const confirmRotation = ref(false)
  const rotationUserId = ref<string | null>(null)
  const busy = computed(() => importPending.value || rotatePending.value || downloadPending.value)
  let disposed = false
  let batchRequest = 0
  const pagination = useOffsetPagination(async ({ offset, limit }) => ({
    items: (batch.value?.rows ?? []).slice(offset, offset + limit),
    total: batch.value?.rows?.length ?? 0,
  }), { initialPageSize: 25 })

  watch(batchId, () => {
    batchRequest += 1
    batch.value = null
    loadPending.value = false
    pagination.reset()
  }, { flush: 'sync' })
  onScopeDispose(() => { disposed = true; batchRequest += 1; batch.value = null })

  function chooseFile(event: Event): void {
    const input = event.target as HTMLInputElement
    selectedFile.value = input.files?.[0] ?? null
    importIssues.value = []
    importError.value = null
  }

  async function loadBatch(): Promise<void> {
    if (!batchId.value || !canJudge.value) return
    const requestedBatch = batchId.value
    const request = ++batchRequest
    loadPending.value = true
    importError.value = null
    try {
      const { data, error } = await adminGetCompetitionCredentialBatch({
        path: { competitionId, batchId: requestedBatch },
      })
      if (disposed || request !== batchRequest) return
      if (error || !data) throw error ?? new Error(translate('administration.credentials.loadFailed'))
      batch.value = data
      pagination.reset()
      await pagination.loadPage()
    }
    catch (error) {
      if (disposed || request !== batchRequest) return
      batch.value = null
      importError.value = parseApiError(error, describeMessage('administration.credentials.loadFailed')).displayMessage
    }
    finally {
      if (!disposed && request === batchRequest) loadPending.value = false
    }
  }

  async function loadBatches(): Promise<void> {
    if (!canJudge.value) return
    const { data, error } = await adminListCompetitionCredentialBatches({ path: { competitionId } })
    if (disposed) return
    if (error || !data) {
      importError.value = parseApiError(error, describeMessage('administration.credentials.loadFailed')).displayMessage
      return
    }
    batches.value = data.items ?? []
    if (!batchId.value && batches.value[0]?.batchId) {
      batchId.value = batches.value[0].batchId
      await loadBatch()
    }
  }

  async function importCredentials(): Promise<void> {
    if (!selectedFile.value || busy.value || !canJudge.value) return
    if (!/\.(csv|xlsx|xls)$/i.test(selectedFile.value.name) || selectedFile.value.size === 0 || selectedFile.value.size > 10_000_000) {
      importError.value = describeMessage('administration.credentials.invalidFileSelection')
      return
    }
    importPending.value = true
    importError.value = null
    importIssues.value = []
    try {
      const { data, error } = await adminImportCompetitionCredentials({
        path: { competitionId },
        body: { file: selectedFile.value },
      })
      if (disposed) return
      if (error && typeof error === 'object' && 'issues' in error && Array.isArray(error.issues)) {
        const feedback = credentialImportFeedback(error as NoCtfapiEndpointsAdministrationCompetitionsCredentialsImportCompetitionCredentialsResponse)
        importIssues.value = feedback.issues
        importError.value = feedback.error
        return
      }
      if (error || !data) throw error ?? new Error(translate('administration.credentials.importFailed'))
      if ((data.issues?.length ?? 0) > 0 || !data.batchId)
        throw new Error(data.issues?.map(issue => issue.message).join('；') || translate('administration.credentials.importFailed'))
      batchId.value = data.batchId
      await loadBatch()
      await loadBatches()
      toast.success(describeMessage('administration.credentials.imported', {
        users: data.createdUsers ?? 0,
        teams: data.createdTeams ?? 0,
      }))
    }
    catch (error) {
      importError.value = parseApiError(error, describeMessage('administration.credentials.importFailed')).displayMessage
    }
    finally {
      importPending.value = false
    }
  }

  async function downloadPackage(): Promise<void> {
    const id = batch.value?.batchId
    if (!id) return
    await download(() => downloadSdkFile(
      adminDownloadCompetitionCredentialBatch({ path: { competitionId, batchId: id }, parseAs: 'blob' }),
      `noctf-credentials-${id}.zip`,
    ))
  }

  async function downloadPdf(): Promise<void> {
    const id = batch.value?.batchId
    if (!id) return
    await download(() => downloadSdkFile(
      adminDownloadCompetitionCredentialBatchPdf({ path: { competitionId, batchId: id }, parseAs: 'blob' }),
      `noctf-credentials-${id}.pdf`,
    ))
  }

  async function downloadTemplate(): Promise<void> {
    await download(() => downloadSdkFile(
      adminDownloadCompetitionCredentialTemplate({ path: { competitionId }, parseAs: 'blob' }),
      'noctf-credential-import.csv',
    ))
  }

  async function download(action: () => Promise<void>): Promise<void> {
    if (busy.value || !canJudge.value) return
    downloadPending.value = true
    try { await action() }
    catch (error) { importError.value = parseApiError(error).displayMessage }
    finally { downloadPending.value = false }
  }

  function requestBatchRotation(): void {
    if (busy.value || !batch.value?.batchId) return
    rotationUserId.value = null
    confirmRotation.value = true
  }

  function requestUserRotation(userId?: string): void {
    if (!userId || busy.value) return
    rotationUserId.value = userId
    confirmRotation.value = true
  }

  async function applyRotation(): Promise<void> {
    confirmRotation.value = false
    if (rotationUserId.value) await rotateUser(rotationUserId.value)
    else await rotateBatch()
  }

  async function rotateBatch(): Promise<void> {
    const id = batch.value?.batchId
    if (!id || busy.value || !canJudge.value) return
    rotatePending.value = true
    try {
      const { error } = await adminRotateCompetitionCredentialBatch({ path: { competitionId, batchId: id } })
      if (error) throw error
      await loadBatch()
      await loadBatches()
      toast.success(describeMessage('administration.credentials.rotated'))
    }
    catch (error) {
      importError.value = parseApiError(error, describeMessage('administration.credentials.rotateFailed')).displayMessage
    }
    finally {
      rotatePending.value = false
    }
  }

  async function rotateUser(userId?: string): Promise<void> {
    if (!userId || busy.value || !canJudge.value) return
    rotatePending.value = true
    try {
      const { error } = await adminRotateCompetitionCredentialUser({ path: { competitionId, userId } })
      if (error) throw error
      await loadBatch()
      await loadBatches()
      toast.success(describeMessage('administration.credentials.rotated'))
    }
    catch (error) {
      importError.value = parseApiError(error, describeMessage('administration.credentials.rotateFailed')).displayMessage
    }
    finally {
      rotatePending.value = false
    }
  }

  onMounted(() => { void loadBatches() })

  return {
    canJudge,
    selectedFile,
    chooseFile,
    importPending,
    importError,
    importIssues,
    importCredentials,
    batch,
    batchId,
    batches,
    loadPending,
    loadBatch,
    loadBatches,
    rotatePending,
    requestBatchRotation,
    requestUserRotation,
    confirmRotation,
    applyRotation,
    busy,
    downloadPackage,
    downloadPdf,
    downloadTemplate,
    page: pagination.page,
    pageCount: pagination.pageCount,
    pageLimit: pagination.limit,
    total: pagination.total,
    rows: pagination.items,
    loadPage: pagination.loadPage,
    setPageSize: pagination.setPageSize,
  }
}

export type AdminCompetitionsByIdCredentialsPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminCompetitionsByIdCredentialsPage>>>
