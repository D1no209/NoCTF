<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import type {
  NoCtfapiEndpointsCompetitionsAwdDashboardDto,
  NoCtfapiEndpointsCompetitionsChallengeDto,
} from '@/api/generated/types.gen'
import { competitionApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { useSignalR, type AttackLogDto } from '@/composables/useSignalR'
import { useAuthStore } from '@/stores/auth'
import type { CommandAwdAttack, CommandAwdChallenge, CommandAwdService } from '../components/awd-contract'
import CommandAwdDashboardWorkspace from '../components/CommandAwdDashboardWorkspace.vue'

const guidPattern = /^[\da-f]{8}-(?:[\da-f]{4}-){3}[\da-f]{12}$/i

const route = useRoute()
const queryClient = useQueryClient()
const auth = useAuthStore()
const selectedChallengeId = ref('')
const flag = ref('')
const operationMessage = ref('')
const operationTone = ref<'success' | 'danger'>('success')
const attacks = ref<CommandAwdAttack[]>([])

const competitionId = computed(() => typeof route.params.id === 'string' ? route.params.id.trim() : '')
const hasValidCompetitionId = computed(() => guidPattern.test(competitionId.value))

const dashboardQuery = useQuery({
  queryKey: computed(() => queryKeys.awdDashboard(competitionId.value)),
  queryFn: () => competitionApi.awdDashboard<NoCtfapiEndpointsCompetitionsAwdDashboardDto>(competitionId.value),
  enabled: hasValidCompetitionId,
  refetchInterval: 10_000,
})

const challengesQuery = useQuery({
  queryKey: computed(() => queryKeys.challenges(competitionId.value)),
  queryFn: () => competitionApi.challenges<NoCtfapiEndpointsCompetitionsChallengeDto[]>(competitionId.value),
  enabled: hasValidCompetitionId,
})

const services = computed<CommandAwdService[]>(() => (dashboardQuery.data.value?.services ?? [])
  .filter((service): service is Required<NoCtfapiEndpointsCompetitionsAwdDashboardDto>['services'][number] & { teamId: string, challengeId: string } =>
    Boolean(service.teamId && service.challengeId),
  )
  .map(service => ({
    teamId: service.teamId,
    teamName: service.teamName?.trim() || 'Unknown team',
    challengeId: service.challengeId,
    challengeName: service.challengeName?.trim() || 'Unknown service',
    status: normalizeServiceStatus(service.status),
  })))

const challenges = computed<CommandAwdChallenge[]>(() => (challengesQuery.data.value ?? [])
  .filter((challenge): challenge is NoCtfapiEndpointsCompetitionsChallengeDto & { id: string } => Boolean(challenge.id))
  .map(challenge => ({ id: challenge.id, title: challenge.title?.trim() || 'Untitled challenge' })))

const signalR = useSignalR({
  hubUrl: `/hubs/game?competitionId=${competitionId.value}`,
  accessToken: () => auth.accessToken,
})

signalR.onRoundStarted(() => {
  void dashboardQuery.refetch()
})
signalR.onAttackLog((log) => {
  attacks.value = [mapAttack(log), ...attacks.value].slice(0, 100)
})

const submitMutation = useMutation({
  mutationFn: async () => {
    const challengeId = selectedChallengeId.value.trim()
    const submittedFlag = flag.value.trim()
    if (!challengeId || !submittedFlag)
      throw new Error('A challenge and flag value are required.')
    return competitionApi.submitFlag<{ correct?: boolean, message?: string }>(competitionId.value, challengeId, submittedFlag)
  },
  onSuccess: async (result) => {
    if (result?.correct) {
      flag.value = ''
      operationTone.value = 'success'
      operationMessage.value = result.message || 'Flag accepted. Competition data has been refreshed.'
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.awdDashboard(competitionId.value) }),
        queryClient.invalidateQueries({ queryKey: queryKeys.leaderboard(competitionId.value) }),
        queryClient.invalidateQueries({ queryKey: queryKeys.submissions(competitionId.value) }),
      ])
      return
    }
    operationTone.value = 'danger'
    operationMessage.value = result?.message || 'The server rejected this flag.'
  },
  onError: (error) => {
    operationTone.value = 'danger'
    operationMessage.value = error instanceof Error ? error.message : 'Flag submission failed.'
  },
})

const workspaceState = computed<'invalid' | 'loading' | 'error' | 'ready'>(() => {
  if (!hasValidCompetitionId.value)
    return 'invalid'
  if (dashboardQuery.isLoading.value)
    return 'loading'
  if (dashboardQuery.isError.value)
    return 'error'
  return 'ready'
})
const errorMessage = computed(() => {
  const error = dashboardQuery.error.value
  return error instanceof Error ? error.message : undefined
})
const round = computed(() => dashboardQuery.data.value?.currentRound ?? 0)
const remainingSeconds = computed(() => dashboardQuery.data.value?.remainingSeconds ?? 0)
const totalSeconds = computed(() => dashboardQuery.data.value?.roundDurationSeconds ?? 300)

function retry() {
  void dashboardQuery.refetch()
  void challengesQuery.refetch()
}

function normalizeServiceStatus(value?: string): CommandAwdService['status'] {
  const status = value?.trim().toLowerCase()
  if (status === 'healthy' || status === 'up' || status === 'serviceok')
    return 'healthy'
  if (status === 'down' || status === 'error' || status === 'serviceerror')
    return 'down'
  return 'unknown'
}

function mapAttack(log: AttackLogDto): CommandAwdAttack {
  const timestamp = log.timestamp || new Date().toISOString()
  return {
    id: `${log.attackerTeamId}:${log.victimTeamId}:${log.challengeId}:${timestamp}`,
    attackerTeamName: log.attackerTeamName || 'Unknown attacker',
    victimTeamName: log.victimTeamName || 'Unknown target',
    challengeName: log.challengeName || 'Unknown service',
    roundNumber: log.roundNumber ?? round.value,
    timestamp,
  }
}

onMounted(() => {
  if (hasValidCompetitionId.value)
    void signalR.start()
})

onUnmounted(() => {
  void signalR.stop()
})
</script>

<template>
  <CommandAwdDashboardWorkspace
    v-model:selected-challenge-id="selectedChallengeId"
    v-model:flag="flag"
    :state="workspaceState"
    :error-message="errorMessage"
    :round="round"
    :remaining-seconds="remainingSeconds"
    :total-seconds="totalSeconds"
    :services="services"
    :attacks="attacks"
    :challenges="challenges"
    :submit-pending="submitMutation.isPending.value"
    :operation-message="operationMessage"
    :operation-tone="operationTone"
    :signal-connected="signalR.isConnected.value"
    @retry="retry"
    @submit="submitMutation.mutate()"
  />
</template>
