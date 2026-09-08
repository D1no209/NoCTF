import { markRaw } from 'vue'

import { Download } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { adminExportPlatformAuditArchive, adminPlatformListAuditLogs } from '../../../../api'
import type { NoCtfapiEndpointsAdministrationPlatformPlatformAuditLogResponse, NoCtfapiEndpointsAdministrationPlatformPlatformAuditKindProtocol } from '../../../../api'
import { downloadSdkFile } from '../../../../utils/download'
import { platformAuditActionText } from '../../../../utils/platform-audit'
import AdminDateTimeComponent from '../../../admin/AdminDateTime.vue'

type AuditLog = NoCtfapiEndpointsAdministrationPlatformPlatformAuditLogResponse

/** Owns state, effects and commands for AdminPlatformAuditPage. */
export function useAdminPlatformAuditPage() {
  const KIND_LABELS: Record<string, string> = {
    CompetitionLifecycle: translate("ui.competitionLifeCycle"), UserAccountLifecycle: translate("ui.accountLifeCycle"), PlatformAdministration: translate("ui.platformAdmin"), CompetitionAdministration: translate("ui.competitionAdmin"), CompetitionLeaderboardVisibility: translate("ui.listVisibility"), CompetitionEvent: translate("ui.competitionEvent"),
  }

  const kind = ref('all')

  const actorId = ref('')

  const competitionId = ref('')

  const from = ref('')

  const to = ref('')

  function toIso(local: string): string | null {
    if (!local) return null
    const date = new Date(local)
    return Number.isNaN(date.getTime()) ? null : date.toISOString()
  }

  const { items, loading, error: listError, hasMore, initialized, loadMore, reset } = useCursorPagination<AuditLog>(async (cursor) => {
    const { data, error } = await adminPlatformListAuditLogs({
      query: {
        kind: kind.value === 'all'
          ? null
          : kind.value as NoCtfapiEndpointsAdministrationPlatformPlatformAuditKindProtocol,
        from: toIso(from.value),
        to: toIso(to.value),
        actorId: actorId.value.trim() || null,
        competitionId: competitionId.value.trim() || null,
        cursor,
        limit: 50,
      },
    })
    if (error || !data) throw parseApiError(error)
    return { items: data.items ?? [], nextCursor: data.nextCursor ?? null }
  })

  function applyFilters(): void {
    reset({ preserveItems: true })
    void loadMore()
  }

  const exportingArchive = ref(false)

  async function exportArchive(): Promise<void> {
    if (exportingArchive.value) return
    exportingArchive.value = true
    try {
      await downloadSdkFile(
        adminExportPlatformAuditArchive({
          body: {
            kind: kind.value === 'all'
              ? null
              : kind.value as NoCtfapiEndpointsAdministrationPlatformPlatformAuditKindProtocol,
            actorId: actorId.value.trim() || null,
            competitionId: competitionId.value.trim() || null,
            from: toIso(from.value),
            to: toIso(to.value),
          },
          parseAs: 'blob',
        }),
        'platform-audit-archive.zip',
      )
      toast.success(translate("ui.theAuditArchiveDownloadHasStarted"))
    }
    catch (e) {
      toast.error(parseApiError(e).message)
    }
    finally {
      exportingArchive.value = false
    }
  }

  onMounted(() => {
    void loadMore()
  })

  const AdminDateTime = markRaw(AdminDateTimeComponent)

  return {
      Download,
      platformAuditActionText,
      KIND_LABELS,
      kind,
      actorId,
      competitionId,
      from,
      to,
      items,
      loading,
      listError,
      hasMore,
      initialized,
      loadMore,
      applyFilters,
      exportingArchive,
      exportArchive,
      AdminDateTime
    }
}

export type AdminPlatformAuditPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminPlatformAuditPage>>>
