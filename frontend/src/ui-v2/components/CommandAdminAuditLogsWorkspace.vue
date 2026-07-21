<script setup lang="ts">
import { computed, ref } from 'vue'
import {
  CheckCircle2,
  Clock,
  Download,
  Eye,
  FileJson,
  Globe,
  RotateCw,
  ScrollText,
  Search,
  ShieldAlert,
  User as UserIcon,
} from 'lucide-vue-next'
import CommandBadge from '../primitives/CommandBadge.vue'
import CommandButton from '../primitives/CommandButton.vue'
import CommandDialog from '../primitives/CommandDialog.vue'
import CommandInput from '../primitives/CommandInput.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandDataTable, { type CommandDataTableColumn } from './CommandDataTable.vue'
import CommandPageHeader from './CommandPageHeader.vue'

export interface CommandAdminAuditLog {
  id: string
  userName: string
  ipAddress: string
  action: string
  entityType: string
  endpointPath: string
  httpMethod: string
  newValues?: string
  oldValues?: string
  diff?: string
  timestamp: string
  exception?: string
}

const props = defineProps<{
  state: 'loading' | 'error' | 'ready'
  errorMessage?: string
  logs: CommandAdminAuditLog[]
  total: number
  totalPages: number
  page: number
  refreshing: boolean
  filterUserName: string
  filterAction: string
  filterEntityType: string
  detailDialogOpen: boolean
  selectedLog: CommandAdminAuditLog | null
  operationMessage?: string
  operationTone?: 'success' | 'danger'
}>()

const emit = defineEmits<{
  search: []
  refresh: []
  retry: []
  exportCsv: []
  openDetail: [log: CommandAdminAuditLog]
  'update:page': [value: number]
  'update:filterUserName': [value: string]
  'update:filterAction': [value: string]
  'update:filterEntityType': [value: string]
  'update:detailDialogOpen': [value: boolean]
}>()

const columns: CommandDataTableColumn[] = [
  { key: 'timestamp', label: 'Timestamp', sortable: true },
  { key: 'userName', label: 'User', sortable: true },
  { key: 'action', label: 'Action', sortable: true },
  { key: 'endpointPath', label: 'Endpoint' },
  { key: 'status', label: 'Status' },
  { key: 'actions', label: 'Actions', align: 'right' },
]

const sortKey = ref('timestamp')
const sortDir = ref<'asc' | 'desc'>('desc')

function toggleSort(key: string) {
  if (sortKey.value === key) {
    sortDir.value = sortDir.value === 'asc' ? 'desc' : 'asc'
    return
  }
  sortKey.value = key
  sortDir.value = 'asc'
}

const sortedLogs = computed(() => {
  const rows = [...props.logs]
  const key = sortKey.value as 'timestamp' | 'userName' | 'action'
  rows.sort((a, b) => {
    const result = (a[key] ?? '').localeCompare(b[key] ?? '')
    return sortDir.value === 'asc' ? result : -result
  })
  return rows
})

function formatTimestamp(value: string) {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '-' : date.toLocaleString()
}

function formatJson(json?: string) {
  if (!json)
    return ''
  try {
    return JSON.stringify(JSON.parse(json), null, 2)
  }
  catch {
    return json
  }
}

function methodTone(method: string): 'primary' | 'default' {
  return method === 'POST' || method === 'PUT' ? 'primary' : 'default'
}
</script>

