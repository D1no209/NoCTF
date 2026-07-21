<script setup lang="ts">
import { KeyRound, Loader2, MailCheck, RefreshCw, Save, Send } from 'lucide-vue-next'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToastMutation } from '@/ui-v1/components/feedback/useToastMutation'
import DataState from '@/ui-v1/components/state/DataState.vue'
import { Badge } from '@/ui-v1/components/ui/badge'
import { Button } from '@/ui-v1/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/ui-v1/components/ui/card'
import { Input } from '@/ui-v1/components/ui/input'
import { Label } from '@/ui-v1/components/ui/label'
import { Panel } from '@/ui-v1/components/ui/panel'
import { useAdminEmailVerificationPage } from '@/features/admin/useAdminEmailVerificationPage'

const { t, d } = useI18n()

const {
  settings: data,
  isLoading,
  isError,
  refetch,
  form,
  saveMutation: save,
  testMutation: test,
} = useAdminEmailVerificationPage()

const saveSettings = useToastMutation(save, {
  success: 'admin.emailVerification.settingsSaved',
  error: 'admin.emailVerification.settingsSaveFailed',
})

const testSettings = useToastMutation(test, {
  success: 'admin.emailVerification.testSent',
  error: 'admin.emailVerification.testFailed',
})

const statusLabel = computed(() => data.value?.enabled
  ? t('admin.emailVerification.enabled')
  : t('admin.emailVerification.disabled'))

const updatedAt = computed(() => data.value?.updatedAt
  ? d(new Date(data.value.updatedAt), 'short')
  : t('admin.emailVerification.notSavedInAdmin'))
</script>

