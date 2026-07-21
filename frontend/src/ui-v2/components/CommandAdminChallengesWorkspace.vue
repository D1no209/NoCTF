<script setup lang="ts">
import { computed, ref } from 'vue'
import { Key, Pencil, Plus, Puzzle, Search, Trash2 } from 'lucide-vue-next'
import CommandBadge from '../primitives/CommandBadge.vue'
import CommandButton from '../primitives/CommandButton.vue'
import CommandDialog from '../primitives/CommandDialog.vue'
import CommandInput from '../primitives/CommandInput.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSelect from '../primitives/CommandSelect.vue'
import CommandDataTable, { type CommandDataTableColumn } from './CommandDataTable.vue'
import CommandChallengeTemplateForm, {
  type CommandChallengeTemplate,
  type CommandChallengeTemplateSubmit,
} from './CommandChallengeTemplateForm.vue'
import CommandPageHeader from './CommandPageHeader.vue'
import { useCommandPagination } from '../composables/useCommandPagination'

export interface CommandAdminChallenge extends CommandChallengeTemplate {
  id: string
}

const props = defineProps<{
  state: 'loading' | 'error' | 'ready'
  errorMessage?: string
  challenges: CommandAdminChallenge[]
  editDialogOpen: boolean
  deleteDialogOpen: boolean
  revealDialogOpen: boolean
  selectedChallenge: CommandAdminChallenge | null
  revealedSecret: string
  savePending: boolean
  deletePending: boolean
  revealPending: boolean
  operationMessage?: string
  operationTone?: 'success' | 'danger'
}>()

const emit = defineEmits<{
  retry: []
  create: []
  openEdit: [challenge: CommandAdminChallenge]
  openDelete: [challenge: CommandAdminChallenge]
  openReveal: [challenge: CommandAdminChallenge]
  save: [value: CommandChallengeTemplateSubmit]
  remove: []
  reveal: []
  'update:editDialogOpen': [value: boolean]
  'update:deleteDialogOpen': [value: boolean]
  'update:revealDialogOpen': [value: boolean]
}>()

const columns: CommandDataTableColumn[] = [
  { key: 'title', label: 'Title', sortable: true },
  { key: 'typeId', label: 'Mode', sortable: true },
  { key: 'attachmentUrl', label: 'Attachment' },
  { key: 'deploymentType', label: 'Deployment' },
  { key: 'containerMode', label: 'Container mode' },
  { key: 'containerImage', label: 'Image' },
  { key: 'actions', label: 'Actions', align: 'right' },
]

const deploymentTypeKeys = ['NoAttachment', 'StaticAttachment', 'DynamicContainer', 'StaticContainer'] as const
const deploymentTypeLabels: Record<typeof deploymentTypeKeys[number], string> = {
  NoAttachment: 'No attachment',
  StaticAttachment: 'Static attachment',
  DynamicContainer: 'Dynamic container',
  StaticContainer: 'Static container',
}
const challengeTypeOptions = [
  { value: 'all', label: 'All modes' },
  { value: 'Ctf', label: 'CTF' },
  { value: 'Awd', label: 'AWD' },
  { value: 'Awdp', label: 'AWDP' },
  { value: 'Koh', label: 'KoH' },
]
const attachmentOptions = [
  { value: 'all', label: 'All attachment states' },
  { value: 'attached', label: 'With attachment' },
  { value: 'none', label: 'Without attachment' },
]
const deploymentOptions = [
  { value: 'all', label: 'All deployment types' },
  ...deploymentTypeKeys.map(key => ({ value: key, label: deploymentTypeLabels[key] })),
]

const search = ref('')
const challengeTypeFilter = ref('all')
const attachmentFilter = ref('all')
const deploymentTypeFilter = ref('all')
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

function deploymentTypeKey(value: CommandAdminChallenge['deploymentType']) {
  return typeof value === 'number' ? deploymentTypeKeys[value] ?? 'NoAttachment' : value ?? 'NoAttachment'
}

function normalizeChallengeType(value?: string | null) {
  const key = (value ?? 'Ctf').trim().toLowerCase()
  if (key === 'awd') return 'Awd'
  if (key === 'awdp') return 'Awdp'
  if (key === 'koh') return 'Koh'
  return 'Ctf'
}

function challengeTypeLabel(value?: string | null) {
  return challengeTypeOptions.find(option => option.value === normalizeChallengeType(value))?.label ?? 'CTF'
}

function containerModeLabel(value: CommandAdminChallenge['containerMode']) {
  return value === 1 || value === 'DockerCompose' ? 'Docker compose' : 'Single image'
}

