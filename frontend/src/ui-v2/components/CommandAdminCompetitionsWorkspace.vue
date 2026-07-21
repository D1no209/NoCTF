<script setup lang="ts">
import { computed, ref } from 'vue'
import {
  ExternalLink,
  Plus,
  Search,
  Settings,
  Trash2,
  Trophy,
  Users2,
} from 'lucide-vue-next'
import CommandBadge from '../primitives/CommandBadge.vue'
import CommandButton from '../primitives/CommandButton.vue'
import CommandDialog from '../primitives/CommandDialog.vue'
import CommandInput from '../primitives/CommandInput.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSelect from '../primitives/CommandSelect.vue'
import CommandTextarea from '../primitives/CommandTextarea.vue'
import CommandDataTable, { type CommandDataTableColumn } from './CommandDataTable.vue'
import CommandPageHeader from './CommandPageHeader.vue'
import { useCommandPagination } from '../composables/useCommandPagination'

export interface CommandAdminCompetition {
  id: string
  title: string
  gameModeType: string
  status: string
  startTime: string
  endTime: string
}

export interface CommandAdminCompetitionForm {
  title: string
  description: string
  gameModeType: string
  status: string
  startTime: string
  endTime: string
}

const props = defineProps<{
  state: 'loading' | 'error' | 'ready'
  errorMessage?: string
  competitions: CommandAdminCompetition[]
  createDialogOpen: boolean
  deleteDialogOpen: boolean
  selectedCompetition: CommandAdminCompetition | null
  form: CommandAdminCompetitionForm
  canSave: boolean
  savePending: boolean
  deletePending: boolean
  operationMessage?: string
  operationTone?: 'success' | 'danger'
}>()

const emit = defineEmits<{
  retry: []
  openCreate: []
  openDelete: [competition: CommandAdminCompetition]
  save: []
  remove: []
  manage: [competition: CommandAdminCompetition]
  collaborators: [competition: CommandAdminCompetition]
  viewPublic: [competition: CommandAdminCompetition]
  awdpScreen: [competition: CommandAdminCompetition]
  'update:createDialogOpen': [value: boolean]
  'update:deleteDialogOpen': [value: boolean]
  'update:form': [value: CommandAdminCompetitionForm]
}>()

const columns: CommandDataTableColumn[] = [
  { key: 'title', label: 'Title', sortable: true },
  { key: 'gameModeType', label: 'Mode', sortable: true },
  { key: 'status', label: 'Status' },
  { key: 'startTime', label: 'Start' },
  { key: 'endTime', label: 'End' },
  { key: 'actions', label: 'Actions', align: 'right' },
]

const gameModeOptions = [
  { value: 'Ctf', label: 'CTF' },
  { value: 'Awd', label: 'AWD' },
  { value: 'Awdp', label: 'AWDP' },
  { value: 'Koh', label: 'KoH' },
]

const statusOptions = [
  { value: 'Draft', label: 'Draft' },
  { value: 'Published', label: 'Published' },
  { value: 'Running', label: 'Running' },
  { value: 'Paused', label: 'Paused' },
  { value: 'Finished', label: 'Finished' },
]

const search = ref('')
const sortKey = ref('title')
const sortDir = ref<'asc' | 'desc'>('asc')

function toggleSort(key: string) {
  if (sortKey.value === key) {
    sortDir.value = sortDir.value === 'asc' ? 'desc' : 'asc'
    return
  }
  sortKey.value = key
  sortDir.value = 'asc'
}

const filteredCompetitions = computed(() => {
  const query = search.value.trim().toLowerCase()
  const rows = query
    ? props.competitions.filter(competition =>
        competition.title.toLowerCase().includes(query)
        || competition.gameModeType.toLowerCase().includes(query)
        || competition.status.toLowerCase().includes(query))
    : [...props.competitions]
  const key = sortKey.value as 'title' | 'gameModeType'
  rows.sort((a, b) => {
    const result = (a[key] ?? '').localeCompare(b[key] ?? '')
    return sortDir.value === 'asc' ? result : -result
  })
  return rows
})

const { page, pageCount, pagedRows } = useCommandPagination(filteredCompetitions)

