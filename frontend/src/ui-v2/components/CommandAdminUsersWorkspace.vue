<script setup lang="ts">
import { computed, ref } from 'vue'
import { KeyRound, Search, ShieldAlert, UserCog, UsersRound } from 'lucide-vue-next'
import CommandBadge from '../primitives/CommandBadge.vue'
import CommandButton from '../primitives/CommandButton.vue'
import CommandDialog from '../primitives/CommandDialog.vue'
import CommandInput from '../primitives/CommandInput.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSelect from '../primitives/CommandSelect.vue'
import CommandDataTable, { type CommandDataTableColumn } from './CommandDataTable.vue'
import CommandPageHeader from './CommandPageHeader.vue'
import { useCommandPagination } from '../composables/useCommandPagination'

export interface CommandAdminUser {
  id: string
  userName: string
  email: string
  role: string
}

const props = defineProps<{
  state: 'loading' | 'error' | 'ready'
  errorMessage?: string
  users: CommandAdminUser[]
  roleDialogOpen: boolean
  passwordDialogOpen: boolean
  selectedUser: CommandAdminUser | null
  newRole: string
  newPassword: string
  rolePending: boolean
  passwordPending: boolean
  operationMessage?: string
  operationTone?: 'success' | 'danger'
}>()

const emit = defineEmits<{
  retry: []
  openRole: [user: CommandAdminUser]
  openPassword: [user: CommandAdminUser]
  saveRole: []
  savePassword: []
  'update:roleDialogOpen': [value: boolean]
  'update:passwordDialogOpen': [value: boolean]
  'update:newRole': [value: string]
  'update:newPassword': [value: string]
}>()

const roleOptions = [
  { value: 'user', label: 'User' },
  { value: 'organizer', label: 'Organizer' },
  { value: 'admin', label: 'Admin' },
]

const columns: CommandDataTableColumn[] = [
  { key: 'userName', label: 'User name', sortable: true },
  { key: 'email', label: 'Email', sortable: true },
  { key: 'role', label: 'Role' },
  { key: 'actions', label: 'Actions', align: 'right' },
]

const search = ref('')
const sortKey = ref('userName')
const sortDir = ref<'asc' | 'desc'>('asc')

function toggleSort(key: string) {
  if (sortKey.value === key) {
    sortDir.value = sortDir.value === 'asc' ? 'desc' : 'asc'
    return
  }
  sortKey.value = key
  sortDir.value = 'asc'
}

const filteredUsers = computed(() => {
  const query = search.value.trim().toLowerCase()
  const rows = query
    ? props.users.filter(user => user.userName.toLowerCase().includes(query) || user.email.toLowerCase().includes(query))
    : [...props.users]
  const key = sortKey.value as 'userName' | 'email'
  rows.sort((a, b) => {
    const result = (a[key] ?? '').localeCompare(b[key] ?? '')
    return sortDir.value === 'asc' ? result : -result
  })
  return rows
})

const { page, pageCount, pagedRows } = useCommandPagination(filteredUsers)

function roleTone(role: string): 'danger' | 'info' | 'default' {
  const value = role.toLowerCase()
  if (value === 'admin')
    return 'danger'
  if (value === 'organizer')
    return 'info'
  return 'default'
}
</script>

