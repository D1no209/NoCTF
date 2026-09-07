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
  [METRIC.apiRequestsPerSecond]: '普通 API 请求速率',
  [METRIC.apiP95Milliseconds]: '普通 API P95 耗时',
  [METRIC.apiServerErrorPercent]: '普通 API 5xx 比例',
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
  [METRIC.runnerMinimumAvailablePercent]: '资源池最低剩余配额',
  [METRIC.postgreSqlConnectionUsagePercent]: 'PostgreSQL 连接使用率',
  [METRIC.redisP99Milliseconds]: '平台 Redis 操作 P99 耗时',
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
  if (status === STATUS.insufficientSamples) return translate('样本不足／低置信度')
  if (status === STATUS.observing) return translate('超阈值，持续观察')
  return translate("数据不可用")
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
  return METRIC_LABELS[kind] ?? '未知指标'
}

function formatMetric(metric: MonitoringMetric | undefined): string {
  if (metric?.status === STATUS.noSamples) return translate('暂无样本')
  if (metric?.value === null || metric?.value === undefined) return '—'
  const value = metric.value
  if (metric.kind === METRIC.natsAvailability) return translate(value >= 1 ? '可用' : '不可用')
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

const LATENCY_LABELS = ['普通 REST API', 'SignalR HTTP 连接', '上传请求', '下载请求', '平台 Redis 操作']
const RESOURCE_LABELS = ['内存配额', 'CPU 配额', 'PID 配额']

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
              <p v-if="metricByKind(kind)?.windowSeconds" class="mt-1 text-xs text-muted-foreground">
                {{ $t('近五分钟样本') }}：{{ monitoringNumber(metricByKind(kind)?.sampleCount, 0) }}
                · {{ $t('最低样本数') }} {{ metricByKind(kind)?.minimumSamples }}
              </p>
            </div>
            <Badge :variant="statusVariant(metricByKind(kind)?.status)">
              {{ statusLabel(metricByKind(kind)?.status) }}
            </Badge>
          </div>
        </div>
      </section>
    </div>

    <section v-if="snapshot" class="flex flex-col gap-3">
      <h3 class="text-sm font-semibold">{{ $t('请求与操作耗时明细') }}</h3>
      <p class="max-w-prose text-sm text-muted-foreground">
        {{ $t('同一五分钟窗口内的分位数、均值、样本量、速率及错误率。分位数是桶内估算值；失败请求也计入耗时。') }}
        {{ $t('延迟告警需满足最低样本数，并持续超阈值。持续时间（分钟）') }}：{{ snapshot.latencySustainedWindowMinutes }}。
      </p>
      <p class="max-w-prose text-sm text-muted-foreground">
        {{ $t('普通 API 不含静态资源、健康检查、SignalR 和文件传输。SignalR 耗时是已完成 HTTP 连接的存续时间，不等于消息发布延迟。') }}
      </p>
      <p class="max-w-prose text-sm text-muted-foreground">
        {{ $t('平台 Redis 操作可能包含多次 Redis 调用，不代表单条命令或 Redis 服务端所有命令的延迟。endpoint 标识心跳、容量领取、排行榜发布等封装操作。') }}
      </p>
      <Table>
        <TableCaption>{{ $t('错误率口径：HTTP 为 5xx；Redis 为平台操作失败。') }}</TableCaption>
        <TableHeader><TableRow>
          <TableHead>{{ $t('分类／endpoint') }}</TableHead><TableHead>P95</TableHead><TableHead>P99</TableHead>
          <TableHead>{{ $t('均值') }}</TableHead><TableHead>{{ $t('近五分钟样本') }}</TableHead>
          <TableHead>{{ $t('速率') }}</TableHead><TableHead>{{ $t('错误率') }}</TableHead><TableHead>{{ $t('状态') }}</TableHead>
        </TableRow></TableHeader>
        <TableBody>
          <TableRow v-for="row in snapshot.latencyDetails" :key="`${row.kind}:${row.endpoint}`">
            <TableCell>
              {{ $t(LATENCY_LABELS[row.kind ?? 0] ?? '未知指标') }}
              <span v-if="row.endpoint" class="block font-mono text-xs text-muted-foreground">{{ row.endpoint }}</span>
            </TableCell>
            <TableCell class="font-mono">{{ monitoringMilliseconds(row.p95Milliseconds) }}</TableCell>
            <TableCell class="font-mono">{{ monitoringMilliseconds(row.p99Milliseconds) }}</TableCell>
            <TableCell class="font-mono">{{ monitoringMilliseconds(row.meanMilliseconds) }}</TableCell>
            <TableCell class="font-mono">
              {{ monitoringNumber(row.sampleCount, 0) }}
              <span class="block text-xs text-muted-foreground">{{ $t('最低样本数') }} {{ row.minimumSamples }}</span>
            </TableCell>
            <TableCell class="font-mono">{{ monitoringNumber(row.requestsPerSecond, 2, ' /s') }}</TableCell>
            <TableCell class="font-mono">{{ monitoringNumber(row.errorPercent, 2, '%') }}</TableCell>
            <TableCell><Badge :variant="statusVariant(row.status)">{{ statusLabel(row.status) }}</Badge></TableCell>
          </TableRow>
        </TableBody>
      </Table>
      <p v-if="!snapshot.latencyDetails?.some(row => row.kind === 4)" class="text-sm text-muted-foreground">
        {{ $t('Redis 操作明细暂无样本或暂不可用，请结合上方采集状态判断。') }}
      </p>
    </section>

    <section v-if="snapshot" class="flex flex-col gap-3">
      <h3 class="text-sm font-semibold">{{ $t('资源池剩余配额明细') }}</h3>
      <p class="max-w-prose text-sm text-muted-foreground">
        {{ $t('最低比例取各资源池内存、CPU、PID 的汇总可用量／总配额中的最小值，不是单个 Runner 的最低比例，也不是主机真实利用率。') }}
        {{ $t('仅汇总在线 Runner。节点离线可能使比例上升，请同时查看在线数量；零配额不计算比例。') }}
      </p>
      <Table>
        <TableHeader><TableRow>
          <TableHead>{{ $t('资源池') }}</TableHead><TableHead>{{ $t('资源') }}</TableHead>
          <TableHead>{{ $t('可用量／总配额') }}</TableHead><TableHead>{{ $t('剩余比例') }}</TableHead><TableHead>{{ $t('在线 Runner') }}</TableHead>
        </TableRow></TableHeader>
        <TableBody><TableRow v-for="row in snapshot.poolResources" :key="`${row.pool}:${row.resource}`">
          <TableCell class="font-mono">{{ row.pool }}</TableCell>
          <TableCell>{{ $t(RESOURCE_LABELS[row.resource ?? 0] ?? '未知指标') }}</TableCell>
          <TableCell class="font-mono">{{ monitoringQuota(row.available, row.resource) }} / {{ monitoringQuota(row.total, row.resource) }}</TableCell>
          <TableCell class="font-mono">{{ monitoringQuotaPercent(row.available, row.total) === null ? $t('暂无样本') : `${monitoringNumber(monitoringQuotaPercent(row.available, row.total))}%` }}</TableCell>
          <TableCell class="font-mono">{{ monitoringNumber(row.onlineRunners, 0) }}</TableCell>
        </TableRow></TableBody>
      </Table>
      <p v-if="!snapshot.poolResources?.length" class="text-sm text-muted-foreground">{{ $t('资源池配额暂无样本或暂不可用，请结合上方采集状态判断。') }}</p>
      <p class="text-sm text-muted-foreground">{{ $t('主机真实利用率请查看现有 Grafana 主机监控，与此处调度配额分开判断。') }}</p>
    </section>

    <div v-if="snapshot" class="flex flex-wrap items-center justify-between gap-2 border-t pt-4 text-xs text-muted-foreground">
      <span>{{ $t('数据每 15 秒自动刷新') }}</span>
      <span class="font-mono tabular-nums">{{ $t('采集时间') }}: {{ formatCapturedAt(snapshot.capturedAt) }}</span>
    </div>
  </div>
</template>
