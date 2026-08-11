<script setup lang="ts">
import { Download, FilePlus2, RefreshCw } from '@lucide/vue'
import { toast } from 'vue-sonner'
import {
  adminCreatePlatformAuditDataExport,
  adminDownloadDataExport,
  adminListPlatformAuditDataExports,
  adminPlatformListAuditLogs,
} from '~/api'
import type {
  NoCtfapiEndpointsAdministrationDataExportsDataExportResponse,
  NoCtfapiEndpointsAdministrationPlatformPlatformAuditLogResponse,
  NoCtfapiEndpointsAdministrationPlatformPlatformAuditKindProtocol,
} from '~/api'
import { downloadProtectedFile } from '~/utils/download'

definePageMeta({ middleware: 'platform-admin' })

type AuditLog = NoCtfapiEndpointsAdministrationPlatformPlatformAuditLogResponse
type DataExport = NoCtfapiEndpointsAdministrationDataExportsDataExportResponse

const KIND_LABELS: Record<string, string> = {
  CompetitionLifecycle: translate("竞赛生命周期"), UserAccountLifecycle: translate("账户生命周期"), CompetitionLeaderboardVisibility: translate("榜单可见性"), CompetitionEvent: translate("竞赛事件"),
}
const EXPORT_STATUS: Record<string, { label: string; variant: 'secondary' | 'outline' | 'destructive' | 'default' }> = {
  Queued: { label: translate("排队中"), variant: 'outline' }, Processing: { label: translate("处理中"), variant: 'secondary' }, Available: { label: translate("可下载"), variant: 'default' }, Failed: { label: translate("失败"), variant: 'destructive' }, Expired: { label: translate("已过期"), variant: 'outline' },
}
const COMPETITION_STATUS_LABELS: Record<string, string> = {
  Draft: translate("草稿"), Visible: translate("可见"), Published: translate("已发布"), Running: translate("进行中"), Paused: translate("已暂停"), Finished: translate("已结束"),
}
const VISIBILITY_LABELS: Record<string, string> = { Normal: translate("正常"), Frozen: translate("冻结"), Blackout: translate("封榜") }
const ACCOUNT_ACTION_LABELS: Record<string, string> = { Banned: translate("封禁"), Disabled: translate("禁用"), Anonymized: translate("匿名化"), PhysicallyDeleted: translate("物理删除") }

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

