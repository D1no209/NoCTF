<script setup lang="ts">
import { Download, Radio } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { adminPlatformListLogs } from '~/api'
import { downloadProtectedFile } from '~/utils/download'
import type {
  NoCtfapiEndpointsAdministrationPlatformPlatformLogResponse,
  NoCtfApplicationAdministrationPlatformLogsPlatformLogLevel,
  NoCtfApplicationAdministrationPlatformLogsPlatformLogService,
} from '~/api'

definePageMeta({ middleware: 'platform-admin' })

type PlatformLog = NoCtfapiEndpointsAdministrationPlatformPlatformLogResponse

const LEVEL_LABELS: Record<number, string> = {
  0: '跟踪',
  1: '调试',
  2: '信息',
  3: '警告',
  4: '错误',
  5: '严重',
}
const SERVICE_LABELS: Record<number, string> = { 0: 'API', 1: 'Worker', 2: 'Runner' }
const LIVE_LIMIT = 200

const minimumLevel = ref('2')
const service = ref('all')
const search = ref('')
const from = ref('')
const to = ref('')
const exporting = ref(false)

function toIso(local: string): string | null {
  if (!local) return null
  const date = new Date(local)
  return Number.isNaN(date.getTime()) ? null : date.toISOString()
}

const { items, loading, hasMore, initialized, loadMore, reset } = useCursorPagination<PlatformLog>(async (cursor) => {
  const { data, error } = await adminPlatformListLogs({
    query: {
      minimumLevel: Number(minimumLevel.value) as NoCtfApplicationAdministrationPlatformLogsPlatformLogLevel,
      service: service.value === 'all'
        ? null
        : (Number(service.value) as NoCtfApplicationAdministrationPlatformLogsPlatformLogService),
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
  reset()
  void loadMore()
}

// ---------- SignalR 实时流 ----------
const live = ref(true)

const { state: hubState, start, stop } = usePlatformLogHub((log) => {
  if (!matchesLiveFilters(log)) return
  if (items.value.some(existing => existing.cursor === log.cursor)) return
  items.value.unshift(log)
  if (items.value.length > LIVE_LIMIT) items.value.length = LIVE_LIMIT
})

function matchesLiveFilters(log: PlatformLog): boolean {
  if ((log.level ?? 0) < Number(minimumLevel.value)) return false
  if (service.value !== 'all' && log.service !== Number(service.value)) return false
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
      return { label: '实时流已连接', variant: 'secondary' as const }
    case 'connecting':
      return { label: '实时流连接中', variant: 'outline' as const }
    case 'reconnecting':
      return { label: '实时流重连中', variant: 'outline' as const }
    default:
      return { label: '实时流已断开', variant: 'destructive' as const }
  }
})

// ---------- 导出 ----------
async function exportLogs(): Promise<void> {
  const fromIso = toIso(from.value)
  const toIsoValue = toIso(to.value)
  if (!fromIso || !toIsoValue) {
    toast.error('导出需要选择起止时间')
    return
  }
  exporting.value = true
  try {
    const params = new URLSearchParams({
      minimumLevel: minimumLevel.value,
      from: fromIso,
      to: toIsoValue,
    })
    if (service.value !== 'all') params.set('service', service.value)
    if (search.value.trim()) params.set('search', search.value.trim())
    await downloadProtectedFile(`/api/v1/admin/platform/logs/export?${params.toString()}`, 'platform-logs.jsonl')
    toast.success('日志导出已开始下载')
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
</script>

<template>
  <div class="flex flex-col gap-4">
    <div class="flex flex-wrap items-end gap-4">
      <Field>
        <FieldLabel for="log-level">最低级别</FieldLabel>
        <Select v-model="minimumLevel">
          <SelectTrigger id="log-level" class="w-32">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectGroup>
              <SelectItem value="0">跟踪</SelectItem>
              <SelectItem value="1">调试</SelectItem>
              <SelectItem value="2">信息</SelectItem>
              <SelectItem value="3">警告</SelectItem>
              <SelectItem value="4">错误</SelectItem>
              <SelectItem value="5">严重</SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
      </Field>
      <Field>
        <FieldLabel for="log-service">服务</FieldLabel>
        <Select v-model="service">
          <SelectTrigger id="log-service" class="w-32">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectGroup>
              <SelectItem value="all">全部</SelectItem>
              <SelectItem value="0">API</SelectItem>
              <SelectItem value="1">Worker</SelectItem>
              <SelectItem value="2">Runner</SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
      </Field>
      <Field>
        <FieldLabel for="log-from">起始时间</FieldLabel>
        <Input id="log-from" v-model="from" type="datetime-local" />
      </Field>
      <Field>
        <FieldLabel for="log-to">结束时间</FieldLabel>
        <Input id="log-to" v-model="to" type="datetime-local" />
      </Field>
      <Field class="min-w-56 flex-1">
        <FieldLabel for="log-search">搜索</FieldLabel>
        <Input id="log-search" v-model="search" placeholder="消息或类别关键字" @keyup.enter="applyFilters" />
      </Field>
      <div class="flex items-center gap-2">
        <Button @click="applyFilters">查询</Button>
        <Button variant="outline" :disabled="exporting" @click="exportLogs">
          <Spinner v-if="exporting" data-icon="inline-start" />
          <Download v-else data-icon="inline-start" />
          导出
        </Button>
      </div>
    </div>

    <div class="flex items-center gap-3">
      <Switch id="live-stream" v-model:checked="live" />
      <Label for="live-stream" class="inline-flex items-center gap-1">
        <Radio class="size-4" />
        实时接收新日志
      </Label>
      <Badge :variant="hubStateBadge.variant">{{ hubStateBadge.label }}</Badge>
    </div>

    <Card v-if="loading && items.length === 0">
      <CardContent class="flex flex-col gap-3 pt-6">
        <Skeleton v-for="i in 8" :key="i" class="h-8 w-full" />
      </CardContent>
    </Card>

    <Empty v-else-if="initialized && items.length === 0">
      <EmptyHeader>
        <EmptyTitle>没有匹配的日志</EmptyTitle>
        <EmptyDescription>调整级别、时间范围或搜索关键字。</EmptyDescription>
      </EmptyHeader>
    </Empty>

    <Card v-else>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead class="w-44">时间</TableHead>
            <TableHead class="w-20">级别</TableHead>
            <TableHead class="w-20">服务</TableHead>
            <TableHead class="w-56">类别</TableHead>
            <TableHead>消息</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="log in items" :key="log.cursor">
            <TableCell class="text-sm">
              <AdminDateTime :value="log.timestamp" />
            </TableCell>
            <TableCell>
              <Badge :variant="(log.level ?? 0) >= 4 ? 'destructive' : (log.level ?? 0) === 3 ? 'secondary' : 'outline'">
                {{ LEVEL_LABELS[log.level ?? 0] ?? log.level }}
              </Badge>
            </TableCell>
            <TableCell class="text-muted-foreground">{{ SERVICE_LABELS[log.service ?? 0] ?? log.service }}</TableCell>
            <TableCell class="max-w-56 truncate text-sm text-muted-foreground" :title="log.category">
              {{ log.category }}
            </TableCell>
            <TableCell class="max-w-xl">
              <div class="truncate" :title="log.message">{{ log.message }}</div>
              <div v-if="log.exceptionType" class="truncate text-xs text-destructive" :title="log.exceptionMessage ?? ''">
                {{ log.exceptionType }}: {{ log.exceptionMessage }}
              </div>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </Card>

    <AdminLoadMore :loading="loading" :has-more="hasMore" @load="loadMore" />
  </div>
</template>
