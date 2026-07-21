<script setup lang="ts">
import { ref, computed, h } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  useVueTable,
  getCoreRowModel,
  getPaginationRowModel,
  getSortedRowModel,
  createColumnHelper,
  type SortingState,
} from '@tanstack/vue-table'
import { Button } from '@/ui-v1/components/ui/button'
import { Badge } from '@/ui-v1/components/ui/badge'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/ui-v1/components/ui/table'
import { RotateCw, Download, Eye, Loader2, ShieldAlert, CheckCircle2, Clock, User as UserIcon } from 'lucide-vue-next'
import { vAutoAnimate } from '@formkit/auto-animate/vue'
import { toast } from 'vue-sonner'
import AdminAuditLogDetailDialog from '@/ui-v1/components/admin/audit-logs/AdminAuditLogDetailDialog.vue'
import AdminAuditLogsFilters from '@/ui-v1/components/admin/audit-logs/AdminAuditLogsFilters.vue'
import { Card } from '@/ui-v1/components/ui/card'
import { useAdminAuditLogsPage, type AuditLogDto } from '@/features/admin/useAdminAuditLogsPage'

const { t } = useI18n()

const sorting = ref<SortingState>([])

const detailDialog = ref(false)
const selectedLog = ref<AuditLogDto | null>(null)

const {
  filterUserName,
  filterAction,
  filterEntityType,
  page,
  pageSize,
  logs,
  total,
  isLoading,
  isFetching,
  refetch,
} = useAdminAuditLogsPage()

const totalPages = computed(() => Math.ceil(total.value / pageSize))

function openDetail(log: AuditLogDto) {
  selectedLog.value = log
  detailDialog.value = true
}

function exportCsv() {
  const items = logs.value
  if (!items.length) return
  try {
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
    toast.success(t('admin.auditLogs.exportSuccess'))
  } catch {
    toast.error(t('admin.auditLogs.exportError'))
  }
}

const columnHelper = createColumnHelper<AuditLogDto>()

const columns = [
  columnHelper.accessor('timestamp', {
    header: t('admin.auditLogs.timestamp'),
    cell: info => h('div', { class: 'flex items-center gap-2 whitespace-nowrap' }, [
      h(Clock, { class: 'size-3 text-muted-foreground' }),
      h('span', new Date(info.getValue()).toLocaleString())
    ]),
    enableSorting: true,
  }),
  columnHelper.accessor('userName', { 
    header: t('admin.auditLogs.user'), 
    enableSorting: true,
    cell: info => h('div', { class: 'flex items-center gap-2' }, [
      h(UserIcon, { class: 'size-3 text-muted-foreground' }),
      h('span', info.getValue() || t('admin.auditLogs.anonymous'))
    ])
  }),
  columnHelper.accessor('action', { 
    header: t('admin.auditLogs.action'), 
    enableSorting: true,
    cell: info => h(Badge, { variant: 'outline', class: 'font-mono text-[10px]' }, () => info.getValue())
  }),
  columnHelper.accessor('endpointPath', { 
    header: t('admin.auditLogs.endpoint'),
    cell: info => h('code', { class: 'text-[10px] bg-muted px-1 rounded truncate max-w-[150px] inline-block' }, info.getValue())
  }),
  columnHelper.accessor('exception', {
    header: t('admin.auditLogs.status'),
    cell: info => {
      const val = info.getValue()
      return h('div', { class: 'flex items-center justify-center' }, [
        val 
          ? h(ShieldAlert, { class: 'size-4 text-destructive' }) 
          : h(CheckCircle2, { class: 'size-4 text-[var(--semantic-success)]' })
      ])
    },
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
  <div class="space-y-6">
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

    <AdminAuditLogsFilters
      v-model:filter-user-name="filterUserName"
      v-model:filter-action="filterAction"
      v-model:filter-entity-type="filterEntityType"
      @search="page = 1; refetch()"
    />

    <!-- Table -->
    <Card class="p-0 overflow-hidden">
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
                  <span v-else-if="header.column.getIsSorted() === 'desc'" class="text-[10px]">▼</span>
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
            <TableCell :colspan="columns.length + 1" class="h-24 text-center text-muted-foreground text-sm">
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
              <Button variant="ghost" size="icon" class="size-8 opacity-0 group-hover:opacity-100 transition-opacity" @click="openDetail(row.original)">
                <Eye class="size-4" />
              </Button>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </Card>

    <!-- Pagination -->
    <div class="flex flex-col gap-3 text-xs text-muted-foreground sm:flex-row sm:items-center sm:justify-between">
      <p class="text-xs text-muted-foreground">
        {{ t('admin.auditLogs.pageTotal', { page, totalPages, total }) }}
      </p>
      <div class="flex items-center gap-2">
        <Button size="sm" variant="outline" :disabled="page <= 1" @click="page--; refetch()">{{ t('common.previous') }}</Button>
        <Button size="sm" variant="outline" :disabled="page >= totalPages" @click="page++; refetch()">{{ t('common.next') }}</Button>
      </div>
    </div>

    <AdminAuditLogDetailDialog
      v-model:open="detailDialog"
      :selected-log="selectedLog"
    />
  </div>
</template>
