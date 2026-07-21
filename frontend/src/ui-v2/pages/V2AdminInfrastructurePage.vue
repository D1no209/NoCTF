<script setup lang="ts">
import { computed } from 'vue'
import { useAdminInfrastructurePage } from '@/features/admin/useAdminInfrastructurePage'
import CommandAdminInfrastructureWorkspace, {
  type CommandAdminInfrastructure,
} from '../components/CommandAdminInfrastructureWorkspace.vue'

const { data, isLoading, isError, isFetching, error, refetch } = useAdminInfrastructurePage()

const infrastructure = computed<CommandAdminInfrastructure | null>(() => {
  const value = data.value
  if (!value)
    return null
  return {
    runnerProvider: value.runnerProvider?.trim() || 'unknown',
    runnerBaseUrl: value.runnerBaseUrl ?? null,
    runnerReachable: Boolean(value.runnerReachable),
    runnerInfo: Object.entries(value.runnerInfo ?? {}),
    kubernetes: Object.entries(value.kubernetes ?? {})
      .filter(([, entryValue]) => entryValue !== null && entryValue !== undefined && entryValue !== ''),
  }
})

const workspaceState = computed<'loading' | 'error' | 'ready'>(() => {
  if (isLoading.value)
    return 'loading'
  if (isError.value)
    return 'error'
  return 'ready'
})

const errorMessage = computed(() => {
  const cause = error.value
  return cause instanceof Error ? cause.message : undefined
})

function refresh() {
  void refetch()
}
</script>

<template>
  <CommandAdminInfrastructureWorkspace
    :state="workspaceState"
    :error-message="errorMessage"
    :infrastructure="infrastructure"
    :refreshing="isFetching"
    @refresh="refresh"
    @retry="refresh"
  />
</template>
