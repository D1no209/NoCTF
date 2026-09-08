

import { toast } from 'vue-sonner'
import { adminExportCompetitionArchive, adminExportCompetitionEvents } from '../../../../../api'
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
      toast.error(translate("ui.pleaseSelectTheExportTimeRange"))
      return
    }
    exportingEvents.value = true
    try {
      await downloadSdkFile(
        adminExportCompetitionEvents({
          path: { competitionId },
          query: { from, to },
          parseAs: 'blob',
        }),
        `competition-${competitionId}-events.jsonl`,
      )
      toast.success(translate("ui.eventExportHasStartedDownloading"))
    }
    catch (e) {
      toast.error(userFacingErrorMessage(e instanceof Error ? e.message : null, translate("ui.exportFailed")))
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
      toast.error(translate("ui.enterAnExportReasonOf8512CharactersWhenIncluding"))
      return
    }
    exportingArchive.value = true
    try {
      await downloadSdkFile(
        adminExportCompetitionArchive({
          path: { competitionId },
          body: {
            includeProtectedFlags: includeProtectedFlags.value,
            reason: reason || null,
          },
          parseAs: 'blob',
        }),
        `competition-${competitionId}-archive.zip`,
      )
      toast.success(translate("ui.theCompetitionArchiveDownloadHasStarted"))
      exportReason.value = ''
    }
    catch (e) {
      toast.error(parseApiError(e).message)
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
