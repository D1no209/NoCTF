<script setup lang="ts">
import { computed } from 'vue'
import { KeyRound, MailCheck, RefreshCw, Save, Send } from 'lucide-vue-next'
import CommandBadge from '../primitives/CommandBadge.vue'
import CommandButton from '../primitives/CommandButton.vue'
import CommandCheckbox from '../primitives/CommandCheckbox.vue'
import CommandInput from '../primitives/CommandInput.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSignal from '../primitives/CommandSignal.vue'
import CommandPageHeader from './CommandPageHeader.vue'

export interface CommandAdminEmailVerificationForm {
  enabled: boolean
  publicBaseUrl: string
  tokenLifetimeMinutes: string
  resendCooldownSeconds: string
  smtpHost: string
  smtpPort: string
  smtpEnableSsl: boolean
  smtpUserName: string
  smtpPassword: string
  smtpFromAddress: string
  smtpFromName: string
  smtpTimeoutSeconds: string
}

export interface CommandAdminEmailVerificationStatus {
  enabled: boolean
  smtpPasswordConfigured: boolean
  persisted: boolean
  updatedAt: string | null
}

const props = defineProps<{
  state: 'loading' | 'error' | 'ready'
  errorMessage?: string
  status: CommandAdminEmailVerificationStatus
  form: CommandAdminEmailVerificationForm
  savePending: boolean
  testPending: boolean
  operationMessage?: string
  operationTone?: 'success' | 'danger'
}>()

const emit = defineEmits<{
  refresh: []
  retry: []
  save: []
  test: []
  'update:form': [value: CommandAdminEmailVerificationForm]
}>()

const updatedAtLabel = computed(() => {
  if (!props.status.updatedAt)
    return 'Not saved from the admin console'
  const date = new Date(props.status.updatedAt)
  return Number.isNaN(date.getTime()) ? 'Not saved from the admin console' : date.toLocaleString()
})

function patchForm(patch: Partial<CommandAdminEmailVerificationForm>) {
  emit('update:form', { ...props.form, ...patch })
}
</script>

<template>
  <section class="admin-email">
    <CommandPageHeader
      signal-label="Admin API / email verification"
      signal-tone="warning"
      title="Email verification"
      description="Control the registration email gate and the SMTP delivery configuration."
      :stat-icon="MailCheck"
      :stat-value="props.status.enabled ? 'ON' : 'OFF'"
      stat-label="verification"
    >
      <CommandButton label="Refresh" tone="outline" @click="emit('refresh')">
        <template #icon><RefreshCw class="size-4" /></template>
      </CommandButton>
    </CommandPageHeader>

    <p v-if="props.operationMessage" class="admin-email__message" :class="`admin-email__message--${props.operationTone || 'success'}`" role="status">
      {{ props.operationMessage }}
    </p>

    <CommandPanel v-if="props.state === 'error'" class="admin-email__state" tone="warning">
      <h2>Unable to load settings</h2>
      <p>{{ props.errorMessage || 'The service did not return the email verification settings.' }}</p>
      <CommandButton label="Retry" tone="outline" @click="emit('retry')" />
    </CommandPanel>

    <CommandPanel v-else-if="props.state === 'loading'" class="admin-email__state" aria-busy="true">
      <CommandSignal label="Synchronizing" tone="info" />
      <p>Loading the email verification settings.</p>
    </CommandPanel>

    <template v-else>
      <div class="admin-email__stats">
        <CommandPanel class="admin-email__stat">
          <MailCheck class="size-5 text-[var(--v2-primary)]" />
          <span class="admin-email__stat-body">
            <span class="admin-email__stat-label">Registration gate</span>
            <strong>{{ props.status.enabled ? 'Enabled' : 'Disabled' }}</strong>
          </span>
        </CommandPanel>
        <CommandPanel class="admin-email__stat">
          <KeyRound class="size-5 text-[var(--v2-primary)]" />
          <span class="admin-email__stat-body">
            <span class="admin-email__stat-label">SMTP credential</span>
            <strong>{{ props.status.smtpPasswordConfigured ? 'Configured' : 'Not configured' }}</strong>
          </span>
        </CommandPanel>
        <CommandPanel class="admin-email__stat">
          <Save class="size-5 text-[var(--v2-primary)]" />
          <span class="admin-email__stat-body">
            <span class="admin-email__stat-label">Configuration source</span>
            <strong>{{ props.status.persisted ? 'Admin configuration' : 'Deployment defaults' }}</strong>
          </span>
        </CommandPanel>
      </div>

      <CommandPanel class="admin-email__panel">
        <div class="admin-email__panel-head">
          <div class="admin-email__panel-title">
            <h2>Delivery settings</h2>
            <p>Gate registrations behind a verified email address.</p>
          </div>
          <CommandBadge :label="props.form.enabled ? 'Enabled' : 'Disabled'" :tone="props.form.enabled ? 'success' : 'default'" />
        </div>
        <CommandCheckbox
          :checked="props.form.enabled"
          label="Enable verification"
          description="New accounts must verify their email address before signing in."
          @update:checked="patchForm({ enabled: $event })"
        />
        <div class="admin-email__grid">
          <div class="admin-email__grid-span">
            <CommandInput
              :model-value="props.form.publicBaseUrl"
              type="url"
              label="Public base URL"
              placeholder="https://ctf.example.com"
              autocomplete="url"
              @update:model-value="patchForm({ publicBaseUrl: $event })"
            />
            <p class="admin-email__hint">Used to build the verification links embedded in outgoing emails.</p>
          </div>
          <CommandInput
            :model-value="props.form.tokenLifetimeMinutes"
            type="number"
            label="Token lifetime (minutes)"
            :min="5"
            :max="10080"
            @update:model-value="patchForm({ tokenLifetimeMinutes: $event })"
          />
          <CommandInput
            :model-value="props.form.resendCooldownSeconds"
            type="number"
            label="Resend cooldown (seconds)"
            :min="1"
            :max="3600"
            @update:model-value="patchForm({ resendCooldownSeconds: $event })"
          />
        </div>
      </CommandPanel>

      <CommandPanel class="admin-email__panel">
        <div class="admin-email__panel-head">
          <div class="admin-email__panel-title">
            <h2>SMTP settings</h2>
            <p>Delivery credentials for the outbound verification emails.</p>
          </div>
        </div>
        <div class="admin-email__grid">
          <CommandInput
            :model-value="props.form.smtpHost"
            label="SMTP host"
            placeholder="smtp.example.com"
            @update:model-value="patchForm({ smtpHost: $event })"
          />
          <CommandInput
            :model-value="props.form.smtpPort"
            type="number"
            label="SMTP port"
            :min="1"
            :max="65535"
            @update:model-value="patchForm({ smtpPort: $event })"
          />
          <CommandInput
            :model-value="props.form.smtpUserName"
            label="SMTP username"
            autocomplete="username"
            @update:model-value="patchForm({ smtpUserName: $event })"
          />
          <div>
            <CommandInput
              :model-value="props.form.smtpPassword"
              type="password"
              label="SMTP password"
              autocomplete="new-password"
              :placeholder="props.status.smtpPasswordConfigured ? 'Keep the current password' : 'Enter a password'"
              @update:model-value="patchForm({ smtpPassword: $event })"
            />
            <p class="admin-email__hint">Leave empty to keep the currently stored password.</p>
          </div>
          <CommandInput
            :model-value="props.form.smtpFromAddress"
            type="email"
            label="From address"
            placeholder="no-reply@example.com"
            autocomplete="email"
            @update:model-value="patchForm({ smtpFromAddress: $event })"
          />
          <CommandInput
            :model-value="props.form.smtpFromName"
            label="From name"
            autocomplete="organization"
            @update:model-value="patchForm({ smtpFromName: $event })"
          />
          <CommandInput
            :model-value="props.form.smtpTimeoutSeconds"
            type="number"
            label="Timeout (seconds)"
            :min="1"
            :max="120"
            @update:model-value="patchForm({ smtpTimeoutSeconds: $event })"
          />
          <CommandCheckbox
            :checked="props.form.smtpEnableSsl"
            label="Enable TLS"
            @update:checked="patchForm({ smtpEnableSsl: $event })"
          />
        </div>
        <div class="admin-email__footer">
          <p class="admin-email__updated">Last updated: {{ updatedAtLabel }}</p>
          <div class="admin-email__actions">
            <CommandButton
              :label="props.testPending ? 'Sending' : 'Send test email'"
              tone="outline"
              :disabled="props.testPending || !props.status.persisted"
              @click="emit('test')"
            >
              <template #icon><Send class="size-4" /></template>
            </CommandButton>
            <CommandButton
              :label="props.savePending ? 'Saving' : 'Save settings'"
              :disabled="props.savePending"
              @click="emit('save')"
            />
          </div>
        </div>
      </CommandPanel>
    </template>
  </section>