function statusTone(status: string): 'success' | 'info' | 'default' {
  const value = status.toLowerCase()
  if (value === 'running' || value === 'active')
    return 'success'
  if (value === 'published')
    return 'info'
  return 'default'
}

function formatDate(value: string) {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '-' : date.toLocaleDateString()
}

function isAwdp(competition: CommandAdminCompetition) {
  return competition.gameModeType.toLowerCase() === 'awdp'
}

function patchForm(patch: Partial<CommandAdminCompetitionForm>) {
  emit('update:form', { ...props.form, ...patch })
}
</script>

<template>
  <section class="admin-competitions">
    <CommandPageHeader
      signal-label="Admin API / competition registry"
      signal-tone="warning"
      title="Competitions"
      description="Create competitions and jump into per-competition management."
      :stat-icon="Trophy"
      :stat-value="String(props.competitions.length).padStart(2, '0')"
      stat-label="competitions"
    >
      <CommandButton label="New competition" @click="emit('openCreate')">
        <template #icon><Plus class="size-4" /></template>
      </CommandButton>
    </CommandPageHeader>

    <p v-if="props.operationMessage" class="admin-competitions__message" :class="`admin-competitions__message--${props.operationTone || 'success'}`" role="status">
      {{ props.operationMessage }}
    </p>

    <CommandPanel class="admin-competitions__filters">
      <div class="admin-competitions__search">
        <Search class="size-4 text-[var(--v2-text-faint)]" />
        <CommandInput v-model="search" type="search" label="Search competitions" placeholder="Search by title, mode, or status" />
      </div>
    </CommandPanel>

    <CommandPanel v-if="props.state === 'error'" class="admin-competitions__state" tone="warning">
      <h2>Unable to load competitions</h2>
      <p>{{ props.errorMessage || 'The service did not return a usable competition list.' }}</p>
      <CommandButton label="Retry" tone="outline" @click="emit('retry')" />
    </CommandPanel>

    <CommandDataTable
      v-else
      :columns="columns"
      :row-count="filteredCompetitions.length"
      :loading="props.state === 'loading'"
      empty-label="No competitions found."
      :page="page"
      :page-count="pageCount"
      :total-count="props.competitions.length"
      :sort-key="sortKey"
      :sort-dir="sortDir"
      @update:page="page = $event"
      @sort="toggleSort"
    >
      <tr v-for="competition in pagedRows" :key="competition.id">
        <td><span class="cell-strong">{{ competition.title }}</span></td>
        <td><code class="cell-mono">{{ competition.gameModeType }}</code></td>
        <td><CommandBadge :label="competition.status" :tone="statusTone(competition.status)" /></td>
        <td>{{ formatDate(competition.startTime) }}</td>
        <td>{{ formatDate(competition.endTime) }}</td>
        <td>
          <div class="cell-actions">
            <CommandButton label="Manage" tone="ghost" @click="emit('manage', competition)">
              <template #icon><Settings class="size-4" /></template>
            </CommandButton>
            <CommandButton label="Collaborators" tone="ghost" @click="emit('collaborators', competition)">
              <template #icon><Users2 class="size-4" /></template>
            </CommandButton>
            <CommandButton label="View" tone="ghost" @click="emit('viewPublic', competition)">
              <template #icon><ExternalLink class="size-4" /></template>
            </CommandButton>
            <CommandButton v-if="isAwdp(competition)" label="Screen" tone="ghost" @click="emit('awdpScreen', competition)">
              <template #icon><ExternalLink class="size-4" /></template>
            </CommandButton>
            <CommandButton label="Delete" tone="ghost" class="admin-competitions__danger-action" @click="emit('openDelete', competition)">
              <template #icon><Trash2 class="size-4" /></template>
            </CommandButton>
          </div>
        </td>
      </tr>
    </CommandDataTable>

    <CommandDialog
      :open="props.createDialogOpen"
      signal-label="Competition editor"
      signal-tone="info"
      title="New competition"
      width="560px"
      @update:open="emit('update:createDialogOpen', $event)"
    >
      <div class="admin-competitions__form">
        <CommandInput
          :model-value="props.form.title"
          label="Title"
          placeholder="Competition title"
          @update:model-value="patchForm({ title: $event })"
        />
        <CommandTextarea
          :model-value="props.form.description"
          label="Description"
          placeholder="Optional description"
          :rows="3"
          @update:model-value="patchForm({ description: $event })"
        />
        <div class="admin-competitions__form-row">
          <CommandSelect
            :model-value="props.form.gameModeType"
            label="Game mode"
            :options="gameModeOptions"
            @update:model-value="patchForm({ gameModeType: $event })"
          />
          <CommandSelect
            :model-value="props.form.status"
            label="Status"
            :options="statusOptions"
            @update:model-value="patchForm({ status: $event })"
          />
        </div>
        <div class="admin-competitions__form-row">
          <CommandInput
            :model-value="props.form.startTime"
            type="datetime-local"
            label="Start time"
            @update:model-value="patchForm({ startTime: $event })"
          />
          <CommandInput
            :model-value="props.form.endTime"
            type="datetime-local"
            label="End time"
            @update:model-value="patchForm({ endTime: $event })"
          />
        </div>
        <p v-if="props.form.startTime && props.form.endTime && !props.canSave && props.form.title.trim()" class="admin-competitions__form-hint">
          The end time must be later than the start time.
        </p>
      </div>
      <template #footer>
        <CommandButton label="Cancel" tone="ghost" @click="emit('update:createDialogOpen', false)" />
        <CommandButton
          :label="props.savePending ? 'Saving' : 'Create'"
          :disabled="props.savePending || !props.canSave"
          @click="emit('save')"
        />
      </template>
    </CommandDialog>

    <CommandDialog
      :open="props.deleteDialogOpen"
      signal-label="Destructive action"
      signal-tone="danger"
      :title="`Delete competition / ${props.selectedCompetition?.title ?? ''}`"
      width="425px"
      @update:open="emit('update:deleteDialogOpen', $event)"
    >
      <p class="admin-competitions__delete-warning">
        <Trash2 class="size-4" />
        Delete {{ props.selectedCompetition?.title }}? Every deployment, registration, and scoreboard record is removed. This action cannot be undone.
      </p>
      <template #footer>
        <CommandButton label="Cancel" tone="ghost" :disabled="props.deletePending" @click="emit('update:deleteDialogOpen', false)" />
        <CommandButton
          :label="props.deletePending ? 'Deleting' : 'Delete'"
          :disabled="props.deletePending"
          class="admin-competitions__danger-action admin-competitions__danger-action--solid"
          @click="emit('remove')"
        />
      </template>
    </CommandDialog>
  </section>
