import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { useQuery } from '@tanstack/vue-query'
import type {
  NoCtfapiEndpointsCompetitionsCompetitionListItemDto,
  NoCtfapiEndpointsTeamsMyTeamDto,
} from '@/api/generated/types.gen'
import { competitionApi, teamApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { useAuthStore } from '@/stores/auth'

export type HomeCompetitionDto = NoCtfapiEndpointsCompetitionsCompetitionListItemDto
export type HomeTeamDto = NoCtfapiEndpointsTeamsMyTeamDto

const statusPriority = new Map([
  ['running', 0],
  ['published', 1],
  ['draft', 2],
  ['paused', 3],
  ['finished', 4],
])

export function useHomePage() {
  const router = useRouter()
  const auth = useAuthStore()

  const competitionsQuery = useQuery({
    queryKey: queryKeys.competitions,
    queryFn: () => competitionApi.list<HomeCompetitionDto[]>(),
  })

  const teamsQuery = useQuery({
    queryKey: queryKeys.myTeams,
    queryFn: () => teamApi.mine<HomeTeamDto[]>(),
  })

  const canManage = computed(() => ['Admin', 'Organizer'].includes(auth.userRole))

  const activeCompetitions = computed(() => [...(competitionsQuery.data.value ?? [])]
    .sort((a, b) => {
      const statusA = statusPriority.get((a.status ?? '').toLowerCase()) ?? 5
      const statusB = statusPriority.get((b.status ?? '').toLowerCase()) ?? 5
      if (statusA !== statusB)
        return statusA - statusB
      return Date.parse(a.startTime ?? '') - Date.parse(b.startTime ?? '')
    })
    .slice(0, 3))

  const recentTeams = computed(() => (teamsQuery.data.value ?? []).slice(0, 3))
  const approvedTeams = computed(() => (teamsQuery.data.value ?? [])
    .filter(team => team.registrationStatus === 'approved').length)
  const pendingTeams = computed(() => (teamsQuery.data.value ?? [])
    .filter(team => team.registrationStatus === 'pending').length)

  function goCompetitions() {
    void router.push({ name: 'competitions' })
  }

  function goTeams() {
    void router.push({ name: 'teams' })
  }

  function goCompetitionDetail(id: string) {
    void router.push({ name: 'competition-detail', params: { id } })
  }

  function goCompetitionRegister(id: string) {
    void router.push({ name: 'competition-register', params: { id } })
  }

  return {
    canManage,
    teams: teamsQuery.data,
    activeCompetitions,
    recentTeams,
    approvedTeams,
    pendingTeams,
    loadingCompetitions: competitionsQuery.isLoading,
    loadingTeams: teamsQuery.isLoading,
    goCompetitions,
    goTeams,
    goCompetitionDetail,
    goCompetitionRegister,
  }
}
