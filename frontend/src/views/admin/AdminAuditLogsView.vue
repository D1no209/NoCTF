<script setup lang="ts">
import { ref, computed, h } from 'vue'
import { useI18n } from 'vue-i18n'
import { useQuery } from '@tanstack/vue-query'
import {
  useVueTable,
  getCoreRowModel,
  getPaginationRowModel,
  getSortedRowModel,
  createColumnHelper,
  type SortingState,
} from '@tanstack/vue-table'
import { client } from '@/api/generated/client.gen'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Badge } from '@/components/ui/badge'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'

const { t } = useI18n()

interface AuditLogDto {
  id: string
  userId?: string
  userName?: string
  ipAddress?: string
  action: string
  entityType?: string
  endpointPath: string
  httpMethod: string
  newValues?: string
  oldValues?: string
  diff?: string
  timestamp: string
  exception?: string
}

interface AuditLogsResponse {
  items: AuditLogDto[]
  total: number
  page: number
  pageSize: number
}

const filterUserName = ref('')
const filterAction = ref('')
const filterEntityType = ref('')
const sorting = ref<SortingState>([])
const page = ref(1)
const pageSize = 50

const detailDialog = ref(false)
const selectedLog = ref<AuditLogDto | null>(null)

const queryParams = computed(() => ({
  userName: filterUserName.value || undefined,
  action: filterAction.value || undefined,
  entityType: filterEntityType.value || undefined,
  page: page.value,
  pageSize,
}))

const { data, isLoading, refetch } = useQuery({
  queryKey: ['admin-audit-logs', queryParams],
  queryFn: async () => {
    const params = new URLSearchParams()
    if (queryParams.value.userName) params.set('userName', queryParams.value.userName)
    if (queryParams.value.action) params.set('action', queryParams.value.action)
    if (queryParams.value.entityType) params.set('entityType', queryParams.value.entityType)
    params.set('page', String(queryParams.value.page))
    params.set('pageSize', String(queryParams.value.pageSize))
    const res = await client.get<{ 200: AuditLogsResponse }, unknown, false>({
      url: `/api/admin/audit-logs?${params.toString()}`,
    })
    return res.data ?? { items: [], total: 0, page: 1, pageSize }
  },
})

const logs = computed(() => data.value?.items ?? [])
const total = computed(() => data.value?.total ?? 0)
const totalPages = computed(() => Math.ceil(total.value / pageSize))

function openDetail(log: AuditLogDto) {
  selectedLog.value = log
  detailDialog.value = true
}

function formatJson(json?: string) {
  if (!json) return ''
  try {
    return JSON.stringify(JSON.parse(json), null, 2)
  } catch {
    return json
  }
}

