import { useQuery } from '@tanstack/vue-query'
import { adminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'

export interface AdminPluginDto {
  name: string
  type: string
  version: string
}

export function useAdminPluginsPage() {
  const pluginsQuery = useQuery({
    queryKey: queryKeys.adminPlugins,
    queryFn: () => adminApi.plugins<AdminPluginDto[]>(),
  })

  return {
    plugins: pluginsQuery.data,
    isLoading: pluginsQuery.isLoading,
    isError: pluginsQuery.isError,
    error: pluginsQuery.error,
    refetch: pluginsQuery.refetch,
  }
}
