<script setup lang="ts">
import { toast } from 'vue-sonner'
import {
  adminCreateCompetitionDataExport,
  adminListCompetitionDataExports,
} from '~/api'
import type { NoCtfapiEndpointsAdministrationDataExportsDataExportResponse } from '~/api'
import { getAccessToken } from '~/lib/session'
import { useCompetitionAdmin } from '~/lib/admin-competition'

definePageMeta({ middleware: 'auth' })

const { competitionId, canWrite } = useCompetitionAdmin()

// ---- Authenticated file download helper ----
async function downloadFile(url: string, fallbackName: string) {
  const token = getAccessToken()
  const response = await fetch(url, {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
    credentials: 'same-origin',
  })
  if (!response.ok) {
    let message = `下载失败(${response.status})`
    try {
      const problem = await response.json()
      message = problem.detail ?? problem.title ?? message
    }
    catch { /* not json */ }
    throw new Error(message)
  }
  const blob = await response.blob()
  const disposition = response.headers.get('Content-Disposition') ?? ''
  const match = /filename\*?=(?:UTF-8''|")?([^";]+)/i.exec(disposition)
  const name = match?.[1] ? decodeURIComponent(match[1]) : fallbackName
  const link = document.createElement('a')
  link.href = URL.createObjectURL(blob)
  link.download = name
  link.click()
  URL.revokeObjectURL(link.href)
}

// ---- Events JSONL export ----
const eventsFrom = ref('')
const eventsTo = ref('')
const exportingEvents = ref(false)

async function exportEvents() {
  const from = localInputToIso(eventsFrom.value)
  const to = localInputToIso(eventsTo.value)
  if (!from || !to) {
    toast.error('请选择导出时间范围')
    return
  }
  exportingEvents.value = true
  try {
    const params = new URLSearchParams({ from, to })
    await downloadFile(
      `/api/v1/admin/competitions/${competitionId}/events/export?${params}`,
      `competition-${competitionId}-events.jsonl`,
    )
    toast.success('事件导出已开始下载')
  }
  catch (e) {
    toast.error(e instanceof Error ? e.message : '导出失败')
  }
  finally {
    exportingEvents.value = false
  }
}

// ---- Data exports ----
const exports_ = ref<NoCtfapiEndpointsAdministrationDataExportsDataExportResponse[]>([])
const loadingExports = ref(true)
const includeProtectedFlags = ref(false)
const exportReason = ref('')
const creating = ref(false)
const downloadingId = ref<string | null>(null)

async function loadExports() {
  loadingExports.value = true
  const { data, error } = await adminListCompetitionDataExports({ path: { competitionId } })
  if (!error) exports_.value = data?.items ?? []
  loadingExports.value = false
}

async function createExport() {
  creating.value = true
  try {
    const { error } = await adminCreateCompetitionDataExport({
      path: { competitionId },
      body: {
        includeProtectedFlags: includeProtectedFlags.value,
        reason: exportReason.value.trim() || null,
      },
    })
    if (error) throw error
    toast.success('导出任务已创建,完成后可下载')
    exportReason.value = ''
    await loadExports()
  }
  catch (e) {
    toast.error(parseApiError(e).message)
  }
  finally {
    creating.value = false
  }
}

async function downloadExport(item: NoCtfapiEndpointsAdministrationDataExportsDataExportResponse) {
  if (!item.id) return
  downloadingId.value = item.id
  try {
    await downloadFile(`/api/v1/admin/data-exports/${item.id}/download`, item.fileName ?? `export-${item.id}.zip`)
  }
  catch (e) {
    toast.error(e instanceof Error ? e.message : '下载失败')
  }
  finally {
    downloadingId.value = null
  }
}

const hasActive = computed(() => exports_.value.some(e => e.status === 'Queued' || e.status === 'Processing'))

onMounted(loadExports)
</script>

