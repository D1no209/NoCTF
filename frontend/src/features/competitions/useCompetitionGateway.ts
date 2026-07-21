import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { useQuery } from '@tanstack/vue-query'
import { competitionApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'

export interface CompetitionGatewayDto {
  id: string
  gameModeType: string
}

// V1's CompetitionGatewayView behavior: fetch the competition by route id and
// expose the lowercased game mode so the theme can dispatch to the matching
// workspace (ctf/awdp detail, awd/koh/penetration dashboards).
export function useCompetitionGateway() {
  const route = useRoute()

  const competitionId = computed(() => typeof route.params.id === 'string' ? route.params.id.trim() : '')
  const hasValidCompetitionId = computed(() => competitionId.value.length > 0)

  const gatewayQuery = useQuery({
    queryKey: computed(() => queryKeys.competition(competitionId.value)),
    queryFn: () => competitionApi.get<CompetitionGatewayDto>(competitionId.value),
    enabled: computed(() => Boolean(competitionId.value)),
  })

  const gameMode = computed(() => gatewayQuery.data.value?.gameModeType?.toLowerCase())
  const isSupportedMode = computed(() =>
    gameMode.value === 'ctf'
    || gameMode.value === 'awdp'
    || gameMode.value === 'awd'
    || gameMode.value === 'koh'
    || gameMode.value === 'penetration')

  return {
    competitionId,
    hasValidCompetitionId,
    competition: gatewayQuery.data,
    gameMode,
    isSupportedMode,
    isLoading: gatewayQuery.isLoading,
    isError: gatewayQuery.isError,
    error: gatewayQuery.error,
    refetch: gatewayQuery.refetch,
  }
}
