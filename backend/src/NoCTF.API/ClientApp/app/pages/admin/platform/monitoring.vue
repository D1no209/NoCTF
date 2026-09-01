<script setup lang="ts">
import { Activity, ExternalLink, RefreshCw } from '@lucide/vue'
import { adminPlatformGetMonitoring } from '~/api'
import type {
  NoCtfapiEndpointsAdministrationPlatformPlatformMonitoringMetricResponse,
  NoCtfapiEndpointsAdministrationPlatformPlatformMonitoringResponse,
  NoCtfApplicationAdministrationMonitoringPlatformMonitoringMetricKind,
  NoCtfApplicationAdministrationMonitoringPlatformMonitoringStatus,
  NoCtfApplicationAdministrationMonitoringPlatformMonitoringUnit,
} from '~/api'

definePageMeta({ middleware: 'platform-admin' })

type MonitoringSnapshot = NoCtfapiEndpointsAdministrationPlatformPlatformMonitoringResponse
type MonitoringMetric = NoCtfapiEndpointsAdministrationPlatformPlatformMonitoringMetricResponse
type MetricKind = NoCtfApplicationAdministrationMonitoringPlatformMonitoringMetricKind
type MonitoringStatus = NoCtfApplicationAdministrationMonitoringPlatformMonitoringStatus
type MonitoringUnit = NoCtfApplicationAdministrationMonitoringPlatformMonitoringUnit

const STATUS = {
  healthy: 0,
  warning: 1,
  critical: 2,
  unavailable: 3,
  noSamples: 4,
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
    title: 'API 与实时连接',
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
    title: '事务消息',
    kinds: [METRIC.wolverineOutboxCount, METRIC.wolverineInboxCount],
  },
  {
    title: '排行榜链路',
    kinds: [
      METRIC.leaderboardMergeDispatchFailuresPerSecond,
      METRIC.leaderboardCacheMissRebuildFailuresPerSecond,
      METRIC.leaderboardProjectionP95Milliseconds,
      METRIC.leaderboardPublishFailuresPerSecond,
      METRIC.leaderboardSignalRPublishFailuresPerSecond,
    ],
  },
  {
    title: '运行环境与 Runner',
    kinds: [METRIC.runtimeWaitingCount, METRIC.runtimeOldestWaitingSeconds, METRIC.runnerOnlineCount, METRIC.runnerMinimumAvailablePercent],
  },
  {
    title: '基础设施',
    kinds: [METRIC.postgreSqlConnectionUsagePercent, METRIC.redisP99Milliseconds, METRIC.diskAvailablePercent],
  },
]

const METRIC_LABELS: Record<number, string> = {
  [METRIC.apiRequestsPerSecond]: 'API 请求速率',
  [METRIC.apiP95Milliseconds]: 'API P95 延迟',
  [METRIC.apiServerErrorPercent]: 'API 5xx 比例',
  [METRIC.signalRConnections]: 'SignalR 当前连接',
  [METRIC.natsAvailability]: 'NATS 可用性',
  [METRIC.jetStreamStorageUsagePercent]: 'JetStream 存储使用率',
  [METRIC.criticalQueuePendingCount]: '关键消息待投递',
  [METRIC.criticalQueueAckPendingCount]: '关键消息处理中未确认',
  [METRIC.criticalQueueRedeliveredCount]: '关键消息重投递',
  [METRIC.wolverineOutboxCount]: 'Wolverine Outbox 积压',
  [METRIC.wolverineInboxCount]: 'Wolverine Inbox 积压',
  [METRIC.runtimeWaitingCount]: '等待中的 Runtime',
  [METRIC.runtimeOldestWaitingSeconds]: '最长 Runtime 等待',
  [METRIC.leaderboardMergeDispatchFailuresPerSecond]: '排行榜合并分发失败率',
  [METRIC.leaderboardCacheMissRebuildFailuresPerSecond]: '排行榜缓存重建失败率',
  [METRIC.leaderboardProjectionP95Milliseconds]: '排行榜投影 P95 延迟',
  [METRIC.leaderboardPublishFailuresPerSecond]: '排行榜缓存发布失败率',
  [METRIC.leaderboardSignalRPublishFailuresPerSecond]: '排行榜 SignalR 发布失败率',
  [METRIC.runnerOnlineCount]: '在线 Runner',
  [METRIC.runnerMinimumAvailablePercent]: 'Runner 最低可用容量',
  [METRIC.postgreSqlConnectionUsagePercent]: 'PostgreSQL 连接使用率',
  [METRIC.redisP99Milliseconds]: 'Redis P99 延迟',
  [METRIC.diskAvailablePercent]: '磁盘最低可用空间',
}

const snapshot = ref<MonitoringSnapshot | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)
let refreshTimer: ReturnType<typeof setInterval> | undefined

function statusLabel(status: MonitoringStatus | undefined): string {
  if (status === STATUS.healthy) return translate("运行正常")
  if (status === STATUS.warning) return translate("需要关注")
  if (status === STATUS.critical) return translate("严重异常")
  if (status === STATUS.noSamples) return translate("暂无样本")
  return translate("数据不可用")
}

function statusVariant(status: MonitoringStatus | undefined): 'default' | 'secondary' | 'destructive' | 'outline' {
  if (status === STATUS.healthy) return 'default'
  if (status === STATUS.warning) return 'secondary'
  if (status === STATUS.critical) return 'destructive'
  return 'outline'
}