<template>
  <div class="flex flex-col gap-6">
    <Card>
      <CardHeader>
        <CardTitle>事件导出(JSONL)</CardTitle>
        <CardDescription>按时间范围导出竞赛事件流,每行一个 JSON 事件</CardDescription>
      </CardHeader>
      <CardContent class="flex flex-wrap items-end gap-3">
        <Field>
          <FieldLabel for="ev-from">起始时间</FieldLabel>
          <Input id="ev-from" v-model="eventsFrom" type="datetime-local" />
        </Field>
        <Field>
          <FieldLabel for="ev-to">结束时间</FieldLabel>
          <Input id="ev-to" v-model="eventsTo" type="datetime-local" />
        </Field>
        <Button :disabled="exportingEvents" @click="exportEvents">
          <Spinner v-if="exportingEvents" data-icon="inline-start" />
          导出事件
        </Button>
      </CardContent>
    </Card>

    <Card>
      <CardHeader>
        <CardTitle>数据导出</CardTitle>
        <CardDescription>创建竞赛数据归档导出任务,生成后可下载</CardDescription>
      </CardHeader>
      <CardContent class="flex flex-col gap-4">
        <div v-if="canWrite" class="flex flex-wrap items-end gap-3">
          <Field>
            <FieldLabel for="ex-reason">导出原因(可选)</FieldLabel>
            <Input id="ex-reason" v-model="exportReason" class="w-72" placeholder="记入审计" />
          </Field>
          <Field orientation="horizontal">
            <Checkbox id="ex-flags" v-model="includeProtectedFlags" />
            <FieldLabel for="ex-flags" class="font-normal">包含受保护的 Flag</FieldLabel>
          </Field>
          <Button :disabled="creating" @click="createExport">
            <Spinner v-if="creating" data-icon="inline-start" />
            创建导出任务
          </Button>
        </div>

        <div class="flex items-center justify-between">
          <h3 class="text-sm font-medium">导出任务</h3>
          <Button variant="ghost" size="sm" @click="loadExports">
            <Spinner v-if="loadingExports" data-icon="inline-start" />
            刷新
          </Button>
        </div>
        <Alert v-if="hasActive">
          <AlertDescription>有导出任务正在处理中,可稍后刷新查看进度</AlertDescription>
        </Alert>
        <Skeleton v-if="loadingExports && exports_.length === 0" class="h-32 w-full" />
        <Empty v-else-if="exports_.length === 0">
          <EmptyHeader>
            <EmptyTitle>暂无导出任务</EmptyTitle>
          </EmptyHeader>
        </Empty>
        <Table v-else>
          <TableHeader>
            <TableRow>
              <TableHead class="w-44">创建时间</TableHead>
              <TableHead class="w-24">状态</TableHead>
              <TableHead>含 Flag</TableHead>
              <TableHead>原因</TableHead>
              <TableHead>文件名</TableHead>
              <TableHead class="w-44">过期时间</TableHead>
              <TableHead class="w-28 text-right">操作</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-for="e in exports_" :key="e.id">
              <TableCell>{{ adminFormatDateTime(e.requestedAt) }}</TableCell>
              <TableCell>
                  <Badge :variant="e.status === 'Available' ? 'default' : e.status === 'Failed' ? 'destructive' : 'secondary'">
                  {{ enumLabel(DataExportStatusLabel, e.status) }}
                </Badge>
              </TableCell>
              <TableCell>{{ e.includeProtectedFlags ? '是' : '否' }}</TableCell>
              <TableCell class="max-w-40 truncate">{{ e.reason ?? '—' }}</TableCell>
              <TableCell class="max-w-48 truncate font-mono text-xs">
                {{ e.fileName ?? '—' }}
                <span v-if="e.failureDetail" class="block text-destructive">{{ e.failureDetail }}</span>
              </TableCell>
              <TableCell>{{ adminFormatDateTime(e.expiresAt) }}</TableCell>
              <TableCell class="text-right">
                <Button
                  v-if="e.status === 'Available'"
                  variant="outline"
                  size="sm"
                  :disabled="downloadingId === e.id"
                  @click="downloadExport(e)"
                >
                  <Spinner v-if="downloadingId === e.id" data-icon="inline-start" />
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
