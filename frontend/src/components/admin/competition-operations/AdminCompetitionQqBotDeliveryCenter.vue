<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { BellRing, Eye, FileText, Loader2, MessageSquareText, RefreshCw, RotateCcw, Save, Send, ShieldCheck, UsersRound } from 'lucide-vue-next'
import { computed, reactive, ref, watch } from 'vue'
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
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'

type QqEventType = 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7

interface QqRule {
  eventType: QqEventType
  enabled: boolean
  templateId?: string | null
}

interface QqBinding {
  agentId: string
  groupId: string
  qqGroupId: number
  groupName?: string
  isDefault?: boolean
  eventTypes?: QqEventType[]
}

interface QqConfig {
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

interface QqGroup {
  id: string
  agentId: string
  groupId: number
  groupName: string
}

interface QqTemplate {
  id?: string | null
  eventType?: QqEventType
  name?: string
  content?: string
  isDefault?: boolean
}

interface QqPreview {
  renderedText?: string
  characterCount?: number
  warnings?: string[]
}

interface QqDelivery {
  id?: string
  eventType?: QqEventType
  groupName?: string
  status?: number
  attemptCount?: number
  maxAttempts?: number
  messageSummary?: string
  lastErrorSummary?: string | null
}

type BooleanConfigKey = Exclude<keyof QqConfig, 'eventRules' | 'groupBindings' | 'warnings'>

const props = defineProps<{ competitionId: string }>()
const { t, te } = useI18n()
const qc = useQueryClient()
const noTemplateValue = '__none__'
const selectedTemplateId = ref(noTemplateValue)
const preview = ref<QqPreview | null>(null)
const selectedGroupIds = ref<string[]>([])
const templateForm = reactive({ id: null as string | null, eventType: 0 as QqEventType, name: '', content: '', isDefault: false })
const announcementForm = reactive({ title: '', content: '', isTest: false })
const form = reactive<QqConfig>({
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
const deliveryOptions: Array<{ key: BooleanConfigKey, label: string }> = [
  { key: 'enabled', label: 'admin.qqBot.competition.enableDelivery' },
  { key: 'allowMessages', label: 'admin.qqBot.competition.allowMessages' },
  { key: 'allowManualNotifications', label: 'admin.qqBot.competition.allowManualAnnouncements' },
  { key: 'stopNormalEventsAfterFinished', label: 'admin.qqBot.competition.stopNormalEventsAfterFinish' },
]
const messageOptions: Array<{ key: BooleanConfigKey, label: string }> = [
  { key: 'mentionAll', label: 'admin.qqBot.competition.mentionAll' },
  { key: 'showTeamName', label: 'admin.qqBot.competition.showTeamName' },
  { key: 'showUserName', label: 'admin.qqBot.competition.showUserName' },
  { key: 'showChallengeCategory', label: 'admin.qqBot.competition.showChallengeCategory' },
  { key: 'includeCompetitionLink', label: 'admin.qqBot.competition.includeCompetitionLink' },
  { key: 'includeChallengeLink', label: 'admin.qqBot.competition.includeChallengeLink' },
  { key: 'hidePenaltyDetails', label: 'admin.qqBot.competition.hidePenaltyDetails' },
]

const { data: config, isLoading, isError, refetch } = useQuery({
  queryKey: computed(() => ['competition-qqbot', props.competitionId]),
  queryFn: () => adminApi.competitionQqBot(props.competitionId) as Promise<QqConfig>,
})
const { data: templates, isLoading: loadingTemplates } = useQuery({
  queryKey: computed(() => ['competition-qqbot-templates', props.competitionId]),
  queryFn: () => adminApi.competitionQqBotTemplates(props.competitionId) as Promise<QqTemplate[]>,
})
const { data: availableGroups } = useQuery({
  queryKey: computed(() => ['competition-qqbot-groups', props.competitionId]),
  queryFn: () => adminApi.availableQqBotGroups(props.competitionId) as Promise<QqGroup[]>,
  retry: false,
})
const { data: deliveryLog, refetch: refetchDeliveryLog } = useQuery({
  queryKey: computed(() => ['competition-qqbot-log', props.competitionId]),
  queryFn: () => adminApi.qqBotLogs(props.competitionId, {} as never) as Promise<{ items?: QqDelivery[] }>,
  refetchInterval: 15_000,
})

const groupsById = computed(() => new Map((availableGroups.value ?? []).map(group => [group.id, group])))
const bindings = computed<QqBinding[]>(() => selectedGroupIds.value.flatMap((groupId) => {
  const existing = form.groupBindings.find(binding => binding.groupId === groupId)
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
const deliveryItems = computed(() => deliveryLog.value?.items ?? [])
const selectableTemplates = computed(() => (templates.value ?? []).filter(template => Boolean(template.id)))

watch(config, (value) => {
  if (!value)
    return
  Object.assign(form, {
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
  })
  selectedGroupIds.value = (value.groupBindings ?? []).map(binding => binding.groupId)
}, { immediate: true })

watch(templates, (items) => {
  if (selectedTemplateId.value === noTemplateValue)
    selectedTemplateId.value = items?.find(template => template.id)?.id ?? noTemplateValue
}, { immediate: true })

function toggleGroup(groupId: string) {
  selectedGroupIds.value = selectedGroupIds.value.includes(groupId)
    ? selectedGroupIds.value.filter(id => id !== groupId)
    : [...selectedGroupIds.value, groupId]
}

function selectTemplate(template: QqTemplate) {
  templateForm.id = template.id ?? null
  templateForm.eventType = template.eventType ?? 0
  templateForm.name = template.name ?? ''
  templateForm.content = template.content ?? ''
  templateForm.isDefault = template.isDefault ?? false
  selectedTemplateId.value = template.id ?? noTemplateValue
}

function resetTemplate() {
  templateForm.id = null
  templateForm.eventType = 0
  templateForm.name = ''
  templateForm.content = ''
  templateForm.isDefault = false
}

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

function warningLabel(code: string) {
  const key = `admin.qqBot.competition.warnings.${code}`
  return te(key) ? t(key) : code
}

const saveConfig = useMutation({
  mutationFn: () => adminApi.updateCompetitionQqBot(props.competitionId, { ...form, groupBindings: bindings.value }),
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: ['competition-qqbot', props.competitionId] })
    toast.success(t('admin.qqBot.competition.configurationSaved'))
  },
  onError: () => toast.error(t('admin.qqBot.competition.configurationSaveFailed')),
})
const saveTemplate = useMutation({
  mutationFn: () => adminApi.upsertCompetitionQqBotTemplate(props.competitionId, {
    id: templateForm.id,
    eventType: templateForm.eventType,
    name: templateForm.name.trim(),
    content: templateForm.content,
    isDefault: templateForm.isDefault,
  }),
  onSuccess: () => {
    qc.invalidateQueries({ queryKey: ['competition-qqbot-templates', props.competitionId] })
    toast.success(t('admin.qqBot.competition.templateSaved'))
  },
  onError: () => toast.error(t('admin.qqBot.competition.templateSaveFailed')),
})
const previewTemplate = useMutation({
  mutationFn: () => adminApi.previewQqBot(props.competitionId, {
    eventType: templateForm.eventType,
    templateId: templateForm.id,
    announcementTitle: announcementForm.title.trim() || null,
    announcementContent: announcementForm.content.trim() || null,
  }),
  onSuccess: (value) => { preview.value = value as QqPreview },
  onError: () => toast.error(t('admin.qqBot.competition.previewFailed')),
})
const sendAnnouncement = useMutation({
  mutationFn: () => adminApi.sendQqBotNotification(props.competitionId, {
    title: announcementForm.title.trim(),
    content: announcementForm.content.trim(),
    templateId: selectedTemplateId.value === noTemplateValue ? null : selectedTemplateId.value,
    groupIds: bindings.value.map(binding => binding.groupId),
    isTest: announcementForm.isTest,
  }),
  onSuccess: () => {
    announcementForm.title = ''
    announcementForm.content = ''
    qc.invalidateQueries({ queryKey: ['competition-qqbot-log', props.competitionId] })
    toast.success(t('admin.qqBot.competition.notificationQueued'))
  },
  onError: () => toast.error(t('admin.qqBot.competition.notificationQueueFailed')),
})
const retryDelivery = useMutation({
  mutationFn: (deliveryId: string) => adminApi.retryQqBotDelivery(props.competitionId, deliveryId),
  onSuccess: () => {
    refetchDeliveryLog()
    toast.success(t('admin.qqBot.competition.deliveryRetryQueued'))
  },
  onError: () => toast.error(t('admin.qqBot.competition.deliveryRetryFailed')),
})
</script>

<template>
  <DataState v-if="isLoading" loading />
  <DataState v-else-if="isError" error :retry-label="t('admin.qqBot.retry')" @retry="refetch()" />

  <div v-else class="space-y-6">
    <Card class="overflow-hidden">
      <CardHeader class="grid-cols-[minmax(0,1fr)_auto] items-center gap-4 border-b bg-muted/20">
        <div><CardTitle class="flex items-center gap-2"><BellRing class="size-5 text-primary" />{{ t('admin.qqBot.competition.deliveryConfiguration') }}</CardTitle><p class="mt-1 text-sm text-muted-foreground">{{ t('admin.qqBot.competition.deliveryConfigurationDescription') }}</p></div>
        <Button variant="outline" size="sm" @click="refetch()"><RefreshCw class="size-4" />{{ t('common.refresh') }}</Button>
      </CardHeader>
      <CardContent class="p-0">
        <div v-if="form.warnings.length" class="m-5 border border-amber-500/30 bg-amber-500/5 p-3 text-sm text-amber-800">
          <ul class="list-disc space-y-1 pl-5">
            <li v-for="warning in form.warnings" :key="warning">{{ warningLabel(warning) }}</li>
          </ul>
        </div>

        <div class="grid xl:grid-cols-2">
          <section class="space-y-4 p-5 xl:border-r">
            <div class="flex items-start gap-3">
              <ShieldCheck class="mt-0.5 size-5 shrink-0 text-primary" />
              <div><h3 class="font-semibold">{{ t('admin.qqBot.competition.deliveryControls') }}</h3><p class="text-sm text-muted-foreground">{{ t('admin.qqBot.competition.deliveryControlsDescription') }}</p></div>
            </div>
            <div class="divide-y border">
              <label v-for="item in deliveryOptions" :key="item.key" class="flex min-h-12 items-center justify-between gap-4 px-4 py-3 text-sm" :class="item.key === 'enabled' && 'bg-primary/5 font-medium'"><span>{{ t(item.label) }}</span><input v-model="form[item.key]" type="checkbox" class="size-4 shrink-0"></label>
            </div>
          </section>

          <section class="space-y-4 border-t p-5 xl:border-t-0">
            <div class="flex items-start gap-3">
              <MessageSquareText class="mt-0.5 size-5 shrink-0 text-primary" />
              <div><h3 class="font-semibold">{{ t('admin.qqBot.competition.messageContentOptions') }}</h3><p class="text-sm text-muted-foreground">{{ t('admin.qqBot.competition.messageContentOptionsDescription') }}</p></div>
            </div>
            <div class="grid divide-y border sm:grid-cols-2 sm:divide-y-0">
              <label v-for="item in messageOptions" :key="item.key" class="flex min-h-12 items-center justify-between gap-4 border-b px-4 py-3 text-sm sm:odd:border-r"><span>{{ t(item.label) }}</span><input v-model="form[item.key]" type="checkbox" class="size-4 shrink-0"></label>
            </div>
          </section>
        </div>

        <div class="grid border-t xl:grid-cols-[minmax(0,0.9fr)_minmax(0,1.1fr)]">
          <section class="space-y-4 p-5 xl:border-r">
            <div class="flex items-start gap-3"><UsersRound class="mt-0.5 size-5 shrink-0 text-primary" /><div><h3 class="font-semibold">{{ t('admin.qqBot.competition.boundGroups') }}</h3><p class="text-sm text-muted-foreground">{{ t('admin.qqBot.competition.boundGroupsDescription') }}</p></div></div>
            <div v-if="availableGroups?.length" class="grid gap-2 sm:grid-cols-2"><Button v-for="group in availableGroups" :key="group.id" variant="outline" class="h-auto justify-start p-3 text-left" :class="selectedGroupIds.includes(group.id) && 'border-primary bg-primary/5'" @click="toggleGroup(group.id)"><span class="min-w-0"><span class="block truncate font-medium">{{ group.groupName }}</span><span class="block text-xs text-muted-foreground">{{ group.groupId }}</span></span></Button></div><p v-else class="text-sm text-muted-foreground">{{ t('admin.qqBot.competition.noAvailableGroups') }}</p><div v-if="bindings.length" class="flex flex-wrap gap-2"><Badge v-for="binding in bindings" :key="binding.groupId" variant="secondary">{{ binding.groupName ?? binding.qqGroupId }}</Badge></div>
          </section>
          <section class="space-y-4 border-t p-5 xl:border-t-0">
            <div><h3 class="font-semibold">{{ t('admin.qqBot.competition.eventRules') }}</h3><p class="text-sm text-muted-foreground">{{ t('admin.qqBot.competition.eventRulesDescription') }}</p></div>
            <div class="grid gap-2 sm:grid-cols-2"><Panel v-for="rule in form.eventRules" :key="rule.eventType" class="flex min-h-12 items-center justify-between gap-3 p-3"><span class="text-sm">{{ eventLabels[rule.eventType] }}</span><input v-model="rule.enabled" type="checkbox" class="size-4 shrink-0"></Panel></div>
          </section>
        </div>

        <div class="flex justify-end border-t bg-muted/20 p-4">
          <Button :disabled="saveConfig.isPending.value" @click="saveConfig.mutate()"><Loader2 v-if="saveConfig.isPending.value" class="size-4 animate-spin" /><Save v-else class="size-4" />{{ t('admin.qqBot.competition.saveConfiguration') }}</Button>
        </div>
      </CardContent>
    </Card>

    <div class="grid gap-6 xl:grid-cols-[minmax(0,1fr)_420px]">
      <Card>
        <CardHeader class="grid-cols-[minmax(0,1fr)_auto] items-center gap-4"><div><CardTitle class="flex items-center gap-2"><FileText class="size-5 text-primary" />{{ t('admin.qqBot.competition.messageTemplates') }}</CardTitle><p class="mt-1 text-sm text-muted-foreground">{{ t('admin.qqBot.competition.messageTemplatesDescription') }}</p></div><Button variant="outline" size="sm" @click="resetTemplate">{{ t('admin.qqBot.competition.newTemplate') }}</Button></CardHeader>
        <CardContent class="space-y-4">
          <div v-if="loadingTemplates" class="text-sm text-muted-foreground">{{ t('admin.qqBot.competition.loadingTemplates') }}</div>
          <div v-else-if="templates?.length" class="flex flex-wrap gap-2"><Button v-for="template in templates" :key="template.id ?? template.name" size="sm" :variant="templateForm.id === template.id ? 'default' : 'outline'" @click="selectTemplate(template)">{{ template.name || eventLabels[template.eventType ?? 0] }}</Button></div>
          <div class="grid gap-3 md:grid-cols-2"><div class="grid gap-2"><Label>{{ t('admin.qqBot.competition.event') }}</Label><Select v-model="templateForm.eventType"><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem v-for="(_, eventType) in eventLabels" :key="eventType" :value="Number(eventType)">{{ eventLabels[Number(eventType) as QqEventType] }}</SelectItem></SelectContent></Select></div><div class="grid gap-2"><Label>{{ t('common.name') }}</Label><Input v-model="templateForm.name" :placeholder="t('admin.qqBot.competition.templateName')" /></div></div>
          <div class="grid gap-2"><Label>{{ t('admin.qqBot.competition.content') }}</Label><Textarea v-model="templateForm.content" rows="10" class="font-mono text-xs" :placeholder="t('admin.qqBot.competition.templateContentPlaceholder')" /></div>
          <label class="flex items-center gap-2 text-sm"><input v-model="templateForm.isDefault" type="checkbox" class="size-4">{{ t('admin.qqBot.competition.useAsEventDefault') }}</label>
          <div class="flex flex-wrap gap-2"><Button :disabled="!templateForm.name.trim() || !templateForm.content.trim() || saveTemplate.isPending.value" @click="saveTemplate.mutate()"><Loader2 v-if="saveTemplate.isPending.value" class="size-4 animate-spin" /><Save v-else class="size-4" />{{ t('admin.qqBot.competition.saveTemplate') }}</Button><Button variant="outline" :disabled="previewTemplate.isPending.value" @click="previewTemplate.mutate()"><Loader2 v-if="previewTemplate.isPending.value" class="size-4 animate-spin" /><Eye v-else class="size-4" />{{ t('admin.qqBot.competition.preview') }}</Button></div>
          <Panel v-if="preview" class="space-y-2 p-4"><div class="flex items-center justify-between text-sm"><span class="font-medium">{{ t('admin.qqBot.competition.preview') }}</span><Badge variant="outline">{{ t('admin.qqBot.competition.characterCount', { count: preview.characterCount ?? 0 }) }}</Badge></div><pre class="whitespace-pre-wrap break-words text-sm">{{ preview.renderedText }}</pre><ul v-if="preview.warnings?.length" class="list-disc space-y-1 pl-5 text-xs text-amber-700"><li v-for="warning in preview.warnings" :key="warning">{{ warningLabel(warning) }}</li></ul></Panel>
        </CardContent>
      </Card>

      <Card>
        <CardHeader><CardTitle class="flex items-center gap-2"><Send class="size-5 text-primary" />{{ t('admin.qqBot.competition.manualAnnouncement') }}</CardTitle></CardHeader>
        <CardContent class="space-y-4"><div class="grid gap-2"><Label>{{ t('common.title') }}</Label><Input v-model="announcementForm.title" :placeholder="t('admin.qqBot.competition.announcementTitle')" /></div><div class="grid gap-2"><Label>{{ t('admin.qqBot.competition.content') }}</Label><Textarea v-model="announcementForm.content" rows="8" :placeholder="t('admin.qqBot.competition.messageContent')" /></div><div class="grid gap-2"><Label>{{ t('admin.qqBot.competition.template') }}</Label><Select v-model="selectedTemplateId"><SelectTrigger><SelectValue :placeholder="t('admin.qqBot.competition.noTemplate')" /></SelectTrigger><SelectContent><SelectItem :value="noTemplateValue">{{ t('admin.qqBot.competition.noTemplate') }}</SelectItem><SelectItem v-for="template in selectableTemplates" :key="String(template.id)" :value="String(template.id)">{{ template.name || eventLabels[template.eventType ?? 0] }}</SelectItem></SelectContent></Select></div><p class="text-xs text-muted-foreground">{{ t('admin.qqBot.competition.boundGroupRecipients', { count: bindings.length }) }}</p><label class="flex items-center gap-2 text-sm"><input v-model="announcementForm.isTest" type="checkbox" class="size-4">{{ t('admin.qqBot.competition.sendAsTest') }}</label><Button class="w-full" :disabled="!announcementForm.title.trim() || !announcementForm.content.trim() || !bindings.length || sendAnnouncement.isPending.value" @click="sendAnnouncement.mutate()"><Loader2 v-if="sendAnnouncement.isPending.value" class="size-4 animate-spin" /><Send v-else class="size-4" />{{ t('admin.qqBot.competition.queueAnnouncement') }}</Button></CardContent>
      </Card>
    </div>

    <Card>
      <CardHeader class="grid-cols-[minmax(0,1fr)_auto] items-center gap-4"><div><CardTitle>{{ t('admin.qqBot.competition.deliveryLog') }}</CardTitle><p class="mt-1 text-sm text-muted-foreground">{{ t('admin.qqBot.competition.deliveryLogDescription') }}</p></div><Button variant="outline" size="sm" @click="refetchDeliveryLog()"><RefreshCw class="size-4" />{{ t('common.refresh') }}</Button></CardHeader>
      <CardContent class="space-y-2"><Panel v-for="delivery in deliveryItems" :key="delivery.id" class="flex flex-col gap-3 p-4 md:flex-row md:items-center md:justify-between"><div class="min-w-0"><div class="flex flex-wrap items-center gap-2"><span class="font-medium">{{ delivery.groupName || t('admin.qqBot.competition.unknownGroup') }}</span><Badge :variant="statusVariant(delivery.status)">{{ statusLabel(delivery.status) }}</Badge><span class="text-xs text-muted-foreground">{{ eventLabels[delivery.eventType ?? 0] }}</span></div><p class="mt-1 truncate text-sm text-muted-foreground">{{ delivery.messageSummary }}</p><p v-if="delivery.lastErrorSummary" class="mt-1 text-xs text-destructive">{{ delivery.lastErrorSummary }}</p></div><div class="flex items-center gap-3"><span class="text-xs tabular-nums text-muted-foreground">{{ delivery.attemptCount ?? 0 }} / {{ delivery.maxAttempts ?? 0 }}</span><Button v-if="delivery.id && delivery.status === 3" size="sm" variant="outline" :disabled="retryDelivery.isPending.value" @click="retryDelivery.mutate(delivery.id)"><RotateCcw class="size-4" />{{ t('admin.qqBot.retry') }}</Button></div></Panel><p v-if="!deliveryItems.length" class="py-4 text-sm text-muted-foreground">{{ t('admin.qqBot.competition.noDeliveries') }}</p></CardContent>
    </Card>
  </div>
</template>
