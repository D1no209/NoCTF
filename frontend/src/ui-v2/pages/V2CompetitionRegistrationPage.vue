<script setup lang="ts">
import { computed, ref } from 'vue'
import CommandCompetitionRegistrationWorkspace, {
  type CommandRegistrationCompetition,
  type CommandRegistrationTeam,
} from '../components/CommandCompetitionRegistrationWorkspace.vue'
import { useCompetitionRegistrationPage } from '@/features/competitions/useCompetitionRegistrationPage'

const {
  newTeamName,
  selectedTrackName,
  joinToken,
  hasValidCompetitionId,
  competition: competitionRaw,
  loadingCompetition,
  competitionError,
  competitionQueryError,
  refetchCompetition,
  myTeams,
  loadingTeams,
  teamsError,
  refetchTeams,
  createTeamMutation,
  joinByTokenMutation,
  copyToken,
  goCompetitions,
  goCompetitionDetail,
} = useCompetitionRegistrationPage()

const operationMessage = ref('')
const operationTone = ref<'success' | 'danger'>('success')

const competition = computed<CommandRegistrationCompetition | undefined>(() => {
  const source = competitionRaw.value
  if (!source?.id)
    return undefined

  return {
    id: source.id,
    title: source.title?.trim() || 'Untitled competition',
    description: source.description ?? null,
    status: source.status?.trim() || 'Unknown',
    gameModeType: source.gameModeType?.trim() || 'CTF',
    startTime: source.startTime ?? null,
    maxTeamMembers: source.maxTeamMembers ?? null,
    teamRegistrationAutoApprove: source.teamRegistrationAutoApprove ?? false,
    tracksEnabled: source.tracksEnabled ?? false,
    trackNames: source.trackNames ?? [],
  }
})

const teams = computed<CommandRegistrationTeam[]>(() => (myTeams.value ?? [])
  .filter((team): team is typeof team & { id: string, competitionId: string } => Boolean(team.id && team.competitionId))
  .map(team => ({
    id: team.id,
    competitionId: team.competitionId,
    name: team.name?.trim() || 'Untitled team',
    inviteToken: team.inviteToken ?? null,
    memberCount: team.memberCount ?? null,
    isLocked: team.isLocked ?? false,
    isBanned: team.isBanned ?? false,
    bannedReason: team.bannedReason ?? null,
    trackName: team.trackName ?? null,
    registrationStatus: team.registrationStatus?.trim() || 'Unknown',
    isCaptain: team.isCaptain ?? false,
  })))

const workspaceState = computed<'invalid' | 'loading' | 'error' | 'ready'>(() => {
  if (!hasValidCompetitionId.value)
    return 'invalid'
  if (loadingCompetition.value)
    return 'loading'
  if (competitionError.value || !competition.value)
    return 'error'
  return 'ready'
})

const errorMessage = computed(() => {
  const error = competitionQueryError.value
  return error instanceof Error ? error.message : undefined
})

function reportSuccess(message: string) {
  operationTone.value = 'success'
  operationMessage.value = message
}

function reportError(error: unknown, fallback: string) {
  operationTone.value = 'danger'
  operationMessage.value = error instanceof Error ? error.message : fallback
}

function createTeam() {
  createTeamMutation.mutate(undefined, {
    onSuccess: () => reportSuccess('Team created. Registration status has been synchronized.'),
    onError: error => reportError(error, 'Team creation failed.'),
  })
}

function joinByToken() {
  joinByTokenMutation.mutate(undefined, {
    onSuccess: () => reportSuccess('Team joined. Registration status has been synchronized.'),
    onError: error => reportError(error, 'Unable to join the team with this token.'),
  })
}

function retry() {
  refetchCompetition()
  refetchTeams()
}

async function copyInviteToken(token: string) {
  if (await copyToken(token))
    reportSuccess('Invitation token copied to the clipboard.')
  else
    reportError(null, 'Unable to copy the invitation token in this browser.')
}
</script>

<template>
  <CommandCompetitionRegistrationWorkspace
    v-model:new-team-name="newTeamName"
    v-model:selected-track-name="selectedTrackName"
    v-model:join-token="joinToken"
    :state="workspaceState"
    :error-message="errorMessage"
    :competition="competition"
    :teams="teams"
    :loading-teams="loadingTeams"
    :teams-error="teamsError"
    :create-pending="createTeamMutation.isPending.value"
    :join-pending="joinByTokenMutation.isPending.value"
    :operation-message="operationMessage"
    :operation-tone="operationTone"
    @back="goCompetitions"
    @retry="retry"
    @enter="goCompetitionDetail"
    @create="createTeam"
    @join="joinByToken"
    @copy="copyInviteToken"
  />
</template>
