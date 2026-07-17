<script setup lang="ts">
import { computed, ref } from 'vue'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { useRoute, useRouter } from 'vue-router'
import type {
  NoCtfapiEndpointsCompetitionsCompetitionDetailDto,
  NoCtfapiEndpointsCompetitionsMyCompetitionTeamDto,
} from '@/api/generated/types.gen'
import { competitionApi, teamApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import CommandCompetitionRegistrationWorkspace, {
  type CommandRegistrationCompetition,
  type CommandRegistrationTeam,
} from '../components/CommandCompetitionRegistrationWorkspace.vue'

const guidPattern = /^[\da-f]{8}-(?:[\da-f]{4}-){3}[\da-f]{12}$/i

const route = useRoute()
const router = useRouter()
const queryClient = useQueryClient()
const newTeamName = ref('')
const selectedTrackName = ref('')
const joinToken = ref('')
const operationMessage = ref('')
const operationTone = ref<'success' | 'danger'>('success')

const competitionId = computed(() => typeof route.params.id === 'string' ? route.params.id.trim() : '')
const hasValidCompetitionId = computed(() => guidPattern.test(competitionId.value))

const competitionQuery = useQuery({
  queryKey: computed(() => queryKeys.competition(competitionId.value)),
  queryFn: () => competitionApi.get<NoCtfapiEndpointsCompetitionsCompetitionDetailDto>(competitionId.value),
  enabled: hasValidCompetitionId,
})

const teamsQuery = useQuery({
  queryKey: computed(() => queryKeys.myCompetitionTeams(competitionId.value)),
  queryFn: () => competitionApi.myTeams<NoCtfapiEndpointsCompetitionsMyCompetitionTeamDto[]>(competitionId.value),
  enabled: computed(() => hasValidCompetitionId.value && Boolean(competitionQuery.data.value?.id)),
})

const competition = computed<CommandRegistrationCompetition | undefined>(() => {
  const source = competitionQuery.data.value
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

const teams = computed<CommandRegistrationTeam[]>(() => (teamsQuery.data.value ?? [])
  .filter((team): team is NoCtfapiEndpointsCompetitionsMyCompetitionTeamDto & { id: string, competitionId: string } => Boolean(team.id && team.competitionId))
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
  if (competitionQuery.isLoading.value)
    return 'loading'
  if (competitionQuery.isError.value || !competition.value)
    return 'error'
  return 'ready'
})

const errorMessage = computed(() => {
  const error = competitionQuery.error.value
  return error instanceof Error ? error.message : undefined
})

const createTeamMutation = useMutation({
  mutationFn: () => teamApi.create<NoCtfapiEndpointsCompetitionsMyCompetitionTeamDto>({
    competitionId: competitionId.value,
    name: newTeamName.value.trim(),
    trackName: competition.value?.tracksEnabled ? selectedTrackName.value : undefined,
  }),
  onSuccess: async () => {
    newTeamName.value = ''
    selectedTrackName.value = ''
    operationTone.value = 'success'
    operationMessage.value = 'Team created. Registration status has been synchronized.'
    await refreshTeamData()
  },
  onError: (error) => {
    operationTone.value = 'danger'
    operationMessage.value = error instanceof Error ? error.message : 'Team creation failed.'
  },
})

const joinByTokenMutation = useMutation({
  mutationFn: () => teamApi.joinByToken<NoCtfapiEndpointsCompetitionsMyCompetitionTeamDto>(joinToken.value.trim()),
  onSuccess: async (team) => {
    joinToken.value = ''
    operationTone.value = 'success'
    operationMessage.value = 'Team joined. Registration status has been synchronized.'
    await refreshTeamData(team.competitionId)
    if (team.competitionId && team.competitionId !== competitionId.value)
      router.push({ name: 'competition-register', params: { id: team.competitionId } })
  },
  onError: (error) => {
    operationTone.value = 'danger'
    operationMessage.value = error instanceof Error ? error.message : 'Unable to join the team with this token.'
  },
})

function refreshTeamData(relatedCompetitionId?: string) {
  const keys = [
    queryClient.invalidateQueries({ queryKey: queryKeys.myCompetitionTeams(competitionId.value) }),
    queryClient.invalidateQueries({ queryKey: queryKeys.myTeams }),
  ]
  if (relatedCompetitionId && relatedCompetitionId !== competitionId.value)
    keys.push(queryClient.invalidateQueries({ queryKey: queryKeys.myCompetitionTeams(relatedCompetitionId) }))
  return Promise.all(keys)
}

function backToRegistry() {
  router.push({ name: 'competitions' })
}

function enterCompetition() {
  router.push({ name: 'competition-detail', params: { id: competitionId.value } })
}

function retry() {
  competitionQuery.refetch()
  teamsQuery.refetch()
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
  <CommandCompetitionRegistrationWorkspace
    v-model:new-team-name="newTeamName"
    v-model:selected-track-name="selectedTrackName"
    v-model:join-token="joinToken"
    :state="workspaceState"
    :error-message="errorMessage"
    :competition="competition"
    :teams="teams"
    :loading-teams="teamsQuery.isLoading.value"
    :teams-error="teamsQuery.isError.value"
    :create-pending="createTeamMutation.isPending.value"
    :join-pending="joinByTokenMutation.isPending.value"
    :operation-message="operationMessage"
    :operation-tone="operationTone"
    @back="backToRegistry"
    @retry="retry"
    @enter="enterCompetition"
    @create="createTeamMutation.mutate()"
    @join="joinByTokenMutation.mutate()"
    @copy="copyInviteToken"
  />
</template>