</template>

<style scoped>
.admin-email { display: grid; gap: 16px; }
.admin-email__message { margin: 0; border-radius: 12px; padding: 12px 14px; color: var(--v2-cyan); background: var(--v2-surface); box-shadow: var(--v2-inset); font-size: 13px; }
.admin-email__message--danger { color: var(--v2-danger); }
.admin-email__state { display: grid; min-height: 150px; align-content: center; justify-items: start; gap: 10px; padding: 24px; }
.admin-email__state h2 { margin: 0; color: var(--v2-text); font-size: 17px; font-weight: 600; }
.admin-email__state p { margin: 0; color: var(--v2-text-muted); font-size: 13px; }
.admin-email__stats { display: grid; gap: 12px; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); }
.admin-email__stat { display: flex; align-items: center; gap: 12px; padding: 14px 16px; }
.admin-email__stat-body { display: grid; min-width: 0; gap: 2px; }
.admin-email__stat-label { color: var(--v2-text-faint); font-size: 10px; letter-spacing: 0.08em; }
.admin-email__stat-body strong { overflow: hidden; color: var(--v2-text); font-size: 14px; font-weight: 600; text-overflow: ellipsis; white-space: nowrap; }
.admin-email__panel { display: grid; gap: 16px; padding: 18px; }
.admin-email__panel-head { display: flex; align-items: flex-start; justify-content: space-between; gap: 12px; }
.admin-email__panel-title { display: grid; gap: 4px; }
.admin-email__panel-title h2 { margin: 0; color: var(--v2-text); font-size: 15px; font-weight: 600; }
.admin-email__panel-title p { margin: 0; color: var(--v2-text-muted); font-size: 12px; }
.admin-email__grid { display: grid; gap: 14px; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); }
.admin-email__grid-span { grid-column: 1 / -1; display: grid; gap: 6px; }
.admin-email__hint { margin: 4px 0 0; color: var(--v2-text-faint); font-size: 11px; }
.admin-email__footer { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 12px; }
.admin-email__updated { margin: 0; color: var(--v2-text-faint); font-size: 11px; }
.admin-email__actions { display: flex; flex-wrap: wrap; gap: 10px; }
</style>
