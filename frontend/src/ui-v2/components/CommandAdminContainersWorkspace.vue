<script setup lang="ts">
import { computed } from 'vue'
import { Activity, Cpu, Layers, Monitor, RefreshCw, Trash2 } from 'lucide-vue-next'
import CommandBadge from '../primitives/CommandBadge.vue'
import CommandButton from '../primitives/CommandButton.vue'
import CommandDialog from '../primitives/CommandDialog.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandDataTable, { type CommandDataTableColumn } from './CommandDataTable.vue'
import CommandPageHeader from './CommandPageHeader.vue'
import { useCommandPagination } from '../composables/useCommandPagination'

export interface CommandAdminContainer {
  containerId: string
  competitionId: string
  teamId: string
  challengeId: string
  status: string
}

const props = defineProps<{
  state: 'loading' | 'error' | 'ready'
  errorMessage?: string
  containers: CommandAdminContainer[]
  refreshing: boolean
  destroyDialogOpen: boolean
  selectedContainer: CommandAdminContainer | null
  destroyPending: boolean
  operationMessage?: string
  operationTone?: 'success' | 'danger'
}>()

const emit = defineEmits<{
  refresh: []
  retry: []
  openDestroy: [container: CommandAdminContainer]
  destroy: []
  'update:destroyDialogOpen': [value: boolean]
}>()

const columns: CommandDataTableColumn[] = [
  { key: 'containerId', label: 'Container ID' },
  { key: 'competitionId', label: 'Competition' },
  { key: 'teamId', label: 'Team' },
  { key: 'challengeId', label: 'Challenge' },
  { key: 'status', label: 'Status' },
  { key: 'actions', label: 'Actions', align: 'right' },
]

const rows = computed(() => props.containers)
const { page, pageCount, pagedRows } = useCommandPagination(rows)

const runningCount = computed(() => props.containers.filter(container => container.status.toLowerCase() === 'running').length)
const terminatedCount = computed(() => props.containers.length - runningCount.value)

function statusTone(status: string): 'success' | 'info' | 'danger' | 'default' {
  const value = status.toLowerCase()
  if (value === 'running' || value === 'active')
    return 'success'
  if (value === 'created' || value === 'paused')
    return 'info'
  if (value === 'exited' || value === 'dead')
    return 'danger'
  return 'default'
}
</script>

<template>
  <section class="admin-containers">
    <CommandPageHeader
      signal-label="Admin API / container fleet"
      signal-tone="warning"
      title="Container instances"
      description="Inspect every runtime container and destroy orphaned instances."
      :stat-icon="Layers"
      :stat-value="String(props.containers.length).padStart(2, '0')"
      stat-label="instances"
    >
      <CommandButton
        :label="props.refreshing ? 'Refreshing' : 'Refresh'"
        tone="outline"
        :disabled="props.refreshing"
        @click="emit('refresh')"
      >
        <template #icon><RefreshCw class="size-4" :class="{ 'animate-spin': props.refreshing }" /></template>
      </CommandButton>
    </CommandPageHeader>

    <p v-if="props.operationMessage" class="admin-containers__message" :class="`admin-containers__message--${props.operationTone || 'success'}`" role="status">
      {{ props.operationMessage }}
    </p>

    <div class="admin-containers__stats">
      <CommandPanel class="admin-containers__stat">
        <span class="admin-containers__stat-icon"><Layers class="size-4" /></span>
        <span class="admin-containers__stat-body">
          <span class="admin-containers__stat-label">Total instances</span>
          <strong>{{ props.containers.length }}</strong>
        </span>
      </CommandPanel>
      <CommandPanel class="admin-containers__stat">
        <span class="admin-containers__stat-icon admin-containers__stat-icon--success"><Activity class="size-4" /></span>
        <span class="admin-containers__stat-body">
          <span class="admin-containers__stat-label">Healthy</span>
          <strong>{{ runningCount }}</strong>
        </span>
      </CommandPanel>
      <CommandPanel class="admin-containers__stat">
        <span class="admin-containers__stat-icon admin-containers__stat-icon--danger"><Cpu class="size-4" /></span>
        <span class="admin-containers__stat-body">
          <span class="admin-containers__stat-label">Terminated</span>
          <strong>{{ terminatedCount }}</strong>
        </span>
      </CommandPanel>
      <CommandPanel class="admin-containers__stat">
        <span class="admin-containers__stat-icon admin-containers__stat-icon--muted"><Monitor class="size-4" /></span>
        <span class="admin-containers__stat-body">
          <span class="admin-containers__stat-label">Engine</span>
          <strong>Docker</strong>
        </span>
      </CommandPanel>
    </div>

    <CommandPanel v-if="props.state === 'error'" class="admin-containers__state" tone="warning">
      <h2>Unable to load containers</h2>
      <p>{{ props.errorMessage || 'The service did not return a usable container list.' }}</p>
      <CommandButton label="Retry" tone="outline" @click="emit('retry')" />
    </CommandPanel>

    <CommandDataTable
      v-else
      :columns="columns"
      :row-count="rows.length"
      :loading="props.state === 'loading'"
      empty-label="No containers found."
      :page="page"
      :page-count="pageCount"
      :total-count="props.containers.length"
      @update:page="page = $event"
    >
      <tr v-for="container in pagedRows" :key="container.containerId">
        <td><code class="cell-mono admin-containers__id">{{ container.containerId.slice(0, 12) }}</code></td>
        <td><span class="cell-mono admin-containers__ref">{{ container.competitionId.slice(0, 8) }}</span></td>
        <td><span class="cell-mono admin-containers__ref">{{ container.teamId.slice(0, 8) }}</span></td>
        <td><span class="cell-mono admin-containers__ref">{{ container.challengeId.slice(0, 8) }}</span></td>
        <td><CommandBadge :label="container.status" :tone="statusTone(container.status)" /></td>
        <td>
          <div class="cell-actions">
            <CommandButton label="Destroy" tone="ghost" class="admin-containers__danger-action" @click="emit('openDestroy', container)">
              <template #icon><Trash2 class="size-4" /></template>
            </CommandButton>
          </div>
        </td>
      </tr>
    </CommandDataTable>

    <CommandDialog
      :open="props.destroyDialogOpen"
      signal-label="Destructive action"
      signal-tone="danger"
      title="Destroy container"
      width="425px"
      @update:open="emit('update:destroyDialogOpen', $event)"
    >
      <p class="admin-containers__destroy-warning">
        <Trash2 class="size-4" />
        Destroy this container instance? Running workloads are terminated immediately. This action cannot be undone.
      </p>
      <code class="admin-containers__destroy-id">ID: {{ props.selectedContainer?.containerId }}</code>
      <template #footer>
        <CommandButton label="Cancel" tone="ghost" :disabled="props.destroyPending" @click="emit('update:destroyDialogOpen', false)" />
        <CommandButton
          :label="props.destroyPending ? 'Destroying' : 'Destroy'"
          :disabled="props.destroyPending"
          class="admin-containers__danger-action admin-containers__danger-action--solid"
          @click="emit('destroy')"
        />
      </template>
    </CommandDialog>
  </section>
