<script setup lang="ts">
import { computed, ref } from 'vue'
import { useAdminContainersPage, type AdminContainerDto } from '@/features/admin/useAdminContainersPage'
import CommandAdminContainersWorkspace, { type CommandAdminContainer } from '../components/CommandAdminContainersWorkspace.vue'

const {
  containers: containerData,
  isLoading,
  isFetching,
  isError,
  error,
  refetch,
  destroyMutation,
} = useAdminContainersPage()

const destroyDialogOpen = ref(false)
const selectedContainer = ref<CommandAdminContainer | null>(null)
const operationMessage = ref('')
const operationTone = ref<'success' | 'danger'>('success')

const containers = computed<CommandAdminContainer[]>(() => (containerData.value ?? [])
  .filter((container): container is AdminContainerDto & { containerId: string } => Boolean(container.containerId))
  .map(container => ({
    containerId: container.containerId,
    competitionId: container.competitionId ?? '',
    teamId: container.teamId ?? '',
    challengeId: container.challengeId ?? '',
    status: container.status?.trim() || 'unknown',
  })))

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

function openDestroy(container: CommandAdminContainer) {
  selectedContainer.value = container
  destroyDialogOpen.value = true
}

function destroy() {
  const container = selectedContainer.value
  if (!container)
    return
  destroyMutation.mutate(container.containerId, {
    onSuccess: () => {
      destroyDialogOpen.value = false
      operationTone.value = 'success'
      operationMessage.value = 'Container destroyed.'
    },
    onError: (cause) => {
      operationTone.value = 'danger'
      operationMessage.value = cause instanceof Error ? cause.message : 'Unable to destroy the container.'
    },
  })
}

function refresh() {
  void refetch()
}
</script>

<template>
  <CommandAdminContainersWorkspace
    v-model:destroy-dialog-open="destroyDialogOpen"
    :state="workspaceState"
    :error-message="errorMessage"
    :containers="containers"
    :refreshing="isFetching"
    :selected-container="selectedContainer"
    :destroy-pending="destroyMutation.isPending.value"
    :operation-message="operationMessage"
    :operation-tone="operationTone"
    @refresh="refresh"
    @retry="refresh"
    @open-destroy="openDestroy"
    @destroy="destroy"
  />
</template>