const filteredChallenges = computed(() => {
  const query = search.value.trim().toLowerCase()
  const rows = props.challenges.filter((challenge) => {
    if (challengeTypeFilter.value !== 'all' && normalizeChallengeType(challenge.typeId) !== challengeTypeFilter.value)
      return false
    if (attachmentFilter.value === 'attached' && !challenge.attachmentUrl)
      return false
    if (attachmentFilter.value === 'none' && challenge.attachmentUrl)
      return false
    if (deploymentTypeFilter.value !== 'all' && deploymentTypeKey(challenge.deploymentType) !== deploymentTypeFilter.value)
      return false
    if (!query)
      return true
    return (challenge.title ?? '').toLowerCase().includes(query)
      || challengeTypeLabel(challenge.typeId).toLowerCase().includes(query)
      || (challenge.containerImage ?? '').toLowerCase().includes(query)
  })
  const key = sortKey.value as 'title' | 'typeId'
  rows.sort((a, b) => {
    const aValue = key === 'typeId' ? challengeTypeLabel(a.typeId) : (a.title ?? '')
    const bValue = key === 'typeId' ? challengeTypeLabel(b.typeId) : (b.title ?? '')
    const result = aValue.localeCompare(bValue)
    return sortDir.value === 'asc' ? result : -result
  })
  return rows
})

const { page, pageCount, pagedRows } = useCommandPagination(filteredChallenges)
</script>

<template>
  <section class="admin-challenges">
    <CommandPageHeader
      signal-label="Admin API / challenge templates"
      signal-tone="warning"
      title="Challenge templates"
      description="Maintain the reusable challenge template catalog."
      :stat-icon="Puzzle"
      :stat-value="String(props.challenges.length).padStart(2, '0')"
      stat-label="templates"
    >
      <CommandButton label="New challenge" @click="emit('create')">
        <template #icon><Plus class="size-4" /></template>
      </CommandButton>
    </CommandPageHeader>

    <p v-if="props.operationMessage" class="admin-challenges__message" :class="`admin-challenges__message--${props.operationTone || 'success'}`" role="status">
      {{ props.operationMessage }}
    </p>

    <CommandPanel class="admin-challenges__filters">
      <div class="admin-challenges__search">
        <Search class="size-4 text-[var(--v2-text-faint)]" />
        <CommandInput v-model="search" type="search" label="Search challenges" placeholder="Search by title, mode, or image" />
      </div>
      <CommandSelect v-model="challengeTypeFilter" label="Challenge mode filter" :options="challengeTypeOptions" />
      <CommandSelect v-model="attachmentFilter" label="Attachment filter" :options="attachmentOptions" />
      <CommandSelect v-model="deploymentTypeFilter" label="Deployment type filter" :options="deploymentOptions" />
    </CommandPanel>

    <CommandPanel v-if="props.state === 'error'" class="admin-challenges__state" tone="warning">
      <h2>Unable to load challenges</h2>
      <p>{{ props.errorMessage || 'The service did not return a usable challenge list.' }}</p>
      <CommandButton label="Retry" tone="outline" @click="emit('retry')" />
    </CommandPanel>

    <CommandDataTable
      v-else
      :columns="columns"
      :row-count="filteredChallenges.length"
      :loading="props.state === 'loading'"
      empty-label="No challenge templates found."
      :page="page"
      :page-count="pageCount"
      :total-count="props.challenges.length"
      :sort-key="sortKey"
      :sort-dir="sortDir"
      @update:page="page = $event"
      @sort="toggleSort"
    >
      <tr v-for="challenge in pagedRows" :key="challenge.id">
        <td><span class="cell-strong">{{ challenge.title }}</span></td>
        <td><CommandBadge :label="challengeTypeLabel(challenge.typeId)" tone="info" /></td>
        <td>
          <span v-if="challenge.attachmentUrl">Attached</span>
          <span v-else class="admin-challenges__none">-</span>
        </td>
        <td><CommandBadge :label="deploymentTypeLabels[deploymentTypeKey(challenge.deploymentType)]" /></td>
        <td><CommandBadge :label="containerModeLabel(challenge.containerMode)" /></td>
        <td>
          <code v-if="challenge.containerImage" class="cell-mono admin-challenges__image">{{ challenge.containerImage }}</code>
          <span v-else class="admin-challenges__none">-</span>
        </td>
        <td>
          <div class="cell-actions">
            <CommandButton label="Edit" tone="ghost" @click="emit('openEdit', challenge)">
              <template #icon><Pencil class="size-4" /></template>
            </CommandButton>
            <CommandButton label="Secret" tone="ghost" @click="emit('openReveal', challenge)">
              <template #icon><Key class="size-4" /></template>
            </CommandButton>
            <CommandButton label="Delete" tone="ghost" class="admin-challenges__danger-action" @click="emit('openDelete', challenge)">
              <template #icon><Trash2 class="size-4" /></template>
            </CommandButton>
          </div>
        </td>
      </tr>
    </CommandDataTable>

    <CommandDialog
      :open="props.editDialogOpen"
      signal-label="Challenge editor"
      signal-tone="info"
      title="Edit challenge template"
      width="680px"
      @update:open="emit('update:editDialogOpen', $event)"
    >
      <CommandChallengeTemplateForm
        :template="props.selectedChallenge"
        :saving="props.savePending"
        submit-text="Save"
        cancel-text="Cancel"
        @submit="emit('save', $event)"
        @cancel="emit('update:editDialogOpen', false)"
      />
    </CommandDialog>

    <CommandDialog
      :open="props.deleteDialogOpen"
      signal-label="Destructive action"
      signal-tone="danger"
      title="Delete challenge template"
      width="425px"
      @update:open="emit('update:deleteDialogOpen', $event)"
    >
      <p class="admin-challenges__delete-warning">
        <Trash2 class="size-4" />
        Delete {{ props.selectedChallenge?.title }}? Competitions already using this template keep their deployed copies. This action cannot be undone.
      </p>
      <template #footer>
        <CommandButton label="Cancel" tone="ghost" :disabled="props.deletePending" @click="emit('update:deleteDialogOpen', false)" />
        <CommandButton
          :label="props.deletePending ? 'Deleting' : 'Delete'"
          :disabled="props.deletePending"
          class="admin-challenges__danger-action admin-challenges__danger-action--solid"
          @click="emit('remove')"
        />
      </template>
    </CommandDialog>

    <CommandDialog
      :open="props.revealDialogOpen"
      signal-label="Flag secret"
      signal-tone="warning"
      :title="`Reveal secret / ${props.selectedChallenge?.title ?? ''}`"
      width="480px"
      @update:open="emit('update:revealDialogOpen', $event)"
    >
      <div class="admin-challenges__reveal">
        <p class="admin-challenges__reveal-hint">Revealing a flag secret is recorded in the audit log.</p>
        <pre v-if="props.revealedSecret" class="admin-challenges__secret">{{ props.revealedSecret }}</pre>
      </div>
      <template #footer>
        <CommandButton label="Close" tone="ghost" @click="emit('update:revealDialogOpen', false)" />
        <CommandButton
          :label="props.revealPending ? 'Revealing' : 'Reveal secret'"
          :disabled="props.revealPending"
          class="admin-challenges__danger-action admin-challenges__danger-action--solid"
          @click="emit('reveal')"
        />
      </template>
    </CommandDialog>
  </section>
