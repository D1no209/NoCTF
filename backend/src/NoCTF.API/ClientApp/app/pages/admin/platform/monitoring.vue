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
} as const satisfies Record<string, MonitoringStatus>

const METRIC = {
  apiRequestsPerSecond: 0,
  apiP95Milliseconds: 1,
  apiServerErrorPercent: 2,
  signalRConnections: 3,
  criticalQueueDepth: 4,
  criticalQueueOldestSeconds: 5,
  runtimeWaitingCount: 6,
  runtimeOldestWaitingSeconds: 7,
  leaderboardDirtyCount: 8,
  leaderboardDirtyOldestSeconds: 9,
  runnerOnlineCount: 10,
  runnerMinimumAvailablePercent: 11,
  postgreSqlConnectionUsagePercent: 12,
  redisP99Milliseconds: 13,
  diskAvailablePercent: 14,
} as const satisfies Record<string, MetricKind>

const UNIT = {
  count: 0,
  perSecond: 1,
  milliseconds: 2,
  seconds: 3,
  percent: 4,
} as const satisfies Record<string, MonitoringUnit>

const GROUPS: Array<{ title: string; kinds: MetricKind[] }> = [
  {
    title: 'API 与实时连接',
    kinds: [METRIC.apiRequestsPerSecond, METRIC.apiP95Milliseconds, METRIC.apiServerErrorPercent, METRIC.signalRConnections],
  },
  {
    title: '队列与投影',
    kinds: [METRIC.criticalQueueDepth, METRIC.criticalQueueOldestSeconds, METRIC.leaderboardDirtyCount, METRIC.leaderboardDirtyOldestSeconds],
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

const METRIC_LABELS: Record<MetricKind, string> = {
  [METRIC.apiRequestsPerSecond]: 'API 请求速率',
  [METRIC.apiP95Milliseconds]: 'API P95 延迟',
  [METRIC.apiServerErrorPercent]: 'API 5xx 比例',
  [METRIC.signalRConnections]: 'SignalR 当前连接',
  [METRIC.criticalQueueDepth]: '关键队列积压',
  [METRIC.criticalQueueOldestSeconds]: '最旧关键消息等待',
  [METRIC.runtimeWaitingCount]: '等待中的 Runtime',
  [METRIC.runtimeOldestWaitingSeconds]: '最长 Runtime 等待',
  [METRIC.leaderboardDirtyCount]: '待投影排行榜',
  [METRIC.leaderboardDirtyOldestSeconds]: '最老排行榜脏标记',
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
  return translate("数据不可用")
}

function statusVariant(status: MonitoringStatus | undefined): 'default' | 'secondary' | 'destructive' | 'outline' {
  if (status === STATUS.healthy) return 'default'
  if (status === STATUS.warning) return 'secondary'
  if (status === STATUS.critical) return 'destructive'
  return 'outline'
}

function metricByKind(kind: MetricKind): MonitoringMetric | undefined {
  return snapshot.value?.metrics?.find(metric => metric.kind === kind)
}

function formatMetric(metric: MonitoringMetric | undefined): string {
  if (metric?.value === null || metric?.value === undefined) return '—'
  const value = metric.value
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

    <Alert v-if="snapshot && !snapshot.sourceAvailable">
      <AlertDescription>
        {{ $t('监控数据源暂不可用,请检查 Prometheus 与采集目标') }}
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
              <p class="text-sm text-muted-foreground">{{ $t(METRIC_LABELS[kind]) }}</p>
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
