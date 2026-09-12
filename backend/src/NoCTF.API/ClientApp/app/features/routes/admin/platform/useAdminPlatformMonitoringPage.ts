

import { Activity, ExternalLink as ExternalLinkIcon, RefreshCw } from '@lucide/vue'
import { adminPlatformGetMonitoring } from '../../../../api'
import type { NoCtfapiEndpointsAdministrationPlatformPlatformMonitoringMetricResponse, NoCtfapiEndpointsAdministrationPlatformPlatformMonitoringResponse, NoCtfApplicationAdministrationMonitoringPlatformMonitoringStatus, NoCtfApplicationAdministrationMonitoringPlatformMonitoringUnit } from '../../../../api'

type MonitoringSnapshot = NoCtfapiEndpointsAdministrationPlatformPlatformMonitoringResponse

type MonitoringMetric = NoCtfapiEndpointsAdministrationPlatformPlatformMonitoringMetricResponse

type MonitoringStatus = NoCtfApplicationAdministrationMonitoringPlatformMonitoringStatus

type MonitoringUnit = NoCtfApplicationAdministrationMonitoringPlatformMonitoringUnit

/** Owns state, effects and commands for AdminPlatformMonitoringPage. */
export function useAdminPlatformMonitoringPage() {
  const STATUS = {
    healthy: 0,
    warning: 1,
    critical: 2,
    unavailable: 3,
    noSamples: 4,
    insufficientSamples: 5,
    observing: 6,
  } as const satisfies Record<string, MonitoringStatus>

  const METRIC = {
    apiRequestsPerSecond: 0,
    apiP95Milliseconds: 1,
    apiServerErrorPercent: 2,
    signalRConnections: 3,
    natsAvailability: 4,
    jetStreamStorageUsagePercent: 5,
    criticalQueuePendingCount: 6,
    criticalQueueAckPendingCount: 7,
    criticalQueueRedeliveredCount: 8,
    wolverineOutboxCount: 9,
    wolverineInboxCount: 10,
    runtimeWaitingCount: 11,
    runtimeOldestWaitingSeconds: 12,
    leaderboardMergeDispatchFailuresPerSecond: 13,
    leaderboardCacheMissRebuildFailuresPerSecond: 14,
    leaderboardProjectionP95Milliseconds: 15,
    leaderboardPublishFailuresPerSecond: 16,
    leaderboardSignalRPublishFailuresPerSecond: 17,
    runnerOnlineCount: 18,
    runnerMinimumAvailablePercent: 19,
    postgreSqlConnectionUsagePercent: 20,
    redisP99Milliseconds: 21,
    diskAvailablePercent: 22,
    flagSubmissionsPerSecond: 23,
    flagSubmissionsLastFiveMinutes: 24,
    fixSubmissionsPerSecond: 25,
    fixSubmissionsLastFiveMinutes: 26,
    flagCorrectPercent: 27,
    flagProcessingP95Milliseconds: 28,
    flagPlatformErrorPercent: 29,
  } as const

  const UNIT = {
    count: 0,
    perSecond: 1,
    milliseconds: 2,
    seconds: 3,
    percent: 4,
  } as const satisfies Record<string, MonitoringUnit>

  const GROUPS: Array<{ title: string; kinds: number[] }> = [
    {
      title: "ui.apiRealtime",
      kinds: [METRIC.apiRequestsPerSecond, METRIC.apiP95Milliseconds, METRIC.apiServerErrorPercent, METRIC.signalRConnections],
    },
    {
      title: "ui.natsJetstream",
      kinds: [
        METRIC.natsAvailability,
        METRIC.jetStreamStorageUsagePercent,
        METRIC.criticalQueuePendingCount,
        METRIC.criticalQueueAckPendingCount,
        METRIC.criticalQueueRedeliveredCount,
      ],
    },
    {
      title: "ui.transactionalMessaging",
      kinds: [METRIC.wolverineOutboxCount, METRIC.wolverineInboxCount],
    },
    {
      title: "ui.leaderboardPipeline",
      kinds: [
        METRIC.leaderboardMergeDispatchFailuresPerSecond,
        METRIC.leaderboardCacheMissRebuildFailuresPerSecond,
        METRIC.leaderboardProjectionP95Milliseconds,
        METRIC.leaderboardPublishFailuresPerSecond,
        METRIC.leaderboardSignalRPublishFailuresPerSecond,
      ],
    },
    {
      title: "ui.runtimesRunners",
      kinds: [METRIC.runtimeWaitingCount, METRIC.runtimeOldestWaitingSeconds, METRIC.runnerOnlineCount, METRIC.runnerMinimumAvailablePercent],
    },
    {
      title: "ui.infrastructure",
      kinds: [METRIC.postgreSqlConnectionUsagePercent, METRIC.redisP99Milliseconds, METRIC.diskAvailablePercent],
    },
  ]

  const GAMEPLAY = {
    title: "ui.gameplaySubmissions",
    qualityKind: METRIC.flagCorrectPercent,
    healthKinds: [
      METRIC.flagProcessingP95Milliseconds,
      METRIC.flagPlatformErrorPercent,
    ],
    primaryKinds: [
      METRIC.flagCorrectPercent,
      METRIC.flagProcessingP95Milliseconds,
      METRIC.flagPlatformErrorPercent,
    ],
    volumeKinds: [
      METRIC.flagSubmissionsPerSecond,
      METRIC.flagSubmissionsLastFiveMinutes,
      METRIC.fixSubmissionsPerSecond,
      METRIC.fixSubmissionsLastFiveMinutes,
    ],
    kinds: [
      METRIC.flagCorrectPercent,
      METRIC.flagProcessingP95Milliseconds,
      METRIC.flagPlatformErrorPercent,
    ],
  }

  const HERO_KINDS = [
    METRIC.apiRequestsPerSecond,
    METRIC.apiP95Milliseconds,
    METRIC.apiServerErrorPercent,
  ]

  const METRIC_LABELS: Record<number, string> = {
    [METRIC.apiRequestsPerSecond]: "ui.ordinaryApiRequestRate",
    [METRIC.apiP95Milliseconds]: "ui.ordinaryApiP95Duration",
    [METRIC.apiServerErrorPercent]: "ui.ordinaryApi5xxRatio",
    [METRIC.signalRConnections]: "ui.currentSignalrConnections",
    [METRIC.natsAvailability]: "ui.natsAvailability",
    [METRIC.jetStreamStorageUsagePercent]: "ui.jetstreamStorageUsage",
    [METRIC.criticalQueuePendingCount]: "ui.criticalMessagesPendingDelivery",
    [METRIC.criticalQueueAckPendingCount]: "ui.criticalMessagesAwaitingAcknowledgement",
    [METRIC.criticalQueueRedeliveredCount]: "ui.criticalMessageRedeliveries",
    [METRIC.wolverineOutboxCount]: "ui.wolverineOutboxBacklog",
    [METRIC.wolverineInboxCount]: "ui.wolverineInboxBacklog",
    [METRIC.runtimeWaitingCount]: "ui.waitingRuntimes",
    [METRIC.runtimeOldestWaitingSeconds]: "ui.longestRuntimeWait",
    [METRIC.leaderboardMergeDispatchFailuresPerSecond]: "ui.leaderboardMergeDispatchFailureRate",
    [METRIC.leaderboardCacheMissRebuildFailuresPerSecond]: "ui.leaderboardCacheRebuildFailureRate",
    [METRIC.leaderboardProjectionP95Milliseconds]: "ui.p95",
    [METRIC.leaderboardPublishFailuresPerSecond]: "ui.leaderboardCachePublishFailureRate",
    [METRIC.leaderboardSignalRPublishFailuresPerSecond]: "ui.leaderboardSignalrPublishFailureRate",
    [METRIC.runnerOnlineCount]: "ui.onlineRunners",
    [METRIC.runnerMinimumAvailablePercent]: "ui.lowestRemainingPoolQuota",
    [METRIC.postgreSqlConnectionUsagePercent]: "ui.postgresqlConnectionUsage",
    [METRIC.redisP99Milliseconds]: "ui.platformRedisOperationP99Duration",
    [METRIC.diskAvailablePercent]: "ui.lowestDiskAvailability",
    [METRIC.flagSubmissionsPerSecond]: "ui.flagSubmissionRateFiveMinutes",
    [METRIC.flagSubmissionsLastFiveMinutes]: "ui.flagSubmissionsLastFiveMinutes",
    [METRIC.fixSubmissionsPerSecond]: "ui.fixSubmissionRateFiveMinutes",
    [METRIC.fixSubmissionsLastFiveMinutes]: "ui.fixSubmissionsLastFiveMinutes",
    [METRIC.flagCorrectPercent]: "ui.flagCorrectRateFiveMinutes",
    [METRIC.flagProcessingP95Milliseconds]: "ui.flagProcessingP95FiveMinutes",
    [METRIC.flagPlatformErrorPercent]: "ui.flagPlatformErrorRateFiveMinutes",
  }

  const snapshot = ref<MonitoringSnapshot | null>(null)

  const loading = ref(false)

  const error = ref<string | null>(null)

  let refreshTimer: ReturnType<typeof setInterval> | undefined

  function statusLabel(status: MonitoringStatus | undefined): string {
    if (status === STATUS.healthy) return translate("ui.healthy")
    if (status === STATUS.warning) return translate("ui.needsAttention")
    if (status === STATUS.critical) return translate("ui.critical")
    if (status === STATUS.noSamples) return translate("ui.noSamples")
    if (status === STATUS.insufficientSamples) return translate("ui.insufficientSamplesLowConfidence")
    if (status === STATUS.observing) return translate("ui.aboveThresholdObserving")
    return translate("ui.unavailable")
  }

  function statusVariant(status: MonitoringStatus | undefined): 'default' | 'secondary' | 'destructive' | 'outline' {
    if (status === STATUS.healthy) return 'default'
    if (status === STATUS.warning || status === STATUS.observing) return 'secondary'
    if (status === STATUS.critical) return 'destructive'
    return 'outline'
  }

  function statusKey(status: MonitoringStatus | undefined): string {
    if (status === STATUS.healthy) return 'healthy'
    if (status === STATUS.warning || status === STATUS.observing) return 'warning'
    if (status === STATUS.critical) return 'critical'
    return 'neutral'
  }

  function metricByKind(kind: number): MonitoringMetric | undefined {
    return snapshot.value?.metrics?.find(metric => metric.kind === kind)
  }

  function metricLabel(kind: number): string {
    return METRIC_LABELS[kind] ?? "ui.message10"
  }

  function formatMetric(metric: MonitoringMetric | undefined): string {
    if (metric?.status === STATUS.noSamples) return translate("ui.noSamples")
    if (metric?.value === null || metric?.value === undefined) return '—'
    const value = metric.value
    if (metric.kind === METRIC.natsAvailability) return translate(value >= 1 ? "ui.available" : "ui.unavailable2")
    if (metric.unit === UNIT.perSecond) return `${value.toFixed(value < 10 ? 2 : 1)} /s`
    if (metric.unit === UNIT.milliseconds) return monitoringMilliseconds(value)
    if (metric.unit === UNIT.seconds) return `${Math.round(value)} s`
    if (metric.unit === UNIT.percent) return `${value.toFixed(1)}%`
    return Math.round(value).toLocaleString()
  }

  function formatCapturedAt(value: string | undefined): string {
    if (!value) return '—'
    return new Date(value).toLocaleString(localeTag(), { hour12: false })
  }

  function metricVisualPercent(metric: MonitoringMetric | undefined): number {
    if (metric?.value == null || !Number.isFinite(metric.value)) return 0
    if (metric.kind === METRIC.natsAvailability)
      return metric.value >= 1 ? 100 : 0
    if (metric.unit === UNIT.percent)
      return Math.min(100, Math.max(0, metric.value))

    const maximumByKind: Record<number, number> = {
      [METRIC.apiRequestsPerSecond]: 250,
      [METRIC.apiP95Milliseconds]: 1000,
      [METRIC.signalRConnections]: 500,
      [METRIC.redisP99Milliseconds]: 250,
      [METRIC.runtimeOldestWaitingSeconds]: 60,
      [METRIC.leaderboardProjectionP95Milliseconds]: 1000,
      [METRIC.runnerOnlineCount]: 12,
      [METRIC.flagSubmissionsPerSecond]: 100,
      [METRIC.flagSubmissionsLastFiveMinutes]: 30_000,
      [METRIC.fixSubmissionsPerSecond]: 5,
      [METRIC.fixSubmissionsLastFiveMinutes]: 1_500,
      [METRIC.flagProcessingP95Milliseconds]: 10_000,
    }
    const maximum = maximumByKind[metric.kind ?? -1]
      ?? (metric.unit === UNIT.perSecond ? 5 : 100)
    return Math.min(100, Math.max(0, Math.abs(metric.value) / maximum * 100))
  }

  const latencyScaleMax = computed(() => Math.max(
    1,
    ...(snapshot.value?.latencyDetails ?? [])
      .map(row => row.p99Milliseconds ?? 0)
      .filter(Number.isFinite),
  ))

  function latencyVisualPercent(value?: number | null): number {
    if (value == null || !Number.isFinite(value)) return 0
    return Math.min(100, Math.max(0, value / latencyScaleMax.value * 100))
  }

  function poolVisualPercent(available?: number | null, total?: number | null): number {
    const percent = monitoringQuotaPercent(available, total)
    return percent === null ? 0 : Math.min(100, Math.max(0, percent))
  }

  function poolStatusKey(available?: number | null, total?: number | null): string {
    const percent = monitoringQuotaPercent(available, total)
    if (percent === null) return 'neutral'
    if (percent < 15) return 'critical'
    if (percent < 30) return 'warning'
    return 'healthy'
  }

  const metricCount = computed(() => snapshot.value?.metrics?.length ?? 0)

  const healthyMetricCount = computed(() => snapshot.value?.metrics?.filter(metric => metric.status === STATUS.healthy).length ?? 0)

  const healthPercent = computed(() => metricCount.value === 0 ? 0 : healthyMetricCount.value / metricCount.value * 100)

  function groupStatus(group: { kinds: number[] }): MonitoringStatus {
    const statuses = group.kinds.map(kind => metricByKind(kind)?.status)
    if (statuses.includes(STATUS.critical)) return STATUS.critical
    if (statuses.includes(STATUS.warning)) return STATUS.warning
    if (statuses.includes(STATUS.observing)) return STATUS.observing
    if (statuses.includes(STATUS.unavailable)) return STATUS.unavailable
    if (statuses.includes(STATUS.healthy)) return STATUS.healthy
    if (statuses.includes(STATUS.insufficientSamples)) return STATUS.insufficientSamples
    if (statuses.includes(STATUS.noSamples)) return STATUS.noSamples
    return STATUS.unavailable
  }

  async function refreshMonitoring(): Promise<void> {
    if (loading.value) return
    loading.value = true
    try {
      const { data, error: responseError } = await adminPlatformGetMonitoring()
      if (responseError || !data) throw responseError
      error.value = null
      snapshot.value = data
    }
    catch (responseError) {
      error.value = parseApiError(responseError).message
    }
    finally {
      loading.value = false
    }
  }

  const LATENCY_LABELS = ["ui.ordinaryRestApi", "ui.signalrHttpConnections", "ui.uploads", "ui.downloads", "ui.platformRedisOperations"]

  const RESOURCE_LABELS = ["ui.memoryQuota", "ui.cpuQuota", "ui.pidQuota"]

  function refreshWhenVisible(): void {
    if (document.visibilityState === 'visible') void refreshMonitoring()
  }

  onMounted(() => {
    void refreshMonitoring()
    refreshTimer = setInterval(refreshWhenVisible, 15_000)
    document.addEventListener('visibilitychange', refreshWhenVisible)
  })

  onUnmounted(() => {
    if (refreshTimer) clearInterval(refreshTimer)
    document.removeEventListener('visibilitychange', refreshWhenVisible)
  })

  return {
      Activity,
      ExternalLinkIcon,
      RefreshCw,
      GROUPS,
      GAMEPLAY,
      HERO_KINDS,
      snapshot,
      loading,
      error,
      statusLabel,
      statusVariant,
      statusKey,
      groupStatus,
      metricByKind,
      metricLabel,
      formatMetric,
      metricVisualPercent,
      latencyVisualPercent,
      poolVisualPercent,
      poolStatusKey,
      healthPercent,
      healthyMetricCount,
      metricCount,
      formatCapturedAt,
      refreshMonitoring,
      LATENCY_LABELS,
      RESOURCE_LABELS
    }
}

export type AdminPlatformMonitoringPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminPlatformMonitoringPage>>>
