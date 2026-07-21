<script setup lang="ts">
import { computed, ref } from 'vue'
import { Search, Shield, Trash2, Trophy, User, Users, UsersRound } from 'lucide-vue-next'
import CommandBadge from '../primitives/CommandBadge.vue'
import CommandButton from '../primitives/CommandButton.vue'
import CommandDialog from '../primitives/CommandDialog.vue'
import CommandInput from '../primitives/CommandInput.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSignal from '../primitives/CommandSignal.vue'
import CommandDataTable, { type CommandDataTableColumn } from './CommandDataTable.vue'
import CommandPageHeader from './CommandPageHeader.vue'
import { useCommandPagination } from '../composables/useCommandPagination'

export interface CommandAdminTeam {
  id: string
  name: string
  captainName: string
  memberCount: number
  registrationStatus: string
  competitionTitle: string
}

export interface CommandAdminTeamMember {
  userId: string
  userName: string
  role: string
}

const props = defineProps<{
  state: 'loading' | 'error' | 'ready'
  errorMessage?: string
  teams: CommandAdminTeam[]
  membersDialogOpen: boolean
  disbandDialogOpen: boolean
  selectedTeam: CommandAdminTeam | null
  members: CommandAdminTeamMember[]
  membersLoading: boolean
  membersError?: string
  disbandPending: boolean
  operationMessage?: string
  operationTone?: 'success' | 'danger'
}>()

const emit = defineEmits<{
  retry: []
  openMembers: [team: CommandAdminTeam]
  openDisband: [team: CommandAdminTeam]
  disband: []
  'update:membersDialogOpen': [value: boolean]
  'update:disbandDialogOpen': [value: boolean]
}>()

const columns: CommandDataTableColumn[] = [
  { key: 'name', label: 'Name', sortable: true },
  { key: 'captainName', label: 'Captain', sortable: true },
  { key: 'memberCount', label: 'Members', sortable: true },
  { key: 'registrationStatus', label: 'Registration status', sortable: true },
  { key: 'competitionTitle', label: 'Competition', sortable: true },
  { key: 'actions', label: 'Actions', align: 'right' },
]

const search = ref('')
const sortKey = ref('name')
const sortDir = ref<'asc' | 'desc'>('asc')

function toggleSort(key: string) {
  if (sortKey.value === key) {
    sortDir.value = sortDir.value === 'asc' ? 'desc' : 'asc'
    return
  }
  sortKey.value = key
  sortDir.value = 'asc'
}

function statusLabel(status: string) {
  return status.trim() || 'pending'
}

function statusTone(status: string): 'success' | 'danger' | 'default' {
  const value = status.trim().toLowerCase()
  if (value === 'approved')
    return 'success'
  if (value === 'rejected')
    return 'danger'
  return 'default'
}

function isCaptain(role: string) {
  return role.toLowerCase() === 'captain'
}

const filteredTeams = computed(() => {
  const query = search.value.trim().toLowerCase()
  const rows = query
    ? props.teams.filter(team =>
        team.name.toLowerCase().includes(query)
        || team.captainName.toLowerCase().includes(query)
        || statusLabel(team.registrationStatus).toLowerCase().includes(query)
        || team.competitionTitle.toLowerCase().includes(query))
    : [...props.teams]
  const key = sortKey.value
  rows.sort((a, b) => {
    if (key === 'memberCount') {
      const result = a.memberCount - b.memberCount
      return sortDir.value === 'asc' ? result : -result
    }
    const aValue = key === 'registrationStatus' ? statusLabel(a.registrationStatus) : String(a[key as 'name' | 'captainName' | 'competitionTitle'] ?? '')
    const bValue = key === 'registrationStatus' ? statusLabel(b.registrationStatus) : String(b[key as 'name' | 'captainName' | 'competitionTitle'] ?? '')
    const result = aValue.localeCompare(bValue)
    return sortDir.value === 'asc' ? result : -result
  })
  return rows
})