function metricByKind(kind: number): MonitoringMetric | undefined {
  return snapshot.value?.metrics?.find(metric => metric.kind === kind)
}

function metricLabel(kind: number): string {
  return METRIC_LABELS[kind] ?? '未知指标'
}

function formatMetric(metric: MonitoringMetric | undefined): string {
  if (metric?.status === STATUS.noSamples) return translate('暂无样本')
  if (metric?.value === null || metric?.value === undefined) return '—'
  const value = metric.value
  if (metric.kind === METRIC.natsAvailability) return translate(value >= 1 ? '可用' : '不可用')
  if (metric.unit === UNIT.perSecond) return `${value.toFixed(value < 10 ? 2 : 1)} /s`
  if (metric.unit === UNIT.milliseconds) return `${Math.round(value)} ms`
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
  const { data, error: responseError } = await adminPlatformGetMonitoring()
  loading.value = false
  if (responseError || !data) {
    error.value = parseApiError(responseError).message
    return
  }
  error.value = null
  snapshot.value = data
}

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
</script>

<template>
  <div class="flex flex-col gap-6">
    <div class="flex flex-wrap items-start justify-between gap-4 border-b pb-5">
      <div class="flex min-w-0 items-start gap-3">
        <div class="flex size-10 shrink-0 items-center justify-center rounded-md bg-primary/10 text-primary">
          <Activity class="size-5" />
        </div>
        <div>
          <div class="flex flex-wrap items-center gap-2">
            <h2 class="text-display text-xl">{{ $t('监控') }}</h2>
            <Badge :variant="statusVariant(snapshot?.status)">
              {{ statusLabel(snapshot?.status) }}
            </Badge>
          </div>
          <p class="mt-1 text-sm text-muted-foreground">
            {{ $t('平台关键链路与容量摘要') }}
          </p>
          <div v-if="snapshot" class="mt-2 flex flex-wrap gap-2">
            <Badge :variant="snapshot.prometheusAvailable ? 'default' : 'destructive'">
              Prometheus · {{ $t(snapshot.prometheusAvailable ? '可用' : '不可用') }}
            </Badge>
            <Badge :variant="snapshot.natsAvailable ? 'default' : 'destructive'">
              NATS · {{ $t(snapshot.natsAvailable ? '可用' : '不可用') }}
            </Badge>
          </div>
        </div>
      </div>
      <div class="flex flex-wrap gap-2">
        <Button v-if="snapshot?.dashboardUrl" variant="outline" as-child>
          <a :href="snapshot.dashboardUrl" target="_blank" rel="noopener noreferrer">
            <ExternalLink data-icon="inline-start" />
            {{ $t('打开 Grafana') }}
          </a>
        </Button>
        <Button variant="outline" :disabled="loading" @click="refreshMonitoring">
          <Spinner v-if="loading" data-icon="inline-start" />
          <RefreshCw v-else data-icon="inline-start" />
          {{ $t('刷新') }}
        </Button>
      </div>
    </div>

    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>

    <Alert v-if="snapshot && !snapshot.prometheusAvailable" variant="destructive">
      <AlertDescription>
        {{ $t('Prometheus 暂不可用,当前监控摘要不是实时状态') }}
      </AlertDescription>
    </Alert>

    <Alert v-else-if="snapshot && !snapshot.natsAvailable" variant="destructive">
      <AlertDescription>
        {{ $t('NATS Exporter 或 NATS 监控接口不可用,消息链路状态为严重异常') }}
      </AlertDescription>
    </Alert>

    <div v-if="!snapshot && loading" class="grid gap-4 md:grid-cols-2">
      <Card v-for="index in 4" :key="index">
        <CardHeader><Skeleton class="h-6 w-32" /></CardHeader>
        <CardContent class="grid gap-3 sm:grid-cols-2">
          <Skeleton v-for="item in 4" :key="item" class="h-20 w-full" />
        </CardContent>
      </Card>
    </div>

    <div v-else-if="snapshot" class="grid gap-4 lg:grid-cols-2">
      <section v-for="group in GROUPS" :key="group.title" class="border-t pt-4">
        <h3 class="mb-3 text-sm font-semibold">{{ $t(group.title) }}</h3>
        <div class="grid gap-x-5 sm:grid-cols-2">
          <div
            v-for="kind in group.kinds"
            :key="kind"
            class="flex min-h-20 items-center justify-between gap-3 border-b py-3"
          >
            <div class="min-w-0">
              <p class="text-sm text-muted-foreground">{{ $t(metricLabel(kind)) }}</p>
              <p class="mt-1 font-mono text-xl font-semibold tabular-nums">
                {{ formatMetric(metricByKind(kind)) }}
              </p>
            </div>
            <Badge :variant="statusVariant(metricByKind(kind)?.status)">
              {{ statusLabel(metricByKind(kind)?.status) }}
            </Badge>
          </div>
        </div>
      </section>
    </div>

    <div v-if="snapshot" class="flex flex-wrap items-center justify-between gap-2 border-t pt-4 text-xs text-muted-foreground">
      <span>{{ $t('数据每 15 秒自动刷新') }}</span>
      <span class="font-mono tabular-nums">{{ $t('采集时间') }}: {{ formatCapturedAt(snapshot.capturedAt) }}</span>
    </div>
  </div>
</template>
