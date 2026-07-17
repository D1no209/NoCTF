<script setup lang="ts">
import { computed, ref } from 'vue'
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { useRoute, useRouter } from 'vue-router'
import type {
  NoCtfapiEndpointsCompetitionsChallengeDto,
  NoCtfapiEndpointsCompetitionsCompetitionDetailDto,
  NoCtfapiEndpointsCompetitionsGetLeaderboardResponse,
  NoCtfapiEndpointsCompetitionsMyCompetitionTeamDto,
} from '@/api/generated/types.gen'
import { competitionApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import CommandChallengeConsole from '../components/CommandChallengeConsole.vue'
import CommandCompetitionDetailWorkspace, {
  type CommandCompetitionDetail,
} from '../components/CommandCompetitionDetailWorkspace.vue'
import type { CommandChallenge } from '../components/CommandChallengeGrid.vue'
import type { CommandLeaderboardEntry } from '../components/CommandLeaderboardTable.vue'

const guidPattern = /^[\da-f]{8}-(?:[\da-f]{4}-){3}[\da-f]{12}$/i

const route = useRoute()
const router = useRouter()
const queryClient = useQueryClient()
const selectedChallenge = ref<CommandChallenge | null>(null)
const challengeConsoleOpen = ref(false)

const competitionId = computed(() => typeof route.params.id === 'string' ? route.params.id.trim() : '')
const hasValidCompetitionId = computed(() => guidPattern.test(competitionId.value))

const competitionQuery = useQuery({
  queryKey: computed(() => queryKeys.competition(competitionId.value)),
  queryFn: () => competitionApi.get<NoCtfapiEndpointsCompetitionsCompetitionDetailDto>(competitionId.value),
  enabled: hasValidCompetitionId,
})

const challengesQuery = useQuery({
  queryKey: computed(() => queryKeys.challenges(competitionId.value)),
  queryFn: () => competitionApi.challenges<NoCtfapiEndpointsCompetitionsChallengeDto[]>(competitionId.value),
  enabled: computed(() => hasValidCompetitionId.value && Boolean(competitionQuery.data.value?.id)),
})

const leaderboardQuery = useQuery({
  queryKey: computed(() => queryKeys.leaderboard(competitionId.value)),
  queryFn: () => competitionApi.leaderboard(competitionId.value) as Promise<NoCtfapiEndpointsCompetitionsGetLeaderboardResponse>,
  enabled: computed(() => hasValidCompetitionId.value && Boolean(competitionQuery.data.value?.id)),
})

const myTeamsQuery = useQuery({
  queryKey: computed(() => queryKeys.myCompetitionTeams(competitionId.value)),
  queryFn: () => competitionApi.myTeams<NoCtfapiEndpointsCompetitionsMyCompetitionTeamDto[]>(competitionId.value),
  enabled: computed(() => hasValidCompetitionId.value && Boolean(competitionQuery.data.value?.id)),
})

const competition = computed<CommandCompetitionDetail | undefined>(() => {
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
    endTime: source.endTime ?? null,
    maxTeamMembers: source.maxTeamMembers ?? null,
    tracksEnabled: source.tracksEnabled ?? false,
    trackNames: source.trackNames ?? [],
  }
})

const challenges = computed<CommandChallenge[]>(() => (challengesQuery.data.value ?? [])
  .filter((challenge): challenge is NoCtfapiEndpointsCompetitionsChallengeDto & { id: string } => Boolean(challenge.id))
  .map(challenge => ({
    id: challenge.id,
    title: challenge.title?.trim() || 'Untitled challenge',
    description: challenge.description ?? null,
    typeId: challenge.typeId?.trim() || 'misc',
    points: challenge.points ?? 0,
    solveCount: challenge.solveCount ?? 0,
  })))

const leaderboard = computed<CommandLeaderboardEntry[]>(() => leaderboardQuery.data.value?.entries ?? [])
const isAwdp = computed(() => competition.value?.gameModeType.toLowerCase() === 'awdp')
const approvedTeam = computed(() => (myTeamsQuery.data.value ?? []).find(team => team.registrationStatus?.toLowerCase() === 'approved'))
const canUseParticipantActions = computed(() => Boolean(approvedTeam.value && !approvedTeam.value.isBanned))

