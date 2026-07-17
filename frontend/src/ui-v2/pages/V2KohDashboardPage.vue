<script setup lang="ts">
import { computed, onMounted, onUnmounted } from 'vue'
import { useRoute } from 'vue-router'
import { useQuery } from '@tanstack/vue-query'
import type { NoCtfapiEndpointsCompetitionsKohDashboardDto } from '@/api/generated/types.gen'
import { competitionApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { useSignalR } from '@/composables/useSignalR'
import { useAuthStore } from '@/stores/auth'
import CommandKohDashboardWorkspace, { type CommandKohHill } from '../components/CommandKohDashboardWorkspace.vue'

const guidPattern = /^[\da-f]{8}-(?:[\da-f]{4}-){3}[\da-f]{12}$/i

const route = useRoute()
const auth = useAuthStore()

const competitionId = computed(() => typeof route.params.id === 'string' ? route.params.id.trim() : '')
const hasValidCompetitionId = computed(() => guidPattern.test(competitionId.value))

const dashboardQuery = useQuery({
  queryKey: computed(() => queryKeys.kohDashboard(competitionId.value)),
  queryFn: () => competitionApi.kohDashboard<NoCtfapiEndpointsCompetitionsKohDashboardDto>(competitionId.value),
  enabled: hasValidCompetitionId,
  refetchInterval: 15_000,
})

const hills = computed<CommandKohHill[]>(() => (dashboardQuery.data.value?.challenges ?? [])
  .filter((challenge): challenge is Required<NoCtfapiEndpointsCompetitionsKohDashboardDto>['challenges'][number] & { challengeId: string } =>
    Boolean(challenge.challengeId),
  )
  .map(challenge => ({
    challengeId: challenge.challengeId,
    challengeName: challenge.challengeName?.trim() || 'Unnamed hill',
    controllerTeamId: challenge.currentControllerTeamId ?? null,
    controllerTeamName: challenge.currentControllerTeamName?.trim() || null,
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

const signalR = useSignalR({
  hubUrl: `/hubs/game?competitionId=${competitionId.value}`,
  accessToken: () => auth.accessToken,
})

signalR.onKohUpdate((dto) => {
  if (dto.challengeId)
    void dashboardQuery.refetch()
})

onMounted(() => {
  if (hasValidCompetitionId.value)
    void signalR.start()
})

onUnmounted(() => {
  void signalR.stop()
})

function retry() {
  void dashboardQuery.refetch()
}
</script>

<template>
  <CommandKohDashboardWorkspace
    :state="workspaceState"
    :error-message="errorMessage"
    :hills="hills"
    :signal-connected="signalR.isConnected.value"
    @retry="retry"
  />
</template>
