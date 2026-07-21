<script setup lang="ts">
import { Eye, FileText, Loader2, RefreshCw, RotateCcw, Save, Send } from 'lucide-vue-next'
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
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/ui-v1/components/ui/select'
import { Textarea } from '@/ui-v1/components/ui/textarea'
import {
  useCompetitionQqBotDelivery,
  type QqConfig,
  type QqEventType,
} from '@/features/admin/useCompetitionQqBotDelivery'

type BooleanConfigKey = Exclude<keyof QqConfig, 'eventRules' | 'groupBindings' | 'warnings'>

const props = defineProps<{ competitionId: string }>()
const { t } = useI18n()

const {
  form,
  selectedGroupIds,
  selectedTemplateId,
  preview,
  templateForm,
  announcementForm,
  isLoading,
  isError,
  refetch,
  templates,
  loadingTemplates,
  availableGroups,
  deliveryItems,
  refetchDeliveryLog,
  bindings,
  toggleGroup,
  selectTemplate,
  resetTemplate,
  saveConfigMutation: saveConfigAction,
  saveTemplateMutation: saveTemplateAction,
  previewTemplateMutation: previewTemplateAction,
  sendAnnouncementMutation: sendAnnouncementAction,
  retryDeliveryMutation: retryDeliveryAction,
} = useCompetitionQqBotDelivery(() => props.competitionId)

const eventLabels = computed<Record<QqEventType, string>>(() => ({
  0: t('admin.qqBot.competition.events.competitionStart'),
  1: t('admin.qqBot.competition.events.competitionEnd'),
  2: t('admin.qqBot.competition.events.challengeSolved'),
  3: t('admin.qqBot.competition.events.firstBlood'),
  4: t('admin.qqBot.competition.events.teamPenalty'),
  5: t('admin.qqBot.competition.events.systemNotice'),
  6: t('admin.qqBot.competition.events.manualAnnouncement'),
  7: t('admin.qqBot.competition.events.scoreboardUpdate'),
}))
const configOptions: Array<{ key: BooleanConfigKey, label: string }> = [
  { key: 'enabled', label: 'admin.qqBot.competition.enableDelivery' },
  { key: 'allowMessages', label: 'admin.qqBot.competition.allowMessages' },
  { key: 'allowManualNotifications', label: 'admin.qqBot.competition.allowManualAnnouncements' },
  { key: 'stopNormalEventsAfterFinished', label: 'admin.qqBot.competition.stopNormalEventsAfterFinish' },
  { key: 'mentionAll', label: 'admin.qqBot.competition.mentionAll' },
  { key: 'showTeamName', label: 'admin.qqBot.competition.showTeamName' },
  { key: 'showUserName', label: 'admin.qqBot.competition.showUserName' },
  { key: 'showChallengeCategory', label: 'admin.qqBot.competition.showChallengeCategory' },
  { key: 'includeCompetitionLink', label: 'admin.qqBot.competition.includeCompetitionLink' },
  { key: 'includeChallengeLink', label: 'admin.qqBot.competition.includeChallengeLink' },
  { key: 'hidePenaltyDetails', label: 'admin.qqBot.competition.hidePenaltyDetails' },
]

const saveConfig = useToastMutation(saveConfigAction, {
  success: 'admin.qqBot.competition.configurationSaved',
  error: 'admin.qqBot.competition.configurationSaveFailed',
})
const saveTemplate = useToastMutation(saveTemplateAction, {
  success: 'admin.qqBot.competition.templateSaved',
  error: 'admin.qqBot.competition.templateSaveFailed',
})
const previewTemplate = useToastMutation(previewTemplateAction, {
  error: 'admin.qqBot.competition.previewFailed',
})
const sendAnnouncement = useToastMutation(sendAnnouncementAction, {
  success: 'admin.qqBot.competition.notificationQueued',
  error: 'admin.qqBot.competition.notificationQueueFailed',
})
const retryDelivery = useToastMutation<string>(retryDeliveryAction, {
  success: 'admin.qqBot.competition.deliveryRetryQueued',
  error: 'admin.qqBot.competition.deliveryRetryFailed',
})

function statusLabel(status?: number) {
  const statusKeys = ['queued', 'leased', 'sent', 'failed', 'cancelled', 'retrying']
  const key = statusKeys[status ?? 0]
  return key ? t(`admin.qqBot.competition.statuses.${key}`) : t('admin.qqBot.competition.statuses.unknown')
}

function statusVariant(status?: number): 'default' | 'secondary' | 'destructive' | 'outline' {
  if (status === 2)
    return 'default'
  if (status === 3 || status === 4)
    return 'destructive'
  return 'outline'
}
</script>

