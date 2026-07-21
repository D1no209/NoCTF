import { computed, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useQuery } from '@tanstack/vue-query'
import { competitionApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { useAuthStore } from '@/stores/auth'
import { useScoreStore } from '@/stores/score'

export interface GameLayoutTeamDto {
  id: string
  name: string
  registrationStatus: string
  isBanned: boolean
}

export interface GameLayoutLeaderboardEntryDto {
  teamId?: string | null
  teamName?: string | null
  totalScore?: number | null
}

export interface GameLayoutLeaderboardDto {
  entries?: GameLayoutLeaderboardEntryDto[]
}

// Canonical behavior follows V1's GameLayout: poll the competition leaderboard
// for the approved (non-banned) team every 10s and mirror the score into the
// score store so the game chrome can render it.
export function useGameLayoutScore() {
  const route = useRoute()
  const auth = useAuthStore()
  const scoreStore = useScoreStore()

  const competitionId = computed(() => typeof route.params.id === 'string' ? route.params.id : '')

  const myTeamsQuery = useQuery({
    queryKey: computed(() => queryKeys.myCompetitionTeams(competitionId.value)),
    queryFn: () => competitionApi.myTeams<GameLayoutTeamDto[]>(competitionId.value),
    enabled: computed(() => Boolean(competitionId.value)),
  })

  const approvedTeam = computed(() =>
    (myTeamsQuery.data.value ?? []).find(team => team.registrationStatus === 'approved' && !team.isBanned) ?? null,
  )

  const leaderboardQuery = useQuery({
    queryKey: computed(() => queryKeys.leaderboard(competitionId.value)),
    queryFn: () => competitionApi.leaderboard(competitionId.value) as Promise<GameLayoutLeaderboardDto>,
    enabled: computed(() => Boolean(competitionId.value) && Boolean(approvedTeam.value?.id)),
    refetchInterval: computed(() => approvedTeam.value?.id ? 10_000 : false),
  })

  watch(
    () => [competitionId.value, approvedTeam.value?.id, leaderboardQuery.data.value?.entries] as const,
    () => {
      const team = approvedTeam.value
      if (!competitionId.value || !team) {
        scoreStore.setCurrentTeamScore(competitionId.value || null, null, null, null)
        return
      }

      const entry = (leaderboardQuery.data.value?.entries ?? []).find(item => item.teamId === team.id)
      scoreStore.setCurrentTeamScore(
        competitionId.value,
        team.id,
        entry?.teamName ?? team.name,
        entry?.totalScore ?? 0,
      )
    },
    { immediate: true },
  )

  const displayName = computed(() => scoreStore.myTeamName ?? auth.user?.userName ?? '')
  const displayScore = computed(() => scoreStore.myTeamScore)

  return {
    competitionId,
    displayName,
    displayScore,
  }
}