</template>

<style scoped>
.admin-competitions { display: grid; gap: 16px; }
.admin-competitions__message { margin: 0; border-radius: 12px; padding: 12px 14px; color: var(--v2-cyan); background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 13px; }
.admin-competitions__message--danger { color: var(--v2-danger); }
.admin-competitions__filters { padding: 14px 16px; }
.admin-competitions__search { display: grid; grid-template-columns: auto minmax(0, 1fr); align-items: center; gap: 10px; }
.admin-competitions__state { display: grid; min-height: 150px; align-content: center; justify-items: start; gap: 10px; padding: 24px; }
.admin-competitions__state h2 { margin: 0; color: var(--v2-text); font-size: 17px; font-weight: 600; }
.admin-competitions__state p { margin: 0; color: var(--v2-text-muted); font-size: 13px; }
.admin-competitions__danger-action { color: var(--v2-danger); }
.admin-competitions__danger-action--solid { color: #ffffff; background: var(--v2-danger); }
.admin-competitions__form { display: grid; gap: 14px; }
.admin-competitions__form-row { display: grid; gap: 12px; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); }
.admin-competitions__form-hint { margin: 0; border-radius: 12px; padding: 10px 12px; color: var(--v2-danger); background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 12px; }
.admin-competitions__delete-warning { display: flex; align-items: flex-start; gap: 8px; margin: 0; border-radius: 12px; padding: 12px; color: var(--v2-danger); background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 12px; line-height: 1.5; }
</style>
