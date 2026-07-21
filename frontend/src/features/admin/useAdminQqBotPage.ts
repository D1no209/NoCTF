import { ref, watch } from 'vue'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { adminApi } from '@/api/noctf'
import { numberField } from '../shared/number'

export interface AdminQqBotSettingsDto {
  enabled: boolean
  longPollSeconds: number
  deliveryLeaseSeconds: number
  maxDeliveryAttempts: number
  maxMessageLength: number
  maxPendingDeliveries: number
  groupCooldownMilliseconds: number
  competitionCooldownMilliseconds: number
  manualNotificationCooldownSeconds: number
}

export interface AdminQqBotAgentDto {
  id: string
  name: string
  enabled: boolean
  apiReachable: boolean
  qqOnline: boolean
  botNickname?: string | null
  pendingDeliveries: number
  recentFailed: number
  lastErrorSummary?: string | null
}

export interface AdminQqBotGroupDto {
  id: string
  groupName: string
  groupId: number
  isPresent: boolean
  isAuthorized: boolean
}

export interface AdminQqBotOverviewDto {
  pluginAvailable: boolean
  connectionType: string
  settings: AdminQqBotSettingsDto
  agents: AdminQqBotAgentDto[]
  groups: AdminQqBotGroupDto[]
}

export interface AdminQqBotSettingsForm {
  enabled: boolean
  longPollSeconds: string
  deliveryLeaseSeconds: string
  maxDeliveryAttempts: string
  maxMessageLength: string
  maxPendingDeliveries: string
  groupCooldownMilliseconds: string
  competitionCooldownMilliseconds: string
  manualNotificationCooldownSeconds: string
}

export interface AdminQqBotAgentForm {
  name: string
  publicKeyPem: string
  enabled: boolean
}

const DEFAULT_AGENT_FORM: AdminQqBotAgentForm = { name: '', publicKeyPem: '', enabled: true }

export function useAdminQqBotPage() {
  const queryClient = useQueryClient()

  const settingsForm = ref<AdminQqBotSettingsForm>({
    enabled: false,
    longPollSeconds: '25',
    deliveryLeaseSeconds: '45',
    maxDeliveryAttempts: '5',
    maxMessageLength: '1000',
    maxPendingDeliveries: '500',
    groupCooldownMilliseconds: '1000',
    competitionCooldownMilliseconds: '1000',
    manualNotificationCooldownSeconds: '30',
  })

  const agentForm = ref<AdminQqBotAgentForm>({ ...DEFAULT_AGENT_FORM })

  const overviewQuery = useQuery({
    queryKey: ['admin-qqbot'],
    queryFn: () => adminApi.qqBotOverview() as Promise<AdminQqBotOverviewDto>,
  })

  watch(() => overviewQuery.data.value?.settings, (settings) => {
    if (!settings)
      return
    settingsForm.value = {
      enabled: settings.enabled,
      longPollSeconds: String(settings.longPollSeconds ?? 25),
      deliveryLeaseSeconds: String(settings.deliveryLeaseSeconds ?? 45),
      maxDeliveryAttempts: String(settings.maxDeliveryAttempts ?? 5),
      maxMessageLength: String(settings.maxMessageLength ?? 1000),
      maxPendingDeliveries: String(settings.maxPendingDeliveries ?? 500),
      groupCooldownMilliseconds: String(settings.groupCooldownMilliseconds ?? 1000),
      competitionCooldownMilliseconds: String(settings.competitionCooldownMilliseconds ?? 1000),
      manualNotificationCooldownSeconds: String(settings.manualNotificationCooldownSeconds ?? 30),
    }
  }, { immediate: true })

  const saveSettingsMutation = useMutation({
    mutationFn: () => adminApi.updateQqBotSettings({
      enabled: settingsForm.value.enabled,
      longPollSeconds: numberField(settingsForm.value.longPollSeconds),
      deliveryLeaseSeconds: numberField(settingsForm.value.deliveryLeaseSeconds),
      maxDeliveryAttempts: numberField(settingsForm.value.maxDeliveryAttempts),
      maxMessageLength: numberField(settingsForm.value.maxMessageLength),
      maxPendingDeliveries: numberField(settingsForm.value.maxPendingDeliveries),
      groupCooldownMilliseconds: numberField(settingsForm.value.groupCooldownMilliseconds),
      competitionCooldownMilliseconds: numberField(settingsForm.value.competitionCooldownMilliseconds),
      manualNotificationCooldownSeconds: numberField(settingsForm.value.manualNotificationCooldownSeconds),
    }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['admin-qqbot'] })
    },
  })

  const saveAgentMutation = useMutation({
    mutationFn: () => adminApi.upsertQqBotAgent({
      id: null,
      name: agentForm.value.name.trim(),
      enabled: agentForm.value.enabled,
      publicKeyPem: agentForm.value.publicKeyPem,
      previousKeyOverlapMinutes: 60,
    }),
    onSuccess: async () => {
      agentForm.value = { ...DEFAULT_AGENT_FORM }
      await queryClient.invalidateQueries({ queryKey: ['admin-qqbot'] })
    },
  })

  const updateGroupMutation = useMutation({
    mutationFn: ({ id, isAuthorized }: { id: string, isAuthorized: boolean }) =>
      adminApi.authorizeQqBotGroup(id, { isAuthorized }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['admin-qqbot'] })
    },
  })

  return {
    data: overviewQuery.data,
    isLoading: overviewQuery.isLoading,
    isError: overviewQuery.isError,
    error: overviewQuery.error,
    refetch: overviewQuery.refetch,
    settingsForm,
    agentForm,
    saveSettingsMutation,
    saveAgentMutation,
    updateGroupMutation,
  }
}
