import { useQuery } from '@tanstack/vue-query'
import { adminApi } from '@/api/noctf'

export interface AdminInfrastructureDto {
  runnerProvider: string
  runnerBaseUrl?: string | null
  runnerReachable: boolean
  runnerInfo?: Record<string, unknown> | null
  kubernetes?: Record<string, unknown> | null
}

export function useAdminInfrastructurePage() {
  const infrastructureQuery = useQuery({
    queryKey: ['admin-infrastructure'],
    queryFn: () => adminApi.infrastructure() as Promise<AdminInfrastructureDto>,
    refetchInterval: 30_000,
  })

  return {
    data: infrastructureQuery.data,
    isLoading: infrastructureQuery.isLoading,
    isError: infrastructureQuery.isError,
    isFetching: infrastructureQuery.isFetching,
    error: infrastructureQuery.error,
    refetch: infrastructureQuery.refetch,
  }
}
