<script setup lang="ts">
import { computed } from 'vue'
import { useAdminPluginsPage, type AdminPluginDto } from '@/features/admin/useAdminPluginsPage'
import CommandAdminPluginsWorkspace, { type CommandAdminPlugin } from '../components/CommandAdminPluginsWorkspace.vue'

const { plugins: pluginData, isLoading, isError, error, refetch } = useAdminPluginsPage()

const plugins = computed<CommandAdminPlugin[]>(() => (pluginData.value ?? [])
  .filter((plugin): plugin is AdminPluginDto & { name: string } => Boolean(plugin.name))
  .map(plugin => ({
    name: plugin.name,
    type: plugin.type?.trim() || 'plugin',
    version: plugin.version?.trim() || '0.0.0',
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

function retry() {
  void refetch()
}
</script>

<template>
  <CommandAdminPluginsWorkspace
    :state="workspaceState"
    :error-message="errorMessage"
    :plugins="plugins"
    @retry="retry"
  />
</template>
