import { computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { useQuery } from '@tanstack/vue-query'
import { competitionApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { useSignalR } from '@/composables/useSignalR'
import { useAuthStore } from '@/stores/auth'

export interface KohControlEntryDto {
  teamId: string | null
  teamName: string | null
  startTime: string
  endTime: string | null
}

export interface KohChallengeStatusDto {
  challengeId: string
  challengeName: string
  controllerTeamId: string | null
  controllerTeamName: string | null
  controlStartTime: string | null
  controlDurationSeconds: number
  history: KohControlEntryDto[]
}

export interface KohDashboardChallengeDto {
  challengeId: string
  challengeName: string
  currentControllerTeamId: string | null
  currentControllerTeamName: string | null
  controlDurationSeconds: number
  history: KohControlEntryDto[]
}

export interface KohDashboardDto {
  competitionId: string
  challenges: KohDashboardChallengeDto[]
}

// Canonical behavior follows V1's KohDashboardWorkspace: 15s polling plus
// SignalR koh updates triggering a refetch.
export function useKohDashboardPage() {
  const route = useRoute()
  const auth = useAuthStore()

  const competitionId = computed(() => typeof route.params.id === 'string' ? route.params.id.trim() : '')
  const hasValidCompetitionId = computed(() => competitionId.value.length > 0)

  const dashboardQuery = useQuery({
    queryKey: computed(() => queryKeys.kohDashboard(competitionId.value)),
    queryFn: () => competitionApi.kohDashboard<KohDashboardDto>(competitionId.value),
    enabled: computed(() => Boolean(competitionId.value)),
    refetchInterval: 15_000,
  })

  const challenges = computed<KohChallengeStatusDto[]>(() => (dashboardQuery.data.value?.challenges ?? []).map(challenge => ({
    challengeId: challenge.challengeId,
    challengeName: challenge.challengeName,
    controllerTeamId: challenge.currentControllerTeamId,
    controllerTeamName: challenge.currentControllerTeamName,
    controlStartTime: challenge.history.find(entry => entry.endTime === null)?.startTime ?? null,
    controlDurationSeconds: challenge.controlDurationSeconds,
    history: challenge.history,
  })))

  const signalR = useSignalR({
    hubUrl: `/hubs/game?competitionId=${competitionId.value}`,
    accessToken: () => auth.accessToken,
  })

  signalR.onKohUpdate((dto) => {
    if (dto.challengeId)
      void dashboardQuery.refetch()
  })

  onMounted(() => {
    if (competitionId.value)
      void signalR.start()
  })

  return {
    competitionId,
    hasValidCompetitionId,
    challenges,
    isLoading: dashboardQuery.isLoading,
    isError: dashboardQuery.isError,
    error: dashboardQuery.error,
    refetch: dashboardQuery.refetch,
    signalR,
  }
}
