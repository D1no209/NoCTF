<script setup lang="ts">
import { computed } from 'vue'
import { Bot, KeyRound, Radio, RefreshCw, UsersRound } from 'lucide-vue-next'
import CommandBadge from '../primitives/CommandBadge.vue'
import CommandButton from '../primitives/CommandButton.vue'
import CommandCheckbox from '../primitives/CommandCheckbox.vue'
import CommandInput from '../primitives/CommandInput.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSignal from '../primitives/CommandSignal.vue'
import CommandTextarea from '../primitives/CommandTextarea.vue'
import CommandPageHeader from './CommandPageHeader.vue'

export interface CommandAdminQqBotSettingsForm {
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

export interface CommandAdminQqBotAgentForm {
  name: string
  publicKeyPem: string
  enabled: boolean
}

export interface CommandAdminQqBotAgent {
  id: string
  name: string
  qqOnline: boolean
  botNickname: string | null
  pendingDeliveries: number
  recentFailed: number
  lastErrorSummary: string | null
}

export interface CommandAdminQqBotGroup {
  id: string
  groupName: string
  groupId: number
  isAuthorized: boolean
}

export interface CommandAdminQqBotOverview {
  pluginAvailable: boolean
  connectionType: string
  agents: CommandAdminQqBotAgent[]
  groups: CommandAdminQqBotGroup[]
}

const props = defineProps<{
  state: 'loading' | 'error' | 'ready'
  errorMessage?: string
  overview: CommandAdminQqBotOverview | null
  settingsForm: CommandAdminQqBotSettingsForm
  agentForm: CommandAdminQqBotAgentForm
  saveSettingsPending: boolean
  saveAgentPending: boolean
  groupPending: boolean
  operationMessage?: string
  operationTone?: 'success' | 'danger'
}>()

const emit = defineEmits<{
  refresh: []
  retry: []
  saveSettings: []
  saveAgent: []
  toggleGroup: [group: CommandAdminQqBotGroup]
  'update:settingsForm': [value: CommandAdminQqBotSettingsForm]
  'update:agentForm': [value: CommandAdminQqBotAgentForm]
}>()

const settingsFields: { key: Exclude<keyof CommandAdminQqBotSettingsForm, 'enabled'>, label: string }[] = [
  { key: 'longPollSeconds', label: 'Long poll (seconds)' },
  { key: 'deliveryLeaseSeconds', label: 'Delivery lease (seconds)' },
  { key: 'maxDeliveryAttempts', label: 'Max delivery attempts' },
  { key: 'maxMessageLength', label: 'Max message length' },
  { key: 'maxPendingDeliveries', label: 'Max pending deliveries' },
  { key: 'groupCooldownMilliseconds', label: 'Group cooldown (ms)' },
  { key: 'competitionCooldownMilliseconds', label: 'Competition cooldown (ms)' },
  { key: 'manualNotificationCooldownSeconds', label: 'Manual notification cooldown (seconds)' },
]

const authorizedGroupCount = computed(() => props.overview?.groups.filter(group => group.isAuthorized).length ?? 0)
const canRegisterAgent = computed(() =>
  Boolean(props.agentForm.name.trim()) && Boolean(props.agentForm.publicKeyPem.trim()) && !props.saveAgentPending)

function patchSettings(patch: Partial<CommandAdminQqBotSettingsForm>) {
  emit('update:settingsForm', { ...props.settingsForm, ...patch })
}

function patchAgent(patch: Partial<CommandAdminQqBotAgentForm>) {
  emit('update:agentForm', { ...props.agentForm, ...patch })
}
</script>

<template>
  <section class="admin-qqbot">
    <CommandPageHeader
      signal-label="Admin API / QQ bot delivery"
      signal-tone="warning"
      title="QQ Bot"
      description="Manage the notification delivery agents and authorized QQ groups."
      :stat-icon="Bot"
      :stat-value="String(props.overview?.agents.length ?? 0).padStart(2, '0')"
      stat-label="agents"
    >
      <CommandBadge
        :label="props.overview?.pluginAvailable ? 'Plugin available' : 'Plugin unavailable'"
        :tone="props.overview?.pluginAvailable ? 'success' : 'danger'"
      />
      <CommandButton label="Refresh" tone="outline" @click="emit('refresh')">
        <template #icon><RefreshCw class="size-4" /></template>
      </CommandButton>
    </CommandPageHeader>

    <p v-if="props.operationMessage" class="admin-qqbot__message" :class="`admin-qqbot__message--${props.operationTone || 'success'}`" role="status">
      {{ props.operationMessage }}
    </p>

