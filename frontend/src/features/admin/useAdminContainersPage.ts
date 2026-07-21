import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { adminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'

export interface AdminContainerDto {
  containerId: string
  competitionId: string
  teamId: string
  challengeId: string
  status: string
}

export function useAdminContainersPage() {
  const queryClient = useQueryClient()

  const containersQuery = useQuery({
    queryKey: queryKeys.adminContainers,
    queryFn: () => adminApi.containers<AdminContainerDto[]>(),
  })

  const destroyMutation = useMutation({
    mutationFn: async (containerId: string) => {
      await adminApi.destroyContainer(containerId)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.adminContainers })
    },
  })

  return {
    containers: containersQuery.data,
    isLoading: containersQuery.isLoading,
    isFetching: containersQuery.isFetching,
    isError: containersQuery.isError,
    error: containersQuery.error,
    refetch: containersQuery.refetch,
    destroyMutation,
  }
}
