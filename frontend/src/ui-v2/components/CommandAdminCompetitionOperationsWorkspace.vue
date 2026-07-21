<script setup lang="ts">
import { computed } from 'vue'
import {
  Bot,
  Eye,
  FileText,
  Network,
  RefreshCw,
  RotateCcw,
  Save,
  Send,
  Server,
  Trash2,
} from 'lucide-vue-next'
import CommandBadge from '../primitives/CommandBadge.vue'
import CommandButton from '../primitives/CommandButton.vue'
import CommandCheckbox from '../primitives/CommandCheckbox.vue'
import CommandInput from '../primitives/CommandInput.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSelect from '../primitives/CommandSelect.vue'
import CommandSignal from '../primitives/CommandSignal.vue'
import CommandTextarea from '../primitives/CommandTextarea.vue'
import CommandPageHeader from './CommandPageHeader.vue'

type QqEventType = 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7

export interface CommandAdminOperationsInstance {
  id: string
  teamId?: string
  status?: string
  teamName?: string
  challengeId?: string
  challengeTitle?: string
  entryUrl?: string | null
  expiresAt?: string | null
  resetCount?: number
  lastError?: string | null
}

export interface CommandAdminOperationsInstanceDetail extends CommandAdminOperationsInstance {
  entryHost?: string | null
  entryPort?: number | null
  containerIdsJson?: string | null
  portMappingsJson?: string | null
}

export interface CommandAdminOperationsGroup {
  id: string
  agentId: string
  groupId: number
  groupName: string
}

export interface CommandAdminOperationsDelivery {
  id?: string
  eventType?: QqEventType
  groupName?: string
  status?: number
  attemptCount?: number
  maxAttempts?: number
  messageSummary?: string
  lastErrorSummary?: string | null
}

export interface CommandAdminOperationsQqTemplate {
  id?: string | null
  eventType?: QqEventType
  name?: string
  content?: string
  isDefault?: boolean
}

export interface CommandAdminOperationsQqRule {
  eventType: QqEventType
  enabled: boolean
  templateId?: string | null
}

export interface CommandAdminOperationsQqBinding {
  agentId: string
  groupId: string
  qqGroupId: number
  groupName?: string
  isDefault?: boolean
  eventTypes?: QqEventType[]
}

export interface CommandAdminOperationsQqConfigForm {
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
  eventRules: CommandAdminOperationsQqRule[]
  groupBindings: CommandAdminOperationsQqBinding[]
  warnings: string[]
}

export interface CommandAdminOperationsTemplateForm {
  id: string | null
  eventType: QqEventType
  name: string
  content: string
  isDefault: boolean
}

export interface CommandAdminOperationsAnnouncementForm {
  title: string
  content: string
  isTest: boolean
}

export interface CommandAdminOperationsPreview {
  renderedText?: string
  characterCount?: number
  warnings?: string[]
}

const props = defineProps<{
  activeTab: string
  penetrationChallenges: { id: string, title: string }[]
  loadingChallenges: boolean
  selectedChallengeId: string
  topologyJson: string
  loadingTopology: boolean
  topologyError: boolean
  instances: CommandAdminOperationsInstance[]
  loadingInstances: boolean
  instanceChallengeFilter: string
  selectedInstanceId: string
  selectedInstance: CommandAdminOperationsInstanceDetail | null
  loadingInstanceDetail: boolean
  savingTopology: boolean
  instanceActionPending: boolean
  qqState: 'loading' | 'error' | 'ready'
  qqErrorMessage?: string
  qqConfigForm: CommandAdminOperationsQqConfigForm
  availableGroups: CommandAdminOperationsGroup[]
  selectedGroupIds: string[]
  bindings: CommandAdminOperationsQqBinding[]
  templates: CommandAdminOperationsQqTemplate[]
  loadingTemplates: boolean
  selectedTemplateId: string
  templateForm: CommandAdminOperationsTemplateForm
  announcementForm: CommandAdminOperationsAnnouncementForm
  preview: CommandAdminOperationsPreview | null
  deliveryItems: CommandAdminOperationsDelivery[]
  saveConfigPending: boolean
  saveTemplatePending: boolean
  previewPending: boolean
  sendPending: boolean
  retryDeliveryPending: boolean
  operationMessage?: string
  operationTone?: 'success' | 'danger'
}>()

