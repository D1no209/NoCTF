import { dateObject } from '../../../../../utils/date-value'

import { api, nativeResponse } from '../../../../../lib/api'
import { message as describeMessage } from '../../../../../utils/i18n'


import { toast } from '../../../../../utils/message-toast'

import { useCompetitionAdmin } from '../../../../../lib/admin-competition'
import { downloadSdkFile } from '../../../../../utils/download'
import { userFacingErrorMessage } from '../../../../../utils/api-error'

/** Owns state, effects and commands for AdminCompetitionsByIdExportsPage. */
export function useAdminCompetitionsByIdExportsPage() {
  const { competitionId, canWrite } = useCompetitionAdmin()

  const eventsFrom = ref('')

  const eventsTo = ref('')

  const exportingEvents = ref(false)

  async function exportEvents() {
    const from = localInputToIso(eventsFrom.value)
    const to = localInputToIso(eventsTo.value)
    if (!from || !to) {
      toast.error(describeMessage("administration.competitionsBy.description.selectExportTimeRange"))
      return
    }
    exportingEvents.value = true
    try {
      await downloadSdkFile(
        nativeResponse(responseOptions => api.api.v1.admin.competitions.byCompetitionId(competitionId).events.exportEscaped.get({ queryParameters: { from: dateObject(from ?? undefined), to: dateObject(to ?? undefined) }, options: [...responseOptions] })),
        `competition-${competitionId}-events.jsonl`,
      )
      toast.success(describeMessage("administration.competitionsBy.label.eventExportStartedDownloading"))
    }
    catch (e) {
      toast.error(userFacingErrorMessage(e instanceof Error ? e.message : null, translate("administration.error.exportFailed")))
    }
    finally {
      exportingEvents.value = false
    }
  }

  const includeProtectedFlags = ref(false)

  const exportReason = ref('')

  const exportingArchive = ref(false)

  async function exportArchive() {
    if (exportingArchive.value) return
    const reason = exportReason.value.trim()
    if (includeProtectedFlags.value && (reason.length < 8 || reason.length > 512)) {
      toast.error(describeMessage("administration.competitionsBy.description.enterExportReasonCharacters"))
      return
    }
    exportingArchive.value = true
    try {
      await downloadSdkFile(
        nativeResponse(responseOptions => api.api.v1.admin.competitions.byCompetitionId(competitionId).dataExport.post({
            includeProtectedFlags: includeProtectedFlags.value,
            reason: reason || null,
          }, { options: [...responseOptions] })),
        `competition-${competitionId}-archive.zip`,
      )
      toast.success(describeMessage("administration.competitionsBy.description.competitionArchiveDownloadStarted"))
      exportReason.value = ''
    }
    catch (e) {
      toast.error(parseApiError(e).displayMessage)
    }
    finally {
      exportingArchive.value = false
    }
  }

  return {
      canWrite,
      eventsFrom,
      eventsTo,
      exportingEvents,
      exportEvents,
      includeProtectedFlags,
      exportReason,
      exportingArchive,
      exportArchive
    }
}

export type AdminCompetitionsByIdExportsPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminCompetitionsByIdExportsPage>>>
