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
import { adminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
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
  DialogDescription,
} from '@/components/ui/dialog'
import {
  Search,
  RotateCw,
  Download,
  Eye,
  Loader2,
  FileJson,
  ShieldAlert,
  CheckCircle2,
  Clock,
  User as UserIcon,
  Globe,
} from 'lucide-vue-next'
import { vAutoAnimate } from '@formkit/auto-animate/vue'
import { toast } from 'vue-sonner'

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

const { data, isLoading, refetch, isFetching } = useQuery({
  queryKey: computed(() => queryKeys.adminAuditLogs(page.value, queryParams.value)),
  queryFn: async () => {
    return adminApi.auditLogs<AuditLogsResponse>(queryParams.value)
  },
})

const logs = computed(() => data.value?.items ?? [])
const total = computed(() => data.value?.total ?? 0)
const totalPages = computed(() => Math.ceil(total.value / pageSize))

function openDetail(log: AuditLogDto) {
  selectedLog.value = log
  detailDialog.value = true
}

function applyFilters() {
  page.value = 1
  refetch()
}

function previousPage() {
  if (page.value <= 1)
    return
  page.value -= 1
  refetch()
}

function nextPage() {
  if (page.value >= totalPages.value)
    return
  page.value += 1
  refetch()
}

function formatJson(json?: string) {
  if (!json) return ''
  try {
    return JSON.stringify(JSON.parse(json), null, 2)
  } catch {
    return json
  }
}

function httpMethodVariant(method?: string) {
  const normalized = method?.toUpperCase()
  if (normalized === 'DELETE') return 'destructive'
  if (normalized === 'POST' || normalized === 'PUT' || normalized === 'PATCH') return 'warning'
  if (normalized === 'GET') return 'info'
  return 'neutral'
}

function exportCsv() {
  const items = logs.value
  if (!items.length) return
  try {
    const headers = [
      t('admin.auditLogs.timestamp'),
      t('admin.auditLogs.user'),
      t('admin.auditLogs.action'),
      t('admin.auditLogs.endpoint'),
      t('admin.auditLogs.method'),
      t('admin.auditLogs.status'),
    ]
    const rows = items.map((l) => [
      l.timestamp,
      l.userName ?? '',
      l.action,
      l.endpointPath,
      l.httpMethod,
      l.exception ? t('common.error') : t('common.success'),
    ])
    const csv = [headers, ...rows]
      .map((r) => r.map((v) => `"${String(v).replace(/"/g, '""')}"`).join(','))
      .join('\n')
    const blob = new Blob([csv], { type: 'text/csv' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `audit-logs-${new Date().toISOString().slice(0, 10)}.csv`
    a.click()
    URL.revokeObjectURL(url)
    toast.success(t('admin.auditLogs.exportSuccess'))
  } catch {
    toast.error(t('admin.auditLogs.exportError'))
  }
}

const columnHelper = createColumnHelper<AuditLogDto>()

const columns = [
  columnHelper.accessor('timestamp', {
    header: t('admin.auditLogs.timestamp'),
    cell: (info) =>
      h('div', { class: 'flex items-center gap-2 whitespace-nowrap' }, [
        h(Clock, { class: 'size-3 text-muted-foreground' }),
        h('span', new Date(info.getValue()).toLocaleString()),
      ]),
    enableSorting: true,
  }),
  columnHelper.accessor('userName', {
    header: t('admin.auditLogs.user'),
    enableSorting: true,
    cell: (info) =>
      h('div', { class: 'flex items-center gap-2' }, [
        h(UserIcon, { class: 'size-3 text-muted-foreground' }),
        h('span', info.getValue() || 'Anonymous'),
      ]),
  }),
  columnHelper.accessor('action', {
    header: t('admin.auditLogs.action'),
    enableSorting: true,
    cell: (info) =>
      h(Badge, { variant: 'outline', class: 'font-mono text-[10px]' }, () => info.getValue()),
  }),
  columnHelper.accessor('endpointPath', {
    header: t('admin.auditLogs.endpoint'),
    cell: (info) =>
      h(
        'code',
        { class: 'text-[10px] bg-muted px-1 rounded truncate max-w-[150px] inline-block' },
        info.getValue(),
      ),
  }),
  columnHelper.accessor('exception', {
    header: t('admin.auditLogs.status'),
    cell: (info) => {
      const val = info.getValue()
      return h('div', { class: 'flex items-center justify-center' }, [
        val
          ? h(ShieldAlert, { class: 'size-4 text-danger' })
          : h(CheckCircle2, { class: 'size-4 text-success' }),
      ])
    },
  }),
]

const table = useVueTable({
  get data() {
    return logs.value
  },
  columns,
  state: {
    get sorting() {
      return sorting.value
    },
  },
  onSortingChange: (updater) => {
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
  <div class="noctf-admin-page">
    <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
      <div class="space-y-1">
        <h2 class="text-2xl font-bold tracking-tight">{{ t('admin.auditLogs.title') }}</h2>
        <p class="text-sm text-muted-foreground">{{ t('admin.auditLogs.subtitle') }}</p>
      </div>
      <div class="flex items-center gap-2">
        <Button variant="outline" size="sm" @click="refetch()" :disabled="isFetching">
          <RotateCw class="mr-2 size-4" :class="{ 'animate-spin': isFetching }" />
          {{ t('admin.auditLogs.refresh') }}
        </Button>
        <Button variant="outline" size="sm" @click="exportCsv()">
          <Download class="mr-2 size-4" />
          {{ t('admin.auditLogs.exportCsv') }}
        </Button>
      </div>
    </div>

    <!-- Filters -->
    <div class="noctf-filter-bar md:grid-cols-4 md:items-end">
      <div class="space-y-2">
        <label class="noctf-label ml-1">{{ t('admin.auditLogs.user') }}</label>
        <div class="relative">
          <Search class="absolute left-3 top-1/2 size-3.5 -translate-y-1/2 text-muted-foreground" />
          <Input
            v-model="filterUserName"
            :placeholder="t('admin.auditLogs.filterUser')"
            class="pl-9 h-9"
            @keyup.enter="applyFilters"
          />
        </div>
      </div>
      <div class="space-y-2">
        <label class="noctf-label ml-1">{{ t('admin.auditLogs.action') }}</label>
        <Input v-model="filterAction" :placeholder="t('admin.auditLogs.filterAction')" class="h-9" @keyup.enter="applyFilters" />
      </div>
      <div class="space-y-2">
        <label class="noctf-label ml-1">{{ t('admin.auditLogs.entityType') }}</label>
        <Input v-model="filterEntityType" :placeholder="t('admin.auditLogs.filterEntity')" class="h-9" @keyup.enter="applyFilters" />
      </div>
      <Button class="h-9" @click="applyFilters">{{ t('common.search') }}</Button>
    </div>

    <!-- Table -->
    <div class="noctf-table-shell">
      <Table>
        <TableHeader>
          <TableRow v-for="headerGroup in table.getHeaderGroups()" :key="headerGroup.id">
            <TableHead
              v-for="header in headerGroup.headers"
              :key="header.id"
              class="h-11 px-4 text-left align-middle font-medium text-muted-foreground"
              :class="header.column.getCanSort() ? 'cursor-pointer select-none' : ''"
              @click="header.column.getToggleSortingHandler()?.($event)"
            >
              <template v-if="!header.isPlaceholder">
                <div class="flex items-center gap-2">
                  <span>{{ header.column.columnDef.header as string }}</span>
                  <span v-if="header.column.getIsSorted() === 'asc'" class="text-[10px]">▲</span>
                  <span v-else-if="header.column.getIsSorted() === 'desc'" class="text-[10px]"
                    >▼</span
                  >
                </div>
              </template>
            </TableHead>
            <TableHead class="w-[60px] text-right px-4"></TableHead>
          </TableRow>
        </TableHeader>
        <TableBody v-auto-animate>
          <TableRow v-if="isLoading">
            <TableCell :colspan="columns.length + 1" class="h-24 text-center">
              <Loader2 class="size-5 animate-spin mx-auto text-muted-foreground" />
            </TableCell>
          </TableRow>
          <TableRow v-else-if="logs.length === 0">
            <TableCell
              :colspan="columns.length + 1"
              class="h-24 text-center text-muted-foreground text-sm"
            >
              {{ t('admin.auditLogs.empty') }}
            </TableCell>
          </TableRow>
          <TableRow
            v-else
            v-for="row in table.getRowModel().rows"
            :key="row.id"
            class="group hover:bg-muted/50 transition-colors"
          >
            <TableCell v-for="cell in row.getVisibleCells()" :key="cell.id" class="px-4 py-3">
              <component :is="() => cell.renderValue()" />
            </TableCell>
            <TableCell class="px-4 py-3 text-right">
              <Button
                variant="ghost"
                size="icon"
                class="size-8 text-muted-foreground hover:text-foreground"
                aria-label="Open audit log details"
                @click="openDetail(row.original)"
              >
                <Eye class="size-4" />
              </Button>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </div>

    <!-- Pagination -->
    <div class="noctf-table-footer">
      <p class="text-xs text-muted-foreground">
        {{ t('admin.auditLogs.pageTotal', { page, totalPages, total }) }}
      </p>
      <div class="flex items-center gap-2">
        <Button size="sm" variant="outline" :disabled="page <= 1" @click="previousPage">{{ t('common.previous') }}</Button>
        <Button
          size="sm"
          variant="outline"
          :disabled="page >= totalPages"
          @click="nextPage"
          >{{ t('common.next') }}</Button
        >
      </div>
    </div>

    <!-- Detail Dialog -->
    <Dialog v-model:open="detailDialog">
      <DialogContent class="sm:max-w-[700px] max-h-[85vh] flex flex-col p-0">
        <DialogHeader class="p-6 pb-0">
          <DialogTitle class="flex items-center gap-2 text-xl">
            <FileJson class="size-5 text-primary" />
            {{ t('admin.auditLogs.detailDialogTitle') }}
          </DialogTitle>
          <DialogDescription>{{ t('admin.auditLogs.detailDialogDescription') }}</DialogDescription>
        </DialogHeader>

        <div v-if="selectedLog" class="flex-1 overflow-y-auto p-6 pt-4 space-y-6">
          <div class="grid grid-cols-2 sm:grid-cols-3 gap-4">
            <div class="noctf-row-panel space-y-1">
              <span class="noctf-label block">{{ t('admin.auditLogs.user') }}</span>
              <p class="text-sm font-medium">{{ selectedLog.userName || 'Anonymous' }}</p>
            </div>
            <div class="noctf-row-panel space-y-1">
              <span class="noctf-label block">{{ t('admin.auditLogs.action') }}</span>
              <Badge variant="outline" class="mt-0.5 font-mono">{{ selectedLog.action }}</Badge>
            </div>
            <div class="noctf-row-panel space-y-1">
              <span class="noctf-label block">{{ t('admin.auditLogs.ip') }}</span>
              <div class="flex items-center gap-1.5 mt-0.5">
                <Globe class="size-3 text-muted-foreground" />
                <p class="text-sm font-mono">{{ selectedLog.ipAddress || '-' }}</p>
              </div>
            </div>
          </div>

          <!-- HTTP Detail -->
          <div class="overflow-hidden rounded-md border">
            <div class="bg-muted/50 px-4 py-2 border-b flex items-center justify-between">
              <span class="text-xs font-bold uppercase tracking-wider">{{
                t('admin.auditLogs.endpointDetails')
              }}</span>
              <Badge :variant="httpMethodVariant(selectedLog.httpMethod)">
                {{ selectedLog.httpMethod }}
              </Badge>
            </div>
            <div class="p-4 bg-muted/20">
              <code class="text-xs break-all text-primary font-mono font-bold">{{
                selectedLog.endpointPath
              }}</code>
            </div>
          </div>

          <!-- Exception if any -->
          <div v-if="selectedLog.exception" class="noctf-danger-panel space-y-2">
            <div class="flex items-center gap-2 text-danger">
              <ShieldAlert class="size-4" />
              <span class="text-xs font-bold uppercase tracking-wider">{{
                t('admin.auditLogs.exceptionLogged')
              }}</span>
            </div>
            <pre class="text-[10px] font-mono whitespace-pre-wrap break-all opacity-80">{{
              selectedLog.exception
            }}</pre>
          </div>

          <!-- JSON Payloads -->
          <div class="space-y-4">
            <div v-if="selectedLog.diff" class="space-y-2">
              <div class="flex items-center justify-between px-1">
                <span class="text-xs font-bold uppercase tracking-wider text-muted-foreground">{{
                  t('admin.auditLogs.diff')
                }}</span>
              </div>
              <pre
                class="max-h-60 overflow-auto rounded-md border bg-sidebar p-4 font-mono text-[11px] text-sidebar-foreground"
                >{{ formatJson(selectedLog.diff) }}</pre>
            </div>

            <div v-if="selectedLog.newValues" class="space-y-2">
              <span class="text-xs font-bold uppercase tracking-wider text-muted-foreground px-1">{{
                t('admin.auditLogs.newValues')
              }}</span>
              <pre
                class="max-h-60 overflow-auto rounded-md border bg-sidebar p-4 font-mono text-[11px] text-sidebar-foreground"
                >{{ formatJson(selectedLog.newValues) }}</pre>
            </div>

            <div v-if="selectedLog.oldValues" class="space-y-2">
              <span class="text-xs font-bold uppercase tracking-wider text-muted-foreground px-1">{{
                t('admin.auditLogs.oldValues')
              }}</span>
              <pre
                class="max-h-60 overflow-auto rounded-md border bg-sidebar p-4 font-mono text-[11px] text-sidebar-foreground"
                >{{ formatJson(selectedLog.oldValues) }}</pre>
            </div>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  </div>
</template>