<template>
  <div class="space-y-6">
    <div class="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
      <div>
        <h2 class="text-2xl font-bold tracking-tight">
          {{ t('admin.emailVerification.title') }}
        </h2>
        <p class="mt-1 max-w-3xl text-sm text-muted-foreground">
          {{ t('admin.emailVerification.subtitle') }}
        </p>
      </div>
      <Button variant="outline" :disabled="isLoading" @click="refetch()">
        <RefreshCw class="size-4" />
        {{ t('common.refresh') }}
      </Button>
    </div>

    <DataState v-if="isLoading" loading />
    <DataState v-else-if="isError" error :retry-label="t('common.retry')" @retry="refetch()" />

    <template v-else-if="data">
      <div class="grid gap-4 md:grid-cols-3">
        <Panel class="flex items-center gap-3 p-4">
          <MailCheck class="size-5 text-primary" />
          <div>
            <div class="text-xs text-muted-foreground">
              {{ t('admin.emailVerification.registrationGate') }}
            </div>
            <div class="font-semibold">
              {{ statusLabel }}
            </div>
          </div>
        </Panel>
        <Panel class="flex items-center gap-3 p-4">
          <KeyRound class="size-5 text-primary" />
          <div>
            <div class="text-xs text-muted-foreground">
              {{ t('admin.emailVerification.smtpCredential') }}
            </div>
            <div class="font-semibold">
              {{ data.smtpPasswordConfigured ? t('admin.emailVerification.configured') : t('admin.emailVerification.notConfigured') }}
            </div>
          </div>
        </Panel>
        <Panel class="flex items-center gap-3 p-4">
          <Save class="size-5 text-primary" />
          <div class="min-w-0">
            <div class="text-xs text-muted-foreground">
              {{ t('admin.emailVerification.configurationSource') }}
            </div>
            <div class="truncate font-semibold">
              {{ data.persisted ? t('admin.emailVerification.adminConfiguration') : t('admin.emailVerification.deploymentDefaults') }}
            </div>
          </div>
        </Panel>
      </div>

      <Card>
        <CardHeader class="flex-row items-start justify-between gap-4">
          <div>
            <CardTitle>{{ t('admin.emailVerification.deliverySettings') }}</CardTitle>
            <p class="mt-1 text-sm text-muted-foreground">
              {{ t('admin.emailVerification.deliverySettingsDescription') }}
            </p>
          </div>
          <Badge :variant="form.enabled ? 'default' : 'outline'">
            {{ statusLabel }}
          </Badge>
        </CardHeader>
        <CardContent class="space-y-6">
          <label class="flex items-start justify-between gap-4 border p-4">
            <span>
              <span class="block font-semibold">{{ t('admin.emailVerification.enableVerification') }}</span>
              <span class="mt-1 block text-sm text-muted-foreground">{{ t('admin.emailVerification.enableVerificationDescription') }}</span>
            </span>
            <input v-model="form.enabled" type="checkbox" class="mt-1 size-4">
          </label>

          <div class="grid gap-4 lg:grid-cols-2">
            <div class="grid gap-2 lg:col-span-2">
              <Label for="email-public-url">{{ t('admin.emailVerification.publicBaseUrl') }}</Label>
              <Input id="email-public-url" v-model="form.publicBaseUrl" type="url" autocomplete="url" placeholder="https://ctf.example.com" />
              <p class="text-xs text-muted-foreground">
                {{ t('admin.emailVerification.publicBaseUrlHelp') }}
              </p>
            </div>
            <div class="grid gap-2">
              <Label for="email-token-lifetime">{{ t('admin.emailVerification.tokenLifetime') }}</Label>
              <Input id="email-token-lifetime" v-model.number="form.tokenLifetimeMinutes" type="number" min="5" max="10080" />
            </div>
            <div class="grid gap-2">
              <Label for="email-resend-cooldown">{{ t('admin.emailVerification.resendCooldown') }}</Label>
              <Input id="email-resend-cooldown" v-model.number="form.resendCooldownSeconds" type="number" min="1" max="3600" />
            </div>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>{{ t('admin.emailVerification.smtpSettings') }}</CardTitle>
          <p class="mt-1 text-sm text-muted-foreground">
            {{ t('admin.emailVerification.smtpSettingsDescription') }}
          </p>
        </CardHeader>
        <CardContent class="space-y-6">
          <div class="grid gap-4 lg:grid-cols-2">
            <div class="grid gap-2">
              <Label for="smtp-host">{{ t('admin.emailVerification.smtpHost') }}</Label>
              <Input id="smtp-host" v-model="form.smtpHost" autocomplete="off" placeholder="smtp.example.com" />
            </div>
            <div class="grid gap-2">
              <Label for="smtp-port">{{ t('admin.emailVerification.smtpPort') }}</Label>
              <Input id="smtp-port" v-model.number="form.smtpPort" type="number" min="1" max="65535" />
            </div>
            <div class="grid gap-2">
              <Label for="smtp-username">{{ t('admin.emailVerification.smtpUsername') }}</Label>
              <Input id="smtp-username" v-model="form.smtpUserName" autocomplete="username" />
            </div>
            <div class="grid gap-2">
              <Label for="smtp-password">{{ t('admin.emailVerification.smtpPassword') }}</Label>
              <Input id="smtp-password" v-model="form.smtpPassword" type="password" autocomplete="new-password" :placeholder="data.smtpPasswordConfigured ? t('admin.emailVerification.keepPassword') : t('admin.emailVerification.enterPassword')" />
              <p class="text-xs text-muted-foreground">
                {{ t('admin.emailVerification.passwordHelp') }}
              </p>
            </div>
            <div class="grid gap-2">
              <Label for="smtp-from-address">{{ t('admin.emailVerification.fromAddress') }}</Label>
              <Input id="smtp-from-address" v-model="form.smtpFromAddress" type="email" autocomplete="email" placeholder="no-reply@example.com" />
            </div>
            <div class="grid gap-2">
              <Label for="smtp-from-name">{{ t('admin.emailVerification.fromName') }}</Label>
              <Input id="smtp-from-name" v-model="form.smtpFromName" autocomplete="organization" />
            </div>
            <div class="grid gap-2">
              <Label for="smtp-timeout">{{ t('admin.emailVerification.timeout') }}</Label>
              <Input id="smtp-timeout" v-model.number="form.smtpTimeoutSeconds" type="number" min="1" max="120" />
            </div>
            <label class="flex items-center gap-3 self-end border p-3 text-sm">
              <input v-model="form.smtpEnableSsl" type="checkbox" class="size-4">
              <span>{{ t('admin.emailVerification.enableTls') }}</span>
            </label>
          </div>

          <div class="flex flex-col gap-3 border-t pt-5 sm:flex-row sm:items-center sm:justify-between">
            <p class="text-xs text-muted-foreground">
              {{ t('admin.emailVerification.lastUpdated', { value: updatedAt }) }}
            </p>
            <div class="flex flex-col gap-2 sm:flex-row">
              <Button variant="outline" :disabled="testSettings.isPending.value || !data.persisted" @click="testSettings.mutate()">
                <Loader2 v-if="testSettings.isPending.value" class="size-4 animate-spin" />
                <Send v-else class="size-4" />
                {{ t('admin.emailVerification.sendTest') }}
              </Button>
              <Button :disabled="saveSettings.isPending.value" @click="saveSettings.mutate()">
                <Loader2 v-if="saveSettings.isPending.value" class="size-4 animate-spin" />
                <Save v-else class="size-4" />
                {{ t('admin.emailVerification.saveSettings') }}
              </Button>
            </div>
          </div>
        </CardContent>
      </Card>
    </template>
  </div>
</template>
