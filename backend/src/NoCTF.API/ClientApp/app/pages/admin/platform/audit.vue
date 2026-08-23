<script setup lang="ts">
import { Download } from '@lucide/vue'
import { toast } from 'vue-sonner'
import {
  adminExportPlatformAuditArchive,
  adminPlatformListAuditLogs,
} from '~/api'
import type {
  NoCtfapiEndpointsAdministrationPlatformPlatformAuditLogResponse,
  NoCtfapiEndpointsAdministrationPlatformPlatformAuditKindProtocol,
} from '~/api'
import { downloadSdkFile } from '~/utils/download'
import { platformAuditActionText } from '~/utils/platform-audit'

definePageMeta({ middleware: 'platform-admin' })

type AuditLog = NoCtfapiEndpointsAdministrationPlatformPlatformAuditLogResponse

const KIND_LABELS: Record<string, string> = {
  CompetitionLifecycle: '竞赛生命周期', UserAccountLifecycle: '账户生命周期', PlatformAdministration: '平台管理', CompetitionAdministration: '竞赛管理', CompetitionLeaderboardVisibility: '榜单可见性', CompetitionEvent: '竞赛事件',
}

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

const exportingArchive = ref(false)

async function exportArchive(): Promise<void> {
  if (exportingArchive.value) return
  exportingArchive.value = true
  try {
    await downloadSdkFile(
      adminExportPlatformAuditArchive({
        body: {
          kind: kind.value === 'all'
            ? null
            : kind.value as NoCtfapiEndpointsAdministrationPlatformPlatformAuditKindProtocol,
          actorId: actorId.value.trim() || null,
          competitionId: competitionId.value.trim() || null,
          from: toIso(from.value),
          to: toIso(to.value),
        },
        parseAs: 'blob',
      }),
      'platform-audit-archive.zip',
    )
    toast.success(translate('审计归档已开始下载'))
  }
  catch (e) {
    toast.error(parseApiError(e).message)
  }
  finally {
    exportingArchive.value = false
  }
}

onMounted(() => {
  void loadMore()
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
                <SelectItem value="PlatformAdministration">{{ $t('平台管理') }}</SelectItem>
                <SelectItem value="CompetitionAdministration">{{ $t('竞赛管理') }}</SelectItem>
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
              <TableHead>{{ $t('操作') }}</TableHead>
              <TableHead class="w-32">{{ $t('操作者') }}</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-for="log in items" :key="log.id">
              <TableCell class="text-sm">
                <AdminDateTime :value="log.occurredAt" />
              </TableCell>
              <TableCell>
                <Badge variant="secondary">{{ KIND_LABELS[String(log.kind)] ? $t(KIND_LABELS[String(log.kind)]!) : log.kind }}</Badge>
                <Badge v-if="log.automatic" variant="outline" class="ml-1">{{ $t('自动') }}</Badge>
              </TableCell>
              <TableCell class="max-w-56">
                <div class="truncate" :title="log.subjectId">{{ log.subjectDisplayName ?? log.subjectId }}</div>
              </TableCell>
              <TableCell class="max-w-md">
                <div class="font-medium" :title="platformAuditActionText(log)">{{ platformAuditActionText(log) }}</div>
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
          <CardDescription>{{ $t('按当前筛选条件同步生成审计归档并立即下载。') }}</CardDescription>
        </div>
        <Button :disabled="exportingArchive" @click="exportArchive">
          <Spinner v-if="exportingArchive" data-icon="inline-start" />
          <Download v-else data-icon="inline-start" /> {{ $t('下载审计归档') }}
        </Button>
      </CardHeader>
    </Card>
  </div>
</template>
