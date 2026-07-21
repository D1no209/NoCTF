<script setup lang="ts">
import { Bot, KeyRound, Loader2, Radio, Save, UsersRound } from 'lucide-vue-next'
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
import { Textarea } from '@/ui-v1/components/ui/textarea'
import { useAdminQqBotPage, type AdminQqBotSettingsForm } from '@/features/admin/useAdminQqBotPage'

type NumericSettingKey = Exclude<keyof AdminQqBotSettingsForm, 'enabled'>

const { t } = useI18n()
const {
  data,
  isLoading,
  isError,
  refetch,
  settingsForm: form,
  agentForm,
  saveSettingsMutation,
  saveAgentMutation,
  updateGroupMutation,
} = useAdminQqBotPage()

const settingsFields: Array<{ key: NumericSettingKey, label: string }> = [
  { key: 'longPollSeconds', label: 'admin.qqBot.longPollSeconds' },
  { key: 'deliveryLeaseSeconds', label: 'admin.qqBot.deliveryLeaseSeconds' },
  { key: 'maxDeliveryAttempts', label: 'admin.qqBot.maxDeliveryAttempts' },
  { key: 'maxMessageLength', label: 'admin.qqBot.maxMessageLength' },
  { key: 'maxPendingDeliveries', label: 'admin.qqBot.maxPendingDeliveries' },
  { key: 'groupCooldownMilliseconds', label: 'admin.qqBot.groupCooldownMilliseconds' },
  { key: 'competitionCooldownMilliseconds', label: 'admin.qqBot.competitionCooldownMilliseconds' },
  { key: 'manualNotificationCooldownSeconds', label: 'admin.qqBot.manualNotificationCooldownSeconds' },
]

const saveSettings = useToastMutation(saveSettingsMutation, {
  success: 'admin.qqBot.settingsSaved',
  error: 'admin.qqBot.settingsSaveFailed',
})
const saveAgent = useToastMutation(saveAgentMutation, {
  success: 'admin.qqBot.agentRegistered',
  error: 'admin.qqBot.agentRegisterFailed',
})
const updateGroup = useToastMutation<{ id: string, isAuthorized: boolean }>(updateGroupMutation, {
  error: 'admin.qqBot.groupAuthorizationFailed',
})

const statusText = computed(() => data.value?.pluginAvailable ? t('admin.qqBot.available') : t('admin.qqBot.unavailable'))
</script>