const { page, pageCount, pagedRows } = useCommandPagination(filteredTeams)
</script>

<template>
  <section class="admin-teams">
    <CommandPageHeader
      signal-label="Admin API / team directory"
      signal-tone="warning"
      title="Registered teams"
      description="Audit team rosters, registration states, and disband teams when required."
      :stat-icon="UsersRound"
      :stat-value="String(props.teams.length).padStart(2, '0')"
      stat-label="teams"
    >
      <CommandBadge label="Admin only" tone="danger" />
    </CommandPageHeader>

    <p v-if="props.operationMessage" class="admin-teams__message" :class="`admin-teams__message--${props.operationTone || 'success'}`" role="status">
      {{ props.operationMessage }}
    </p>

    <CommandPanel class="admin-teams__filters">
      <div class="admin-teams__search">
        <Search class="size-4 text-[var(--v2-text-faint)]" />
        <CommandInput v-model="search" type="search" label="Search teams" placeholder="Search by name, captain, status, or competition" />
      </div>
    </CommandPanel>

    <CommandPanel v-if="props.state === 'error'" class="admin-teams__state" tone="warning">
      <h2>Unable to load teams</h2>
      <p>{{ props.errorMessage || 'The service did not return a usable team list.' }}</p>
      <CommandButton label="Retry" tone="outline" @click="emit('retry')" />
    </CommandPanel>

    <CommandDataTable
      v-else
      :columns="columns"
      :row-count="filteredTeams.length"
      :loading="props.state === 'loading'"
      empty-label="No teams found."
      :page="page"
      :page-count="pageCount"
      :total-count="props.teams.length"
      :sort-key="sortKey"
      :sort-dir="sortDir"
      @update:page="page = $event"
      @sort="toggleSort"
    >
      <tr v-for="team in pagedRows" :key="team.id">
        <td><span class="cell-strong">{{ team.name }}</span></td>
        <td>{{ team.captainName }}</td>
        <td><CommandBadge :label="String(team.memberCount)" /></td>
        <td><CommandBadge :label="statusLabel(team.registrationStatus)" :tone="statusTone(team.registrationStatus)" /></td>
        <td>
          <span class="admin-teams__competition">
            <Trophy class="size-3 text-[var(--v2-text-faint)]" />
            {{ team.competitionTitle }}
          </span>
        </td>
        <td>
          <div class="cell-actions">
            <CommandButton label="Members" tone="ghost" @click="emit('openMembers', team)">
              <template #icon><Users class="size-4" /></template>
            </CommandButton>
            <CommandButton label="Disband" tone="ghost" class="admin-teams__danger-action" @click="emit('openDisband', team)">
              <template #icon><Trash2 class="size-4" /></template>
            </CommandButton>
          </div>
        </td>
      </tr>
    </CommandDataTable>

    <CommandDialog
      :open="props.membersDialogOpen"
      signal-label="Team roster"
      signal-tone="info"
      :title="`Team members / ${props.selectedTeam?.name ?? ''}`"
      width="425px"
      @update:open="emit('update:membersDialogOpen', $event)"
    >
      <div v-if="props.membersLoading" class="admin-teams__members-state" aria-busy="true">
        <CommandSignal label="Synchronizing" tone="info" />
        <span>Loading members.</span>
      </div>
      <p v-else-if="props.membersError" class="admin-teams__members-error" role="alert">
        {{ props.membersError }}
      </p>
      <p v-else-if="props.members.length === 0" class="admin-teams__members-empty">
        No members in this team.
      </p>
      <ul v-else class="admin-teams__members">
        <li v-for="member in props.members" :key="member.userId" class="admin-teams__member">
          <span class="admin-teams__member-icon">
            <Shield v-if="isCaptain(member.role)" class="size-4 text-[var(--v2-primary)]" />
            <User v-else class="size-4 text-[var(--v2-text-faint)]" />
          </span>
          <span class="admin-teams__member-identity">
            <strong>{{ member.userName }}</strong>
            <span class="admin-teams__member-id">{{ member.userId.slice(0, 8) }}</span>
          </span>
          <CommandBadge :label="member.role" :tone="isCaptain(member.role) ? 'primary' : 'default'" />
        </li>
      </ul>
      <template #footer>
        <CommandButton label="Close" tone="ghost" @click="emit('update:membersDialogOpen', false)" />
      </template>
    </CommandDialog>

    <CommandDialog
      :open="props.disbandDialogOpen"
      signal-label="Destructive action"
      signal-tone="danger"
      :title="`Disband team / ${props.selectedTeam?.name ?? ''}`"
      width="425px"
      @update:open="emit('update:disbandDialogOpen', $event)"
    >
      <p class="admin-teams__disband-warning">
        <Trash2 class="size-4" />
        Disband {{ props.selectedTeam?.name }}? This permanently removes the team and its registrations. This action cannot be undone.
      </p>
      <template #footer>
        <CommandButton label="Cancel" tone="ghost" :disabled="props.disbandPending" @click="emit('update:disbandDialogOpen', false)" />
        <CommandButton
          :label="props.disbandPending ? 'Disbanding' : 'Disband'"
          :disabled="props.disbandPending"
          class="admin-teams__danger-action admin-teams__danger-action--solid"
          @click="emit('disband')"
        />
      </template>
    </CommandDialog>
  </section>
