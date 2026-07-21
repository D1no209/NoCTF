<script setup lang="ts">
import { computed, ref } from 'vue'
import CommandAdminCompetitionOperationsWorkspace from '../components/CommandAdminCompetitionOperationsWorkspace.vue'
import { useAdminCompetitionOperationsPage } from '@/features/admin/useAdminCompetitionOperationsPage'
import { useCompetitionQqBotDelivery } from '@/features/admin/useCompetitionQqBotDelivery'

const activeTab = ref('penetration')
const operationMessage = ref('')
const operationTone = ref<'success' | 'danger'>('success')

function reportError(error: unknown, fallback: string) {
  operationTone.value = 'danger'
  operationMessage.value = error instanceof Error ? error.message : fallback
}

function reportSuccess(message: string) {
  operationTone.value = 'success'
  operationMessage.value = message
}

// ---- Penetration ----

const {
  competitionId,
  selectedChallengeId,
  topologyJson,
  instanceChallengeFilter,
  selectedInstanceId,
  penetrationChallenges: rawPenetrationChallenges,
  loadingChallenges,
  loadingTopology,
  topologyError,
  refetchTopology,
  instances,
  loadingInstances,
  refetchInstances,
  selectedInstance,
  loadingInstanceDetail,
  saveTopologyMutation,
  instanceActionMutation,
} = useAdminCompetitionOperationsPage()

const penetrationChallenges = computed(() => rawPenetrationChallenges.value
  .map(item => ({ id: item.id, title: item.title?.trim() || 'Untitled challenge' })))

function saveTopology() {
  saveTopologyMutation.mutate(undefined, {
    onSuccess: () => reportSuccess('Topology saved.'),
    onError: () => reportError(null, 'Topology JSON is invalid or could not be saved.'),
  })
}

function instanceAction(payload: { id: string, action: 'reset' | 'destroy' }) {
  instanceActionMutation.mutate(payload, {
    onSuccess: () => reportSuccess('Instance updated.'),
    onError: error => reportError(error, 'Instance operation failed.'),
  })
}

// ---- QQ Bot ----

const {
  form: qqConfigForm,
  selectedGroupIds,
  selectedTemplateId,
  preview,
  templateForm,
  announcementForm,
  isLoading: qqLoading,
  isError: qqIsError,
  error: qqError,
  refetch: refetchQqConfig,
  templates: rawTemplates,
  loadingTemplates,
  availableGroups: rawAvailableGroups,
  deliveryItems,
  refetchDeliveryLog,
  bindings,
  toggleGroup,
  selectTemplate,
  resetTemplate,
  saveConfigMutation,
  saveTemplateMutation,
  previewTemplateMutation,
  sendAnnouncementMutation,
  retryDeliveryMutation,
} = useCompetitionQqBotDelivery(() => competitionId.value)

const templates = computed(() => rawTemplates.value ?? [])
const availableGroups = computed(() => rawAvailableGroups.value ?? [])

const qqState = computed<'loading' | 'error' | 'ready'>(() => {
  if (qqLoading.value)
    return 'loading'
  if (qqIsError.value)
    return 'error'
  return 'ready'
})

const qqErrorMessage = computed(() => {
  const error = qqError.value
  return error instanceof Error ? error.message : undefined
})

function saveConfig() {
  saveConfigMutation.mutate(undefined, {
    onSuccess: () => reportSuccess('QQ bot configuration saved.'),
    onError: error => reportError(error, 'Unable to save the QQ bot configuration.'),
  })
}

function saveTemplate() {
  saveTemplateMutation.mutate(undefined, {
    onSuccess: () => reportSuccess('Message template saved.'),
    onError: error => reportError(error, 'Unable to save the message template.'),
  })
}

function previewTemplate() {
  previewTemplateMutation.mutate(undefined, {
    onError: error => reportError(error, 'Unable to render the preview.'),
  })
}

function sendAnnouncement() {
  sendAnnouncementMutation.mutate(undefined, {
    onSuccess: () => reportSuccess('Announcement queued.'),
    onError: error => reportError(error, 'Unable to queue the announcement.'),
  })
}

function retryDelivery(deliveryId: string) {
  retryDeliveryMutation.mutate(deliveryId, {
    onSuccess: () => reportSuccess('Delivery retry queued.'),
    onError: error => reportError(error, 'Unable to retry the delivery.'),
  })
}
</script>

<template>
  <CommandAdminCompetitionOperationsWorkspace
    v-model:active-tab="activeTab"
    v-model:selected-challenge-id="selectedChallengeId"
    v-model:topology-json="topologyJson"
    v-model:instance-challenge-filter="instanceChallengeFilter"
    v-model:selected-instance-id="selectedInstanceId"
    v-model:qq-config-form="qqConfigForm"
    v-model:template-form="templateForm"
    v-model:announcement-form="announcementForm"
    v-model:selected-template-id="selectedTemplateId"
    :penetration-challenges="penetrationChallenges"
    :loading-challenges="loadingChallenges"
    :loading-topology="loadingTopology"
    :topology-error="topologyError"
    :instances="instances"
    :loading-instances="loadingInstances"
    :selected-instance="selectedInstance ?? null"
    :loading-instance-detail="loadingInstanceDetail"
    :saving-topology="saveTopologyMutation.isPending.value"
    :instance-action-pending="instanceActionMutation.isPending.value"
    :qq-state="qqState"
    :qq-error-message="qqErrorMessage"
    :available-groups="availableGroups"
    :selected-group-ids="selectedGroupIds"
    :bindings="bindings"
    :templates="templates"
    :loading-templates="loadingTemplates"
    :preview="preview"
    :delivery-items="deliveryItems"
    :save-config-pending="saveConfigMutation.isPending.value"
    :save-template-pending="saveTemplateMutation.isPending.value"
    :preview-pending="previewTemplateMutation.isPending.value"
    :send-pending="sendAnnouncementMutation.isPending.value"
    :retry-delivery-pending="retryDeliveryMutation.isPending.value"
    :operation-message="operationMessage"
    :operation-tone="operationTone"
    @save-topology="saveTopology"
    @retry-topology="refetchTopology()"
    @refresh-instances="refetchInstances()"
    @instance-action="instanceAction"
    @retry-qq-config="refetchQqConfig()"
    @save-config="saveConfig"
    @toggle-group="toggleGroup"
    @select-template="selectTemplate"
    @reset-template="resetTemplate"
    @save-template="saveTemplate"
    @preview-template="previewTemplate"
    @send-announcement="sendAnnouncement"
    @retry-delivery="retryDelivery"
    @refresh-deliveries="refetchDeliveryLog()"
  />
</template>
