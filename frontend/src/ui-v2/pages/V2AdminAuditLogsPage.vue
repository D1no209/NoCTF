<script setup lang="ts">
import { computed, ref } from 'vue'
import CommandAdminAuditLogsWorkspace, {
  type CommandAdminAuditLog,
} from '../components/CommandAdminAuditLogsWorkspace.vue'
import { useAdminAuditLogsPage } from '@/features/admin/useAdminAuditLogsPage'

const detailDialogOpen = ref(false)
const selectedLog = ref<CommandAdminAuditLog | null>(null)
const operationMessage = ref('')
const operationTone = ref<'success' | 'danger'>('success')

const {
  filterUserName,
  filterAction,
  filterEntityType,
  page,
  pageSize,
  logs: auditLogs,
  total,
  isLoading,
  isError,
  isFetching,
  error,
  refetch,
  search,
} = useAdminAuditLogsPage()

const logs = computed<CommandAdminAuditLog[]>(() => auditLogs.value
  .map(log => ({
    id: log.id,
    userName: log.userName ?? '',
    ipAddress: log.ipAddress ?? '',
    action: log.action ?? '',
    entityType: log.entityType ?? '',
    endpointPath: log.endpointPath ?? '',
    httpMethod: log.httpMethod ?? '',
    newValues: log.newValues,
    oldValues: log.oldValues,
    diff: log.diff,
    timestamp: log.timestamp ?? '',
    exception: log.exception,
  })))

const totalPages = computed(() => Math.max(1, Math.ceil(total.value / pageSize)))

const workspaceState = computed<'loading' | 'error' | 'ready'>(() => {
  if (isLoading.value)
    return 'loading'
  if (isError.value)
    return 'error'
  return 'ready'
})

const errorMessage = computed(() => {
  const value = error.value
  return value instanceof Error ? value.message : undefined
})

function refresh() {
  void refetch()
}

function openDetail(log: CommandAdminAuditLog) {
  selectedLog.value = log
  detailDialogOpen.value = true
}

function exportCsv() {
  const items = logs.value
  if (!items.length)
    return
  try {
    const headers = ['Timestamp', 'User', 'Action', 'Endpoint', 'Method', 'Status']
    const rows = items.map(log => [
      log.timestamp,
      log.userName,
      log.action,
      log.endpointPath,
      log.httpMethod,
      log.exception ? 'Error' : 'Success',
    ])
    const csv = [headers, ...rows]
      .map(row => row.map(value => `"${String(value).replace(/"/g, '""')}"`).join(','))
      .join('\n')
    const blob = new Blob([csv], { type: 'text/csv' })
    const url = URL.createObjectURL(blob)
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = `audit-logs-${new Date().toISOString().slice(0, 10)}.csv`
    anchor.click()
    URL.revokeObjectURL(url)
    operationTone.value = 'success'
    operationMessage.value = 'Audit logs exported.'
  }
  catch {
    operationTone.value = 'danger'
    operationMessage.value = 'Unable to export the audit logs.'
  }
}
</script>

<template>
  <CommandAdminAuditLogsWorkspace
    v-model:filter-user-name="filterUserName"
    v-model:filter-action="filterAction"
    v-model:filter-entity-type="filterEntityType"
    v-model:page="page"
    v-model:detail-dialog-open="detailDialogOpen"
    :state="workspaceState"
    :error-message="errorMessage"
    :logs="logs"
    :total="total"
    :total-pages="totalPages"
    :refreshing="isFetching"
    :selected-log="selectedLog"
    :operation-message="operationMessage"
    :operation-tone="operationTone"
    @search="search"
    @refresh="refresh"
    @retry="refresh"
    @export-csv="exportCsv"
    @open-detail="openDetail"
  />
</template>
