<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { useRouter } from 'vue-router'
import type {
  NoCtfapiEndpointsCompetitionsCompetitionDetailDto,
  NoCtfapiEndpointsCompetitionsCompetitionListItemDto,
  NoCtfapiEndpointsTeamsMyTeamDto,
  NoCtfapiEndpointsTeamsTeamDto,
} from '@/api/generated/types.gen'
import { competitionApi, teamApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import CommandTeamsWorkspace, {
  type CommandMyTeam,
  type CommandTeamCompetition,
} from '../components/CommandTeamsWorkspace.vue'

const router = useRouter()
const queryClient = useQueryClient()
const selectedCompetitionId = ref('')
const selectedTrackName = ref('')
const newTeamName = ref('')
const joinToken = ref('')
const operationMessage = ref('')
const operationTone = ref<'success' | 'danger'>('success')
const leavingTeamId = ref<string | null>(null)

const teamsQuery = useQuery({
  queryKey: queryKeys.myTeams,
  queryFn: () => teamApi.mine<NoCtfapiEndpointsTeamsMyTeamDto[]>(),
})

const competitionsQuery = useQuery({
  queryKey: queryKeys.competitions,
  queryFn: () => competitionApi.list<NoCtfapiEndpointsCompetitionsCompetitionListItemDto[]>(),
})

const selectedCompetitionQuery = useQuery({
  queryKey: computed(() => queryKeys.competition(selectedCompetitionId.value)),
  queryFn: () => competitionApi.get<NoCtfapiEndpointsCompetitionsCompetitionDetailDto>(selectedCompetitionId.value),
  enabled: computed(() => Boolean(selectedCompetitionId.value)),
})

const teams = computed<CommandMyTeam[]>(() => (teamsQuery.data.value ?? [])
  .filter((team): team is NoCtfapiEndpointsTeamsMyTeamDto & { id: string, competitionId: string } => Boolean(team.id && team.competitionId))
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

const competitions = computed<CommandTeamCompetition[]>(() => (competitionsQuery.data.value ?? [])
  .filter((competition): competition is NoCtfapiEndpointsCompetitionsCompetitionListItemDto & { id: string } => Boolean(competition.id))
  .map(competition => ({
    id: competition.id,
    title: competition.title?.trim() || 'Untitled competition',
    status: competition.status?.trim() || 'Unknown',
    gameModeType: competition.gameModeType?.trim() || 'CTF',
  })))

const selectedTracks = computed(() => selectedCompetitionQuery.data.value?.trackNames ?? [])
const selectedCompetitionRequiresTrack = computed(() => selectedCompetitionQuery.data.value?.tracksEnabled ?? false)

const teamsState = computed<'loading' | 'error' | 'ready'>(() => {
  if (teamsQuery.isLoading.value)
    return 'loading'
  if (teamsQuery.isError.value)
    return 'error'
  return 'ready'
})

const teamsErrorMessage = computed(() => {
  const error = teamsQuery.error.value
  return error instanceof Error ? error.message : undefined
})

watch(selectedCompetitionId, () => {
  selectedTrackName.value = ''
})

const createTeamMutation = useMutation({
  mutationFn: () => teamApi.create<NoCtfapiEndpointsTeamsTeamDto>({
    competitionId: selectedCompetitionId.value,
    name: newTeamName.value.trim(),
    trackName: selectedCompetitionRequiresTrack.value ? selectedTrackName.value : undefined,
  }),
  onSuccess: async (team) => {
    newTeamName.value = ''
    selectedTrackName.value = ''
    operationTone.value = 'success'
    operationMessage.value = 'Team created. Your team list has been synchronized.'
    await refreshTeamData(team?.competitionId ?? selectedCompetitionId.value)
  },
  onError: (error) => {
    operationTone.value = 'danger'
    operationMessage.value = error instanceof Error ? error.message : 'Team creation failed.'
  },
})

const joinByTokenMutation = useMutation({
  mutationFn: () => teamApi.joinByToken<NoCtfapiEndpointsTeamsTeamDto>(joinToken.value.trim()),
  onSuccess: async (team) => {
    joinToken.value = ''
    operationTone.value = 'success'
    operationMessage.value = 'Team joined. Your team list has been synchronized.'
    await refreshTeamData(team?.competitionId)
  },
  onError: (error) => {
    operationTone.value = 'danger'
    operationMessage.value = error instanceof Error ? error.message : 'Unable to join the team with this token.'
  },
})

async function leaveTeam(team: CommandMyTeam) {
  leavingTeamId.value = team.id
  operationMessage.value = ''
  try {
    await teamApi.leave(team.id)
    operationTone.value = 'success'
    operationMessage.value = `Left ${team.name}. Your team list has been synchronized.`
    await refreshTeamData(team.competitionId)
  }
  catch (error) {
    operationTone.value = 'danger'
    operationMessage.value = error instanceof Error ? error.message : 'Unable to leave this team.'
  }
  finally {
    leavingTeamId.value = null
  }
}

function refreshTeamData(competitionId?: string) {
  const invalidations = [
    queryClient.invalidateQueries({ queryKey: queryKeys.myTeams }),
  ]
  if (competitionId)
    invalidations.push(queryClient.invalidateQueries({ queryKey: queryKeys.myCompetitionTeams(competitionId) }))
  return Promise.all(invalidations)
}

function retry() {
  teamsQuery.refetch()
  competitionsQuery.refetch()
}

function openCompetition(competitionId: string) {
  router.push({ name: 'competition-detail', params: { id: competitionId } })
}

async function copyInviteToken(token: string) {
  try {
    await navigator.clipboard.writeText(token)
    operationTone.value = 'success'
    operationMessage.value = 'Invitation token copied to the clipboard.'
  }
  catch {
    operationTone.value = 'danger'
    operationMessage.value = 'Unable to copy the invitation token in this browser.'
  }
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
    :loading-competitions="competitionsQuery.isLoading.value"
    :tracks="selectedTracks"
    :tracks-required="selectedCompetitionRequiresTrack"
    :loading-tracks="selectedCompetitionQuery.isLoading.value"
    :create-pending="createTeamMutation.isPending.value"
    :join-pending="joinByTokenMutation.isPending.value"
    :leaving-team-id="leavingTeamId"
    :operation-message="operationMessage"
    :operation-tone="operationTone"
    @retry="retry"
    @create="createTeamMutation.mutate()"
    @join="joinByTokenMutation.mutate()"
    @leave="leaveTeam"
    @copy="copyInviteToken"
    @open="openCompetition"
  />
</template>
