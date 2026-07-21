import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { adminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'

export interface AdminUserDto {
  id: string
  userName: string
  email: string
  role: string
}

export function useAdminUsersPage() {
  const queryClient = useQueryClient()

  const usersQuery = useQuery({
    queryKey: queryKeys.adminUsers,
    queryFn: () => adminApi.users<AdminUserDto[]>(),
  })

  const changeRoleMutation = useMutation({
    mutationFn: async ({ id, role }: { id: string, role: string }) => {
      await adminApi.updateUserRole(id, role)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.adminUsers })
    },
  })

  const resetPasswordMutation = useMutation({
    mutationFn: async ({ id, password }: { id: string, password: string }) => {
      await adminApi.resetUserPassword(id, password)
    },
  })

  return {
    users: usersQuery.data,
    isLoading: usersQuery.isLoading,
    isError: usersQuery.isError,
    error: usersQuery.error,
    refetch: usersQuery.refetch,
    changeRoleMutation,
    resetPasswordMutation,
  }
}
