import { computed, ref } from 'vue'
import { useQuery } from '@tanstack/vue-query'
import { adminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'

export interface AuditLogDto {
  id: string
  userId?: string
  userName?: string
  ipAddress?: string
  action: string
  entityType?: string
  endpointPath: string
  httpMethod: string
  newValues?: string
  oldValues?: string
  diff?: string
  timestamp: string
  exception?: string
}

export interface AuditLogsResponse {
  items: AuditLogDto[]
  total: number
  page: number
  pageSize: number
}

export const AUDIT_LOGS_PAGE_SIZE = 50

export function useAdminAuditLogsPage() {
  const filterUserName = ref('')
  const filterAction = ref('')
  const filterEntityType = ref('')
  const page = ref(1)
  const pageSize = AUDIT_LOGS_PAGE_SIZE

  const queryParams = computed(() => ({
    userName: filterUserName.value || undefined,
    action: filterAction.value || undefined,
    entityType: filterEntityType.value || undefined,
    page: page.value,
    pageSize,
  }))

  const auditLogsQuery = useQuery({
    queryKey: computed(() => queryKeys.adminAuditLogs(page.value, queryParams.value)),
    queryFn: () => adminApi.auditLogs<AuditLogsResponse>(queryParams.value),
  })

  const logs = computed(() => auditLogsQuery.data.value?.items ?? [])
  const total = computed(() => auditLogsQuery.data.value?.total ?? 0)

  function search() {
    page.value = 1
    void auditLogsQuery.refetch()
  }

  return {
    filterUserName,
    filterAction,
    filterEntityType,
    page,
    pageSize,
    logs,
    total,
    isLoading: auditLogsQuery.isLoading,
    isError: auditLogsQuery.isError,
    isFetching: auditLogsQuery.isFetching,
    error: auditLogsQuery.error,
    refetch: auditLogsQuery.refetch,
    search,
  }
}
