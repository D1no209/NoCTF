import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import type {
  NoCtfapiEndpointsCompetitionsCompetitionDetailDto,
  NoCtfapiEndpointsCompetitionsCompetitionListItemDto,
  NoCtfapiEndpointsTeamsMyTeamDto,
} from '@/api/generated/types.gen'
import { competitionApi, teamApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'

export type MyTeamDto = NoCtfapiEndpointsTeamsMyTeamDto
export type TeamCompetitionDto = NoCtfapiEndpointsCompetitionsCompetitionListItemDto
export type TeamCompetitionDetailDto = NoCtfapiEndpointsCompetitionsCompetitionDetailDto

export function useTeamsPage() {
  const router = useRouter()
  const queryClient = useQueryClient()
  const joinToken = ref('')
  const newTeamName = ref('')
  const selectedCompetitionId = ref('')
  const selectedTrackName = ref('')

  const teamsQuery = useQuery({
    queryKey: queryKeys.myTeams,
    queryFn: () => teamApi.mine<MyTeamDto[]>(),
  })

  const competitionsQuery = useQuery({
    queryKey: queryKeys.competitions,
    queryFn: () => competitionApi.list<TeamCompetitionDto[]>(),
  })

  const selectedCompetitionQuery = useQuery({
    queryKey: computed(() => queryKeys.competition(selectedCompetitionId.value)),
    queryFn: () => competitionApi.get<TeamCompetitionDetailDto>(selectedCompetitionId.value),
    enabled: computed(() => Boolean(selectedCompetitionId.value)),
  })

  const activeTeams = computed(() => (teamsQuery.data.value ?? []).filter(team => !team.isBanned))
  const bannedTeams = computed(() => (teamsQuery.data.value ?? []).filter(team => team.isBanned))
  const availableCompetitions = computed(() => competitionsQuery.data.value ?? [])
  const selectedCompetitionRequiresTrack = computed(() => Boolean(selectedCompetitionQuery.data.value?.tracksEnabled))
  const selectedCompetitionTracks = computed(() => selectedCompetitionQuery.data.value?.trackNames ?? [])
  const canCreateTeam = computed(() => {
    if (!selectedCompetitionId.value || !newTeamName.value.trim())
      return false
    if (selectedCompetitionRequiresTrack.value && !selectedTrackName.value)
      return false
    return true
  })

  watch(selectedCompetitionId, () => {
    selectedTrackName.value = ''
  })

  const createTeamMutation = useMutation({
    mutationFn: () => teamApi.create<MyTeamDto>({
      competitionId: selectedCompetitionId.value,
      name: newTeamName.value.trim(),
      trackName: selectedCompetitionRequiresTrack.value ? selectedTrackName.value : undefined,
    }),
    onSuccess: (team) => {
      newTeamName.value = ''
      selectedTrackName.value = ''
      queryClient.invalidateQueries({ queryKey: queryKeys.myTeams })
      queryClient.invalidateQueries({ queryKey: queryKeys.myCompetitionTeams(team?.competitionId ?? selectedCompetitionId.value) })
    },
  })

  const joinByTokenMutation = useMutation({
    mutationFn: () => teamApi.joinByToken<MyTeamDto>(joinToken.value.trim()),
    onSuccess: (team) => {
      joinToken.value = ''
      queryClient.invalidateQueries({ queryKey: queryKeys.myTeams })
      if (team?.competitionId)
        queryClient.invalidateQueries({ queryKey: queryKeys.myCompetitionTeams(team.competitionId) })
    },
  })

  const leaveTeamMutation = useMutation({
    mutationFn: (teamId: string) => teamApi.leave(teamId),
    onSuccess: () => {
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

  function retry() {
    void teamsQuery.refetch()
    void competitionsQuery.refetch()
  }

  function goCompetitionDetail(competitionId: string) {
    void router.push({ name: 'competition-detail', params: { id: competitionId } })
  }

  return {
    joinToken,
    newTeamName,
    selectedCompetitionId,
    selectedTrackName,
    teams: teamsQuery.data,
    isLoading: teamsQuery.isLoading,
    isError: teamsQuery.isError,
    error: teamsQuery.error,
    refetch: teamsQuery.refetch,
    activeTeams,
    bannedTeams,
    availableCompetitions,
    loadingCompetitions: competitionsQuery.isLoading,
    selectedCompetitionRequiresTrack,
    selectedCompetitionTracks,
    loadingSelectedCompetition: selectedCompetitionQuery.isLoading,
    canCreateTeam,
    createTeamMutation,
    joinByTokenMutation,
    leaveTeamMutation,
    copyToken,
    retry,
    goCompetitionDetail,
  }
}
