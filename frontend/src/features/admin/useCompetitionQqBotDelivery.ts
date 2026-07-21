import { computed, ref, watch } from 'vue'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { adminApi } from '@/api/noctf'

export type QqEventType = 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7

export interface QqRule {
  eventType: QqEventType
  enabled: boolean
  templateId?: string | null
}

export interface QqBinding {
  agentId: string
  groupId: string
  qqGroupId: number
  groupName?: string
  isDefault?: boolean
  eventTypes?: QqEventType[]
}

export interface QqConfig {
  enabled: boolean
  allowMessages: boolean
  allowManualNotifications: boolean
  stopNormalEventsAfterFinished: boolean
  mentionAll: boolean
  showTeamName: boolean
  showUserName: boolean
  showChallengeCategory: boolean
  includeCompetitionLink: boolean
  includeChallengeLink: boolean
  hidePenaltyDetails: boolean
  eventRules: QqRule[]
  groupBindings: QqBinding[]
  warnings: string[]
}

export interface QqGroup {
  id: string
  agentId: string
  groupId: number
  groupName: string
}

export interface QqTemplate {
  id?: string | null
  eventType?: QqEventType
  name?: string
  content?: string
  isDefault?: boolean
}

export interface QqPreview {
  renderedText?: string
  characterCount?: number
  warnings?: string[]
}

export interface QqDelivery {
  id?: string
  eventType?: QqEventType
  groupName?: string
  status?: number
  attemptCount?: number
  maxAttempts?: number
  messageSummary?: string
  lastErrorSummary?: string | null
}

export interface QqTemplateForm {
  id: string | null
  eventType: QqEventType
  name: string
  content: string
  isDefault: boolean
}

export interface QqAnnouncementForm {
  title: string
  content: string
  isTest: boolean
}