</template>

<style scoped>
.admin-teams { display: grid; gap: 16px; }
.admin-teams__message { margin: 0; border-radius: 12px; padding: 12px 14px; color: var(--v2-cyan); background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 13px; }
.admin-teams__message--danger { color: var(--v2-danger); }
.admin-teams__filters { padding: 14px 16px; }
.admin-teams__search { display: grid; grid-template-columns: auto minmax(0, 1fr); align-items: center; gap: 10px; }
.admin-teams__state { display: grid; min-height: 150px; align-content: center; justify-items: start; gap: 10px; padding: 24px; }
.admin-teams__state h2 { margin: 0; color: var(--v2-text); font-size: 17px; font-weight: 600; }
.admin-teams__state p { margin: 0; color: var(--v2-text-muted); font-size: 13px; }
.admin-teams__competition { display: inline-flex; align-items: center; gap: 6px; }
.admin-teams__danger-action { color: var(--v2-danger); }
.admin-teams__danger-action--solid { color: #ffffff; background: var(--v2-danger); }
.admin-teams__members-state { display: flex; min-height: 120px; flex-direction: column; align-items: flex-start; justify-content: center; gap: 9px; color: var(--v2-text-muted); font-size: 13px; }
.admin-teams__members-error { margin: 0; border-radius: 12px; padding: 12px 14px; color: var(--v2-danger); background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 13px; }
.admin-teams__members-empty { margin: 0; border-radius: 12px; padding: 24px 14px; color: var(--v2-text-muted); background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 13px; text-align: center; }
.admin-teams__members { display: grid; gap: 10px; margin: 0; padding: 0; list-style: none; }
.admin-teams__member { display: flex; align-items: center; gap: 12px; border-radius: 12px; padding: 10px 12px; background: var(--v2-surface); box-shadow: var(--v2-inset); }
.admin-teams__member-icon { display: grid; width: 32px; height: 32px; flex-shrink: 0; place-items: center; border-radius: 999px; background: var(--v2-surface); box-shadow: var(--v2-inset); }
.admin-teams__member-identity { display: grid; min-width: 0; flex: 1; gap: 3px; }
.admin-teams__member-identity strong { overflow: hidden; color: var(--v2-text); font-size: 13px; font-weight: 600; text-overflow: ellipsis; white-space: nowrap; }
.admin-teams__member-id { color: var(--v2-text-faint); font-family: var(--v2-font-mono); font-size: 10px; }
.admin-teams__disband-warning { display: flex; align-items: flex-start; gap: 8px; margin: 0; border-radius: 12px; padding: 12px; color: var(--v2-danger); background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 12px; line-height: 1.5; }
</style>