</template>

<style scoped>
.admin-containers { display: grid; gap: 16px; }
.admin-containers__message { margin: 0; border-radius: 12px; padding: 12px 14px; color: var(--v2-cyan); background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 13px; }
.admin-containers__message--danger { color: var(--v2-danger); }
.admin-containers__stats { display: grid; gap: 12px; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); }
.admin-containers__stat { display: flex; align-items: center; gap: 12px; padding: 14px 16px; }
.admin-containers__stat-icon { display: grid; width: 36px; height: 36px; flex-shrink: 0; place-items: center; border-radius: 10px; color: var(--v2-primary); background: var(--v2-surface); box-shadow: var(--v2-inset); }
.admin-containers__stat-icon--success { color: var(--v2-cyan); }
.admin-containers__stat-icon--danger { color: var(--v2-danger); }
.admin-containers__stat-icon--muted { color: var(--v2-text-faint); }
.admin-containers__stat-body { display: grid; gap: 2px; }
.admin-containers__stat-label { color: var(--v2-text-faint); font-size: 10px; letter-spacing: 0.08em; }
.admin-containers__stat-body strong { color: var(--v2-text); font-size: 18px; font-weight: 600; line-height: 1; }
.admin-containers__state { display: grid; min-height: 150px; align-content: center; justify-items: start; gap: 10px; padding: 24px; }
.admin-containers__state h2 { margin: 0; color: var(--v2-text); font-size: 17px; font-weight: 600; }
.admin-containers__state p { margin: 0; color: var(--v2-text-muted); font-size: 13px; }
.admin-containers__id { border-radius: 6px; padding: 2px 6px; color: var(--v2-primary); background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 10px; }
.admin-containers__ref { color: var(--v2-text-faint); font-size: 10px; }
.admin-containers__danger-action { color: var(--v2-danger); }
.admin-containers__danger-action--solid { color: #ffffff; background: var(--v2-danger); }
.admin-containers__destroy-warning { display: flex; align-items: flex-start; gap: 8px; margin: 0; border-radius: 12px; padding: 12px; color: var(--v2-danger); background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 12px; line-height: 1.5; }
.admin-containers__destroy-id { display: block; border-radius: 12px; padding: 10px 12px; color: var(--v2-danger); background: var(--v2-surface); box-shadow: var(--v2-inset); font-family: var(--v2-font-mono); font-size: 11px; word-break: break-all; }
</style>
