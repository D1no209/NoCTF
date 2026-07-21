<script setup lang="ts">
import { computed, ref } from 'vue'
import CommandTeamsWorkspace, {
  type CommandMyTeam,
  type CommandTeamCompetition,
} from '../components/CommandTeamsWorkspace.vue'
import { useTeamsPage } from '@/features/teams/useTeamsPage'

const {
  selectedCompetitionId,
  selectedTrackName,
  newTeamName,
  joinToken,
  teams: rawTeams,
  isLoading,
  isError,
  error,
  availableCompetitions: rawCompetitions,
  loadingCompetitions,
  selectedCompetitionRequiresTrack,
  selectedCompetitionTracks,
  loadingSelectedCompetition,
  createTeamMutation,
  joinByTokenMutation,
  leaveTeamMutation,
  copyToken,
  retry,
  goCompetitionDetail,
} = useTeamsPage()

const operationMessage = ref('')
const operationTone = ref<'success' | 'danger'>('success')
const leavingTeamId = ref<string | null>(null)

const teams = computed<CommandMyTeam[]>(() => (rawTeams.value ?? [])
  .filter((team): team is typeof team & { id: string, competitionId: string } => Boolean(team.id && team.competitionId))
  .map(team => ({
    id: team.id,
    competitionId: team.competitionId,
    name: team.name?.trim() || 'Untitled team',
    inviteToken: team.inviteToken ?? null,
    registrationStatus: team.registrationStatus?.trim() || 'Unknown',
    competitionTitle: team.competitionTitle?.trim() || 'Untitled competition',
    competitionStatus: team.competitionStatus?.trim() || 'Unknown',
    gameModeType: team.gameModeType?.trim() || 'CTF',
    memberCount: team.memberCount ?? null,
    maxTeamMembers: team.maxTeamMembers ?? null,
    isCaptain: team.isCaptain ?? false,
    isLocked: team.isLocked ?? false,
    isBanned: team.isBanned ?? false,
    trackName: team.trackName ?? null,
  })))

const competitions = computed<CommandTeamCompetition[]>(() => rawCompetitions.value
  .filter((competition): competition is typeof competition & { id: string } => Boolean(competition.id))
  .map(competition => ({
    id: competition.id,
    title: competition.title?.trim() || 'Untitled competition',
    status: competition.status?.trim() || 'Unknown',
    gameModeType: competition.gameModeType?.trim() || 'CTF',
  })))

const teamsState = computed<'loading' | 'error' | 'ready'>(() => {
  if (isLoading.value)
    return 'loading'
  if (isError.value)
    return 'error'
  return 'ready'
})

const teamsErrorMessage = computed(() => {
  const value = error.value
  return value instanceof Error ? value.message : undefined
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
    onSuccess: () => reportSuccess('Team created. Your team list has been synchronized.'),
    onError: error => reportError(error, 'Team creation failed.'),
  })
}

function joinByToken() {
  joinByTokenMutation.mutate(undefined, {
    onSuccess: () => reportSuccess('Team joined. Your team list has been synchronized.'),
    onError: error => reportError(error, 'Unable to join the team with this token.'),
  })
}

function leaveTeam(team: CommandMyTeam) {
  leavingTeamId.value = team.id
  operationMessage.value = ''
  leaveTeamMutation.mutate(team.id, {
    onSuccess: () => {
      leavingTeamId.value = null
      reportSuccess(`Left ${team.name}. Your team list has been synchronized.`)
    },
    onError: (error) => {
      leavingTeamId.value = null
      reportError(error, 'Unable to leave this team.')
    },
  })
}

async function copyInviteToken(token: string) {
  if (await copyToken(token))
    reportSuccess('Invitation token copied to the clipboard.')
  else
    reportError(null, 'Unable to copy the invitation token in this browser.')
}
</script>

<template>
  <CommandTeamsWorkspace
    v-model:selected-competition-id="selectedCompetitionId"
    v-model:selected-track-name="selectedTrackName"
    v-model:new-team-name="newTeamName"
    v-model:join-token="joinToken"
    :state="teamsState"
    :error-message="teamsErrorMessage"
    :teams="teams"
    :competitions="competitions"
    :loading-competitions="loadingCompetitions"
    :tracks="selectedCompetitionTracks"
    :tracks-required="selectedCompetitionRequiresTrack"
    :loading-tracks="loadingSelectedCompetition"
    :create-pending="createTeamMutation.isPending.value"
    :join-pending="joinByTokenMutation.isPending.value"
    :leaving-team-id="leavingTeamId"
    :operation-message="operationMessage"
    :operation-tone="operationTone"
    @retry="retry"
    @create="createTeam"
    @join="joinByToken"
    @leave="leaveTeam"
    @copy="copyInviteToken"
    @open="goCompetitionDetail"
  />
</template>
