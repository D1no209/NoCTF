import { markRaw } from 'vue'

import { Download, Radio } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { adminPlatformExportLogs, adminPlatformListLogs } from '../../../../api'
import { downloadSdkFile } from '../../../../utils/download'
import type { NoCtfapiEndpointsAdministrationPlatformPlatformLogResponse, NoCtfapiEndpointsAdministrationPlatformPlatformLogLevelProtocol, NoCtfapiEndpointsAdministrationPlatformPlatformLogServiceProtocol } from '../../../../api'
import AdminDateTimeComponent from '../../../admin/AdminDateTime.vue'

type PlatformLog = NoCtfapiEndpointsAdministrationPlatformPlatformLogResponse

/** Owns state, effects and commands for AdminPlatformLogsPage. */
export function useAdminPlatformLogsPage() {
  const LEVEL_LABELS: Record<string, string> = {
    Trace: translate("ui.track"), Debug: translate("ui.debugging"), Information: translate("ui.information"), Warning: translate("ui.warning"), Error: translate("ui.wrong"), Critical: translate("ui.serious"),
  }

  const SERVICE_LABELS: Record<string, string> = {
    Api: 'API', Worker: 'Worker', Runner: 'Runner', Host: 'Host',
  }

  const LEVEL_ORDER = ['Trace', 'Debug', 'Information', 'Warning', 'Error', 'Critical'] as const

  const levelOrdinal = (level?: string | number) => typeof level === 'number' ? level : LEVEL_ORDER.indexOf(level as typeof LEVEL_ORDER[number])

  const LIVE_LIMIT = 200

  const minimumLevel = ref<NoCtfapiEndpointsAdministrationPlatformPlatformLogLevelProtocol>('Warning')

  const service = ref<'all' | NoCtfapiEndpointsAdministrationPlatformPlatformLogServiceProtocol>('all')

  const search = ref('')

  const from = ref('')

  const to = ref('')

  const exporting = ref(false)

  function toIso(local: string): string | null {
    if (!local) return null
    const date = new Date(local)
    return Number.isNaN(date.getTime()) ? null : date.toISOString()
  }

  const { items, loading, error: listError, hasMore, initialized, loadMore, reset } = useCursorPagination<PlatformLog>(async (cursor) => {
    const { data, error } = await adminPlatformListLogs({
      query: {
        minimumLevel: minimumLevel.value,
        service: service.value === 'all'
          ? null
          : service.value as NoCtfapiEndpointsAdministrationPlatformPlatformLogServiceProtocol,
        from: toIso(from.value),
        to: toIso(to.value),
        search: search.value.trim() || null,
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

  const live = ref(true)

  const { state: hubState, start, stop } = usePlatformLogHub((log) => {
    if (!matchesLiveFilters(log)) return
    if (items.value.some(existing => existing.cursor === log.cursor)) return
    items.value.unshift(log)
    if (items.value.length > LIVE_LIMIT) items.value.length = LIVE_LIMIT
  })

  function matchesLiveFilters(log: PlatformLog): boolean {
    if (levelOrdinal(log.level) < levelOrdinal(minimumLevel.value)) return false
    if (service.value !== 'all' && log.service !== service.value) return false
    const keyword = search.value.trim().toLowerCase()
    if (keyword) {
      const haystack = `${log.message ?? ''} ${log.category ?? ''}`.toLowerCase()
      if (!haystack.includes(keyword)) return false
    }
    return true
  }

  watch(live, (enabled) => {
    if (enabled) void start()
    else void stop()
  })

  const hubStateBadge = computed(() => {
    switch (hubState.value) {
      case 'connected':
        return { label: translate("ui.liveStreamIsConnected"), variant: 'secondary' as const }
      case 'connecting':
        return { label: translate("ui.liveStreamingConnection"), variant: 'outline' as const }
      case 'reconnecting':
        return { label: translate("ui.liveStreamReconnecting"), variant: 'outline' as const }
      default:
        return { label: translate("ui.liveStreamDisconnected"), variant: 'destructive' as const }
    }
  })

  async function exportLogs(): Promise<void> {
    const fromIso = toIso(from.value)
    const toIsoValue = toIso(to.value)
    if (!fromIso || !toIsoValue) {
      toast.error(translate("ui.exportNeedsToSelectTheStartAndEndTime"))
      return
    }
    exporting.value = true
    try {
      await downloadSdkFile(
        adminPlatformExportLogs({
          query: {
            minimumLevel: minimumLevel.value,
            service: service.value === 'all' ? null : service.value,
            from: fromIso,
            to: toIsoValue,
            search: search.value.trim() || null,
          },
          parseAs: 'blob',
        }),
        'platform-logs.jsonl',
      )
      toast.success(translate("ui.logExportDownloadHasStarted"))
    }
    catch (e) {
      toast.error(parseApiError(e).message)
    }
    finally {
      exporting.value = false
    }
  }

  onMounted(() => {
    void loadMore()
    void start()
  })

  const AdminDateTime = markRaw(AdminDateTimeComponent)

  return {
      Download,
      Radio,
      LEVEL_LABELS,
      SERVICE_LABELS,
      levelOrdinal,
      minimumLevel,
      service,
      search,
      from,
      to,
      exporting,
      items,
      loading,
      listError,
      hasMore,
      initialized,
      loadMore,
      applyFilters,
      live,
      start,
      hubStateBadge,
      exportLogs,
      AdminDateTime
    }
}

export type AdminPlatformLogsPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminPlatformLogsPage>>>
