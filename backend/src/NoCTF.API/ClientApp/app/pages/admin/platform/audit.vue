<script setup lang="ts">
import { Download, FilePlus2, RefreshCw } from '@lucide/vue'
import { toast } from 'vue-sonner'
import {
  adminCreatePlatformAuditDataExport,
  adminListPlatformAuditDataExports,
  adminPlatformListAuditLogs,
} from '~/api'
import type {
  NoCtfapiEndpointsAdministrationDataExportsDataExportResponse,
  NoCtfapiEndpointsAdministrationPlatformPlatformAuditLogResponse,
  NoCtfApplicationAdministrationPlatformLogsPlatformAuditKind,
} from '~/api'
import { downloadProtectedFile } from '~/utils/download'

definePageMeta({ middleware: 'platform-admin' })

type AuditLog = NoCtfapiEndpointsAdministrationPlatformPlatformAuditLogResponse
type DataExport = NoCtfapiEndpointsAdministrationDataExportsDataExportResponse

const KIND_LABELS: Record<number, string> = {
  0: '竞赛生命周期',
  1: '账户生命周期',
  2: '榜单可见性',
  3: '竞赛事件',
}
const EXPORT_STATUS: Record<number, { label: string; variant: 'secondary' | 'outline' | 'destructive' | 'default' }> = {
  0: { label: '排队中', variant: 'outline' },
  1: { label: '处理中', variant: 'secondary' },
  2: { label: '可下载', variant: 'default' },
  3: { label: '失败', variant: 'destructive' },
  4: { label: '已过期', variant: 'outline' },
}
const COMPETITION_STATUS_LABELS: Record<number, string> = {
  0: '草稿',
  1: '可见',
  2: '已发布',
  3: '进行中',
  4: '已暂停',
  5: '已结束',
}
const VISIBILITY_LABELS: Record<number, string> = { 0: '正常', 1: '冻结', 2: '封榜' }
const ACCOUNT_ACTION_LABELS: Record<number, string> = { 0: '封禁', 1: '禁用', 2: '匿名化', 3: '物理删除' }

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

