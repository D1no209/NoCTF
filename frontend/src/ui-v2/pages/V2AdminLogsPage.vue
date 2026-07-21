<script setup lang="ts">
import { computed } from 'vue'
import CommandAdminLogsWorkspace from '../components/CommandAdminLogsWorkspace.vue'
import { useAdminLogsPage } from '@/features/admin/useAdminLogsPage'

const {
  logs,
  paused,
  historyError,
  signalR,
  clearLogs,
  togglePause,
} = useAdminLogsPage()

const operationMessage = computed(() => {
  const error = historyError.value
  if (!error)
    return ''
  return error instanceof Error ? error.message : 'Unable to load the log history.'
})
</script>

<template>
  <CommandAdminLogsWorkspace
    :logs="logs"
    :paused="paused"
    :connected="signalR.isConnected.value"
    :operation-message="operationMessage"
    @toggle-pause="togglePause"
    @clear="clearLogs"
  />
</template>