const emit = defineEmits<{
  saveTopology: []
  retryTopology: []
  refreshInstances: []
  instanceAction: [payload: { id: string, action: 'reset' | 'destroy' }]
  retryQqConfig: []
  saveConfig: []
  toggleGroup: [groupId: string]
  selectTemplate: [template: CommandAdminOperationsQqTemplate]
  resetTemplate: []
  saveTemplate: []
  previewTemplate: []
  sendAnnouncement: []
  retryDelivery: [deliveryId: string]
  refreshDeliveries: []
  'update:activeTab': [value: string]
  'update:selectedChallengeId': [value: string]
  'update:topologyJson': [value: string]
  'update:instanceChallengeFilter': [value: string]
  'update:selectedInstanceId': [value: string]
  'update:qqConfigForm': [value: CommandAdminOperationsQqConfigForm]
  'update:templateForm': [value: CommandAdminOperationsTemplateForm]
  'update:announcementForm': [value: CommandAdminOperationsAnnouncementForm]
  'update:selectedTemplateId': [value: string]
}>()

const tabs = [
  { key: 'penetration', label: 'Penetration', icon: Network },
  { key: 'qqbot', label: 'QQ Bot', icon: Bot },
]

const eventLabels: Record<number, string> = {
  0: 'Competition start',
  1: 'Competition end',
  2: 'Challenge solved',
  3: 'First blood',
  4: 'Team penalty',
  5: 'System notice',
  6: 'Manual announcement',
  7: 'Scoreboard update',
}

const eventTypeOptions = Object.entries(eventLabels).map(([value, label]) => ({ value, label }))

const configOptions: { key: Exclude<keyof CommandAdminOperationsQqConfigForm, 'eventRules' | 'groupBindings' | 'warnings'>, label: string }[] = [
  { key: 'enabled', label: 'Enable delivery' },
  { key: 'allowMessages', label: 'Allow messages' },
  { key: 'allowManualNotifications', label: 'Allow manual announcements' },
  { key: 'stopNormalEventsAfterFinished', label: 'Stop normal events after finish' },
  { key: 'mentionAll', label: 'Mention all' },
  { key: 'showTeamName', label: 'Show team name' },
  { key: 'showUserName', label: 'Show user name' },
  { key: 'showChallengeCategory', label: 'Show challenge category' },
  { key: 'includeCompetitionLink', label: 'Include competition link' },
  { key: 'includeChallengeLink', label: 'Include challenge link' },
  { key: 'hidePenaltyDetails', label: 'Hide penalty details' },
]

const challengeOptions = computed(() => props.penetrationChallenges.map(challenge => ({
  value: challenge.id,
  label: challenge.title,
})))

const instanceFilterOptions = computed(() => [
  { value: '', label: 'All penetration challenges' },
  ...challengeOptions.value,
])

const announcementTemplateOptions = computed(() => [
  { value: '', label: 'No template' },
  ...props.templates.map(template => ({
    value: template.id ?? '',
    label: template.name || eventLabels[template.eventType ?? 0],
  })),
])

const canSaveTemplate = computed(() =>
  Boolean(props.templateForm.name.trim()) && Boolean(props.templateForm.content.trim()) && !props.saveTemplatePending)

const canSendAnnouncement = computed(() =>
  Boolean(props.announcementForm.title.trim())
  && Boolean(props.announcementForm.content.trim())
  && props.bindings.length > 0
  && !props.sendPending)

function patchQqConfig(patch: Partial<CommandAdminOperationsQqConfigForm>) {
  emit('update:qqConfigForm', { ...props.qqConfigForm, ...patch })
}

function patchTemplateForm(patch: Partial<CommandAdminOperationsTemplateForm>) {
  emit('update:templateForm', { ...props.templateForm, ...patch })
}

function patchAnnouncementForm(patch: Partial<CommandAdminOperationsAnnouncementForm>) {
  emit('update:announcementForm', { ...props.announcementForm, ...patch })
}

