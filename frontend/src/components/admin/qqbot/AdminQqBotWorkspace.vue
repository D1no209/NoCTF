<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { Bot, KeyRound, Loader2, Radio, Save, UsersRound } from 'lucide-vue-next'
import { computed, reactive, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'
import { adminApi } from '@/api/noctf'
import DataState from '@/components/state/DataState.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Panel } from '@/components/ui/panel'
import { Textarea } from '@/components/ui/textarea'

interface Settings {
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

interface Agent {
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

interface Group {
  id: string
  groupName: string
  groupId: number
  isPresent: boolean
  isAuthorized: boolean
}

interface Overview {
  pluginAvailable: boolean
  connectionType: string
  settings: Settings
  agents: Agent[]
  groups: Group[]
}

type NumericSettingKey = Exclude<keyof Settings, 'enabled'>

const { t } = useI18n()
const qc = useQueryClient()
const form = reactive<Settings>({
  enabled: false,
  longPollSeconds: 25,
  deliveryLeaseSeconds: 45,
  maxDeliveryAttempts: 5,
  maxMessageLength: 1000,
  maxPendingDeliveries: 500,
  groupCooldownMilliseconds: 1000,
  competitionCooldownMilliseconds: 1000,
  manualNotificationCooldownSeconds: 30,
})
const agentForm = reactive({ name: '', publicKeyPem: '', enabled: true })
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

const { data, isLoading, isError, refetch } = useQuery({
  queryKey: ['admin-qqbot'],
  queryFn: () => adminApi.qqBotOverview() as Promise<Overview>,
})

watch(() => data.value?.settings, (settings) => {
  if (settings)
    Object.assign(form, settings)
}, { immediate: true })

const saveSettings = useMutation({
  mutationFn: () => adminApi.updateQqBotSettings({ ...form }),
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: ['admin-qqbot'] })
    toast.success(t('admin.qqBot.settingsSaved'))
  },
  onError: () => toast.error(t('admin.qqBot.settingsSaveFailed')),
})

const saveAgent = useMutation({
  mutationFn: () => adminApi.upsertQqBotAgent({ id: null, name: agentForm.name.trim(), enabled: agentForm.enabled, publicKeyPem: agentForm.publicKeyPem, previousKeyOverlapMinutes: 60 }),
  onSuccess: () => {
    agentForm.name = ''
    agentForm.publicKeyPem = ''
    qc.invalidateQueries({ queryKey: ['admin-qqbot'] })
    toast.success(t('admin.qqBot.agentRegistered'))
  },
  onError: () => toast.error(t('admin.qqBot.agentRegisterFailed')),
})

const updateGroup = useMutation({
  mutationFn: ({ id, isAuthorized }: { id: string, isAuthorized: boolean }) => adminApi.authorizeQqBotGroup(id, { isAuthorized }),
  onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-qqbot'] }),
  onError: () => toast.error(t('admin.qqBot.groupAuthorizationFailed')),
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