interface AwdpChallengeState {
  challengeId: string
  instanceStatus: string
  canSubmitFlag?: boolean
}

interface AwdpStateView {
  viewKey?: string
  data?: {
    challenges?: AwdpChallengeState[]
  }
}

const awdpStateQuery = useQuery({
  queryKey: computed(() => queryKeys.awdpState(competitionId.value)),
  queryFn: () => competitionApi.view<AwdpStateView>(competitionId.value, 'challenge-state'),
  enabled: computed(() => hasValidCompetitionId.value && isAwdp.value && canUseParticipantActions.value),
})

const selectedAwdpState = computed(() => {
  if (!selectedChallenge.value)
    return undefined
  return awdpStateQuery.data.value?.data?.challenges?.find(item => item.challengeId === selectedChallenge.value?.id)
})

const canSubmitSelectedChallenge = computed(() => {
  if (!selectedChallenge.value || !canUseParticipantActions.value)
    return false
  if (!isAwdp.value)
    return true
  return selectedAwdpState.value?.instanceStatus === 'InstanceRunning'
    && selectedAwdpState.value.canSubmitFlag !== false
})

const submissionUnavailableReason = computed(() => {
  if (myTeamsQuery.isLoading.value)
    return 'Checking team enrollment before enabling flag submission.'
  if (!approvedTeam.value)
    return 'Register with an approved team before submitting flags.'
  if (approvedTeam.value.isBanned)
    return 'This team is restricted from participant actions.'
  if (!isAwdp.value)
    return ''
  if (awdpStateQuery.isLoading.value)
    return 'Synchronizing AWDP task state before enabling flag submission.'
  if (awdpStateQuery.isError.value)
    return 'AWDP task state could not be verified, so flag submission is unavailable.'
  if (!selectedAwdpState.value)
    return 'No active AWDP state was returned for this challenge.'
  if (selectedAwdpState.value.instanceStatus !== 'InstanceRunning')
    return 'A running AWDP instance is required before submitting a flag.'
  if (selectedAwdpState.value.canSubmitFlag === false)
    return 'Flag submission is not available for this challenge in the current round.'
  return ''
})

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

function backToRegistry() {
  router.push({ name: 'competitions' })
}

function openRegistration() {
  router.push({ name: 'competition-register', params: { id: competitionId.value } })
}

function openAwdpScreen() {
  router.push({ name: 'awdp-screen', params: { gameId: competitionId.value } })
}

function openAwdDashboard() {
  router.push({ name: 'awd-dashboard', params: { id: competitionId.value } })
}

function selectChallenge(challenge: CommandChallenge) {
  selectedChallenge.value = challenge
  challengeConsoleOpen.value = true
}

function retry() {
  competitionQuery.refetch()
}

function refreshCompetitionData() {
  void Promise.all([
    queryClient.invalidateQueries({ queryKey: queryKeys.challenges(competitionId.value) }),
    queryClient.invalidateQueries({ queryKey: queryKeys.submissions(competitionId.value) }),
    queryClient.invalidateQueries({ queryKey: queryKeys.leaderboard(competitionId.value) }),
    ...(isAwdp.value
      ? [queryClient.invalidateQueries({ queryKey: queryKeys.awdpState(competitionId.value) })]
      : []),
  ])
}
</script>

<template>
  <CommandCompetitionDetailWorkspace
    :state="workspaceState"
    :error-message="errorMessage"
    :competition="competition"
    :challenges="challenges"
    :leaderboard="leaderboard"
    :loading-challenges="challengesQuery.isLoading.value"
    :loading-leaderboard="leaderboardQuery.isLoading.value"
    :challenge-error="challengesQuery.isError.value"
    :leaderboard-error="leaderboardQuery.isError.value"
    @back="backToRegistry"
    @register="openRegistration"
    @retry="retry"
    @open-awdp-screen="openAwdpScreen"
    @open-awd-dashboard="openAwdDashboard"
    @select-challenge="selectChallenge"
  />

  <CommandChallengeConsole
    v-model:open="challengeConsoleOpen"
    :competition-id="competitionId"
    :challenge="selectedChallenge"
    :can-submit="canSubmitSelectedChallenge"
    :submission-unavailable-reason="submissionUnavailableReason"
    @submitted="refreshCompetitionData"
  />
</template>