<template>
  <DataState v-if="isLoading" loading />
  <DataState v-else-if="isError" error :retry-label="t('admin.qqBot.retry')" @retry="refetch()" />

  <div v-else class="space-y-6">
    <Card>
      <CardHeader class="flex-row items-center justify-between gap-4">
        <div><CardTitle>{{ t('admin.qqBot.competition.deliveryConfiguration') }}</CardTitle><p class="mt-1 text-sm text-muted-foreground">{{ t('admin.qqBot.competition.deliveryConfigurationDescription') }}</p></div>
        <Button variant="outline" size="sm" @click="refetch()"><RefreshCw class="size-4" />{{ t('common.refresh') }}</Button>
      </CardHeader>
      <CardContent class="space-y-5">
        <div v-if="form.warnings.length" class="border border-[var(--semantic-warning-border)] bg-[var(--semantic-warning-soft)] p-3 text-sm text-[var(--semantic-warning)]">{{ form.warnings.join(' ') }}</div>
        <div class="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
          <label v-for="item in configOptions" :key="item.key" class="flex items-center justify-between border p-3 text-sm"><span>{{ t(item.label) }}</span><input v-model="form[item.key]" type="checkbox" class="size-4"></label>
        </div>
        <div class="grid gap-5 xl:grid-cols-2">
          <div class="space-y-2"><Label>{{ t('admin.qqBot.competition.boundGroups') }}</Label><div v-if="availableGroups?.length" class="grid gap-2 sm:grid-cols-2"><Button v-for="group in availableGroups" :key="group.id" variant="outline" class="h-auto justify-start p-3 text-left" :class="selectedGroupIds.includes(group.id) && 'border-primary bg-primary/5'" @click="toggleGroup(group.id)"><span class="min-w-0"><span class="block truncate font-medium">{{ group.groupName }}</span><span class="block text-xs text-muted-foreground">{{ group.groupId }}</span></span></Button></div><p v-else class="text-sm text-muted-foreground">{{ t('admin.qqBot.competition.noAvailableGroups') }}</p><div v-if="bindings.length" class="flex flex-wrap gap-2"><Badge v-for="binding in bindings" :key="binding.groupId" variant="secondary">{{ binding.groupName ?? binding.qqGroupId }}</Badge></div></div>
          <div class="space-y-2"><Label>{{ t('admin.qqBot.competition.eventRules') }}</Label><Panel v-for="rule in form.eventRules" :key="rule.eventType" class="flex items-center justify-between gap-3 p-3"><span>{{ eventLabels[rule.eventType] }}</span><input v-model="rule.enabled" type="checkbox" class="size-4"></Panel></div>
        </div>
        <Button :disabled="saveConfig.isPending.value" @click="saveConfig.mutate()"><Loader2 v-if="saveConfig.isPending.value" class="size-4 animate-spin" /><Save v-else class="size-4" />{{ t('admin.qqBot.competition.saveConfiguration') }}</Button>
      </CardContent>
    </Card>

    <div class="grid gap-6 xl:grid-cols-[minmax(0,1fr)_420px]">
      <Card>
        <CardHeader class="flex-row items-center justify-between gap-4"><div><CardTitle class="flex items-center gap-2"><FileText class="size-5 text-primary" />{{ t('admin.qqBot.competition.messageTemplates') }}</CardTitle><p class="mt-1 text-sm text-muted-foreground">{{ t('admin.qqBot.competition.messageTemplatesDescription') }}</p></div><Button variant="outline" size="sm" @click="resetTemplate">{{ t('admin.qqBot.competition.newTemplate') }}</Button></CardHeader>
        <CardContent class="space-y-4">
          <div v-if="loadingTemplates" class="text-sm text-muted-foreground">{{ t('admin.qqBot.competition.loadingTemplates') }}</div>
          <div v-else-if="templates?.length" class="flex flex-wrap gap-2"><Button v-for="template in templates" :key="template.id ?? template.name" size="sm" :variant="templateForm.id === template.id ? 'default' : 'outline'" @click="selectTemplate(template)">{{ template.name || eventLabels[template.eventType ?? 0] }}</Button></div>
          <div class="grid gap-3 md:grid-cols-2"><div class="grid gap-2"><Label>{{ t('admin.qqBot.competition.event') }}</Label><Select v-model="templateForm.eventType"><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem v-for="(_, eventType) in eventLabels" :key="eventType" :value="Number(eventType)">{{ eventLabels[Number(eventType) as QqEventType] }}</SelectItem></SelectContent></Select></div><div class="grid gap-2"><Label>{{ t('common.name') }}</Label><Input v-model="templateForm.name" :placeholder="t('admin.qqBot.competition.templateName')" /></div></div>
          <div class="grid gap-2"><Label>{{ t('admin.qqBot.competition.content') }}</Label><Textarea v-model="templateForm.content" rows="10" class="font-mono text-xs" :placeholder="t('admin.qqBot.competition.templateContentPlaceholder')" /></div>
          <label class="flex items-center gap-2 text-sm"><input v-model="templateForm.isDefault" type="checkbox" class="size-4">{{ t('admin.qqBot.competition.useAsEventDefault') }}</label>
          <div class="flex flex-wrap gap-2"><Button :disabled="!templateForm.name.trim() || !templateForm.content.trim() || saveTemplate.isPending.value" @click="saveTemplate.mutate()"><Loader2 v-if="saveTemplate.isPending.value" class="size-4 animate-spin" /><Save v-else class="size-4" />{{ t('admin.qqBot.competition.saveTemplate') }}</Button><Button variant="outline" :disabled="previewTemplate.isPending.value" @click="previewTemplate.mutate()"><Loader2 v-if="previewTemplate.isPending.value" class="size-4 animate-spin" /><Eye v-else class="size-4" />{{ t('admin.qqBot.competition.preview') }}</Button></div>
          <Panel v-if="preview" class="space-y-2 p-4"><div class="flex items-center justify-between text-sm"><span class="font-medium">{{ t('admin.qqBot.competition.preview') }}</span><Badge variant="outline">{{ t('admin.qqBot.competition.characterCount', { count: preview.characterCount ?? 0 }) }}</Badge></div><pre class="whitespace-pre-wrap break-words text-sm">{{ preview.renderedText }}</pre><p v-if="preview.warnings?.length" class="text-xs text-[var(--semantic-warning)]">{{ preview.warnings.join(' ') }}</p></Panel>
        </CardContent>
      </Card>

      <Card>
        <CardHeader><CardTitle class="flex items-center gap-2"><Send class="size-5 text-primary" />{{ t('admin.qqBot.competition.manualAnnouncement') }}</CardTitle></CardHeader>
        <CardContent class="space-y-4"><div class="grid gap-2"><Label>{{ t('common.title') }}</Label><Input v-model="announcementForm.title" :placeholder="t('admin.qqBot.competition.announcementTitle')" /></div><div class="grid gap-2"><Label>{{ t('admin.qqBot.competition.content') }}</Label><Textarea v-model="announcementForm.content" rows="8" :placeholder="t('admin.qqBot.competition.messageContent')" /></div><div class="grid gap-2"><Label>{{ t('admin.qqBot.competition.template') }}</Label><Select v-model="selectedTemplateId"><SelectTrigger><SelectValue :placeholder="t('admin.qqBot.competition.noTemplate')" /></SelectTrigger><SelectContent><SelectItem value="">{{ t('admin.qqBot.competition.noTemplate') }}</SelectItem><SelectItem v-for="template in templates ?? []" :key="template.id ?? template.name ?? `event-${template.eventType ?? 0}`" :value="template.id ?? ''">{{ template.name || eventLabels[template.eventType ?? 0] }}</SelectItem></SelectContent></Select></div><p class="text-xs text-muted-foreground">{{ t('admin.qqBot.competition.boundGroupRecipients', { count: bindings.length }) }}</p><label class="flex items-center gap-2 text-sm"><input v-model="announcementForm.isTest" type="checkbox" class="size-4">{{ t('admin.qqBot.competition.sendAsTest') }}</label><Button class="w-full" :disabled="!announcementForm.title.trim() || !announcementForm.content.trim() || !bindings.length || sendAnnouncement.isPending.value" @click="sendAnnouncement.mutate()"><Loader2 v-if="sendAnnouncement.isPending.value" class="size-4 animate-spin" /><Send v-else class="size-4" />{{ t('admin.qqBot.competition.queueAnnouncement') }}</Button></CardContent>
      </Card>
    </div>

    <Card>
      <CardHeader class="flex-row items-center justify-between gap-4"><div><CardTitle>{{ t('admin.qqBot.competition.deliveryLog') }}</CardTitle><p class="mt-1 text-sm text-muted-foreground">{{ t('admin.qqBot.competition.deliveryLogDescription') }}</p></div><Button variant="outline" size="sm" @click="refetchDeliveryLog()"><RefreshCw class="size-4" />{{ t('common.refresh') }}</Button></CardHeader>
      <CardContent class="space-y-2"><Panel v-for="delivery in deliveryItems" :key="delivery.id" class="flex flex-col gap-3 p-4 md:flex-row md:items-center md:justify-between"><div class="min-w-0"><div class="flex flex-wrap items-center gap-2"><span class="font-medium">{{ delivery.groupName || t('admin.qqBot.competition.unknownGroup') }}</span><Badge :variant="statusVariant(delivery.status)">{{ statusLabel(delivery.status) }}</Badge><span class="text-xs text-muted-foreground">{{ eventLabels[delivery.eventType ?? 0] }}</span></div><p class="mt-1 truncate text-sm text-muted-foreground">{{ delivery.messageSummary }}</p><p v-if="delivery.lastErrorSummary" class="mt-1 text-xs text-destructive">{{ delivery.lastErrorSummary }}</p></div><div class="flex items-center gap-3"><span class="text-xs tabular-nums text-muted-foreground">{{ delivery.attemptCount ?? 0 }} / {{ delivery.maxAttempts ?? 0 }}</span><Button v-if="delivery.id && delivery.status === 3" size="sm" variant="outline" :disabled="retryDelivery.isPending.value" @click="retryDelivery.mutate(delivery.id)"><RotateCcw class="size-4" />{{ t('admin.qqBot.retry') }}</Button></div></Panel><p v-if="!deliveryItems.length" class="py-4 text-sm text-muted-foreground">{{ t('admin.qqBot.competition.noDeliveries') }}</p></CardContent>
    </Card>
  </div>
</template>