const { items, loading, hasMore, initialized, loadMore, reset } = useCursorPagination<AuditLog>(async (cursor) => {
  const { data, error } = await adminPlatformListAuditLogs({
    query: {
      kind: kind.value === 'all'
        ? null
        : (Number(kind.value) as NoCtfApplicationAdministrationPlatformLogsPlatformAuditKind),
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
  reset()
  void loadMore()
}

function detailText(log: AuditLog): string {
  if (log.kind === 0 && log.fromCompetitionStatus !== null && log.fromCompetitionStatus !== undefined) {
    const fromLabel = COMPETITION_STATUS_LABELS[log.fromCompetitionStatus] ?? log.fromCompetitionStatus
    const toLabel = COMPETITION_STATUS_LABELS[log.toCompetitionStatus ?? -1] ?? log.toCompetitionStatus
    return `状态 ${fromLabel} → ${toLabel}`
  }
  if (log.kind === 1 && log.userAccountAction !== null && log.userAccountAction !== undefined) {
    return ACCOUNT_ACTION_LABELS[log.userAccountAction] ?? String(log.userAccountAction)
  }
  if (log.kind === 2 && log.fromLeaderboardVisibility !== null && log.fromLeaderboardVisibility !== undefined) {
    const fromLabel = VISIBILITY_LABELS[log.fromLeaderboardVisibility] ?? log.fromLeaderboardVisibility
    const toLabel = VISIBILITY_LABELS[log.toLeaderboardVisibility ?? -1] ?? log.toLeaderboardVisibility
    return `可见性 ${fromLabel} → ${toLabel}`
  }
  return log.reason ?? '—'
}

// ---------- 数据导出 ----------
const exports_ = ref<DataExport[]>([])
const exportsLoading = ref(false)
const creatingExport = ref(false)
const downloadingId = ref<string | null>(null)

async function loadExports(): Promise<void> {
  exportsLoading.value = true
  const { data, error } = await adminListPlatformAuditDataExports()
  exportsLoading.value = false
  if (error) {
    toast.error(parseApiError(error).message)
    return
  }
  exports_.value = data?.items ?? []
}

async function createExport(): Promise<void> {
  creatingExport.value = true
  const { error, response } = await adminCreatePlatformAuditDataExport()
  creatingExport.value = false
  if (error) {
    toast.error(response?.status === 409 ? '已有进行中的导出任务' : parseApiError(error).message)
    await loadExports()
    return
  }
  toast.success('导出任务已创建,完成后可下载')
  await loadExports()
}

async function downloadExport(item: DataExport): Promise<void> {
  if (!item.id) return
  downloadingId.value = item.id
  try {
    await downloadProtectedFile(`/api/v1/admin/data-exports/${item.id}/download`, item.fileName ?? 'platform-audit-export.zip')
    toast.success('导出文件已开始下载')
  }
  catch (e) {
    toast.error(parseApiError(e).message)
  }
  finally {
    downloadingId.value = null
  }
}

onMounted(() => {
  void loadMore()
  void loadExports()
})
</script>

<template>
  <div class="flex flex-col gap-6">
    <div class="flex flex-col gap-4">
      <div class="flex flex-wrap items-end gap-4">
        <Field>
          <FieldLabel for="audit-kind">类型</FieldLabel>
          <Select v-model="kind">
            <SelectTrigger id="audit-kind" class="w-40">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                <SelectItem value="all">全部类型</SelectItem>
                <SelectItem value="0">竞赛生命周期</SelectItem>
                <SelectItem value="1">账户生命周期</SelectItem>
                <SelectItem value="2">榜单可见性</SelectItem>
                <SelectItem value="3">竞赛事件</SelectItem>
              </SelectGroup>
            </SelectContent>
          </Select>
        </Field>
        <Field>
          <FieldLabel for="audit-actor">操作者 ID</FieldLabel>
          <Input id="audit-actor" v-model="actorId" class="w-64 font-mono text-sm" placeholder="可选" />
        </Field>
        <Field>
          <FieldLabel for="audit-competition">竞赛 ID</FieldLabel>
          <Input id="audit-competition" v-model="competitionId" class="w-64 font-mono text-sm" placeholder="可选" />
        </Field>
        <Field>
          <FieldLabel for="audit-from">起始时间</FieldLabel>
          <Input id="audit-from" v-model="from" type="datetime-local" />
        </Field>
        <Field>
          <FieldLabel for="audit-to">结束时间</FieldLabel>
          <Input id="audit-to" v-model="to" type="datetime-local" />
        </Field>
        <Button @click="applyFilters">查询</Button>
      </div>

      <Card v-if="loading && items.length === 0">
        <CardContent class="flex flex-col gap-3 pt-6">
          <Skeleton v-for="i in 6" :key="i" class="h-10 w-full" />
        </CardContent>
      </Card>

      <Empty v-else-if="initialized && items.length === 0">
        <EmptyHeader>
          <EmptyTitle>没有匹配的审计记录</EmptyTitle>
          <EmptyDescription>调整类型、时间范围或筛选条件。</EmptyDescription>
        </EmptyHeader>
      </Empty>

      <Card v-else>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead class="w-44">时间</TableHead>
              <TableHead class="w-32">类型</TableHead>
              <TableHead>主体</TableHead>
              <TableHead>内容</TableHead>
              <TableHead class="w-32">操作者</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-for="log in items" :key="log.id">
              <TableCell class="text-sm">
                <AdminDateTime :value="log.occurredAt" />
              </TableCell>
              <TableCell>
                <Badge variant="secondary">{{ KIND_LABELS[log.kind ?? 0] ?? log.kind }}</Badge>
                <Badge v-if="log.automatic" variant="outline" class="ml-1">自动</Badge>
              </TableCell>
              <TableCell class="max-w-56">
                <div class="truncate" :title="log.subjectId">{{ log.subjectDisplayName ?? log.subjectId }}</div>
              </TableCell>
              <TableCell class="max-w-md">
                <div class="truncate" :title="detailText(log)">{{ detailText(log) }}</div>
              </TableCell>
              <TableCell class="max-w-32 truncate font-mono text-xs text-muted-foreground" :title="log.actorId ?? ''">
                {{ log.actorId ?? '系统' }}
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </Card>

      <AdminLoadMore :loading="loading" :has-more="hasMore" @load="loadMore" />
    </div>

    <Separator />

    <Card>
      <CardHeader class="flex flex-row items-center justify-between gap-4">
        <div>
          <CardTitle>审计数据导出</CardTitle>
          <CardDescription>导出全量平台审计数据,任务完成后可下载,文件有过期时间。</CardDescription>
        </div>
        <div class="flex items-center gap-2">
          <Button variant="outline" :disabled="exportsLoading" @click="loadExports">
            <Spinner v-if="exportsLoading" data-icon="inline-start" />
            <RefreshCw v-else data-icon="inline-start" />
            刷新
          </Button>
          <Button :disabled="creatingExport" @click="createExport">
            <Spinner v-if="creatingExport" data-icon="inline-start" />
            <FilePlus2 v-else data-icon="inline-start" />
            新建导出
          </Button>
        </div>
      </CardHeader>
      <CardContent>
        <div v-if="exportsLoading && exports_.length === 0" class="flex flex-col gap-2">
          <Skeleton v-for="i in 2" :key="i" class="h-10 w-full" />
        </div>
        <Empty v-else-if="exports_.length === 0">
          <EmptyHeader>
            <EmptyTitle>暂无导出任务</EmptyTitle>
          </EmptyHeader>
        </Empty>
        <Table v-else>
          <TableHeader>
            <TableRow>
              <TableHead>申请时间</TableHead>
              <TableHead>状态</TableHead>
              <TableHead>文件名</TableHead>
              <TableHead>过期时间</TableHead>
              <TableHead class="text-right">操作</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-for="item in exports_" :key="item.id">
              <TableCell>
                <AdminDateTime :value="item.requestedAt" />
              </TableCell>
              <TableCell>
                <Badge :variant="EXPORT_STATUS[item.status ?? 0]?.variant ?? 'outline'">
                  {{ EXPORT_STATUS[item.status ?? 0]?.label ?? item.status }}
                </Badge>
                <p v-if="item.failureDetail" class="mt-1 text-xs text-destructive" :title="item.failureDetail">
                  {{ item.failureDetail }}
                </p>
              </TableCell>
              <TableCell class="max-w-56 truncate font-mono text-xs" :title="item.fileName ?? ''">
                {{ item.fileName ?? '—' }}
              </TableCell>
              <TableCell>
                <AdminDateTime :value="item.expiresAt" />
              </TableCell>
              <TableCell class="text-right">
                <Button
                  v-if="item.status === 2"
                  size="sm"
                  variant="outline"
                  :disabled="downloadingId === item.id"
                  @click="downloadExport(item)"
                >
                  <Spinner v-if="downloadingId === item.id" data-icon="inline-start" />
                  <Download v-else data-icon="inline-start" />
                  下载
                </Button>
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  </div>
</template>