<template>
  <section class="admin-audit">
    <CommandPageHeader
      signal-label="Admin API / audit trail"
      signal-tone="warning"
      title="Audit logs"
      description="Trace every privileged action with endpoint and payload diffs."
      :stat-icon="ScrollText"
      :stat-value="String(props.total).padStart(2, '0')"
      stat-label="records"
    >
      <CommandButton
        :label="props.refreshing ? 'Refreshing' : 'Refresh'"
        tone="outline"
        :disabled="props.refreshing"
        @click="emit('refresh')"
      >
        <template #icon><RotateCw class="size-4" :class="{ 'animate-spin': props.refreshing }" /></template>
      </CommandButton>
      <CommandButton label="Export CSV" tone="outline" @click="emit('exportCsv')">
        <template #icon><Download class="size-4" /></template>
      </CommandButton>
    </CommandPageHeader>

    <p v-if="props.operationMessage" class="admin-audit__message" :class="`admin-audit__message--${props.operationTone || 'success'}`" role="status">
      {{ props.operationMessage }}
    </p>

    <CommandPanel class="admin-audit__filters">
      <div class="admin-audit__filter">
        <Search class="size-4 text-[var(--v2-text-faint)]" />
        <CommandInput
          :model-value="props.filterUserName"
          type="search"
          label="Filter by user"
          placeholder="User name"
          @update:model-value="emit('update:filterUserName', $event)"
          @enter="emit('search')"
        />
      </div>
      <CommandInput
        :model-value="props.filterAction"
        type="search"
        label="Filter by action"
        placeholder="Action"
        @update:model-value="emit('update:filterAction', $event)"
        @enter="emit('search')"
      />
      <CommandInput
        :model-value="props.filterEntityType"
        type="search"
        label="Filter by entity type"
        placeholder="Entity type"
        @update:model-value="emit('update:filterEntityType', $event)"
        @enter="emit('search')"
      />
      <CommandButton label="Search" tone="outline" @click="emit('search')" />
    </CommandPanel>

    <CommandPanel v-if="props.state === 'error'" class="admin-audit__state" tone="warning">
      <h2>Unable to load audit logs</h2>
      <p>{{ props.errorMessage || 'The service did not return a usable audit log page.' }}</p>
      <CommandButton label="Retry" tone="outline" @click="emit('retry')" />
    </CommandPanel>

    <CommandDataTable
      v-else
      :columns="columns"
      :row-count="props.logs.length"
      :loading="props.state === 'loading'"
      empty-label="No audit log records found."
      :page="props.page"
      :page-count="props.totalPages"
      :total-count="props.total"
      :sort-key="sortKey"
      :sort-dir="sortDir"
      @update:page="emit('update:page', $event)"
      @sort="toggleSort"
    >
      <tr v-for="log in sortedLogs" :key="log.id">
        <td>
          <span class="admin-audit__timestamp">
            <Clock class="size-3 text-[var(--v2-text-faint)]" />
            {{ formatTimestamp(log.timestamp) }}
          </span>
        </td>
        <td>
          <span class="admin-audit__user">
            <UserIcon class="size-3 text-[var(--v2-text-faint)]" />
            {{ log.userName || 'Anonymous' }}
          </span>
        </td>
        <td><code class="cell-mono">{{ log.action }}</code></td>
        <td><code class="cell-mono admin-audit__endpoint">{{ log.endpointPath }}</code></td>
        <td>
          <ShieldAlert v-if="log.exception" class="size-4 text-[var(--v2-danger)]" />
          <CheckCircle2 v-else class="size-4 text-[var(--v2-cyan)]" />
        </td>
        <td>
          <div class="cell-actions">
            <CommandButton label="Detail" tone="ghost" @click="emit('openDetail', log)">
              <template #icon><Eye class="size-4" /></template>
            </CommandButton>
          </div>
        </td>
      </tr>
    </CommandDataTable>

    <CommandDialog
      :open="props.detailDialogOpen"
      signal-label="Audit record"
      signal-tone="info"
      title="Audit log detail"
      width="700px"
      @update:open="emit('update:detailDialogOpen', $event)"
    >
      <div v-if="props.selectedLog" class="admin-audit__detail">
        <div class="admin-audit__detail-stats">
          <CommandPanel class="admin-audit__detail-stat">
            <span class="admin-audit__detail-label">
              <FileJson class="size-3" />
              User
            </span>
            <strong>{{ props.selectedLog.userName || 'Anonymous' }}</strong>
          </CommandPanel>
          <CommandPanel class="admin-audit__detail-stat">
            <span class="admin-audit__detail-label">Action</span>
            <CommandBadge :label="props.selectedLog.action" />
          </CommandPanel>
          <CommandPanel class="admin-audit__detail-stat">
            <span class="admin-audit__detail-label">
              <Globe class="size-3" />
              IP address
            </span>
            <code class="cell-mono">{{ props.selectedLog.ipAddress || '-' }}</code>
          </CommandPanel>
        </div>

        <CommandPanel class="admin-audit__detail-endpoint">
          <div class="admin-audit__detail-endpoint-head">
            <span class="admin-audit__detail-label">Endpoint</span>
            <CommandBadge :label="props.selectedLog.httpMethod" :tone="methodTone(props.selectedLog.httpMethod)" />
          </div>
          <code class="admin-audit__detail-path">{{ props.selectedLog.endpointPath }}</code>
        </CommandPanel>

        <div v-if="props.selectedLog.exception" class="admin-audit__exception">
          <span class="admin-audit__exception-head">
            <ShieldAlert class="size-4" />
            Exception logged
          </span>
          <pre>{{ props.selectedLog.exception }}</pre>
        </div>

        <div v-if="props.selectedLog.diff" class="admin-audit__payload">
          <span class="admin-audit__detail-label">Diff</span>
          <pre class="admin-audit__json admin-audit__json--diff">{{ formatJson(props.selectedLog.diff) }}</pre>
        </div>
        <div v-if="props.selectedLog.newValues" class="admin-audit__payload">
          <span class="admin-audit__detail-label">New values</span>
          <pre class="admin-audit__json admin-audit__json--new">{{ formatJson(props.selectedLog.newValues) }}</pre>
        </div>
        <div v-if="props.selectedLog.oldValues" class="admin-audit__payload">
          <span class="admin-audit__detail-label">Old values</span>
          <pre class="admin-audit__json admin-audit__json--old">{{ formatJson(props.selectedLog.oldValues) }}</pre>
        </div>
      </div>
      <template #footer>
        <CommandButton label="Close" tone="ghost" @click="emit('update:detailDialogOpen', false)" />
      </template>
    </CommandDialog>
  </section>
