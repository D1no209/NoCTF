<script setup lang="ts">
import { computed, ref } from 'vue'
import { useAdminTeamsPage, type AdminTeamDto, type AdminTeamMemberDto } from '@/features/admin/useAdminTeamsPage'
import CommandAdminTeamsWorkspace, {
  type CommandAdminTeam,
  type CommandAdminTeamMember,
} from '../components/CommandAdminTeamsWorkspace.vue'

const {
  teams: teamData,
  isLoading,
  isError,
  error,
  refetch,
  teamMembers,
  loadingMembers,
  membersError,
  loadMembers,
  disbandMutation,
} = useAdminTeamsPage()

const membersDialogOpen = ref(false)
const disbandDialogOpen = ref(false)
const selectedTeam = ref<CommandAdminTeam | null>(null)
const operationMessage = ref('')
const operationTone = ref<'success' | 'danger'>('success')

const teams = computed<CommandAdminTeam[]>(() => (teamData.value ?? [])
  .filter((team): team is AdminTeamDto & { id: string } => Boolean(team.id))
  .map(team => ({
    id: team.id,
    name: team.name?.trim() || 'Unnamed team',
    captainName: team.captainName?.trim() || '-',
    memberCount: Number.isFinite(team.memberCount) ? team.memberCount : 0,
    registrationStatus: team.registrationStatus?.trim() || 'pending',
    competitionTitle: team.competitionTitle?.trim() || '-',
  })))

const members = computed<CommandAdminTeamMember[]>(() => (teamMembers.value ?? [])
  .filter((member): member is AdminTeamMemberDto & { userId: string } => Boolean(member.userId))
  .map(member => ({
    userId: member.userId,
    userName: member.userName?.trim() || 'Unnamed user',
    role: member.role?.trim() || 'member',
  })))

const membersErrorMessage = computed(() => {
  const cause = membersError.value
  if (!cause)
    return ''
  return cause instanceof Error ? cause.message : 'Unable to load team members.'
})

const workspaceState = computed<'loading' | 'error' | 'ready'>(() => {
  if (isLoading.value)
    return 'loading'
  if (isError.value)
    return 'error'
  return 'ready'
})

const errorMessage = computed(() => {
  const cause = error.value
  return cause instanceof Error ? cause.message : undefined
})

async function openMembers(team: CommandAdminTeam) {
  selectedTeam.value = team
  membersDialogOpen.value = true
  await loadMembers(team.id)
}

function openDisband(team: CommandAdminTeam) {
  selectedTeam.value = team
  disbandDialogOpen.value = true
}

function disband() {
  const team = selectedTeam.value
  if (!team)
    return
  disbandMutation.mutate(team.id, {
    onSuccess: () => {
      disbandDialogOpen.value = false
      operationTone.value = 'success'
      operationMessage.value = 'Team disbanded successfully.'
    },
    onError: (cause) => {
      operationTone.value = 'danger'
      operationMessage.value = cause instanceof Error ? cause.message : 'Failed to disband team.'
    },
  })
}

function retry() {
  void refetch()
}
</script>

<template>
  <CommandAdminTeamsWorkspace
    v-model:members-dialog-open="membersDialogOpen"
    v-model:disband-dialog-open="disbandDialogOpen"
    :state="workspaceState"
    :error-message="errorMessage"
    :teams="teams"
    :selected-team="selectedTeam"
    :members="members"
    :members-loading="loadingMembers"
    :members-error="membersErrorMessage || undefined"
    :disband-pending="disbandMutation.isPending.value"
    :operation-message="operationMessage"
    :operation-tone="operationTone"
    @retry="retry"
    @open-members="openMembers"
    @open-disband="openDisband"
    @disband="disband"
  />
</template>
