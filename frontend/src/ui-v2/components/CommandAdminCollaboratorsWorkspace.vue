<script setup lang="ts">
import { computed } from 'vue'
import { Search, ShieldCheck, Trophy, UserMinus, UserPlus, Users, UsersRound } from 'lucide-vue-next'
import CommandBadge from '../primitives/CommandBadge.vue'
import CommandButton from '../primitives/CommandButton.vue'
import CommandDialog from '../primitives/CommandDialog.vue'
import CommandInput from '../primitives/CommandInput.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSelect from '../primitives/CommandSelect.vue'
import CommandDataTable, { type CommandDataTableColumn } from './CommandDataTable.vue'
import CommandPageHeader from './CommandPageHeader.vue'
import { useCommandPagination } from '../composables/useCommandPagination'

export interface CommandAdminCollaborator {
  userId: string
  userName: string
  role: string
}

export interface CommandAdminCollaboratorCompetition {
  id: string
  title: string
}

export interface CommandAdminCollaboratorUser {
  id: string
  userName: string
}

const props = defineProps<{
  competitions: CommandAdminCollaboratorCompetition[]
  selectedCompetitionId: string
  collaborators: CommandAdminCollaborator[]
  state: 'loading' | 'error' | 'ready'
  errorMessage?: string
  addDialogOpen: boolean
  removeDialogOpen: boolean
  selectedCollaborator: CommandAdminCollaborator | null
  newUserId: string
  newUserSearch: string
  newRole: string
  userSearchResults: CommandAdminCollaboratorUser[]
  addPending: boolean
  removePending: boolean
  operationMessage?: string
  operationTone?: 'success' | 'danger'
}>()

const emit = defineEmits<{
  retry: []
  openAdd: []
  openRemove: [collaborator: CommandAdminCollaborator]
  selectUser: [user: CommandAdminCollaboratorUser]
  add: []
  remove: []
  'update:selectedCompetitionId': [value: string]
  'update:addDialogOpen': [value: boolean]
  'update:removeDialogOpen': [value: boolean]
  'update:newUserSearch': [value: string]
  'update:newRole': [value: string]
}>()

const columns: CommandDataTableColumn[] = [
  { key: 'userName', label: 'User name' },
  { key: 'role', label: 'Role' },
  { key: 'actions', label: 'Actions', align: 'right' },
]

const roleOptions = [
  { value: 'Manager', label: 'Manager' },
  { value: 'Observer', label: 'Observer' },
]

const competitionOptions = computed(() => props.competitions.map(competition => ({
  value: competition.id,
  label: competition.title,
})))

const rows = computed(() => props.collaborators)
const { page, pageCount, pagedRows } = useCommandPagination(rows)

function roleTone(role: string): 'primary' | 'default' {
  return role.toLowerCase() === 'manager' ? 'primary' : 'default'
}
</script>

