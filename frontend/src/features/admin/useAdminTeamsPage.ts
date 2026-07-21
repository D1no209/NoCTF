import { ref } from 'vue'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { adminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'

export interface AdminTeamDto {
  id: string
  competitionId: string
  name: string
  captainName: string
  memberCount: number
  competitionTitle: string
  inviteToken?: string
  isLocked?: boolean
  registrationStatus?: string
}

export interface AdminTeamMemberDto {
  userId: string
  userName: string
  role: string
}

export function useAdminTeamsPage() {
  const queryClient = useQueryClient()

  const teamsQuery = useQuery({
    queryKey: queryKeys.adminTeams,
    queryFn: () => adminApi.teams<AdminTeamDto[]>(),
  })

  const teamMembers = ref<AdminTeamMemberDto[]>([])
  const loadingMembers = ref(false)
  const membersError = ref<unknown>(null)

  async function loadMembers(teamId: string) {
    loadingMembers.value = true
    membersError.value = null
    try {
      teamMembers.value = await adminApi.teamMembers<AdminTeamMemberDto[]>(teamId)
    }
    catch (error) {
      membersError.value = error
    }
    finally {
      loadingMembers.value = false
    }
  }

  const disbandMutation = useMutation({
    mutationFn: async (id: string) => {
      await adminApi.deleteTeam(id)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.adminTeams })
    },
  })

  return {
    teams: teamsQuery.data,
    isLoading: teamsQuery.isLoading,
    isError: teamsQuery.isError,
    error: teamsQuery.error,
    refetch: teamsQuery.refetch,
    teamMembers,
    loadingMembers,
    membersError,
    loadMembers,
    disbandMutation,
  }
}
