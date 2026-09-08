

import { Activity, ExternalLink, RefreshCw } from '@lucide/vue'
import { adminPlatformGetMonitoring } from '../../../../api'
import type { NoCtfapiEndpointsAdministrationPlatformPlatformMonitoringMetricResponse, NoCtfapiEndpointsAdministrationPlatformPlatformMonitoringResponse, NoCtfApplicationAdministrationMonitoringPlatformMonitoringMetricKind, NoCtfApplicationAdministrationMonitoringPlatformMonitoringStatus, NoCtfApplicationAdministrationMonitoringPlatformMonitoringUnit } from '../../../../api'

type MonitoringSnapshot = NoCtfapiEndpointsAdministrationPlatformPlatformMonitoringResponse

type MonitoringMetric = NoCtfapiEndpointsAdministrationPlatformPlatformMonitoringMetricResponse

type MetricKind = NoCtfApplicationAdministrationMonitoringPlatformMonitoringMetricKind

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
      title: 'NATS JetStream',
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

  const METRIC_LABELS: Record<number, string> = {
    [METRIC.apiRequestsPerSecond]: translate("ui.ordinaryApiRequestRate"),
    [METRIC.apiP95Milliseconds]: translate("ui.ordinaryApiP95Duration"),
    [METRIC.apiServerErrorPercent]: translate("ui.ordinaryApi5xxRatio"),
    [METRIC.signalRConnections]: translate("ui.currentSignalrConnections"),
    [METRIC.natsAvailability]: translate("ui.natsAvailability"),
    [METRIC.jetStreamStorageUsagePercent]: translate("ui.jetstreamStorageUsage"),
    [METRIC.criticalQueuePendingCount]: translate("ui.criticalMessagesPendingDelivery"),
    [METRIC.criticalQueueAckPendingCount]: translate("ui.criticalMessagesAwaitingAcknowledgement"),
    [METRIC.criticalQueueRedeliveredCount]: translate("ui.criticalMessageRedeliveries"),
    [METRIC.wolverineOutboxCount]: translate("ui.wolverineOutboxBacklog"),
    [METRIC.wolverineInboxCount]: translate("ui.wolverineInboxBacklog"),
    [METRIC.runtimeWaitingCount]: translate("ui.waitingRuntimes"),
    [METRIC.runtimeOldestWaitingSeconds]: translate("ui.longestRuntimeWait"),
    [METRIC.leaderboardMergeDispatchFailuresPerSecond]: translate("ui.leaderboardMergeDispatchFailureRate"),
    [METRIC.leaderboardCacheMissRebuildFailuresPerSecond]: translate("ui.leaderboardCacheRebuildFailureRate"),
    [METRIC.leaderboardProjectionP95Milliseconds]: translate("ui.p95"),
    [METRIC.leaderboardPublishFailuresPerSecond]: translate("ui.leaderboardCachePublishFailureRate"),
    [METRIC.leaderboardSignalRPublishFailuresPerSecond]: translate("ui.leaderboardSignalrPublishFailureRate"),
    [METRIC.runnerOnlineCount]: translate("ui.onlineRunners"),
    [METRIC.runnerMinimumAvailablePercent]: translate("ui.lowestRemainingPoolQuota"),
    [METRIC.postgreSqlConnectionUsagePercent]: translate("ui.postgresqlConnectionUsage"),
    [METRIC.redisP99Milliseconds]: translate("ui.platformRedisOperationP99Duration"),
    [METRIC.diskAvailablePercent]: translate("ui.lowestDiskAvailability"),
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

  function metricByKind(kind: number): MonitoringMetric | undefined {
    return snapshot.value?.metrics?.find(metric => metric.kind === kind)
  }

  function metricLabel(kind: number): string {
    return METRIC_LABELS[kind] ?? translate("ui.message10")
  }

  function formatMetric(metric: MonitoringMetric | undefined): string {
    if (metric?.status === STATUS.noSamples) return translate("ui.noSamples")
    if (metric?.value === null || metric?.value === undefined) return '—'
    const value = metric.value
    if (metric.kind === METRIC.natsAvailability) return translate(value >= 1 ? translate("ui.available") : translate("ui.unavailable2"))
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

  const LATENCY_LABELS = [translate("ui.ordinaryRestApi"), translate("ui.signalrHttpConnections"), translate("ui.uploads"), translate("ui.downloads"), translate("ui.platformRedisOperations")]

  const RESOURCE_LABELS = [translate("ui.memoryQuota"), translate("ui.cpuQuota"), translate("ui.pidQuota")]

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
      ExternalLink,
      RefreshCw,
      GROUPS,
      snapshot,
      loading,
      error,
      statusLabel,
      statusVariant,
      metricByKind,
      metricLabel,
      formatMetric,
      formatCapturedAt,
      refreshMonitoring,
      LATENCY_LABELS,
      RESOURCE_LABELS
    }
}

export type AdminPlatformMonitoringPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminPlatformMonitoringPage>>>