<template>
  <section class="admin-collaborators">
    <CommandPageHeader
      signal-label="Admin API / competition collaborators"
      signal-tone="warning"
      title="Collaborators"
      description="Grant manager or observer access to a competition without transferring ownership."
      :stat-icon="UsersRound"
      :stat-value="String(props.collaborators.length).padStart(2, '0')"
      stat-label="collaborators"
    />

    <p v-if="props.operationMessage" class="admin-collaborators__message" :class="`admin-collaborators__message--${props.operationTone || 'success'}`" role="status">
      {{ props.operationMessage }}
    </p>

    <CommandPanel class="admin-collaborators__filters">
      <div class="admin-collaborators__competition">
        <Trophy class="size-4 text-[var(--v2-text-faint)]" />
        <CommandSelect
          :model-value="props.selectedCompetitionId"
          label="Competition"
          placeholder="Select a competition"
          :options="competitionOptions"
          @update:model-value="emit('update:selectedCompetitionId', $event)"
        />
      </div>
      <CommandButton v-if="props.selectedCompetitionId" label="Add collaborator" @click="emit('openAdd')">
        <template #icon><UserPlus class="size-4" /></template>
      </CommandButton>
    </CommandPanel>

    <template v-if="props.selectedCompetitionId">
      <CommandPanel v-if="props.state === 'error'" class="admin-collaborators__state" tone="warning">
        <h2>Unable to load collaborators</h2>
        <p>{{ props.errorMessage || 'The service did not return a usable collaborator list.' }}</p>
        <CommandButton label="Retry" tone="outline" @click="emit('retry')" />
      </CommandPanel>

      <CommandDataTable
        v-else
        :columns="columns"
        :row-count="rows.length"
        :loading="props.state === 'loading'"
        empty-label="No collaborators for this competition."
        :page="page"
        :page-count="pageCount"
        :total-count="props.collaborators.length"
        @update:page="page = $event"
      >
        <tr v-for="collaborator in pagedRows" :key="collaborator.userId">
          <td><span class="cell-strong">{{ collaborator.userName }}</span></td>
          <td><CommandBadge :label="collaborator.role" :tone="roleTone(collaborator.role)" /></td>
          <td>
            <div class="cell-actions">
              <CommandButton label="Remove" tone="ghost" class="admin-collaborators__danger-action" @click="emit('openRemove', collaborator)">
                <template #icon><UserMinus class="size-4" /></template>
              </CommandButton>
            </div>
          </td>
        </tr>
      </CommandDataTable>
    </template>

    <CommandPanel v-else class="admin-collaborators__state admin-collaborators__state--idle">
      <Users class="size-8 text-[var(--v2-text-faint)]" />
      <h2>No competition selected</h2>
      <p>Select a competition to review and manage its collaborators.</p>
    </CommandPanel>

    <CommandDialog
      :open="props.addDialogOpen"
      signal-label="Grant access"
      signal-tone="info"
      title="Add collaborator"
      width="425px"
      @update:open="emit('update:addDialogOpen', $event)"
    >
      <div class="admin-collaborators__form">
        <div class="admin-collaborators__search">
          <Search class="size-4 text-[var(--v2-text-faint)]" />
          <CommandInput
            :model-value="props.newUserSearch"
            type="search"
            label="Search user"
            placeholder="Type a user name"
            @update:model-value="emit('update:newUserSearch', $event)"
          />
        </div>
        <div v-if="props.newUserSearch.length >= 2" class="admin-collaborators__results">
          <p v-if="props.userSearchResults.length === 0" class="admin-collaborators__results-empty">No users found.</p>
          <button
            v-for="user in props.userSearchResults"
            :key="user.id"
            type="button"
            class="admin-collaborators__result"
            :class="{ 'admin-collaborators__result--selected': props.newUserId === user.id }"
            @click="emit('selectUser', user)"
          >
            <span>{{ user.userName }}</span>
            <ShieldCheck v-if="props.newUserId === user.id" class="size-4 text-[var(--v2-primary)]" />
          </button>
        </div>
        <CommandSelect
          :model-value="props.newRole"
          label="Role"
          :options="roleOptions"
          @update:model-value="emit('update:newRole', $event)"
        />
      </div>
      <template #footer>
        <CommandButton label="Cancel" tone="ghost" @click="emit('update:addDialogOpen', false)" />
        <CommandButton label="Add" :disabled="props.addPending || !props.newUserId" @click="emit('add')" />
      </template>
    </CommandDialog>

    <CommandDialog
      :open="props.removeDialogOpen"
      signal-label="Revoke access"
      signal-tone="danger"
      title="Remove collaborator"
      width="400px"
      @update:open="emit('update:removeDialogOpen', $event)"
    >
      <p class="admin-collaborators__remove-warning">
        <UserMinus class="size-4" />
        Remove {{ props.selectedCollaborator?.userName }} from this competition? Their collaborator access is revoked immediately.
      </p>
      <template #footer>
        <CommandButton label="Cancel" tone="ghost" :disabled="props.removePending" @click="emit('update:removeDialogOpen', false)" />
        <CommandButton
          :label="props.removePending ? 'Removing' : 'Remove'"
          :disabled="props.removePending"
          class="admin-collaborators__danger-action admin-collaborators__danger-action--solid"
          @click="emit('remove')"
        />
      </template>
    </CommandDialog>
  </section>
</template>

<style scoped>
.admin-collaborators { display: grid; gap: 16px; }
.admin-collaborators__message { margin: 0; border-radius: 12px; padding: 12px 14px; color: var(--v2-cyan); background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 13px; }
.admin-collaborators__message--danger { color: var(--v2-danger); }
.admin-collaborators__filters { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 12px; padding: 14px 16px; }
.admin-collaborators__competition { display: grid; flex: 1; min-width: 240px; max-width: 420px; grid-template-columns: auto minmax(0, 1fr); align-items: center; gap: 10px; }
.admin-collaborators__state { display: grid; min-height: 150px; align-content: center; justify-items: start; gap: 10px; padding: 24px; }
.admin-collaborators__state h2 { margin: 0; color: var(--v2-text); font-size: 17px; font-weight: 600; }
.admin-collaborators__state p { margin: 0; color: var(--v2-text-muted); font-size: 13px; }
.admin-collaborators__danger-action { color: var(--v2-danger); }
.admin-collaborators__danger-action--solid { color: #ffffff; background: var(--v2-danger); }
.admin-collaborators__form { display: grid; gap: 14px; }
.admin-collaborators__search { display: grid; grid-template-columns: auto minmax(0, 1fr); align-items: center; gap: 10px; }
.admin-collaborators__results { display: grid; overflow: hidden; border-radius: 12px; background: var(--v2-surface); box-shadow: var(--v2-inset); }
.admin-collaborators__results-empty { margin: 0; padding: 12px; color: var(--v2-text-muted); font-size: 12px; text-align: center; }
.admin-collaborators__result { display: flex; align-items: center; justify-content: space-between; border: 0; padding: 10px 12px; color: var(--v2-text); background: transparent; cursor: pointer; font-family: inherit; font-size: 13px; text-align: left; }
.admin-collaborators__result:hover { background: var(--v2-canvas); }
.admin-collaborators__result--selected { color: var(--v2-primary); font-weight: 600; }
.admin-collaborators__remove-warning { display: flex; align-items: flex-start; gap: 8px; margin: 0; border-radius: 12px; padding: 12px; color: var(--v2-danger); background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 12px; line-height: 1.5; }
</style>
