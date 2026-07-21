import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { useQuery } from '@tanstack/vue-query'
import type { NoCtfApplicationScoringLeaderboardLeaderboardResponse } from '@/api/generated/types.gen'
import { competitionApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'

// The endpoint union also covers a 202 "processing" payload; consumers render
// entry tables, so the DTO narrows to the 200 snapshot shape (a processing
// response simply has no entries at runtime).
export type CompetitionLeaderboardDto = NoCtfApplicationScoringLeaderboardLeaderboardResponse

// Standalone public leaderboard query. V1 fetches leaderboard data inside its
// own child components, so this feature exists for theme packages (V2) that
// render a leaderboard table directly on the detail page.
export function useCompetitionLeaderboard() {
  const route = useRoute()
  const competitionId = computed(() => typeof route.params.id === 'string' ? route.params.id.trim() : '')

  const leaderboardQuery = useQuery({
    queryKey: computed(() => queryKeys.leaderboard(competitionId.value)),
    queryFn: () => competitionApi.leaderboard(competitionId.value) as Promise<CompetitionLeaderboardDto>,
    enabled: computed(() => Boolean(competitionId.value)),
  })

  return {
    competitionId,
    leaderboard: leaderboardQuery.data,
    loadingLeaderboard: leaderboardQuery.isLoading,
    leaderboardError: leaderboardQuery.isError,
    refetchLeaderboard: leaderboardQuery.refetch,
  }
}
