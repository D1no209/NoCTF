<script setup lang="ts">
import { computed, ref } from 'vue'
import { useAdminQqBotPage } from '@/features/admin/useAdminQqBotPage'
import CommandAdminQqBotWorkspace, {
  type CommandAdminQqBotAgent,
  type CommandAdminQqBotGroup,
  type CommandAdminQqBotOverview,
} from '../components/CommandAdminQqBotWorkspace.vue'

const {
  data,
  isLoading,
  isError,
  error,
  refetch,
  settingsForm,
  agentForm,
  saveSettingsMutation,
  saveAgentMutation,
  updateGroupMutation,
} = useAdminQqBotPage()

const operationMessage = ref('')
const operationTone = ref<'success' | 'danger'>('success')

function reportError(cause: unknown, fallback: string) {
  operationTone.value = 'danger'
  operationMessage.value = cause instanceof Error ? cause.message : fallback
}

function reportSuccess(message: string) {
  operationTone.value = 'success'
  operationMessage.value = message
}

const overview = computed<CommandAdminQqBotOverview | null>(() => {
  const value = data.value
  if (!value)
    return null
  return {
    pluginAvailable: Boolean(value.pluginAvailable),
    connectionType: value.connectionType?.trim() || 'unknown',
    agents: (value.agents ?? []).map<CommandAdminQqBotAgent>(agent => ({
      id: agent.id,
      name: agent.name?.trim() || 'Unnamed agent',
      qqOnline: Boolean(agent.qqOnline),
      botNickname: agent.botNickname ?? null,
      pendingDeliveries: agent.pendingDeliveries ?? 0,
      recentFailed: agent.recentFailed ?? 0,
      lastErrorSummary: agent.lastErrorSummary ?? null,
    })),
    groups: (value.groups ?? []).map<CommandAdminQqBotGroup>(group => ({
      id: group.id,
      groupName: group.groupName?.trim() || 'Unnamed group',
      groupId: group.groupId ?? 0,
      isAuthorized: Boolean(group.isAuthorized),
    })),
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

function saveSettings() {
  saveSettingsMutation.mutate(undefined, {
    onSuccess: () => reportSuccess('QQ bot settings saved.'),
    onError: cause => reportError(cause, 'Unable to save the QQ bot settings.'),
  })
}

function saveAgent() {
  saveAgentMutation.mutate(undefined, {
    onSuccess: () => reportSuccess('Agent registered.'),
    onError: cause => reportError(cause, 'Unable to register the agent.'),
  })
}

function toggleGroup(group: CommandAdminQqBotGroup) {
  updateGroupMutation.mutate({ id: group.id, isAuthorized: !group.isAuthorized }, {
    onError: cause => reportError(cause, 'Unable to update the group authorization.'),
  })
}

function refresh() {
  void refetch()
}
</script>

<template>
  <CommandAdminQqBotWorkspace
    v-model:settings-form="settingsForm"
    v-model:agent-form="agentForm"
    :state="workspaceState"
    :error-message="errorMessage"
    :overview="overview"
    :save-settings-pending="saveSettingsMutation.isPending.value"
    :save-agent-pending="saveAgentMutation.isPending.value"
    :group-pending="updateGroupMutation.isPending.value"
    :operation-message="operationMessage"
    :operation-tone="operationTone"
    @refresh="refresh"
    @retry="refresh"
    @save-settings="saveSettings"
    @save-agent="saveAgent"
    @toggle-group="toggleGroup"
  />
</template>