<template>
  <div class="space-y-6">
    <div class="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
      <div>
        <h2 class="text-2xl font-bold tracking-tight">{{ t('admin.qqBot.title') }}</h2>
        <p class="mt-1 text-sm text-muted-foreground">{{ t('admin.qqBot.subtitle') }}</p>
      </div>
      <div class="flex items-center gap-2">
        <Badge :variant="data?.pluginAvailable ? 'default' : 'destructive'">{{ statusText }}</Badge>
        <Button variant="outline" @click="refetch">{{ t('common.refresh') }}</Button>
      </div>
    </div>

    <DataState v-if="isLoading" loading />
    <DataState v-else-if="isError" error :retry-label="t('admin.qqBot.retry')" @retry="refetch" />

    <template v-else-if="data">
      <div class="grid gap-4 md:grid-cols-3">
        <Card><CardContent class="flex items-center gap-3 p-4"><Bot class="size-5 text-primary" /><div><div class="text-xs text-muted-foreground">{{ t('admin.qqBot.connection') }}</div><div class="font-semibold">{{ data.connectionType }}</div></div></CardContent></Card>
        <Card><CardContent class="flex items-center gap-3 p-4"><Radio class="size-5 text-primary" /><div><div class="text-xs text-muted-foreground">{{ t('admin.qqBot.agents') }}</div><div class="font-semibold">{{ data.agents.length }}</div></div></CardContent></Card>
        <Card><CardContent class="flex items-center gap-3 p-4"><UsersRound class="size-5 text-primary" /><div><div class="text-xs text-muted-foreground">{{ t('admin.qqBot.authorizedGroups') }}</div><div class="font-semibold">{{ data.groups.filter(group => group.isAuthorized).length }}</div></div></CardContent></Card>
      </div>

      <div class="grid gap-6 xl:grid-cols-[minmax(0,1fr)_420px]">
        <Card>
          <CardHeader><CardTitle>{{ t('admin.qqBot.deliverySettings') }}</CardTitle></CardHeader>
          <CardContent class="space-y-4">
            <label class="flex items-center justify-between border p-3 text-sm"><span>{{ t('admin.qqBot.enableGlobalDelivery') }}</span><input v-model="form.enabled" type="checkbox" class="size-4"></label>
            <div class="grid gap-3 sm:grid-cols-2">
              <div v-for="field in settingsFields" :key="field.key" class="grid gap-1.5">
                <Label>{{ t(field.label) }}</Label>
                <Input v-model.number="form[field.key]" type="number" min="0" />
              </div>
            </div>
            <Button :disabled="saveSettings.isPending.value" @click="saveSettings.mutate()"><Loader2 v-if="saveSettings.isPending.value" class="size-4 animate-spin" /><Save v-else class="size-4" />{{ t('admin.qqBot.saveSettings') }}</Button>
          </CardContent>
        </Card>

        <Card>
          <CardHeader><CardTitle>{{ t('admin.qqBot.registerAgent') }}</CardTitle></CardHeader>
          <CardContent class="space-y-3">
            <Input v-model="agentForm.name" :placeholder="t('admin.qqBot.agentName')" />
            <Textarea v-model="agentForm.publicKeyPem" rows="7" :placeholder="t('admin.qqBot.publicKeyPem')" class="font-mono text-xs" />
            <label class="flex items-center gap-2 text-sm"><input v-model="agentForm.enabled" type="checkbox" class="size-4">{{ t('admin.qqBot.enableAgent') }}</label>
            <Button class="w-full" :disabled="!agentForm.name.trim() || !agentForm.publicKeyPem.trim() || saveAgent.isPending.value" @click="saveAgent.mutate()"><KeyRound class="size-4" />{{ t('admin.qqBot.register') }}</Button>
          </CardContent>
        </Card>
      </div>

      <Card>
        <CardHeader><CardTitle>{{ t('admin.qqBot.agents') }}</CardTitle></CardHeader>
        <CardContent class="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
          <Panel v-for="agent in data.agents" :key="agent.id" class="space-y-3 p-4">
            <div class="flex justify-between gap-2"><div class="font-semibold">{{ agent.name }}</div><Badge :variant="agent.qqOnline ? 'default' : 'outline'">{{ agent.qqOnline ? t('admin.qqBot.online') : t('admin.qqBot.offline') }}</Badge></div>
            <div class="text-sm text-muted-foreground">{{ agent.botNickname || t('admin.qqBot.noBotIdentity') }}</div>
            <div class="flex gap-2 text-xs"><Badge variant="outline">{{ t('admin.qqBot.pending', { count: agent.pendingDeliveries }) }}</Badge><Badge :variant="agent.recentFailed ? 'destructive' : 'secondary'">{{ t('admin.qqBot.failed', { count: agent.recentFailed }) }}</Badge></div>
            <div v-if="agent.lastErrorSummary" class="text-xs text-destructive">{{ agent.lastErrorSummary }}</div>
          </Panel>
        </CardContent>
      </Card>

      <Card>
        <CardHeader><CardTitle>{{ t('admin.qqBot.groups') }}</CardTitle></CardHeader>
        <CardContent class="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
          <Panel v-for="group in data.groups" :key="group.id" class="flex items-center justify-between gap-3 p-4">
            <div class="min-w-0"><div class="truncate font-medium">{{ group.groupName }}</div><div class="text-xs text-muted-foreground">{{ group.groupId }}</div></div>
            <Button size="sm" :variant="group.isAuthorized ? 'default' : 'outline'" :disabled="updateGroup.isPending.value" @click="updateGroup.mutate({ id: group.id, isAuthorized: !group.isAuthorized })">{{ group.isAuthorized ? t('admin.qqBot.authorized') : t('admin.qqBot.authorize') }}</Button>
          </Panel>
        </CardContent>
      </Card>
    </template>
  </div>
</template>