function toggleRule(eventType: number, enabled: boolean) {
  patchQqConfig({
    eventRules: props.qqConfigForm.eventRules.map(rule =>
      rule.eventType === eventType ? { ...rule, enabled } : rule),
  })
}

function templateName(template: CommandAdminOperationsQqTemplate) {
  return template.name || eventLabels[template.eventType ?? 0]
}

function deliveryStatusLabel(status?: number) {
  const labels = ['queued', 'leased', 'sent', 'failed', 'cancelled', 'retrying']
  return labels[status ?? 0] ?? 'unknown'
}

function deliveryStatusTone(status?: number): 'success' | 'danger' | 'default' {
  if (status === 2)
    return 'success'
  if (status === 3 || status === 4)
    return 'danger'
  return 'default'
}

function instanceStatusTone(status?: string): 'success' | 'danger' | 'default' {
  const value = status?.toLowerCase()
  if (value === 'running')
    return 'success'
  if (value === 'failed')
    return 'danger'
  return 'default'
}

function formatDateTime(value?: string | null) {
  if (!value)
    return 'Not set'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? 'Not set' : date.toLocaleString()
}
</script>

<template>
  <section class="admin-operations">
    <CommandPageHeader
      signal-label="Admin API / competition operations"
      signal-tone="warning"
      title="Competition operations"
      description="Range topology, running instances, and competition QQ Bot delivery."
      :stat-icon="Server"
      :stat-value="String(props.instances.length).padStart(2, '0')"
      stat-label="instances"
    />

    <p v-if="props.operationMessage" class="admin-operations__message" :class="`admin-operations__message--${props.operationTone || 'success'}`" role="status">
      {{ props.operationMessage }}
    </p>

    <nav class="admin-operations__tabs" aria-label="Operations sections">
      <button
        v-for="tab in tabs"
        :key="tab.key"
        type="button"
        class="admin-operations__tab"
        :class="{ 'admin-operations__tab--active': props.activeTab === tab.key }"
        @click="emit('update:activeTab', tab.key)"
      >
        <component :is="tab.icon" class="size-4" />
        {{ tab.label }}
      </button>
    </nav>

    <!-- Penetration -->
    <template v-if="props.activeTab === 'penetration'">
      <CommandPanel class="admin-operations__panel">
        <div class="admin-operations__panel-title">
          <h2>Range topology</h2>
          <p>Edit the penetration range topology JSON for the selected challenge.</p>
        </div>
        <div v-if="props.loadingChallenges" class="admin-operations__inline-state" aria-busy="true">
          <CommandSignal label="Synchronizing" tone="info" />
          <span>Loading challenges.</span>
        </div>
        <p v-else-if="props.penetrationChallenges.length === 0" class="admin-operations__empty">
          No penetration challenge is published for this competition.
        </p>
        <template v-else>
          <CommandSelect
            :model-value="props.selectedChallengeId"
            label="Penetration challenge"
            :options="challengeOptions"
            @update:model-value="emit('update:selectedChallengeId', $event)"
          />
          <div v-if="props.loadingTopology" class="admin-operations__inline-state" aria-busy="true">
            <CommandSignal label="Synchronizing" tone="info" />
            <span>Loading the topology.</span>
          </div>
          <div v-else-if="props.topologyError" class="admin-operations__inline-state admin-operations__inline-state--error" role="alert">
            <span>Unable to load the topology.</span>
            <CommandButton label="Retry" tone="outline" @click="emit('retryTopology')" />
          </div>
          <template v-else>
            <CommandTextarea
              :model-value="props.topologyJson"
              label="Topology JSON"
              :rows="18"
              mono
              @update:model-value="emit('update:topologyJson', $event)"
            />
            <div class="admin-operations__footer">
              <CommandButton
                :label="props.savingTopology ? 'Saving' : 'Save topology'"
                :disabled="props.savingTopology"
                @click="emit('saveTopology')"
              >
                <template #icon><Save class="size-4" /></template>
              </CommandButton>
            </div>
          </template>
        </template>
      </CommandPanel>

      <CommandPanel class="admin-operations__panel">
        <div class="admin-operations__panel-head">
          <div class="admin-operations__panel-title">
            <h2>Range instances</h2>
            <p>Live penetration instances, refreshed every 15 seconds.</p>
          </div>
          <CommandButton label="Refresh" tone="outline" @click="emit('refreshInstances')">
            <template #icon><RefreshCw class="size-4" /></template>
          </CommandButton>
        </div>
        <CommandSelect
          :model-value="props.instanceChallengeFilter"
          label="Filter by challenge"
          :options="instanceFilterOptions"
          @update:model-value="emit('update:instanceChallengeFilter', $event)"
        />
        <div v-if="props.loadingInstances" class="admin-operations__inline-state" aria-busy="true">
          <CommandSignal label="Synchronizing" tone="info" />
          <span>Loading instances.</span>
        </div>
        <template v-else>
          <CommandPanel v-for="instance in props.instances" :key="instance.id" class="admin-operations__instance">
            <div class="admin-operations__instance-body">
              <div class="admin-operations__instance-head">
                <Server class="size-4 text-[var(--v2-primary)]" />
                <strong>{{ instance.teamName ?? 'Team instance' }}</strong>
                <CommandBadge :label="instance.status ?? 'Unknown'" :tone="instanceStatusTone(instance.status)" />
              </div>
              <span class="admin-operations__instance-meta">{{ instance.challengeTitle }} · {{ instance.entryUrl || 'No entry URL' }}</span>
            </div>
            <div class="admin-operations__instance-actions">
              <CommandButton label="Details" tone="outline" @click="emit('update:selectedInstanceId', instance.id)">
                <template #icon><Eye class="size-4" /></template>
              </CommandButton>
              <CommandButton label="Reset" tone="outline" :disabled="props.instanceActionPending" @click="emit('instanceAction', { id: instance.id, action: 'reset' })">
                <template #icon><RefreshCw class="size-4" /></template>
              </CommandButton>
              <CommandButton label="Destroy" class="admin-operations__danger-action admin-operations__danger-action--solid" :disabled="props.instanceActionPending" @click="emit('instanceAction', { id: instance.id, action: 'destroy' })">
                <template #icon><Trash2 class="size-4" /></template>
              </CommandButton>
            </div>
          </CommandPanel>
          <p v-if="props.instances.length === 0" class="admin-operations__empty">No penetration instances are active.</p>
        </template>

        <CommandPanel v-if="props.selectedInstanceId" class="admin-operations__detail">
          <div class="admin-operations__panel-head">
            <div class="admin-operations__panel-title">
              <h2>Instance details</h2>
            </div>
            <CommandButton label="Close" tone="ghost" @click="emit('update:selectedInstanceId', '')" />
          </div>
          <div v-if="props.loadingInstanceDetail" class="admin-operations__inline-state" aria-busy="true">
            <CommandSignal label="Synchronizing" tone="info" />
            <span>Loading instance details.</span>
          </div>
          <template v-else-if="props.selectedInstance">
            <div class="admin-operations__detail-grid">
              <div class="admin-operations__detail-item">
                <span class="admin-operations__label">Entry</span>
                <code class="admin-operations__detail-value">{{ props.selectedInstance.entryUrl || props.selectedInstance.entryHost || 'Unavailable' }}</code>
              </div>
              <div class="admin-operations__detail-item">
                <span class="admin-operations__label">Reset count</span>
                <code class="admin-operations__detail-value">{{ props.selectedInstance.resetCount ?? 0 }}</code>
              </div>
              <div class="admin-operations__detail-item">
                <span class="admin-operations__label">Expires</span>
                <span class="admin-operations__detail-value">{{ formatDateTime(props.selectedInstance.expiresAt) }}</span>
              </div>
              <div class="admin-operations__detail-item">
                <span class="admin-operations__label">Last error</span>
                <span class="admin-operations__detail-value admin-operations__detail-value--danger">{{ props.selectedInstance.lastError || 'None' }}</span>
              </div>
            </div>
            <details v-if="props.selectedInstance.containerIdsJson || props.selectedInstance.portMappingsJson" class="admin-operations__metadata">
              <summary>Runtime metadata</summary>
              <pre>{{ props.selectedInstance.containerIdsJson }}{{ props.selectedInstance.portMappingsJson ? `\n${props.selectedInstance.portMappingsJson}` : '' }}</pre>
            </details>
          </template>
        </CommandPanel>
      </CommandPanel>
    </template>

    <!-- QQ Bot -->
    <template v-else>
      <CommandPanel v-if="props.qqState === 'error'" class="admin-operations__state" tone="warning">
        <h2>Unable to load the QQ bot configuration</h2>
        <p>{{ props.qqErrorMessage || 'The service did not return a usable configuration.' }}</p>
        <CommandButton label="Retry" tone="outline" @click="emit('retryQqConfig')" />
      </CommandPanel>

      <CommandPanel v-else-if="props.qqState === 'loading'" class="admin-operations__state" aria-busy="true">
        <CommandSignal label="Synchronizing" tone="info" />
        <p>Loading the QQ bot configuration.</p>
      </CommandPanel>

      <template v-else>
        <CommandPanel class="admin-operations__panel">
          <div class="admin-operations__panel-title">
            <h2>Delivery configuration</h2>
            <p>Control which events are dispatched to the bound QQ groups.</p>
          </div>
          <p v-if="props.qqConfigForm.warnings.length" class="admin-operations__warnings" role="alert">
            {{ props.qqConfigForm.warnings.join(' ') }}
          </p>
          <div class="admin-operations__config-grid">
            <CommandCheckbox
              v-for="option in configOptions"
              :key="option.key"
              :checked="props.qqConfigForm[option.key]"
              :label="option.label"
              @update:checked="patchQqConfig({ [option.key]: $event })"
            />
          </div>
          <div class="admin-operations__columns">
            <div class="admin-operations__groups">
              <span class="admin-operations__label">Bound groups</span>
              <div v-if="props.availableGroups.length" class="admin-operations__group-grid">
                <button
                  v-for="group in props.availableGroups"
                  :key="group.id"
                  type="button"
                  class="admin-operations__group"
                  :class="{ 'admin-operations__group--selected': props.selectedGroupIds.includes(group.id) }"
                  @click="emit('toggleGroup', group.id)"
                >
                  <span class="admin-operations__group-name">{{ group.groupName }}</span>
                  <span class="admin-operations__group-id">{{ group.groupId }}</span>
                </button>
              </div>
              <p v-else class="admin-operations__empty">No available groups. Authorize groups on the QQ Bot page first.</p>
              <div v-if="props.bindings.length" class="admin-operations__binding-badges">
                <CommandBadge v-for="binding in props.bindings" :key="binding.groupId" :label="binding.groupName ?? String(binding.qqGroupId)" tone="info" />
              </div>
            </div>
            <div class="admin-operations__rules">
              <span class="admin-operations__label">Event rules</span>
              <CommandPanel v-for="rule in props.qqConfigForm.eventRules" :key="rule.eventType" class="admin-operations__rule">
                <span>{{ eventLabels[rule.eventType] }}</span>
                <CommandCheckbox :checked="rule.enabled" :label="rule.enabled ? 'Enabled' : 'Disabled'" @update:checked="toggleRule(rule.eventType, $event)" />
              </CommandPanel>
              <p v-if="props.qqConfigForm.eventRules.length === 0" class="admin-operations__empty">No event rules configured.</p>
            </div>
          </div>
          <div class="admin-operations__footer">
            <CommandButton
              :label="props.saveConfigPending ? 'Saving' : 'Save configuration'"
              :disabled="props.saveConfigPending"
              @click="emit('saveConfig')"
            >
              <template #icon><Save class="size-4" /></template>
            </CommandButton>
          </div>
        </CommandPanel>

        <div class="admin-operations__columns admin-operations__columns--wide">
          <CommandPanel class="admin-operations__panel">
            <div class="admin-operations__panel-head">
              <div class="admin-operations__panel-title">
                <h2>
                  <FileText class="size-4" />
                  Message templates
                </h2>
                <p>Customize the rendered text for each event type.</p>
              </div>
              <CommandButton label="New template" tone="outline" @click="emit('resetTemplate')" />
            </div>
            <div v-if="props.loadingTemplates" class="admin-operations__inline-state" aria-busy="true">
              <CommandSignal label="Synchronizing" tone="info" />
              <span>Loading templates.</span>
            </div>
            <div v-else-if="props.templates.length" class="admin-operations__template-list">
              <CommandButton
                v-for="template in props.templates"
                :key="template.id ?? template.name"
                :label="templateName(template)"
                :tone="props.templateForm.id === template.id ? 'primary' : 'outline'"
                @click="emit('selectTemplate', template)"
              />
            </div>
            <div class="admin-operations__grid">
              <CommandSelect
                :model-value="String(props.templateForm.eventType)"
                label="Event"
                :options="eventTypeOptions"
                @update:model-value="patchTemplateForm({ eventType: Number($event) as QqEventType })"
              />
              <CommandInput
                :model-value="props.templateForm.name"
                label="Name"
                placeholder="Template name"
                @update:model-value="patchTemplateForm({ name: $event })"
              />
            </div>
            <CommandTextarea
              :model-value="props.templateForm.content"
              label="Content"
              :rows="10"
              mono
              placeholder="Use {{placeholders}} for event fields"
              @update:model-value="patchTemplateForm({ content: $event })"
            />
            <CommandCheckbox
              :checked="props.templateForm.isDefault"
              label="Use as the event default"
              @update:checked="patchTemplateForm({ isDefault: $event })"
            />
            <div class="admin-operations__footer">
              <CommandButton
                :label="props.previewPending ? 'Rendering' : 'Preview'"
                tone="outline"
                :disabled="props.previewPending"
                @click="emit('previewTemplate')"
              >
                <template #icon><Eye class="size-4" /></template>
              </CommandButton>
              <CommandButton
                :label="props.saveTemplatePending ? 'Saving' : 'Save template'"
                :disabled="!canSaveTemplate"
                @click="emit('saveTemplate')"
              >
                <template #icon><Save class="size-4" /></template>
              </CommandButton>
            </div>
            <CommandPanel v-if="props.preview" class="admin-operations__preview">
              <div class="admin-operations__panel-head">
                <span class="admin-operations__label">Preview</span>
                <CommandBadge :label="`${props.preview.characterCount ?? 0} characters`" />
              </div>
              <pre class="admin-operations__preview-text">{{ props.preview.renderedText }}</pre>
              <p v-if="props.preview.warnings?.length" class="admin-operations__preview-warnings">{{ props.preview.warnings.join(' ') }}</p>
            </CommandPanel>
          </CommandPanel>

          <CommandPanel class="admin-operations__panel">
            <div class="admin-operations__panel-title">
              <h2>
                <Send class="size-4" />
                Manual announcement
              </h2>
              <p>Queue an announcement to the bound groups.</p>
            </div>
            <CommandInput
              :model-value="props.announcementForm.title"
              label="Title"
              placeholder="Announcement title"
              @update:model-value="patchAnnouncementForm({ title: $event })"
            />
            <CommandTextarea
              :model-value="props.announcementForm.content"
              label="Content"
              :rows="8"
              placeholder="Message content"
              @update:model-value="patchAnnouncementForm({ content: $event })"
            />
            <CommandSelect
              :model-value="props.selectedTemplateId"
              label="Template"
              :options="announcementTemplateOptions"
              @update:model-value="emit('update:selectedTemplateId', $event)"
            />
            <p class="admin-operations__recipients">{{ props.bindings.length }} bound group recipients</p>
            <CommandCheckbox
              :checked="props.announcementForm.isTest"
              label="Send as test"
              @update:checked="patchAnnouncementForm({ isTest: $event })"
            />
            <CommandButton
              :label="props.sendPending ? 'Queueing' : 'Queue announcement'"
              :disabled="!canSendAnnouncement"
              @click="emit('sendAnnouncement')"
            >
              <template #icon><Send class="size-4" /></template>
            </CommandButton>
          </CommandPanel>
        </div>

        <CommandPanel class="admin-operations__panel">
          <div class="admin-operations__panel-head">
            <div class="admin-operations__panel-title">
              <h2>Delivery log</h2>
              <p>Recent notification deliveries, refreshed every 15 seconds.</p>
            </div>
            <CommandButton label="Refresh" tone="outline" @click="emit('refreshDeliveries')">
              <template #icon><RefreshCw class="size-4" /></template>
            </CommandButton>
          </div>
          <CommandPanel v-for="delivery in props.deliveryItems" :key="delivery.id" class="admin-operations__delivery">
            <div class="admin-operations__delivery-body">
              <div class="admin-operations__instance-head">
                <strong>{{ delivery.groupName || 'Unknown group' }}</strong>
                <CommandBadge :label="deliveryStatusLabel(delivery.status)" :tone="deliveryStatusTone(delivery.status)" />
                <span class="admin-operations__delivery-event">{{ eventLabels[delivery.eventType ?? 0] }}</span>
              </div>
              <p class="admin-operations__delivery-summary">{{ delivery.messageSummary }}</p>
              <p v-if="delivery.lastErrorSummary" class="admin-operations__delivery-error">{{ delivery.lastErrorSummary }}</p>
            </div>
            <div class="admin-operations__delivery-side">
              <span class="admin-operations__attempts">{{ delivery.attemptCount ?? 0 }} / {{ delivery.maxAttempts ?? 0 }}</span>
              <CommandButton
                v-if="delivery.id && delivery.status === 3"
                label="Retry"
                tone="outline"
                :disabled="props.retryDeliveryPending"
                @click="emit('retryDelivery', delivery.id)"
              >
                <template #icon><RotateCcw class="size-4" /></template>
              </CommandButton>
            </div>
          </CommandPanel>
          <p v-if="props.deliveryItems.length === 0" class="admin-operations__empty">No deliveries recorded.</p>
        </CommandPanel>
      </template>
    </template>
  </section>
