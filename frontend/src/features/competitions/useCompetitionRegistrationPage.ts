import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { competitionApi, teamApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'

// Player-facing payloads, matching V1's CompetitionRegistrationWorkspace
// contract (string status/mode, not the generated SDK shapes).
export interface RegistrationCompetitionDto {
  id: string
  title: string
  description?: string | null
  status: string
  startTime: string
  endTime: string
  gameModeType: string
  maxTeamMembers: number
  teamRegistrationAutoApprove: boolean
  tracksEnabled: boolean
  trackNames: string[]
}

export interface RegistrationTeamDto {
  id: string
  competitionId: string
  name: string
  inviteToken: string
  memberCount: number
  isLocked: boolean
  isBanned: boolean
  bannedReason?: string | null
  trackName?: string | null
  registrationStatus: string
  isCaptain: boolean
}

// Canonical behavior follows V1's CompetitionRegistrationWorkspace: the teams
// query is gated only on the route id, and joining another competition's team
// does not redirect. V2-specific UI states (invalid id) derive from
// `hasValidCompetitionId`.
export function useCompetitionRegistrationPage() {
  const route = useRoute()
  const router = useRouter()
  const queryClient = useQueryClient()

  const competitionId = computed(() => typeof route.params.id === 'string' ? route.params.id.trim() : '')
  const hasValidCompetitionId = computed(() => competitionId.value.length > 0)

  const newTeamName = ref('')
  const selectedTrackName = ref('')
  const joinToken = ref('')

  const competitionQuery = useQuery({
    queryKey: computed(() => queryKeys.competition(competitionId.value)),
    queryFn: () => competitionApi.get<RegistrationCompetitionDto>(competitionId.value),
    enabled: computed(() => Boolean(competitionId.value)),
  })

  const teamsQuery = useQuery({
    queryKey: computed(() => queryKeys.myCompetitionTeams(competitionId.value)),
    queryFn: () => competitionApi.myTeams<RegistrationTeamDto[]>(competitionId.value),
    enabled: computed(() => Boolean(competitionId.value)),
  })

  const currentTeam = computed(() => teamsQuery.data.value?.[0] ?? null)
  const approvedTeam = computed(() =>
    (teamsQuery.data.value ?? []).find(team => team.registrationStatus === 'approved') ?? null)

  const createTeamMutation = useMutation({
    mutationFn: () => teamApi.create<RegistrationTeamDto>({
      competitionId: competitionId.value,
      name: newTeamName.value.trim(),
      trackName: competitionQuery.data.value?.tracksEnabled ? selectedTrackName.value : undefined,
    }),
    onSuccess: () => {
      newTeamName.value = ''
      selectedTrackName.value = ''
      queryClient.invalidateQueries({ queryKey: queryKeys.myCompetitionTeams(competitionId.value) })
      queryClient.invalidateQueries({ queryKey: queryKeys.myTeams })
    },
  })

  const joinByTokenMutation = useMutation({
    mutationFn: () => teamApi.joinByToken<RegistrationTeamDto>(joinToken.value.trim()),
    onSuccess: (team) => {
      joinToken.value = ''
      queryClient.invalidateQueries({ queryKey: queryKeys.myCompetitionTeams(competitionId.value) })
      queryClient.invalidateQueries({ queryKey: queryKeys.myCompetitionTeams(team.competitionId) })
      queryClient.invalidateQueries({ queryKey: queryKeys.myTeams })
    },
  })

  async function copyToken(token: string) {
    try {
      await navigator.clipboard.writeText(token)
      return true
    }
    catch {
      return false
    }
  }

  function goCompetitions() {
    void router.push({ name: 'competitions' })
  }

  function goCompetitionDetail() {
    void router.push({ name: 'competition-detail', params: { id: competitionId.value } })
  }

  return {
    competitionId,
    hasValidCompetitionId,
    newTeamName,
    selectedTrackName,
    joinToken,
    competition: competitionQuery.data,
    loadingCompetition: competitionQuery.isLoading,
    competitionError: competitionQuery.isError,
    competitionQueryError: competitionQuery.error,
    refetchCompetition: competitionQuery.refetch,
    myTeams: teamsQuery.data,
    loadingTeams: teamsQuery.isLoading,
    teamsError: teamsQuery.isError,
    refetchTeams: teamsQuery.refetch,
    currentTeam,
    approvedTeam,
    createTeamMutation,
    joinByTokenMutation,
    copyToken,
    goCompetitions,
    goCompetitionDetail,
  }
}