const { items, loading, error: listError, hasMore, initialized, loadMore, reset } = useCursorPagination<AuditLog>(async (cursor) => {
  const { data, error } = await adminPlatformListAuditLogs({
    query: {
      kind: kind.value === 'all'
        ? null
        : kind.value as NoCtfapiEndpointsAdministrationPlatformPlatformAuditKindProtocol,
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
  reset({ preserveItems: true })
  void loadMore()
}

function detailText(log: AuditLog): string {
  if (log.kind === 'CompetitionLifecycle' && log.fromCompetitionStatus !== null && log.fromCompetitionStatus !== undefined) {
    const fromLabel = COMPETITION_STATUS_LABELS[String(log.fromCompetitionStatus)] ?? log.fromCompetitionStatus
    const toLabel = COMPETITION_STATUS_LABELS[String(log.toCompetitionStatus)] ?? log.toCompetitionStatus
    return translate('状态 {from} → {to}', { from: fromLabel ?? '-', to: toLabel ?? '-' })
  }
  if (log.kind === 'UserAccountLifecycle' && log.userAccountAction !== null && log.userAccountAction !== undefined) {
    return ACCOUNT_ACTION_LABELS[String(log.userAccountAction)] ?? String(log.userAccountAction)
  }
  if (log.kind === 'CompetitionLeaderboardVisibility' && log.fromLeaderboardVisibility !== null && log.fromLeaderboardVisibility !== undefined) {
    const fromLabel = VISIBILITY_LABELS[String(log.fromLeaderboardVisibility)] ?? log.fromLeaderboardVisibility
    const toLabel = VISIBILITY_LABELS[String(log.toLeaderboardVisibility)] ?? log.toLeaderboardVisibility
    return translate('可见性 {from} → {to}', { from: fromLabel ?? '-', to: toLabel ?? '-' })
  }
  return log.reason ?? '-'
}

// ---------- 数据导出 ----------
const exports_ = ref<DataExport[]>([])
const exportsLoading = ref(false)
const exportsError = ref<string | null>(null)
const creatingExport = ref(false)
const downloadingId = ref<string | null>(null)

async function loadExports(): Promise<void> {
  exportsLoading.value = true
  const { data, error } = await adminListPlatformAuditDataExports()
  exportsLoading.value = false
  if (error || !data) {
    exportsError.value = parseApiError(error).message
    return
  }
  exportsError.value = null
  exports_.value = data.items ?? []
}

async function createExport(): Promise<void> {
  creatingExport.value = true
  const { error, response } = await adminCreatePlatformAuditDataExport()
  creatingExport.value = false
  if (error) {
    toast.error(response?.status === 409 ? translate("已有进行中的导出任务") : parseApiError(error).message)
    await loadExports()
    return
  }
  toast.success(translate("导出任务已创建,完成后可下载"))
  await loadExports()
}

async function downloadExport(item: DataExport): Promise<void> {
  const dataExportId = item.id
  if (!dataExportId) return
  downloadingId.value = dataExportId
  try {
    await downloadProtectedFile(
      () => adminDownloadDataExport({
        path: { dataExportId },
        parseAs: 'blob',
      }),
      item.fileName ?? 'platform-audit-export.zip',
    )
    toast.success(translate("导出文件已开始下载"))
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
          <FieldLabel for="audit-kind">{{ $t('类型') }}</FieldLabel>
          <Select v-model="kind">
            <SelectTrigger id="audit-kind" class="w-40">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                <SelectItem value="all">{{ $t('全部类型') }}</SelectItem>
                <SelectItem value="CompetitionLifecycle">{{ $t('竞赛生命周期') }}</SelectItem>
                <SelectItem value="UserAccountLifecycle">{{ $t('账户生命周期') }}</SelectItem>
                <SelectItem value="CompetitionLeaderboardVisibility">{{ $t('榜单可见性') }}</SelectItem>
                <SelectItem value="CompetitionEvent">{{ $t('竞赛事件') }}</SelectItem>
              </SelectGroup>
            </SelectContent>
          </Select>
        </Field>
        <Field>
          <FieldLabel for="audit-actor">{{ $t('操作者 ID') }}</FieldLabel>
          <Input id="audit-actor" v-model="actorId" class="w-64 font-mono text-sm" :placeholder="$t('可选')" />
        </Field>
        <Field>
          <FieldLabel for="audit-competition">{{ $t('竞赛 ID') }}</FieldLabel>
          <Input id="audit-competition" v-model="competitionId" class="w-64 font-mono text-sm" :placeholder="$t('可选')" />
        </Field>
        <Field>
          <FieldLabel for="audit-from">{{ $t('起始时间') }}</FieldLabel>
          <Input id="audit-from" v-model="from" type="datetime-local" />
        </Field>
        <Field>
          <FieldLabel for="audit-to">{{ $t('结束时间') }}</FieldLabel>
          <Input id="audit-to" v-model="to" type="datetime-local" />
        </Field>
        <Button @click="applyFilters">{{ $t('查询') }}</Button>
      </div>

      <Alert v-if="listError" variant="destructive">
        <AlertDescription>{{ listError.message }}</AlertDescription>
      </Alert>

      <Card v-if="loading && items.length === 0">
        <CardContent class="flex flex-col gap-3 pt-6">
          <Skeleton v-for="i in 6" :key="i" class="h-10 w-full" />
        </CardContent>
      </Card>

      <Empty v-else-if="!listError && initialized && items.length === 0" class="border border-dashed py-12">
        <EmptyHeader>
          <EmptyTitle>{{ $t('没有匹配的审计记录') }}</EmptyTitle>
          <EmptyDescription>{{ $t('调整类型、时间范围或筛选条件。') }}</EmptyDescription>
        </EmptyHeader>
      </Empty>

      <Card v-else-if="items.length > 0">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead class="w-44">{{ $t('时间') }}</TableHead>
              <TableHead class="w-32">{{ $t('类型') }}</TableHead>
              <TableHead>{{ $t('主体') }}</TableHead>
              <TableHead>{{ $t('内容') }}</TableHead>
              <TableHead class="w-32">{{ $t('操作者') }}</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-for="log in items" :key="log.id">
              <TableCell class="text-sm">
                <AdminDateTime :value="log.occurredAt" />
              </TableCell>
              <TableCell>
                <Badge variant="secondary">{{ KIND_LABELS[String(log.kind)] ?? log.kind }}</Badge>
                <Badge v-if="log.automatic" variant="outline" class="ml-1">{{ $t('自动') }}</Badge>
              </TableCell>
              <TableCell class="max-w-56">
                <div class="truncate" :title="log.subjectId">{{ log.subjectDisplayName ?? log.subjectId }}</div>
              </TableCell>
              <TableCell class="max-w-md">
                <div class="truncate" :title="detailText(log)">{{ detailText(log) }}</div>
              </TableCell>
              <TableCell class="max-w-32 truncate font-mono text-xs text-muted-foreground" :title="log.actorId ?? ''">
                {{ log.actorId ?? $t('系统') }}
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
          <CardTitle>{{ $t('审计数据导出') }}</CardTitle>
          <CardDescription>{{ $t('导出全量平台审计数据,任务完成后可下载,文件有过期时间。') }}</CardDescription>
        </div>
        <div class="flex items-center gap-2">
          <Button variant="outline" :disabled="exportsLoading" @click="loadExports">
            <Spinner v-if="exportsLoading" data-icon="inline-start" />
            <RefreshCw v-else data-icon="inline-start" /> {{ $t('刷新') }} </Button>
          <Button :disabled="creatingExport" @click="createExport">
            <Spinner v-if="creatingExport" data-icon="inline-start" />
            <FilePlus2 v-else data-icon="inline-start" /> {{ $t('新建导出') }} </Button>
        </div>
      </CardHeader>
      <CardContent>
        <Alert v-if="exportsError" variant="destructive" class="mb-3">
          <AlertDescription>{{ exportsError }}</AlertDescription>
        </Alert>
        <div v-if="exportsLoading && exports_.length === 0" class="flex flex-col gap-2">
          <Skeleton v-for="i in 2" :key="i" class="h-10 w-full" />
        </div>
        <Empty v-else-if="!exportsError && exports_.length === 0" class="border border-dashed py-12">
          <EmptyHeader>
            <EmptyTitle>{{ $t('暂无导出任务') }}</EmptyTitle>
          </EmptyHeader>
        </Empty>
        <Table v-else-if="exports_.length > 0">
          <TableHeader>
            <TableRow>
              <TableHead>{{ $t('申请时间') }}</TableHead>
              <TableHead>{{ $t('状态') }}</TableHead>
              <TableHead>{{ $t('文件名') }}</TableHead>
              <TableHead>{{ $t('过期时间') }}</TableHead>
              <TableHead class="text-right">{{ $t('操作') }}</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-for="item in exports_" :key="item.id">
              <TableCell>
                <AdminDateTime :value="item.requestedAt" />
              </TableCell>
              <TableCell>
                <Badge :variant="EXPORT_STATUS[String(item.status)]?.variant ?? 'outline'">
                  {{ EXPORT_STATUS[String(item.status)]?.label ?? item.status }}
                </Badge>
                <p v-if="item.failureDetail" class="mt-1 text-xs text-destructive" :title="item.failureDetail">
                  {{ item.failureDetail }}
                </p>
              </TableCell>
              <TableCell class="max-w-56 truncate font-mono text-xs" :title="item.fileName ?? ''">
                {{ item.fileName ?? '-' }}
              </TableCell>
              <TableCell>
                <AdminDateTime :value="item.expiresAt" />
              </TableCell>
              <TableCell class="text-right">
                <Button
                  v-if="item.status === 'Available'"
                  size="sm"
                  variant="outline"
                  :disabled="downloadingId === item.id"
                  @click="downloadExport(item)"
                >
                  <Spinner v-if="downloadingId === item.id" data-icon="inline-start" />
                  <Download v-else data-icon="inline-start" /> {{ $t('下载') }} </Button>
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  </div>
</template>