</template>

<style scoped>
.admin-audit { display: grid; gap: 16px; }
.admin-audit__message { margin: 0; border-radius: 12px; padding: 12px 14px; color: var(--v2-cyan); background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 13px; }
.admin-audit__message--danger { color: var(--v2-danger); }
.admin-audit__filters { display: grid; gap: 12px; padding: 14px 16px; grid-template-columns: repeat(auto-fit, minmax(170px, 1fr)) auto; align-items: center; }
.admin-audit__filter { display: grid; grid-template-columns: auto minmax(0, 1fr); align-items: center; gap: 10px; }
.admin-audit__state { display: grid; min-height: 150px; align-content: center; justify-items: start; gap: 10px; padding: 24px; }
.admin-audit__state h2 { margin: 0; color: var(--v2-text); font-size: 17px; font-weight: 600; }
.admin-audit__state p { margin: 0; color: var(--v2-text-muted); font-size: 13px; }
.admin-audit__timestamp { display: inline-flex; align-items: center; gap: 6px; white-space: nowrap; }
.admin-audit__user { display: inline-flex; align-items: center; gap: 6px; }
.admin-audit__endpoint { display: inline-block; overflow: hidden; max-width: 200px; text-overflow: ellipsis; vertical-align: middle; white-space: nowrap; }
.admin-audit__detail { display: grid; gap: 14px; }
.admin-audit__detail-stats { display: grid; gap: 10px; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); }
.admin-audit__detail-stat { display: grid; gap: 5px; padding: 12px; }
.admin-audit__detail-stat strong { color: var(--v2-text); font-size: 13px; font-weight: 600; }
.admin-audit__detail-label { display: inline-flex; align-items: center; gap: 5px; color: var(--v2-text-faint); font-size: 10px; letter-spacing: 0.08em; }
.admin-audit__detail-endpoint { display: grid; gap: 10px; padding: 12px; }
.admin-audit__detail-endpoint-head { display: flex; align-items: center; justify-content: space-between; }
.admin-audit__detail-path { color: var(--v2-primary); font-family: var(--v2-font-mono); font-size: 12px; word-break: break-all; }
.admin-audit__exception { display: grid; gap: 8px; border-radius: 12px; padding: 12px; color: var(--v2-danger); background: var(--v2-surface); box-shadow: var(--v2-inset); }
.admin-audit__exception-head { display: flex; align-items: center; gap: 7px; font-size: 11px; font-weight: 600; letter-spacing: 0.06em; }
.admin-audit__exception pre { margin: 0; overflow: auto; font-family: var(--v2-font-mono); font-size: 10px; white-space: pre-wrap; word-break: break-all; }
.admin-audit__payload { display: grid; gap: 6px; }
.admin-audit__json { margin: 0; max-height: 240px; overflow: auto; border-radius: 12px; padding: 12px; background: var(--v2-surface); box-shadow: var(--v2-inset); font-family: var(--v2-font-mono); font-size: 11px; white-space: pre-wrap; word-break: break-all; }
.admin-audit__json--diff { color: var(--v2-cyan); }
.admin-audit__json--new { color: var(--v2-primary); }
.admin-audit__json--old { color: var(--v2-danger); }
</style>