export function useCompetitionQqBotDelivery(competitionId: () => string) {
  const queryClient = useQueryClient()
  const selectedTemplateId = ref('')
  const preview = ref<QqPreview | null>(null)
  const selectedGroupIds = ref<string[]>([])
  const templateForm = ref<QqTemplateForm>({ id: null, eventType: 0, name: '', content: '', isDefault: false })
  const announcementForm = ref<QqAnnouncementForm>({ title: '', content: '', isTest: false })
  const form = ref<QqConfig>({
    enabled: false,
    allowMessages: true,
    allowManualNotifications: false,
    stopNormalEventsAfterFinished: true,
    mentionAll: false,
    showTeamName: true,
    showUserName: false,
    showChallengeCategory: true,
    includeCompetitionLink: false,
    includeChallengeLink: false,
    hidePenaltyDetails: true,
    eventRules: [],
    groupBindings: [],
    warnings: [],
  })

  const configQuery = useQuery({
    queryKey: computed(() => ['competition-qqbot', competitionId()]),
    queryFn: () => adminApi.competitionQqBot(competitionId()) as Promise<QqConfig>,
  })

  const templatesQuery = useQuery({
    queryKey: computed(() => ['competition-qqbot-templates', competitionId()]),
    queryFn: () => adminApi.competitionQqBotTemplates(competitionId()) as Promise<QqTemplate[]>,
  })

  const groupsQuery = useQuery({
    queryKey: computed(() => ['competition-qqbot-groups', competitionId()]),
    queryFn: () => adminApi.availableQqBotGroups(competitionId()) as Promise<QqGroup[]>,
    retry: false,
  })

  const deliveryQuery = useQuery({
    queryKey: computed(() => ['competition-qqbot-log', competitionId()]),
    queryFn: () => adminApi.qqBotLogs(competitionId(), {} as never) as Promise<{ items?: QqDelivery[] }>,
    refetchInterval: 15_000,
  })

  const groupsById = computed(() => new Map((groupsQuery.data.value ?? []).map(group => [group.id, group])))
  const bindings = computed<QqBinding[]>(() => selectedGroupIds.value.flatMap((groupId) => {
    const existing = form.value.groupBindings.find(binding => binding.groupId === groupId)
    if (existing)
      return [existing]
    const group = groupsById.value.get(groupId)
    return group
      ? [{
          agentId: group.agentId,
          groupId: group.id,
          qqGroupId: group.groupId,
          groupName: group.groupName,
          isDefault: true,
          eventTypes: [],
        }]
      : []
  }))
  const deliveryItems = computed(() => deliveryQuery.data.value?.items ?? [])

  watch(configQuery.data, (value) => {
    if (!value)
      return
    form.value = {
      enabled: value.enabled,
      allowMessages: value.allowMessages,
      allowManualNotifications: value.allowManualNotifications,
      stopNormalEventsAfterFinished: value.stopNormalEventsAfterFinished,
      mentionAll: value.mentionAll,
      showTeamName: value.showTeamName,
      showUserName: value.showUserName,
      showChallengeCategory: value.showChallengeCategory,
      includeCompetitionLink: value.includeCompetitionLink,
      includeChallengeLink: value.includeChallengeLink,
      hidePenaltyDetails: value.hidePenaltyDetails,
      eventRules: value.eventRules ?? [],
      groupBindings: value.groupBindings ?? [],
      warnings: value.warnings ?? [],
    }
    selectedGroupIds.value = (value.groupBindings ?? []).map(binding => binding.groupId)
  }, { immediate: true })

  watch(templatesQuery.data, (items) => {
    if (!selectedTemplateId.value)
      selectedTemplateId.value = items?.[0]?.id ?? ''
  }, { immediate: true })

  function toggleGroup(groupId: string) {
    selectedGroupIds.value = selectedGroupIds.value.includes(groupId)
      ? selectedGroupIds.value.filter(id => id !== groupId)
      : [...selectedGroupIds.value, groupId]
  }

  function selectTemplate(template: QqTemplate) {
    templateForm.value = {
      id: template.id ?? null,
      eventType: template.eventType ?? 0,
      name: template.name ?? '',
      content: template.content ?? '',
      isDefault: template.isDefault ?? false,
    }
    selectedTemplateId.value = template.id ?? ''
  }

  function resetTemplate() {
    templateForm.value = { id: null, eventType: 0, name: '', content: '', isDefault: false }
  }

  const saveConfigMutation = useMutation({
    mutationFn: () => adminApi.updateCompetitionQqBot(competitionId(), { ...form.value, groupBindings: bindings.value }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['competition-qqbot', competitionId()] })
    },
  })

  const saveTemplateMutation = useMutation({
    mutationFn: () => adminApi.upsertCompetitionQqBotTemplate(competitionId(), {
      id: templateForm.value.id,
      eventType: templateForm.value.eventType,
      name: templateForm.value.name.trim(),
      content: templateForm.value.content,
      isDefault: templateForm.value.isDefault,
    }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['competition-qqbot-templates', competitionId()] })
    },
  })

  const previewTemplateMutation = useMutation({
    mutationFn: () => adminApi.previewQqBot(competitionId(), {
      eventType: templateForm.value.eventType,
      templateId: templateForm.value.id,
      announcementTitle: announcementForm.value.title.trim() || null,
      announcementContent: announcementForm.value.content.trim() || null,
    }) as Promise<QqPreview>,
    onSuccess: (value) => {
      preview.value = value
    },
  })

  const sendAnnouncementMutation = useMutation({
    mutationFn: () => adminApi.sendQqBotNotification(competitionId(), {
      title: announcementForm.value.title.trim(),
      content: announcementForm.value.content.trim(),
      templateId: selectedTemplateId.value || null,
      groupIds: bindings.value.map(binding => binding.groupId),
      isTest: announcementForm.value.isTest,
    }),
    onSuccess: () => {
      announcementForm.value = { title: '', content: '', isTest: false }
      queryClient.invalidateQueries({ queryKey: ['competition-qqbot-log', competitionId()] })
    },
  })

  const retryDeliveryMutation = useMutation({
    mutationFn: (deliveryId: string) => adminApi.retryQqBotDelivery(competitionId(), deliveryId),
    onSuccess: () => {
      deliveryQuery.refetch()
    },
  })

  return {
    form,
    selectedGroupIds,
    selectedTemplateId,
    preview,
    templateForm,
    announcementForm,
    config: configQuery.data,
    isLoading: configQuery.isLoading,
    isError: configQuery.isError,
    error: configQuery.error,
    refetch: configQuery.refetch,
    templates: templatesQuery.data,
    loadingTemplates: templatesQuery.isLoading,
    availableGroups: groupsQuery.data,
    deliveryItems,
    refetchDeliveryLog: deliveryQuery.refetch,
    bindings,
    toggleGroup,
    selectTemplate,
    resetTemplate,
    saveConfigMutation,
    saveTemplateMutation,
    previewTemplateMutation,
    sendAnnouncementMutation,
    retryDeliveryMutation,
  }
}
