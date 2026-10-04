import { dateObject } from '../../../../utils/date-value'

import { api, nativeResponse } from '../../../../lib/api'
import { message as describeMessage } from '../../../../utils/i18n'
import { markRaw } from 'vue'

import { Download, Radio } from '@lucide/vue'
import { toast } from '../../../../utils/message-toast'

import { downloadSdkFile } from '../../../../utils/download'
import type { NoCTFAPIEndpointsAdministrationPlatformPlatformLogResponse, NoCTFAPIEndpointsAdministrationPlatformPlatformLogLevelProtocol, NoCTFAPIEndpointsAdministrationPlatformPlatformLogServiceProtocol } from '../../../../api/models'
import AdminDateTimeComponent from '../../../admin/AdminDateTime.vue'

type PlatformLog = NoCTFAPIEndpointsAdministrationPlatformPlatformLogResponse

/** Owns state, effects and commands for AdminPlatformLogsPage. */
export function useAdminPlatformLogsPage() {
  const LEVEL_LABELS: Record<string, string> = {
    Trace: "administration.label.track", Debug: "administration.label.debugging", Information: "common.label.information", Warning: "common.label.warning", Error: "common.label.wrong", Critical: "administration.label.serious",
  }

  const SERVICE_LABELS: Record<string, string> = {
    Api: "administration.label.api", Worker: "administration.label.worker", Runner: "common.label.runner", Host: "administration.label.host.platformLogsPage",
  }

  const LEVEL_ORDER = ['Trace', 'Debug', 'Information', 'Warning', 'Error', 'Critical'] as const

  const levelOrdinal = (level?: string | number | null) => typeof level === 'number' ? level : LEVEL_ORDER.indexOf(level as typeof LEVEL_ORDER[number])

  const LIVE_LIMIT = 200

  const minimumLevel = ref<NoCTFAPIEndpointsAdministrationPlatformPlatformLogLevelProtocol>('Warning')

  const service = ref<'all' | NoCTFAPIEndpointsAdministrationPlatformPlatformLogServiceProtocol>('all')

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
    let error: unknown;
    const data = await api.api.v1.admin.platform.logs.get({ queryParameters: {
        minimumLevel: minimumLevel.value,
        service: (service.value === 'all'
          ? null
          : service.value as NoCTFAPIEndpointsAdministrationPlatformPlatformLogServiceProtocol) ?? undefined,
        from: dateObject(toIso(from.value) ?? undefined),
        to: dateObject(toIso(to.value) ?? undefined),
        search: search.value.trim() || undefined,
        cursor: cursor ?? undefined,
        limit: 50,
      } }).catch(cause => { error = cause; return undefined });
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
        return { label: translate("administration.label.liveStreamConnected"), variant: 'secondary' as const }
      case 'connecting':
        return { label: translate("administration.label.liveStreamingConnection"), variant: 'outline' as const }
      case 'reconnecting':
        return { label: translate("administration.label.liveStreamReconnecting"), variant: 'outline' as const }
      default:
        return { label: translate("administration.label.liveStreamDisconnected"), variant: 'destructive' as const }
    }
  })

  async function exportLogs(): Promise<void> {
    const fromIso = toIso(from.value)
    const toIsoValue = toIso(to.value)
    if (!fromIso || !toIsoValue) {
      toast.error(describeMessage("administration.platformLogs.description.exportSelectStartEnd"))
      return
    }
    exporting.value = true
    try {
      await downloadSdkFile(
        nativeResponse(responseOptions => api.api.v1.admin.platform.logs.exportEscaped.get({ queryParameters: {
            minimumLevel: minimumLevel.value,
            service: service.value === 'all' ? undefined : service.value,
            from: dateObject(fromIso ?? undefined),
            to: dateObject(toIsoValue ?? undefined),
            search: search.value.trim() || undefined,
          }, options: [...responseOptions] })),
        'platform-logs.jsonl',
      )
      toast.success(describeMessage("administration.platformLogs.label.logExportDownloadStarted"))
    }
    catch (e) {
      toast.error(parseApiError(e).displayMessage)
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
