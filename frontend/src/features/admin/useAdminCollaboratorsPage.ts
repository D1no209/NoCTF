import { computed, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { adminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'

export interface AdminCollaboratorDto {
  userId: string
  userName: string
  role: string
}

export interface AdminCollaboratorCompetitionDto {
  id: string
  title: string
}

export interface AdminCollaboratorUserDto {
  id: string
  userName: string
}

export function useAdminCollaboratorsPage() {
  const route = useRoute()
  const queryClient = useQueryClient()

  const selectedCompetitionId = ref<string>(typeof route.query.competitionId === 'string' ? route.query.competitionId : '')
  const selectedCompetitionTitle = ref<string>(typeof route.query.competitionTitle === 'string' ? route.query.competitionTitle : '')

  const newUserId = ref('')
  const newUserSearch = ref('')
  const newRole = ref('Observer')

  watch(() => route.query.competitionId, (id) => {
    if (typeof id === 'string' && id) {
      selectedCompetitionId.value = id
      selectedCompetitionTitle.value = typeof route.query.competitionTitle === 'string' ? route.query.competitionTitle : ''
    }
  })

  const competitionsQuery = useQuery({
    queryKey: queryKeys.adminCompetitions,
    queryFn: () => adminApi.competitions<AdminCollaboratorCompetitionDto[]>(),
  })

  const collaboratorsQuery = useQuery({
    queryKey: computed(() => queryKeys.adminCollaborators(selectedCompetitionId.value)),
    queryFn: async () => {
      if (!selectedCompetitionId.value)
        return []
      return adminApi.collaborators<AdminCollaboratorDto[]>(selectedCompetitionId.value)
    },
    enabled: () => Boolean(selectedCompetitionId.value),
  })

  const userSearchQuery = useQuery({
    queryKey: computed(() => ['admin-user-search', newUserSearch.value]),
    queryFn: async () => {
      if (!newUserSearch.value || newUserSearch.value.length < 2)
        return []
      const all = await adminApi.users<AdminCollaboratorUserDto[]>()
      const query = newUserSearch.value.toLowerCase()
      return all.filter(user => user.userName.toLowerCase().includes(query))
    },
    enabled: () => newUserSearch.value.length >= 2,
  })

  const addMutation = useMutation({
    mutationFn: async () => {
      await adminApi.addCollaborator(selectedCompetitionId.value, { userId: newUserId.value, role: newRole.value })
    },
    onSuccess: async () => {
      newUserId.value = ''
      newUserSearch.value = ''
      await queryClient.invalidateQueries({ queryKey: queryKeys.adminCollaborators(selectedCompetitionId.value) })
    },
  })

  const removeMutation = useMutation({
    mutationFn: async (userId: string) => {
      await adminApi.removeCollaborator(selectedCompetitionId.value, userId)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.adminCollaborators(selectedCompetitionId.value) })
    },
  })

  return {
    selectedCompetitionId,
    selectedCompetitionTitle,
    competitions: competitionsQuery.data,
    collaborators: collaboratorsQuery.data,
    isLoading: collaboratorsQuery.isLoading,
    isError: collaboratorsQuery.isError,
    error: collaboratorsQuery.error,
    refetch: collaboratorsQuery.refetch,
    userSearchResults: userSearchQuery.data,
    newUserId,
    newUserSearch,
    newRole,
    addMutation,
    removeMutation,
  }
}