    <CommandPanel v-if="props.state === 'error'" class="admin-qqbot__state" tone="warning">
      <h2>Unable to load the QQ bot overview</h2>
      <p>{{ props.errorMessage || 'The service did not return a usable QQ bot overview.' }}</p>
      <CommandButton label="Retry" tone="outline" @click="emit('retry')" />
    </CommandPanel>

    <CommandPanel v-else-if="props.state === 'loading' || !props.overview" class="admin-qqbot__state" aria-busy="true">
      <CommandSignal label="Synchronizing" tone="info" />
      <p>Loading the QQ bot overview.</p>
    </CommandPanel>

    <template v-else>
      <div class="admin-qqbot__stats">
        <CommandPanel class="admin-qqbot__stat">
          <Bot class="size-5 text-[var(--v2-primary)]" />
          <span class="admin-qqbot__stat-body">
            <span class="admin-qqbot__stat-label">Connection</span>
            <strong>{{ props.overview.connectionType }}</strong>
          </span>
        </CommandPanel>
        <CommandPanel class="admin-qqbot__stat">
          <Radio class="size-5 text-[var(--v2-primary)]" />
          <span class="admin-qqbot__stat-body">
            <span class="admin-qqbot__stat-label">Agents</span>
            <strong>{{ props.overview.agents.length }}</strong>
          </span>
        </CommandPanel>
        <CommandPanel class="admin-qqbot__stat">
          <UsersRound class="size-5 text-[var(--v2-primary)]" />
          <span class="admin-qqbot__stat-body">
            <span class="admin-qqbot__stat-label">Authorized groups</span>
            <strong>{{ authorizedGroupCount }}</strong>
          </span>
        </CommandPanel>
      </div>

      <div class="admin-qqbot__columns">
        <CommandPanel class="admin-qqbot__panel">
          <h2 class="admin-qqbot__panel-title">Delivery settings</h2>
          <CommandCheckbox
            :checked="props.settingsForm.enabled"
            label="Enable global delivery"
            description="Allow the delivery worker to dispatch QQ notifications."
            @update:checked="patchSettings({ enabled: $event })"
          />
          <div class="admin-qqbot__grid">
            <CommandInput
              v-for="field in settingsFields"
              :key="field.key"
              :model-value="props.settingsForm[field.key]"
              type="number"
              :label="field.label"
              :min="0"
              @update:model-value="patchSettings({ [field.key]: $event })"
            />
          </div>
          <CommandButton
            :label="props.saveSettingsPending ? 'Saving' : 'Save settings'"
            :disabled="props.saveSettingsPending"
            class="admin-qqbot__save"
            @click="emit('saveSettings')"
          />
        </CommandPanel>

        <CommandPanel class="admin-qqbot__panel">
          <h2 class="admin-qqbot__panel-title">Register agent</h2>
          <CommandInput
            :model-value="props.agentForm.name"
            label="Agent name"
            placeholder="Agent name"
            @update:model-value="patchAgent({ name: $event })"
          />
          <CommandTextarea
            :model-value="props.agentForm.publicKeyPem"
            label="Public key (PEM)"
            :rows="7"
            mono
            placeholder="-----BEGIN PUBLIC KEY-----"
            @update:model-value="patchAgent({ publicKeyPem: $event })"
          />
          <CommandCheckbox
            :checked="props.agentForm.enabled"
            label="Enable agent"
            @update:checked="patchAgent({ enabled: $event })"
          />
          <CommandButton
            :label="props.saveAgentPending ? 'Registering' : 'Register agent'"
            :disabled="!canRegisterAgent"
            @click="emit('saveAgent')"
          >
            <template #icon><KeyRound class="size-4" /></template>
          </CommandButton>
        </CommandPanel>
      </div>

      <CommandPanel class="admin-qqbot__panel">
        <h2 class="admin-qqbot__panel-title">Agents</h2>
        <div class="admin-qqbot__cards">
          <CommandPanel v-for="agent in props.overview.agents" :key="agent.id" class="admin-qqbot__card">
            <div class="admin-qqbot__card-head">
              <strong>{{ agent.name }}</strong>
              <CommandBadge :label="agent.qqOnline ? 'Online' : 'Offline'" :tone="agent.qqOnline ? 'success' : 'default'" />
            </div>
            <span class="admin-qqbot__card-nick">{{ agent.botNickname || 'No bot identity' }}</span>
            <div class="admin-qqbot__card-badges">
              <CommandBadge :label="`Pending ${agent.pendingDeliveries}`" />
              <CommandBadge :label="`Failed ${agent.recentFailed}`" :tone="agent.recentFailed ? 'danger' : 'default'" />
            </div>
            <p v-if="agent.lastErrorSummary" class="admin-qqbot__card-error">{{ agent.lastErrorSummary }}</p>
          </CommandPanel>
        </div>
        <p v-if="props.overview.agents.length === 0" class="admin-qqbot__empty">No agents registered.</p>
      </CommandPanel>

      <CommandPanel class="admin-qqbot__panel">
        <h2 class="admin-qqbot__panel-title">Groups</h2>
        <div class="admin-qqbot__cards">
          <CommandPanel v-for="group in props.overview.groups" :key="group.id" class="admin-qqbot__group">
            <span class="admin-qqbot__group-identity">
              <strong>{{ group.groupName }}</strong>
              <span class="admin-qqbot__group-id">{{ group.groupId }}</span>
            </span>
            <CommandButton
              :label="group.isAuthorized ? 'Authorized' : 'Authorize'"
              :tone="group.isAuthorized ? 'primary' : 'outline'"
              :disabled="props.groupPending"
              @click="emit('toggleGroup', group)"
            />
          </CommandPanel>
        </div>
        <p v-if="props.overview.groups.length === 0" class="admin-qqbot__empty">No groups discovered.</p>
      </CommandPanel>
    </template>
  </section>
</template>

<style scoped>
.admin-qqbot { display: grid; gap: 16px; }
.admin-qqbot__message { margin: 0; border-radius: 12px; padding: 12px 14px; color: var(--v2-cyan); background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 13px; }
.admin-qqbot__message--danger { color: var(--v2-danger); }
.admin-qqbot__state { display: grid; min-height: 150px; align-content: center; justify-items: start; gap: 10px; padding: 24px; }
.admin-qqbot__state h2 { margin: 0; color: var(--v2-text); font-size: 17px; font-weight: 600; }
.admin-qqbot__state p { margin: 0; color: var(--v2-text-muted); font-size: 13px; }
.admin-qqbot__stats { display: grid; gap: 12px; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); }
.admin-qqbot__stat { display: flex; align-items: center; gap: 12px; padding: 14px 16px; }
.admin-qqbot__stat-body { display: grid; min-width: 0; gap: 2px; }
.admin-qqbot__stat-label { color: var(--v2-text-faint); font-size: 10px; letter-spacing: 0.08em; }
.admin-qqbot__stat-body strong { overflow: hidden; color: var(--v2-text); font-size: 14px; font-weight: 600; text-overflow: ellipsis; white-space: nowrap; }
.admin-qqbot__columns { display: grid; align-items: start; gap: 14px; grid-template-columns: repeat(auto-fit, minmax(320px, 1fr)); }
.admin-qqbot__panel { display: grid; align-content: start; gap: 14px; padding: 18px; }
.admin-qqbot__panel-title { margin: 0; color: var(--v2-text); font-size: 15px; font-weight: 600; }
.admin-qqbot__grid { display: grid; gap: 12px; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); }
.admin-qqbot__save { justify-self: start; }
.admin-qqbot__cards { display: grid; gap: 12px; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); }
.admin-qqbot__card { display: grid; align-content: start; gap: 10px; padding: 14px; }
.admin-qqbot__card-head { display: flex; align-items: center; justify-content: space-between; gap: 10px; }
.admin-qqbot__card-head strong { overflow: hidden; color: var(--v2-text); font-size: 13px; font-weight: 600; text-overflow: ellipsis; white-space: nowrap; }
.admin-qqbot__card-nick { color: var(--v2-text-muted); font-size: 12px; }
.admin-qqbot__card-badges { display: flex; flex-wrap: wrap; gap: 8px; }
.admin-qqbot__card-error { margin: 0; color: var(--v2-danger); font-size: 11px; }
.admin-qqbot__group { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 12px 14px; }
.admin-qqbot__group-identity { display: grid; min-width: 0; gap: 2px; }
.admin-qqbot__group-identity strong { overflow: hidden; color: var(--v2-text); font-size: 13px; font-weight: 600; text-overflow: ellipsis; white-space: nowrap; }
.admin-qqbot__group-id { color: var(--v2-text-faint); font-family: var(--v2-font-mono); font-size: 10px; }
.admin-qqbot__empty { margin: 0; border-radius: 10px; padding: 16px 12px; color: var(--v2-text-muted); background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 12px; text-align: center; }
</style>
