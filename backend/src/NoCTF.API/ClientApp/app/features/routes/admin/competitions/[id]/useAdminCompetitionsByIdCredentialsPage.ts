import { message as describeMessage, translate } from '../../../../../utils/i18n'
import type { UiMessage } from '../../../../../utils/i18n'
import { toast } from '../../../../../utils/message-toast'
import { parseApiError } from '../../../../../utils/api-error'
import {
  adminDownloadCompetitionCredentialBatch,
  adminDownloadCompetitionCredentialBatchPdf,
  adminGetCompetitionCredentialBatch,
  adminImportCompetitionCredentials,
  adminRotateCompetitionCredentialBatch,
  adminRotateCompetitionCredentialUser,
} from '../../../../../api'
import type { NoCtfapiEndpointsAdministrationCompetitionsCredentialsGetCompetitionCredentialBatchResponse } from '../../../../../api'
import { useCompetitionAdmin } from '../../../../../lib/admin-competition'
import { downloadSdkFile } from '../../../../../utils/download'

export function useAdminCompetitionsByIdCredentialsPage() {
  const { competitionId, canJudge } = useCompetitionAdmin()
  const selectedFile = ref<File | null>(null)
  const importPending = ref(false)
  const importError = ref<UiMessage | null>(null)
  const batch = ref<NoCtfapiEndpointsAdministrationCompetitionsCredentialsGetCompetitionCredentialBatchResponse | null>(null)
  const batchId = ref('')
  const loadPending = ref(false)
  const rotatePending = ref(false)

  function chooseFile(event: Event): void {
    const input = event.target as HTMLInputElement
    selectedFile.value = input.files?.[0] ?? null
  }

  async function loadBatch(): Promise<void> {
    if (!batchId.value) return
    loadPending.value = true
    importError.value = null
    try {
      const { data, error } = await adminGetCompetitionCredentialBatch({
        path: { competitionId, batchId: batchId.value },
      })
      if (error || !data) throw error ?? new Error(translate('administration.credentials.loadFailed'))
      batch.value = data
    }
    catch (error) {
      batch.value = null
      importError.value = parseApiError(error, describeMessage('administration.credentials.loadFailed')).displayMessage
    }
    finally {
      loadPending.value = false
    }
  }

  async function importCredentials(): Promise<void> {
    if (!selectedFile.value || importPending.value) return
    importPending.value = true
    importError.value = null
    try {
      const { data, error } = await adminImportCompetitionCredentials({
        path: { competitionId },
        body: { file: selectedFile.value },
      })
      if (error || !data) throw error ?? new Error(translate('administration.credentials.importFailed'))
      if ((data.issues?.length ?? 0) > 0 || !data.batchId)
        throw new Error(data.issues?.map(issue => issue.message).join('；') || translate('administration.credentials.importFailed'))
      batchId.value = data.batchId
      await loadBatch()
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
    if (!batchId.value) return
    await downloadSdkFile(
      adminDownloadCompetitionCredentialBatch({ path: { competitionId, batchId: batchId.value }, parseAs: 'blob' }),
      `noctf-credentials-${batchId.value}.zip`,
    )
  }

  async function downloadPdf(): Promise<void> {
    if (!batchId.value) return
    await downloadSdkFile(
      adminDownloadCompetitionCredentialBatchPdf({ path: { competitionId, batchId: batchId.value }, parseAs: 'blob' }),
      `noctf-credentials-${batchId.value}.pdf`,
    )
  }

  function printPage(): void {
    window.print()
  }

  async function rotateBatch(): Promise<void> {
    if (!batchId.value || rotatePending.value) return
    rotatePending.value = true
    try {
      const { error } = await adminRotateCompetitionCredentialBatch({ path: { competitionId, batchId: batchId.value } })
      if (error) throw error
      await loadBatch()
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
    if (!userId || rotatePending.value) return
    rotatePending.value = true
    try {
      const { error } = await adminRotateCompetitionCredentialUser({ path: { competitionId, userId } })
      if (error) throw error
      await loadBatch()
      toast.success(describeMessage('administration.credentials.rotated'))
    }
    catch (error) {
      importError.value = parseApiError(error, describeMessage('administration.credentials.rotateFailed')).displayMessage
    }
    finally {
      rotatePending.value = false
    }
  }

  return {
    canJudge,
    selectedFile,
    chooseFile,
    importPending,
    importError,
    importCredentials,
    batch,
    batchId,
    loadPending,
    loadBatch,
    rotatePending,
    rotateBatch,
    rotateUser,
    downloadPackage,
    downloadPdf,
    printPage,
  }
}

export type AdminCompetitionsByIdCredentialsPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminCompetitionsByIdCredentialsPage>>>
