import { message as describeMessage } from '../../../../utils/i18n'
import { markRaw } from 'vue'

import { Download, Radio } from '@lucide/vue'
import { toast } from '../../../../utils/message-toast'
import { adminPlatformExportLogs, adminPlatformListLogs } from '../../../../api'
import { downloadSdkFile } from '../../../../utils/download'
import { useCursorPagePagination } from '../../../../composables/useCursorPagePagination'
import type { NoCtfapiEndpointsAdministrationPlatformPlatformLogResponse, NoCtfapiEndpointsAdministrationPlatformPlatformLogLevelProtocol, NoCtfapiEndpointsAdministrationPlatformPlatformLogServiceProtocol } from '../../../../api'
import AdminDateTimeComponent from '../../../admin/AdminDateTime.vue'

type PlatformLog = NoCtfapiEndpointsAdministrationPlatformPlatformLogResponse

/** Owns state, effects and commands for AdminPlatformLogsPage. */
export function useAdminPlatformLogsPage() {
  const LEVEL_LABELS: Record<string, string> = {
    Trace: "administration.label.track", Debug: "administration.label.debugging", Information: "common.label.information", Warning: "common.label.warning", Error: "common.label.wrong", Critical: "administration.label.serious",
  }

  const SERVICE_LABELS: Record<string, string> = {
    Api: "administration.label.api", Worker: "administration.label.worker", Runner: "common.label.runner", Host: "administration.label.host.platformLogsPage",
  }

  const LEVEL_ORDER = ['Trace', 'Debug', 'Information', 'Warning', 'Error', 'Critical'] as const

  const levelOrdinal = (level?: string | number) => typeof level === 'number' ? level : LEVEL_ORDER.indexOf(level as typeof LEVEL_ORDER[number])

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

  function captureFilters() {
    const end = toIso(to.value) ?? new Date().toISOString()
    return {
      minimumLevel: minimumLevel.value,
      service: service.value === 'all' ? null : service.value,
      from: toIso(from.value) ?? new Date(Date.parse(end) - 14 * 24 * 60 * 60 * 1000).toISOString(),
      to: end,
      search: search.value.trim() || null,
    }
  }
  // Keep the filter set and time window stable for every signed cursor in this query.
  let appliedFilters = captureFilters()
  let liveFrom = toIso(from.value)
  let liveTo = toIso(to.value)
  const newLogs = ref(0)
  const seenLiveCursors = new Set<string>()
  const pagination = useCursorPagePagination<PlatformLog>(async (cursor, limit) => {
    const { data, error } = await adminPlatformListLogs({
      query: { ...appliedFilters, cursor, limit },
    })
    if (error || !data) throw parseApiError(error)
    return { items: data.items ?? [], nextCursor: data.nextCursor ?? null }
  })
  const { items, page, limit: pageLimit, loading, error: listError, hasPrevious, hasNext, initialized, loadPage, reset } = pagination

  function applyFilters(): void {
    appliedFilters = captureFilters()
    liveFrom = toIso(from.value)
    liveTo = toIso(to.value)
    newLogs.value = 0
    seenLiveCursors.clear()
    reset({ preserveItems: true })
    void loadPage(1)
  }

  function setPageSize(value: number): void {
    if (![20, 50, 100, 200].includes(value) || value === pageLimit.value) return
    pageLimit.value = value
    applyFilters()
  }

  function viewLatest(): void {
    newLogs.value = 0
    seenLiveCursors.clear()
    if (!liveTo) appliedFilters = { ...appliedFilters, to: new Date().toISOString() }
    if (!liveFrom) appliedFilters = { ...appliedFilters, from: new Date(Date.parse(appliedFilters.to) - 14 * 24 * 60 * 60 * 1000).toISOString() }
    reset({ preserveItems: true })
    void loadPage(1)
  }

  const live = ref(true)

  const { state: hubState, start, stop } = usePlatformLogHub((log) => {
    if (!live.value || !log.cursor || !matchesLiveFilters(log)) return
    if (items.value.some(existing => existing.cursor === log.cursor)) return
    if (seenLiveCursors.has(log.cursor)) return
    seenLiveCursors.add(log.cursor)
    if (seenLiveCursors.size > 200) seenLiveCursors.delete(seenLiveCursors.values().next().value!)
    // History is a snapshot. A live arrival must not evict an unread row or move a page boundary.
    newLogs.value += 1
  })

  function matchesLiveFilters(log: PlatformLog): boolean {
    if (levelOrdinal(log.level) < levelOrdinal(appliedFilters.minimumLevel)) return false
    if (appliedFilters.service && log.service !== appliedFilters.service) return false
    const timestamp = Date.parse(log.timestamp ?? '')
    if (!Number.isFinite(timestamp)) return false
    if (liveFrom && timestamp < Date.parse(liveFrom)) return false
    if (liveTo && timestamp > Date.parse(liveTo)) return false
    const keyword = (appliedFilters.search ?? '').toLowerCase()
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
    void loadPage(1)
    void start()
  })
  onUnmounted(() => reset())

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
      page,
      pageLimit,
      hasPrevious,
      hasNext,
      initialized,
      loadPage,
      setPageSize,
      newLogs,
      viewLatest,
      applyFilters,
      live,
      start,
      hubStateBadge,
      exportLogs,
      AdminDateTime
    }
}

export type AdminPlatformLogsPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminPlatformLogsPage>>>