function exportCsv() {
  const items = logs.value
  if (!items.length) return
  const headers = [t('admin.auditLogs.timestamp'), t('admin.auditLogs.user'), t('admin.auditLogs.action'), t('admin.auditLogs.endpoint'), t('admin.auditLogs.method'), t('admin.auditLogs.status')]
  const rows = items.map(l => [
    l.timestamp,
    l.userName ?? '',
    l.action,
    l.endpointPath,
    l.httpMethod,
    l.exception ? t('common.error') : t('common.success'),
  ])
  const csv = [headers, ...rows].map(r => r.map(v => `"${String(v).replace(/"/g, '""')}"`).join(',')).join('\n')
  const blob = new Blob([csv], { type: 'text/csv' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `audit-logs-${new Date().toISOString().slice(0, 10)}.csv`
  a.click()
  URL.revokeObjectURL(url)
}

const columnHelper = createColumnHelper<AuditLogDto>()

const columns = [
  columnHelper.accessor('timestamp', {
    header: t('admin.auditLogs.timestamp'),
    cell: info => new Date(info.getValue()).toLocaleString(),
    enableSorting: true,
  }),
  columnHelper.accessor('userName', { header: t('admin.auditLogs.user'), enableSorting: true }),
  columnHelper.accessor('action', { header: t('admin.auditLogs.action'), enableSorting: true }),
  columnHelper.accessor('entityType', { header: t('admin.auditLogs.entityType') }),
  columnHelper.accessor('endpointPath', { header: t('admin.auditLogs.endpoint') }),
  columnHelper.accessor('exception', {
    header: t('admin.auditLogs.status'),
    cell: info => {
      const val = info.getValue()
      return h(Badge, { variant: val ? 'destructive' : 'default' }, () => val ? t('common.error') : t('common.success'))
    },
  }),
  columnHelper.display({
    id: 'actions',
    header: '',
    cell: info => h(Button, { size: 'sm', variant: 'outline', onClick: () => openDetail(info.row.original) }, () => t('admin.auditLogs.details')),
  }),
]

const table = useVueTable({
  get data() { return logs.value },
  columns,
  state: {
    get sorting() { return sorting.value },
  },
  onSortingChange: updater => {
    sorting.value = typeof updater === 'function' ? updater(sorting.value) : updater
  },
  getCoreRowModel: getCoreRowModel(),
  getPaginationRowModel: getPaginationRowModel(),
  getSortedRowModel: getSortedRowModel(),
  manualPagination: true,
  pageCount: totalPages.value,
})
</script>

<template>
  <div class="p-6 space-y-4">
    <div class="flex items-center justify-between">
      <h1 class="text-2xl font-bold">{{ t('admin.auditLogs.title') }}</h1>
      <div class="flex gap-2">
        <Button variant="outline" size="sm" @click="refetch()">{{ t('admin.auditLogs.refresh') }}</Button>
        <Button variant="outline" size="sm" @click="exportCsv()">{{ t('admin.auditLogs.exportCsv') }}</Button>
      </div>
    </div>

    <!-- Filters -->
    <div class="flex flex-wrap gap-3">
      <Input v-model="filterUserName" :placeholder="t('admin.auditLogs.filterUser')" class="max-w-xs" @keyup.enter="page = 1; refetch()" />
      <Input v-model="filterAction" :placeholder="t('admin.auditLogs.filterAction')" class="max-w-xs" @keyup.enter="page = 1; refetch()" />
      <Input v-model="filterEntityType" :placeholder="t('admin.auditLogs.filterEntity')" class="max-w-xs" @keyup.enter="page = 1; refetch()" />
      <Button size="sm" @click="page = 1; refetch()">{{ t('common.search') }}</Button>
    </div>

    <!-- Table -->
    <div class="rounded-md border">
      <Table>
        <TableHeader>
          <TableRow v-for="headerGroup in table.getHeaderGroups()" :key="headerGroup.id">
            <TableHead
              v-for="header in headerGroup.headers"
              :key="header.id"
              :class="header.column.getCanSort() ? 'cursor-pointer select-none' : ''"
              @click="header.column.getToggleSortingHandler()?.($event)"
            >
              <template v-if="!header.isPlaceholder">
                {{ header.column.columnDef.header as string }}
                <span v-if="header.column.getIsSorted() === 'asc'"> ↑</span>
                <span v-else-if="header.column.getIsSorted() === 'desc'"> ↓</span>
              </template>
            </TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <template v-if="isLoading">
            <TableRow>
              <TableCell :colspan="columns.length" class="text-center text-muted-foreground py-8">{{ t('admin.auditLogs.loading') }}</TableCell>
            </TableRow>
          </template>
          <template v-else-if="logs.length === 0">
            <TableRow>
              <TableCell :colspan="columns.length" class="text-center text-muted-foreground py-8">{{ t('admin.auditLogs.empty') }}</TableCell>
            </TableRow>
          </template>
          <template v-else>
            <TableRow
              v-for="row in table.getRowModel().rows"
              :key="row.id"
              :class="row.original.exception ? 'bg-destructive/10' : ''"
            >
              <TableCell v-for="cell in row.getVisibleCells()" :key="cell.id">
                <component :is="() => cell.renderValue()" v-if="cell.column.id === 'exception' || cell.column.id === 'actions'" />
                <template v-else>{{ cell.getValue() }}</template>
              </TableCell>
            </TableRow>
          </template>
        </TableBody>
      </Table>
    </div>

    <!-- Pagination -->
    <div class="flex items-center justify-between">
      <span class="text-sm text-muted-foreground">
        {{ t('admin.auditLogs.pageTotal', { page, totalPages, total }) }}
      </span>
      <div class="flex gap-2">
        <Button size="sm" variant="outline" :disabled="page <= 1" @click="page--; refetch()">{{ t('common.previous') }}</Button>
        <Button size="sm" variant="outline" :disabled="page >= totalPages" @click="page++; refetch()">{{ t('common.next') }}</Button>
      </div>
    </div>

    <!-- Detail Dialog -->
    <Dialog v-model:open="detailDialog">
      <DialogContent class="max-w-2xl max-h-[80vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>{{ t('admin.auditLogs.detailDialogTitle') }}</DialogTitle>
        </DialogHeader>
        <div v-if="selectedLog" class="space-y-3 text-sm">
          <div class="grid grid-cols-2 gap-2">
            <div><span class="font-medium">{{ t('admin.auditLogs.timestamp') }}</span> {{ new Date(selectedLog.timestamp).toLocaleString() }}</div>
            <div><span class="font-medium">{{ t('admin.auditLogs.user') }}</span> {{ selectedLog.userName ?? t('admin.auditLogs.anonymous') }}</div>
            <div><span class="font-medium">{{ t('admin.auditLogs.action') }}</span> {{ selectedLog.action }}</div>
            <div><span class="font-medium">{{ t('admin.auditLogs.method') }}</span> {{ selectedLog.httpMethod }}</div>
            <div><span class="font-medium">{{ t('admin.auditLogs.endpoint') }}</span> {{ selectedLog.endpointPath }}</div>
            <div><span class="font-medium">{{ t('admin.auditLogs.ip') }}</span> {{ selectedLog.ipAddress ?? '-' }}</div>
          </div>
          <div v-if="selectedLog.exception" class="p-2 bg-destructive/10 rounded text-destructive text-xs">
            <span class="font-medium">{{ t('admin.auditLogs.error') }}</span> {{ selectedLog.exception }}
          </div>
          <div v-if="selectedLog.newValues">
            <p class="font-medium mb-1">{{ t('admin.auditLogs.newValues') }}</p>
            <pre class="bg-muted p-2 rounded text-xs overflow-auto max-h-40">{{ formatJson(selectedLog.newValues) }}</pre>
          </div>
          <div v-if="selectedLog.oldValues">
            <p class="font-medium mb-1">{{ t('admin.auditLogs.oldValues') }}</p>
            <pre class="bg-muted p-2 rounded text-xs overflow-auto max-h-40">{{ formatJson(selectedLog.oldValues) }}</pre>
          </div>
          <div v-if="selectedLog.diff">
            <p class="font-medium mb-1">{{ t('admin.auditLogs.diff') }}</p>
            <pre class="bg-muted p-2 rounded text-xs overflow-auto max-h-40">{{ formatJson(selectedLog.diff) }}</pre>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  </div>
</template>