</template>

<style scoped>
.admin-operations { display: grid; gap: 16px; }
.admin-operations__message { margin: 0; border-radius: 12px; padding: 12px 14px; color: var(--v2-cyan); background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 13px; }
.admin-operations__message--danger { color: var(--v2-danger); }
.admin-operations__tabs { display: flex; flex-wrap: wrap; gap: 6px; border-radius: 14px; padding: 6px; background: var(--v2-surface); box-shadow: var(--v2-inset); width: fit-content; max-width: 100%; }
.admin-operations__tab { display: inline-flex; align-items: center; gap: 7px; border: 0; border-radius: 10px; padding: 8px 14px; color: var(--v2-text-muted); background: transparent; cursor: pointer; font-family: inherit; font-size: 12px; font-weight: 600; }
.admin-operations__tab--active { color: var(--v2-primary); background: var(--v2-canvas); box-shadow: var(--v2-raised-sm); }
.admin-operations__panel { display: grid; align-content: start; gap: 14px; padding: 18px; }
.admin-operations__panel-head { display: flex; flex-wrap: wrap; align-items: flex-start; justify-content: space-between; gap: 12px; }
.admin-operations__panel-title { display: grid; gap: 4px; }
.admin-operations__panel-title h2 { display: flex; align-items: center; gap: 8px; margin: 0; color: var(--v2-text); font-size: 15px; font-weight: 600; }
.admin-operations__panel-title p { margin: 0; color: var(--v2-text-muted); font-size: 12px; }
.admin-operations__state { display: grid; min-height: 150px; align-content: center; justify-items: start; gap: 10px; padding: 24px; }
.admin-operations__state h2 { margin: 0; color: var(--v2-text); font-size: 17px; font-weight: 600; }
.admin-operations__state p { margin: 0; color: var(--v2-text-muted); font-size: 13px; }
.admin-operations__inline-state { display: flex; align-items: center; gap: 10px; border-radius: 12px; padding: 16px 14px; color: var(--v2-text-muted); background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 13px; }
.admin-operations__inline-state--error { justify-content: space-between; color: var(--v2-danger); }
.admin-operations__empty { margin: 0; border-radius: 12px; padding: 18px 14px; color: var(--v2-text-muted); background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 13px; text-align: center; }
.admin-operations__footer { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: 10px; }
.admin-operations__label { color: var(--v2-text-faint); font-size: 11px; font-weight: 600; letter-spacing: 0.06em; }
.admin-operations__grid { display: grid; gap: 12px; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); }
.admin-operations__columns { display: grid; align-items: start; gap: 14px; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); }
.admin-operations__instance { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 12px; padding: 14px; }
.admin-operations__instance-body { display: grid; min-width: 0; gap: 5px; }
.admin-operations__instance-head { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
.admin-operations__instance-head strong { color: var(--v2-text); font-size: 13px; font-weight: 600; }
.admin-operations__instance-meta { overflow: hidden; color: var(--v2-text-faint); font-size: 11px; text-overflow: ellipsis; white-space: nowrap; }
.admin-operations__instance-actions { display: flex; flex-wrap: wrap; gap: 8px; }
.admin-operations__danger-action--solid { color: #ffffff; background: var(--v2-danger); }
.admin-operations__detail { display: grid; gap: 14px; padding: 16px; }
.admin-operations__detail-grid { display: grid; gap: 12px; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); }
.admin-operations__detail-item { display: grid; gap: 4px; }
.admin-operations__detail-value { overflow-wrap: break-word; color: var(--v2-text); font-family: var(--v2-font-mono); font-size: 12px; }
.admin-operations__detail-value--danger { color: var(--v2-danger); font-family: inherit; }
.admin-operations__metadata { border-radius: 12px; padding: 12px; background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 12px; }
.admin-operations__metadata summary { color: var(--v2-text); cursor: pointer; font-weight: 600; }
.admin-operations__metadata pre { margin: 10px 0 0; overflow: auto; color: var(--v2-text-muted); font-family: var(--v2-font-mono); font-size: 11px; }
.admin-operations__warnings { margin: 0; border-radius: 12px; padding: 12px; color: var(--v2-warning, var(--v2-primary)); background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 12px; }
.admin-operations__config-grid { display: grid; gap: 10px; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); }
.admin-operations__groups { display: grid; align-content: start; gap: 10px; }
.admin-operations__group-grid { display: grid; gap: 8px; grid-template-columns: repeat(auto-fit, minmax(160px, 1fr)); }
.admin-operations__group { display: grid; gap: 3px; border: 0; border-radius: 12px; padding: 10px 12px; background: var(--v2-surface); box-shadow: var(--v2-inset); cursor: pointer; font-family: inherit; text-align: left; }
.admin-operations__group--selected { box-shadow: var(--v2-inset-strong); }
.admin-operations__group-name { overflow: hidden; color: var(--v2-text); font-size: 13px; font-weight: 600; text-overflow: ellipsis; white-space: nowrap; }
.admin-operations__group--selected .admin-operations__group-name { color: var(--v2-primary); }
.admin-operations__group-id { color: var(--v2-text-faint); font-family: var(--v2-font-mono); font-size: 10px; }
.admin-operations__binding-badges { display: flex; flex-wrap: wrap; gap: 8px; }
.admin-operations__rules { display: grid; align-content: start; gap: 8px; }
.admin-operations__rule { display: flex; align-items: center; justify-content: space-between; gap: 10px; padding: 8px 12px; color: var(--v2-text); font-size: 13px; }
.admin-operations__template-list { display: flex; flex-wrap: wrap; gap: 8px; }
.admin-operations__preview { display: grid; gap: 10px; padding: 14px; }
.admin-operations__preview-text { margin: 0; overflow: auto; color: var(--v2-text); font-size: 12px; white-space: pre-wrap; word-break: break-word; }
.admin-operations__preview-warnings { margin: 0; color: var(--v2-warning, var(--v2-primary)); font-size: 11px; }
.admin-operations__recipients { margin: 0; color: var(--v2-text-faint); font-size: 11px; }
.admin-operations__delivery { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 12px; padding: 14px; }
.admin-operations__delivery-body { display: grid; min-width: 0; gap: 5px; }
.admin-operations__delivery-event { color: var(--v2-text-faint); font-size: 11px; }
.admin-operations__delivery-summary { margin: 0; overflow: hidden; color: var(--v2-text-muted); font-size: 12px; text-overflow: ellipsis; white-space: nowrap; }
.admin-operations__delivery-error { margin: 0; color: var(--v2-danger); font-size: 11px; }
.admin-operations__delivery-side { display: flex; align-items: center; gap: 10px; }
.admin-operations__attempts { color: var(--v2-text-faint); font-family: var(--v2-font-mono); font-size: 11px; font-variant-numeric: tabular-nums; }
</style>