</template>

<style scoped>
.admin-challenges { display: grid; gap: 16px; }
.admin-challenges__message { margin: 0; border-radius: 12px; padding: 12px 14px; color: var(--v2-cyan); background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 13px; }
.admin-challenges__message--danger { color: var(--v2-danger); }
.admin-challenges__filters { display: grid; gap: 12px; padding: 14px 16px; grid-template-columns: repeat(auto-fit, minmax(190px, 1fr)); }
.admin-challenges__search { display: grid; grid-template-columns: auto minmax(0, 1fr); align-items: center; gap: 10px; }
.admin-challenges__state { display: grid; min-height: 150px; align-content: center; justify-items: start; gap: 10px; padding: 24px; }
.admin-challenges__state h2 { margin: 0; color: var(--v2-text); font-size: 17px; font-weight: 600; }
.admin-challenges__state p { margin: 0; color: var(--v2-text-muted); font-size: 13px; }
.admin-challenges__none { color: var(--v2-text-faint); }
.admin-challenges__image { display: inline-block; overflow: hidden; max-width: 220px; text-overflow: ellipsis; vertical-align: middle; white-space: nowrap; }
.admin-challenges__danger-action { color: var(--v2-danger); }
.admin-challenges__danger-action--solid { color: #ffffff; background: var(--v2-danger); }
.admin-challenges__delete-warning { display: flex; align-items: flex-start; gap: 8px; margin: 0; border-radius: 12px; padding: 12px; color: var(--v2-danger); background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 12px; line-height: 1.5; }
.admin-challenges__reveal { display: grid; gap: 12px; }
.admin-challenges__reveal-hint { margin: 0; border-radius: 12px; padding: 10px 12px; color: var(--v2-text-muted); background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 12px; }
.admin-challenges__secret { margin: 0; overflow: auto; border-radius: 12px; padding: 14px; color: var(--v2-primary); background: var(--v2-surface); box-shadow: var(--v2-inset); font-family: var(--v2-font-mono); font-size: 12px; white-space: pre-wrap; word-break: break-all; }
</style>
