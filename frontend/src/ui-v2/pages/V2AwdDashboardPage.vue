<script setup lang="ts">
import { computed, ref } from 'vue'
import type { AttackLogDto } from '@/composables/useSignalR'
import type { CommandAwdAttack, CommandAwdChallenge, CommandAwdService } from '../components/awd-contract'
import CommandAwdDashboardWorkspace from '../components/CommandAwdDashboardWorkspace.vue'
import { useAwdDashboardPage } from '@/features/competitions/useAwdDashboardPage'

const {
  hasValidCompetitionId,
  round,
  remainingSeconds,
  totalSeconds,
  services: rawServices,
  challenges: rawChallenges,
  loadingDashboard,
  dashboardError,
  dashboardQueryError,
  refetchDashboard,
  refetchChallenges,
  attackLogs,
  signalR,
  selectedChallenge: selectedChallengeId,
  flagInput: flag,
  flagLoading,
  submitFlag: submitFlagAction,
} = useAwdDashboardPage(() => 'awd')

const operationMessage = ref('')
const operationTone = ref<'success' | 'danger'>('success')

const services = computed<CommandAwdService[]>(() => rawServices.value
  .map(service => ({
    teamId: service.teamId,
    teamName: service.teamName?.trim() || 'Unknown team',
    challengeId: service.challengeId,
    challengeName: service.challengeName?.trim() || 'Unknown service',
    status: normalizeServiceStatus(service.status),
  })))

const challenges = computed<CommandAwdChallenge[]>(() => (rawChallenges.value ?? [])
  .filter((challenge): challenge is typeof challenge & { id: string } => Boolean(challenge.id))
  .map(challenge => ({ id: challenge.id, title: challenge.title?.trim() || 'Untitled challenge' })))

const attacks = computed<CommandAwdAttack[]>(() => attackLogs.value.map(log => mapAttack(log)))

const workspaceState = computed<'invalid' | 'loading' | 'error' | 'ready'>(() => {
  if (!hasValidCompetitionId.value)
    return 'invalid'
  if (loadingDashboard.value)
    return 'loading'
  if (dashboardError.value)
    return 'error'
  return 'ready'
})

const errorMessage = computed(() => {
  const error = dashboardQueryError.value
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

async function submit() {
  const outcome = await submitFlagAction()
  if (outcome.kind === 'correct') {
    reportSuccess('Flag accepted. Competition data has been refreshed.')
  }
  else if (outcome.kind === 'incorrect') {
    reportError(null, outcome.message || 'The server rejected this flag.')
  }
  else if (outcome.kind === 'failed') {
    reportError(null, 'Flag submission failed.')
  }
  else {
    reportError(null, 'A challenge and flag value are required.')
  }
}

function retry() {
  void refetchDashboard()
  void refetchChallenges()
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
    :submit-pending="flagLoading"
    :operation-message="operationMessage"
    :operation-tone="operationTone"
    :signal-connected="signalR.isConnected.value"
    @retry="retry"
    @submit="submit"
  />
</template>