<template>
  <section class="admin-users">
    <CommandPageHeader
      signal-label="Admin API / account directory"
      signal-tone="warning"
      title="User accounts"
      description="Review platform accounts, adjust roles, and reset credentials."
      :stat-icon="UsersRound"
      :stat-value="String(props.users.length).padStart(2, '0')"
      stat-label="accounts"
    >
      <CommandBadge label="Admin only" tone="danger" />
    </CommandPageHeader>

    <p v-if="props.operationMessage" class="admin-users__message" :class="`admin-users__message--${props.operationTone || 'success'}`" role="status">
      {{ props.operationMessage }}
    </p>

    <CommandPanel class="admin-users__filters">
      <div class="admin-users__search">
        <Search class="size-4 text-[var(--v2-text-faint)]" />
        <CommandInput v-model="search" type="search" label="Search users" placeholder="Search by name or email" />
      </div>
    </CommandPanel>

    <CommandPanel v-if="props.state === 'error'" class="admin-users__state" tone="warning">
      <h2>Unable to load users</h2>
      <p>{{ props.errorMessage || 'The service did not return a usable user list.' }}</p>
      <CommandButton label="Retry" tone="outline" @click="emit('retry')" />
    </CommandPanel>

    <CommandDataTable
      v-else
      :columns="columns"
      :row-count="filteredUsers.length"
      :loading="props.state === 'loading'"
      empty-label="No user accounts found."
      :page="page"
      :page-count="pageCount"
      :total-count="props.users.length"
      :sort-key="sortKey"
      :sort-dir="sortDir"
      @update:page="page = $event"
      @sort="toggleSort"
    >
      <tr v-for="user in pagedRows" :key="user.id">
        <td><span class="cell-strong">{{ user.userName }}</span></td>
        <td>{{ user.email }}</td>
        <td><CommandBadge :label="user.role" :tone="roleTone(user.role)" /></td>
        <td>
          <div class="cell-actions">
            <CommandButton label="Change role" tone="ghost" @click="emit('openRole', user)">
              <template #icon><UserCog class="size-4" /></template>
            </CommandButton>
            <CommandButton label="Reset password" tone="ghost" @click="emit('openPassword', user)">
              <template #icon><KeyRound class="size-4" /></template>
            </CommandButton>
          </div>
        </td>
      </tr>
    </CommandDataTable>

    <CommandDialog
      :open="props.roleDialogOpen"
      signal-label="Account role"
      signal-tone="warning"
      :title="`Change role / ${props.selectedUser?.userName ?? ''}`"
      width="400px"
      @update:open="emit('update:roleDialogOpen', $event)"
    >
      <CommandSelect :model-value="props.newRole" label="New role" :options="roleOptions" @update:model-value="emit('update:newRole', $event)" />
      <p v-if="props.newRole === 'admin'" class="admin-users__admin-warning">
        <ShieldAlert class="size-4" />
        Admin accounts bypass every operator restriction. Grant this role only to trusted operators.
      </p>
      <template #footer>
        <CommandButton label="Cancel" tone="ghost" @click="emit('update:roleDialogOpen', false)" />
        <CommandButton :label="props.rolePending ? 'Saving' : 'Save'" :disabled="props.rolePending" @click="emit('saveRole')" />
      </template>
    </CommandDialog>

    <CommandDialog
      :open="props.passwordDialogOpen"
      signal-label="Credential reset"
      signal-tone="warning"
      :title="`Reset password / ${props.selectedUser?.userName ?? ''}`"
      width="400px"
      @update:open="emit('update:passwordDialogOpen', $event)"
    >
      <CommandInput
        :model-value="props.newPassword"
        type="password"
        label="New password"
        placeholder="Minimum 8 characters"
        autocomplete="new-password"
        @update:model-value="emit('update:newPassword', $event)"
      />
      <template #footer>
        <CommandButton label="Cancel" tone="ghost" @click="emit('update:passwordDialogOpen', false)" />
        <CommandButton
          :label="props.passwordPending ? 'Resetting' : 'Reset'"
          :disabled="props.passwordPending || props.newPassword.length < 8"
          @click="emit('savePassword')"
        />
      </template>
    </CommandDialog>
  </section>
</template>

<style scoped>
.admin-users { display: grid; gap: 16px; }
.admin-users__message { margin: 0; border-radius: 12px; padding: 12px 14px; color: var(--v2-cyan); background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 13px; }
.admin-users__message--danger { color: var(--v2-danger); }
.admin-users__filters { padding: 14px 16px; }
.admin-users__search { display: grid; grid-template-columns: auto minmax(0, 1fr); align-items: center; gap: 10px; }
.admin-users__state { display: grid; min-height: 150px; align-content: center; justify-items: start; gap: 10px; padding: 24px; }
.admin-users__state h2 { margin: 0; color: var(--v2-text); font-size: 17px; font-weight: 600; }
.admin-users__state p { margin: 0; color: var(--v2-text-muted); font-size: 13px; }
.admin-users__admin-warning { display: flex; align-items: flex-start; gap: 8px; margin: 0; border-radius: 12px; padding: 12px; color: var(--v2-danger); background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 12px; line-height: 1.5; }
</style>
