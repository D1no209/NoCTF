<script setup lang="ts">
import { computed } from 'vue'
import CommandKohDashboardWorkspace, { type CommandKohHill } from '../components/CommandKohDashboardWorkspace.vue'
import { useKohDashboardPage } from '@/features/competitions/useKohDashboardPage'

const {
  hasValidCompetitionId,
  challenges: rawChallenges,
  isLoading,
  isError,
  error,
  refetch,
  signalR,
} = useKohDashboardPage()

const hills = computed<CommandKohHill[]>(() => rawChallenges.value
  .filter((challenge): challenge is typeof challenge & { challengeId: string } => Boolean(challenge.challengeId))
  .map(challenge => ({
    challengeId: challenge.challengeId,
    challengeName: challenge.challengeName?.trim() || 'Unnamed hill',
    controllerTeamId: challenge.controllerTeamId ?? null,
    controllerTeamName: challenge.controllerTeamName?.trim() || null,
    controlDurationSeconds: challenge.controlDurationSeconds ?? 0,
    history: (challenge.history ?? [])
      .filter((entry): entry is typeof entry & { teamId: string, teamName: string, startTime: string } =>
        Boolean(entry.teamId && entry.teamName && entry.startTime),
      )
      .map(entry => ({
        teamId: entry.teamId,
        teamName: entry.teamName,
        startTime: entry.startTime,
        endTime: entry.endTime ?? null,
      })),
  })))

const workspaceState = computed<'invalid' | 'loading' | 'error' | 'ready'>(() => {
  if (!hasValidCompetitionId.value)
    return 'invalid'
  if (isLoading.value)
    return 'loading'
  if (isError.value)
    return 'error'
  return 'ready'
})

const errorMessage = computed(() => {
  const value = error.value
  return value instanceof Error ? value.message : undefined
})
</script>

<template>
  <CommandKohDashboardWorkspace
    :state="workspaceState"
    :error-message="errorMessage"
    :hills="hills"
    :signal-connected="signalR.isConnected.value"
    @retry="refetch"
  />
</template>
